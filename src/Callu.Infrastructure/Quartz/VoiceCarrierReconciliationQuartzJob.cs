using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Providers.CalluVoice;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Puts the SIP carrier back on a self-hosted voice service that is no longer holding it.</summary>
// The service keeps no carrier across a restart by design, so something has to notice. A provider
// with no trunk chosen is skipped: that says nothing about which carrier, not "remove the one you have".
[DisallowConcurrentExecution]
public sealed class VoiceCarrierReconciliationQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<VoiceCarrierReconciliationQuartzJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Read fresh rather than through the registry snapshot: this host's snapshot may predate
        // the carrier an operator saved on the API, and re-pushing an old one is worse than nothing.
        var providers = await db.Set<CommunicationProvider>().AsNoTracking()
            .Include(p => p.SipTrunk)
            .Where(p => !p.IsDeleted
                        && p.IsEnabled
                        && p.ProviderType == CalluVoiceProvider.TypeName
                        && p.SipTrunkId != null)
            .OrderBy(p => p.Priority).ThenBy(p => p.Id)
            .ToListAsync(ct);

        // Grouped by the voice service each one names, because that is what holds the carrier: two
        // providers on one service reconciled in turn would replace each other's every sweep, and a
        // page placed during a reload dials an endpoint the replacement has already destroyed.
        // A row with no readable base URL is its own group, so it still reports its own failure.
        foreach (var service in providers.GroupBy(p =>
                     CalluVoiceConfig.VoiceServiceKeyFromJson(p.ConfigJson) ?? p.Id.ToString()))
        {
            if (ct.IsCancellationRequested) break;

            var rows = service.ToList();

            // Lowest Priority wins, which is the provider Callu prefers to dial through anyway.
            var owner = rows[0];

            try
            {
                if (rows.Count > 1 && rows.Select(r => r.SipTrunkId).Distinct().Count() > 1)
                    await ReportConflictAsync(scope.ServiceProvider, db, rows, ct);

                await ReconcileAsync(scope.ServiceProvider, owner, ct);
            }
            catch (Exception ex) when (!IsShutdown(ex, ct))
            {
                logger.LogError(ex,
                    "VoiceCarrierReconciliation: could not check the carrier on provider {ProviderId}", owner.Id);
            }
        }
    }

    /// <summary>Reports providers that name one voice service but disagree about its carrier.</summary>
    // Callu keeps reconciling from one of them, so the carrier is stable rather than flapping — but
    // the other provider's trunk is then not the one being dialled through, and nothing else says so.
    private async Task ReportConflictAsync(
        IServiceProvider services, ApplicationDbContext db, List<CommunicationProvider> rows, CancellationToken ct)
    {
        var owner = rows[0];
        var others = string.Join(", ", rows.Skip(1).Select(r => r.Name));

        logger.LogError(
            "Providers {Providers} all point at the same voice service but name different SIP carriers. "
            + "A voice service holds one carrier, so Callu keeps sending {Owner}'s and ignores the rest; "
            + "calls routed through the others leave over a carrier that is not theirs. Switch off all but "
            + "one of them, or give each its own callu-voice instance.",
            string.Join(", ", rows.Select(r => r.Name)), owner.Name);

        var description = Clip(
            $"'{owner.Name}' shares its voice service with {others}, and they name different SIP carriers. "
            + "Callu reconciles from this one and ignores the others; a voice service holds one carrier.");

        // Once a day, not once a sweep: this configuration persists, and a row every few minutes
        // would bury the trail it exists to leave.
        var since = DateTime.UtcNow - ConflictReportInterval;
        var alreadyReported = await db.Set<AuditLog>().AsNoTracking().AnyAsync(
            a => a.ResourceType == "CommunicationProvider"
                 && a.ResourceId == owner.Id
                 && a.Summary == description
                 && a.CreatedAt >= since, ct);

        if (alreadyReported) return;

        await WriteAsync(services, owner, description, ct);
    }

    private static readonly TimeSpan ConflictReportInterval = TimeSpan.FromHours(24);

    private async Task ReconcileAsync(IServiceProvider services, CommunicationProvider row, CancellationToken ct)
    {
        var voice = services.GetRequiredService<CalluVoiceProvider>();
        await voice.InitializeAsync(row.ConfigJson ?? "{}", row.SipTrunk);

        var result = await voice.ReconcileTrunkAsync();
        switch (result.Outcome)
        {
            // Silent on purpose: this runs every few minutes, and a line per sweep would bury the
            // one sweep that had something to say.
            case CalluVoiceTrunkOutcome.InAgreement:
                return;

            case CalluVoiceTrunkOutcome.Restored:
                logger.LogWarning(
                    "The voice service behind provider {ProviderName} was not holding the configured SIP carrier; "
                    + "Callu has sent it again. It was most likely restarted or recreated.", row.Name);
                await WriteAsync(services, row,
                    "The voice service had lost the SIP carrier and Callu restored it.", ct);
                return;

            // No audit row: a service that is briefly unreachable says nothing about the carrier,
            // and one row every few minutes through an outage would drown the trail.
            case CalluVoiceTrunkOutcome.CouldNotRead:
                logger.LogWarning(
                    "Could not read the SIP carrier from the voice service behind provider {ProviderName}: {Error}",
                    row.Name, result.Error);
                return;

            case CalluVoiceTrunkOutcome.CouldNotRestore:
                logger.LogError(
                    "The voice service behind provider {ProviderName} is not holding the configured SIP carrier "
                    + "and Callu could not restore it: {Error}. Calls placed through it may reach nobody.",
                    row.Name, result.Error);
                await WriteAsync(services, row,
                    $"The voice service has lost the SIP carrier and it could NOT be restored: {Clip(result.Error)}", ct);
                return;
        }
    }

    // Swallowed: a failing audit sink must not stop the next provider being checked, and the log
    // line above already carries the fact.
    private async Task WriteAsync(
        IServiceProvider services, CommunicationProvider row, string description, CancellationToken ct)
    {
        try
        {
            var audit = services.GetRequiredService<IAuditLogService>();
            await audit.LogAsync(
                userId: null,
                AuditAction.SettingsChanged,
                "CommunicationProvider",
                row.Id.ToString(),
                description: description,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "VoiceCarrierReconciliation: could not write the audit row for provider {ProviderId}", row.Id);
        }
    }

    // Leaves room under AuditLog.Description for the sentence this is embedded in.
    private const int MaxCarrierErrorChars = 400;

    private static string Clip(string? error)
    {
        var text = (error ?? "unknown error").Trim();
        return text.Length <= MaxCarrierErrorChars ? text : text[..MaxCarrierErrorChars];
    }

    private static bool IsShutdown(Exception ex, CancellationToken ct) =>
        ex is OperationCanceledException && ct.IsCancellationRequested;
}
