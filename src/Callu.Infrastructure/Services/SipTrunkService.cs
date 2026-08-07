using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Shared.Models.Communication;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Service for managing SIP trunk configurations
/// </summary>
public class SipTrunkService : ISipTrunkService
{
    private readonly ISipTrunkSettingsRepository _trunkRepo;
    private readonly ICommunicationProviderRepository _providerRepo;
    private readonly ITransactionManager _transactionManager;
    private readonly SipTrunkPasswordProtector _passwordProtector;
    private readonly IAuditLogService _auditLogService;
    private readonly ICommunicationProviderRegistry _registry;
    private readonly ILogger<SipTrunkService> _logger;

    public SipTrunkService(
        ISipTrunkSettingsRepository trunkRepo,
        ICommunicationProviderRepository providerRepo,
        ITransactionManager transactionManager,
        SipTrunkPasswordProtector passwordProtector,
        IAuditLogService auditLogService,
        ICommunicationProviderRegistry registry,
        ILogger<SipTrunkService> logger)
    {
        _trunkRepo = trunkRepo;
        _providerRepo = providerRepo;
        _transactionManager = transactionManager;
        _passwordProtector = passwordProtector;
        _auditLogService = auditLogService;
        _registry = registry;
        _logger = logger;
    }
    
    public async Task<IEnumerable<SipTrunkDto>> GetTrunksAsync(CancellationToken cancellationToken = default)
    {
        var trunks = await _trunkRepo.FindAsync(t => !t.IsDeleted, cancellationToken);
        return trunks.OrderBy(t => t.Name).Select(t => t.Adapt<SipTrunkDto>());
    }
    
    public async Task<SipTrunkDto?> GetTrunkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (trunk == null) return null;
        
