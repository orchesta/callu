using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;

namespace Callu.Infrastructure.Services;

public sealed class StatusPageSubscriberEmailSender(
    IRepository<StatusPageIncident> incidentRepo,
    IStatusPageRepository statusPageRepo,
    IRepository<StatusPageSubscriber> subscriberRepo,
    IEmailService emailService,
    ILogger<StatusPageSubscriberEmailSender> logger) : IStatusPageSubscriberEmailSender
{
    public async Task SendForIncidentAsync(Guid statusPageIncidentId, CancellationToken cancellationToken = default)
    {
        var incident = await incidentRepo.FindSingleAsync(
            i => i.Id == statusPageIncidentId && !i.IsDeleted, cancellationToken);
        if (incident == null) return;

        var page = await statusPageRepo.FindSingleAsync(
            p => p.Id == incident.StatusPageId && !p.IsDeleted, cancellationToken);
        if (page == null || !page.AllowSubscriptions) return;

        var subscribers = await subscriberRepo.GetQueryable()
            .Where(s => s.StatusPageId == page.Id && !s.IsDeleted && s.IsConfirmed)
            .Select(s => s.Email)
            .ToListAsync(cancellationToken);
        if (subscribers.Count == 0) return;

        var statusLabel = incident.Status switch
        {
            "investigating" => "Investigating",
            "identified"    => "Identified",
            "monitoring"    => "Monitoring",
            "resolved"      => "Resolved",
            _               => incident.Status
        };

        var subject = $"[{page.Name}] {incident.Title} — {statusLabel}";

        var sb = new StringBuilder();
        sb.AppendLine($"<div style=\"font-family:sans-serif;max-width:600px;margin:0 auto;\">");
        sb.AppendLine($"  <h2 style=\"color:#1E293B;\">{page.Name} — Status Update</h2>");
        sb.AppendLine($"  <div style=\"border-left:4px solid #3E7BFA;padding:12px 16px;background:#F8FAFC;border-radius:4px;\">");
        sb.AppendLine($"    <strong style=\"font-size:1.05rem;\">{incident.Title}</strong><br/>");
        sb.AppendLine($"    <span style=\"color:#64748B;font-size:0.9rem;\">Status: {statusLabel}</span>");
        sb.AppendLine($"  </div>");
        sb.AppendLine($"  <p style=\"margin-top:24px;font-size:0.85rem;color:#94A3B8;\">");
        sb.AppendLine($"    You are receiving this email because you subscribed to status updates for <strong>{page.Name}</strong>.");
        sb.AppendLine($"  </p>");
        sb.AppendLine($"</div>");

        var html = sb.ToString();

        var tasks = subscribers.Select(email => emailService.SendAsync(email, subject, html, cancellationToken));
        await Task.WhenAll(tasks);

        logger.LogInformation("[STATUS-PAGE] Notified {Count} subscribers for page {PageId} — incident: {Title}",
            subscribers.Count, page.Id, incident.Title);
    }
}