        return trunk.Adapt<SipTrunkDto>();
    }
    
    public async Task<SipTrunkDto> CreateTrunkAsync(CreateSipTrunkRequest request, CancellationToken cancellationToken = default)
    {
        var trunkId = await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var trunk = new SipTrunkSettings
            {
                Name = request.Name,
                // Trimmed because Asterisk strips whitespace off a config value anyway: an
                // untrimmed host is stored as one thing and dialled as another.
                Server = request.Server.Trim(),
                Port = request.Port,
                Username = request.Username.Trim(),
                Password = _passwordProtector.Protect(request.Password),
                AuthUser = Blank(request.AuthUser),
                CallerId = Blank(request.CallerId),
                DisplayName = Blank(request.DisplayName),
                UseTls = request.UseTls,
                UseTcp = request.UseTcp,
                IsEnabled = true
            };
            
            await _trunkRepo.AddAsync(trunk, cancellationToken);

            // The password is never in the row: an audit entry records that one was set, not what.
            await _auditLogService.LogAsync(
                null, AuditAction.SettingsChanged, "SipTrunk", trunk.Id.ToString(),
                newValues: $"name={trunk.Name}; server={trunk.Server}:{trunk.Port}; user={trunk.Username}",
                description: "Password set",
                cancellationToken: cancellationToken);
            
            _logger.LogInformation("Created SIP trunk: {Name} ({Server})", trunk.Name, trunk.Server);
            
            return trunk.Id;
        }, cancellationToken);
        
        return (await GetTrunkAsync(trunkId, cancellationToken))!;
    }
    
    public async Task UpdateTrunkAsync(Guid id, UpdateSipTrunkRequest request, CancellationToken cancellationToken = default)
    {
        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
            if (trunk == null)
                throw new KeyNotFoundException($"SIP trunk not found: {id}");

            var before = $"name={trunk.Name}; server={trunk.Server}:{trunk.Port}; user={trunk.Username}; enabled={trunk.IsEnabled}";

            trunk.Name = request.Name;
            trunk.Server = request.Server.Trim();
            trunk.Port = request.Port;
            trunk.Username = request.Username.Trim();

            // A field the request says nothing about is not a field it cleared. Saying nothing about
            // the auth user used to null it, which is a carrier that stops authenticating.
            if (request.AuthUser is not null) trunk.AuthUser = Blank(request.AuthUser);
            if (request.CallerId is not null) trunk.CallerId = Blank(request.CallerId);
            if (request.DisplayName is not null) trunk.DisplayName = Blank(request.DisplayName);
            if (request.UseTls is { } tls) trunk.UseTls = tls;
            if (request.UseTcp is { } tcp) trunk.UseTcp = tcp;
            if (request.IsEnabled is { } enabled) trunk.IsEnabled = enabled;

            var passwordChanged = !string.IsNullOrEmpty(request.Password);
            if (passwordChanged)
            {
                trunk.Password = _passwordProtector.Protect(request.Password);
            }

            await _auditLogService.LogAsync(
                null, AuditAction.SettingsChanged, "SipTrunk", trunk.Id.ToString(),
                oldValues: before,
                newValues: $"name={trunk.Name}; server={trunk.Server}:{trunk.Port}; user={trunk.Username}; enabled={trunk.IsEnabled}",
                description: passwordChanged ? "Password replaced" : "Password unchanged",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Updated SIP trunk: {Name}", trunk.Name);
            return true;
        }, cancellationToken);

        await PushToSelfHostedVoiceAsync(id, cancellationToken);
    }

    /// <summary>Sends the edited carrier to every self-hosted voice provider that uses it.</summary>
    // Without this a rotated SIP password is saved here and nowhere else: the voice service keeps
    // registering with the old one, and the panel shows the new one.
    private async Task PushToSelfHostedVoiceAsync(Guid trunkId, CancellationToken cancellationToken)
    {
        var users = await _providerRepo.GetQueryable()
            .Where(p => p.SipTrunkId == trunkId
                        && !p.IsDeleted
                        && p.IsEnabled
                        && p.ProviderType == CalluVoiceProvider.TypeName)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (users.Count == 0)
            return;

        // The registry holds the trunk row it loaded, so it has to see the new one first.
        await _registry.ReloadProvidersAsync(cancellationToken);

        foreach (var providerId in users)
        {
            if (await _registry.GetConfiguredProviderAsync(providerId) is not CalluVoiceProvider voice)
                continue;

            var (applied, error) = await voice.ApplyTrunkAsync();
            if (applied)
                continue;

            _logger.LogError(
                "The SIP trunk was saved but the voice service was not given it: {Error}. "
                + "Calls will use whatever carrier it already had.", error);

            await _auditLogService.LogAsync(
                null, AuditAction.SettingsChanged, "SipTrunk", trunkId.ToString(),
                description: $"Carrier was NOT applied to the voice service: {Clip(error)}",
                cancellationToken: cancellationToken);
        }
    }

    // Asterisk strips whitespace off a config value anyway, and a value of spaces is not a value.
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Leaves room under AuditLog.Description for the sentence this is embedded in.
    private const int MaxCarrierErrorChars = 400;

    private static string Clip(string? error)
    {
        var text = (error ?? "unknown error").Trim();
        return text.Length <= MaxCarrierErrorChars ? text : text[..MaxCarrierErrorChars];
    }

    public async Task DeleteTrunkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
            if (trunk == null)
                throw new KeyNotFoundException($"SIP trunk not found: {id}");

            var usedBy = await _providerRepo.GetQueryable()
                .Where(p => p.SipTrunkId == id && !p.IsDeleted)
                .Select(p => p.Name)
                .ToListAsync(cancellationToken);
            
            if (usedBy.Any())
            {
                throw new InvalidOperationException(
                    $"Cannot delete SIP trunk - used by providers: {string.Join(", ", usedBy)}");
            }
            
            trunk.IsDeleted = true;

            await _auditLogService.LogAsync(
                null, AuditAction.Deleted, "SipTrunk", trunk.Id.ToString(),
                oldValues: $"name={trunk.Name}; server={trunk.Server}:{trunk.Port}",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Deleted SIP trunk: {Name}", trunk.Name);
            return true;
        }, cancellationToken);
    }
    
    public async Task<(bool Success, string Message)> TestTrunkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (trunk == null)
            return (false, "SIP trunk not found");
        
        try
        {
            if (string.IsNullOrEmpty(trunk.Server))
                return (false, "Server address is required");
                
            if (string.IsNullOrEmpty(trunk.Username))
                return (false, "Username is required");
                
            if (trunk.Port <= 0 || trunk.Port > 65535)
                return (false, "Invalid port number");
            
            var protocol = trunk.UseTls ? "sips" : "sip";
            var transport = trunk.UseTcp ? ";transport=tcp" : "";
            var sipUri = $"{protocol}:{trunk.Username}@{trunk.Server}:{trunk.Port}{transport}";

            return (true, $"Configuration is valid (format check only — no live SIP connection was attempted). SIP URI: {sipUri}");
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Failed to validate SIP trunk: {Name}", trunk.Name);
            return (false, $"Validation failed: {ex.Message}");
        }
    }
}
