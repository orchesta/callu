using System.Text;
using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>Every single-row read in the product, inventoried with a verdict.</summary>
public class OrderedSingleRowReadGuardTests
{
    private enum Verdict
    {
        Ordered,
        PrimaryKey,
        InMemory,
        NeedsReview,
        KnownViolation
    }

    private sealed record Read(string File, string Statement, Verdict Verdict, string Why = "");

    // Statement is the key; the failure message prints a paste-ready replacement for this array.
    private static readonly Read[] Inventory =
    [
        new("AlertRuleService.cs", "var rule = await ruleRepo.GetQueryable() .Include(r => r.Service) .Include(r => r.Team) .FirstOrDefaultAsync(r => r.Id == id, ct)", Verdict.PrimaryKey),
        new("AuditChainService.cs", "state = await db.AuditChainStates .FirstAsync(s => s.Id == AuditChainState.SingletonId, ct)", Verdict.PrimaryKey,
            "The chain state is one row by construction: its primary key is a fixed Guid, so a second row cannot exist."),
        new("AuditChainService.cs", "var oldest = await db.AuditLogs .AsNoTracking() .Where(l => l.Sequence != null) .OrderBy(l => l.Sequence) .FirstOrDefaultAsync(ct)", Verdict.Ordered,
            "Sequence is assigned strictly increasing by the sealer, so ordering by it names exactly one oldest surviving entry."),
        new("AuditChainService.cs", "… } private Task<AuditChainState?> LoadStateAsync(CancellationToken ct) => db.AuditChainStates.FirstOrDefaultAsync(s => s.Id == AuditChainState.SingletonId, ct)", Verdict.PrimaryKey,
            "Same fixed primary key; absent means the chain has not started, which each caller handles."),
        new("AuthService.cs", "… Email(request.Email), httpContextAccessor.HttpContext?.Connection.RemoteIpAddress, httpContextAccessor.HttpContext?.Request.Headers.UserAgent.FirstOrDefault()", Verdict.InMemory),
        new("AuthService.cs", "… or.Email(user.Email), httpContextAccessor.HttpContext?.Connection.RemoteIpAddress, httpContextAccessor.HttpContext?.Request.Headers.UserAgent.FirstOrDefault()", Verdict.InMemory),
        new("AuthService.cs", "var role = roles.FirstOrDefault()", Verdict.InMemory),
        new("AuthService.cs", "var role = roles.FirstOrDefault()", Verdict.InMemory),
        new("CallTokenFactoryRepository.cs", "var callDataJson = await context.CallTokens .AsNoTracking() .Where(t => t.Token == token) .Select(t => t.CallDataJson) .FirstOrDefaultAsync(cancellationToken)", Verdict.NeedsReview),
        new("CallTokenFactoryRepository.cs", "… context.CallTokens .AsNoTracking() .Where(t => t.Token == token) .Select(t => new { t.CallDataJson, t.CreatedAt, t.ExpiresAt }) .FirstAsync(cancellationToken)", Verdict.NeedsReview),
        new("CallTokenFactoryRepository.cs", "… here(t => t.Token == token) .Select(t => new { t.CallDataJson, t.IsConsumed, t.ConsumedAt, t.ExpiresAt, t.CreatedAt }) .FirstOrDefaultAsync(cancellationToken)", Verdict.NeedsReview),
        new("CallTokenFactoryRepository.cs", "… sNoTracking() .Where(t => t.Token == token) .Select(t => new { t.IsConsumed, t.ConsumedAt, t.CreatedAt, t.ExpiresAt }) .FirstOrDefaultAsync(cancellationToken)", Verdict.NeedsReview),
        new("CalluVoiceCallbackPersistence.cs", "var incident = await context.Incidents .FirstOrDefaultAsync(i => i.Id == ticket.IncidentId, cancellationToken)", Verdict.PrimaryKey),
        new("CalluVoiceCallbackPersistence.cs", "… uVoiceCallbackApplication.IncidentNotFound; } var existing = await context.CallLogs .FirstOrDefaultAsync(c => c.CallToken == ticket.CallId, cancellationToken)", Verdict.NeedsReview,
            "The id comes from the attempt's own primary key, so the writer cannot produce two, and the schema now says so: IX_CallLogs_CallToken is unique over live, non-null tokens. The read stays keyed on a column that is not the primary key, so it is listed rather than waved through."),
        new("CapabilityProviderMappingRepository.cs", "return await _dbSet .Where(m => m.Capability == capability && !m.IsDeleted) .OrderBy(m => m.Priority) .ThenBy(m => m.Id) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered,
            "Capability carries no unique index yet, so two live rows are reachable from the unrouted starting state; ordering makes the screen and the registry agree on which one wins until the index lands."),
        new("CommunicationEventDispatcher.cs", "… Type().Name); return; } var lifecycle = lifecycles.FirstOrDefault(l => l.ProviderType.Equals(activeProvider.ProviderType, StringComparison.OrdinalIgnoreCase))", Verdict.InMemory),
        new("CommunicationProviderService.cs", "var route = routes .Where(r => r.Capability == capability && r.IsEnabled) .OrderBy(r => r.Priority).ThenBy(r => r.Id) .FirstOrDefault()", Verdict.InMemory,
            "LINQ over the already-materialised route list, and ordered anyway: writing a route replaces the old rows, but a database edited by hand can still hold two, and the screen must then agree with the registry about which one wins."),
        new("CommunicationProviderService.cs", "var target = route is null ? null : providers.FirstOrDefault(p => p.Id == route.ProviderId)", Verdict.InMemory),
        new("CommunicationProviderService.cs", "… onfig.VoiceServiceKeyFromJson(r.ConfigJson), key, StringComparison.Ordinal)) .OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Id) .FirstOrDefault()", Verdict.InMemory,
            "LINQ over the already-materialised provider list, and ordered anyway: several rows really can name one voice service, and the refusal has to name the same rival every time it is triggered rather than a different one per save."),
        new("CommunicationProviderRepository.cs", "return await _dbSet .Include(p => p.SipTrunk) .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("CommunicationProviderRepository.cs", "…  => await _dbSet .AsNoTracking() .Where(p => p.IsEnabled && !p.IsDeleted) .OrderBy(p => p.Priority) .ThenBy(p => p.Id) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered, "Ascending is correct: Priority is documented lower = higher priority, so OrderByDescending would pick the last resort."),
        new("CommunicationProviderService.cs", "var entity = await _providerRepo.FindSingleAsync(p => p.Id == id, cancellationToken)", Verdict.PrimaryKey),
        new("CommunicationProviderService.cs", "var provider = await _providerRepo.FindSingleAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("CommunicationProviderService.cs", "var provider = await _providerRepo.FindSingleAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("EmailTemplateRepository.cs", "return await _dbSet .FirstOrDefaultAsync(t => t.Key == key && !t.IsDeleted, cancellationToken)", Verdict.NeedsReview),
        new("EmailTemplateService.cs", "var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("EmailTemplateService.cs", "var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("EmailTemplateService.cs", "var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("EmailTemplateService.cs", "var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("EmailTemplateService.cs", "var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("AuditSigningKeyService.cs", "var existing = await db.AuditSigningKeys .AsNoTracking() .Where(k => k.RetiredAt == null) .OrderBy(k => k.CreatedAt) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered),
        new("EscalationOrchestrator.cs", "… ps) .ThenInclude(s => s.Team) .Include(i => i.CurrentEscalationStep) .Include(i => i.Service) .FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)", Verdict.PrimaryKey),
        new("EscalationOrchestrator.cs", "… here(i => i.Id == incident.Id && i.CurrentEscalationStepId != null) .Select(i => (int?)i.CurrentEscalationStep!.Level) .FirstOrDefaultAsync(cancellationToken)", Verdict.PrimaryKey),
        new("EscalationPolicyRepository.cs", "return active.FirstOrDefault()", Verdict.InMemory),
        new("EscalationPolicyRepository.cs", "return await _dbSet .FirstOrDefaultAsync(p => EF.Functions.ILike(p.Name, name), cancellationToken)", Verdict.NeedsReview),
        new("EscalationPolicyRepository.cs", "return await _dbSet .Include(p => p.Steps.OrderBy(s => s.Level)) .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)", Verdict.PrimaryKey),
        new("EscalationService.cs", "var policy = await policyRepo.FindSingleAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("EscalationService.cs", "var policy = await policyRepo.GetQueryable() .Include(p => p.Steps) .Where(p => !p.IsDeleted) .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)", Verdict.PrimaryKey),
        new("EscalationService.cs", "var step = await stepRepo.FindSingleAsync( s => s.Id == stepId && s.EscalationPolicyId == policyId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("EscalationService.cs", "… ) .ThenInclude(s => s.Team) .Include(p => p.Steps) .ThenInclude(s => s.TargetedUsers) .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("EscalationService.cs", "… GetQueryable() .Include(s => s.TargetedUsers) .FirstOrDefaultAsync(s => s.Id == stepId && s.EscalationPolicyId == policyId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("EscalationStepRepository.cs", "return await _dbSet .FirstOrDefaultAsync(s => s.EscalationPolicyId == policyId && s.Level == level, cancellationToken)", Verdict.NeedsReview),
        new("FirebaseSettingsRepository.cs", "… etSettingsAsync(CancellationToken cancellationToken = default) => _dbSet .OrderBy(s => s.CreatedAt) .ThenBy(s => s.Id) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered,
            "One row by construction — a check constraint pins the id to a fixed Guid — and ordered anyway, so a database edited by hand resolves to the same row on every read rather than a different set of credentials per send."),
        new("HealthCheckExecutor.cs", "var component = await componentRepo.FindSingleAsync( c => c.Id == componentId && !c.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("HealthCheckExecutor.cs", "var component = await componentRepo.FindSingleAsync( c => c.Id == componentId && !c.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("HealthCheckExecutor.cs", "var page = await statusPageRepo.FindSingleAsync(p => p.Id == pageId && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("IncidentNoteService.cs", "var note = await noteRepo.FindSingleAsync(n => n.Id == noteId && !n.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("IncidentNoteService.cs", "var note = await noteRepo.FindSingleAsync(n => n.Id == noteId && !n.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("IncidentNoteService.cs", "… eryable() .AsNoTracking() .Where(i => i.Id == incidentId && !i.IsDeleted) .Select(i => new { Found = true, i.TeamId }) .FirstOrDefaultAsync(cancellationToken)", Verdict.PrimaryKey),
        new("IncidentQueryService.cs", "var acknowledgedCount = statusCounts .FirstOrDefault(s => s.Status == IncidentStatus.Acknowledged)", Verdict.InMemory),
        new("IncidentQueryService.cs", "statusCounts .FirstOrDefault(s => s.Status == IncidentStatus.Closed)", Verdict.InMemory),
        new("IncidentQueryService.cs", "var criticalCount = severityCounts .FirstOrDefault(s => s.Severity == IncidentSeverity.Critical)", Verdict.InMemory),
        new("IncidentQueryService.cs", "var resolvedCount = statusCounts .FirstOrDefault(s => s.Status == IncidentStatus.Resolved)", Verdict.InMemory),
        new("IncidentQueryService.cs", "… rtId && i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed) .OrderByDescending(i => i.StartedAt) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered),
        new("NotificationChannelService.cs", "… eryRepo.GetQueryable() .Where(d => d.ChannelId == c.Id) .OrderByDescending(d => d.AttemptedAt) .Select(d => new { d.Status, d.AttemptedAt }) .FirstOrDefault()", Verdict.Ordered, "The card badge wants the newest attempt per channel; ordering by AttemptedAt is what makes \"newest\" mean anything, and the (ChannelId, AttemptedAt) index serves it."),
        new("IncidentRepository.cs", "… erByDescending(n => n.CreatedAt)) .Include(i => i.TimelineEvents.OrderByDescending(t => t.CreatedAt)) .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)", Verdict.PrimaryKey),
        new("IncidentRepository.cs", "… ncellationToken cancellationToken = default) => await _dbSet .AsNoTracking() .Include(i => i.Service) .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)", Verdict.PrimaryKey),
        new("IncidentService.cs", "return await scoped.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)", Verdict.PrimaryKey),
        new("IncidentService.cs", "var incident = await baseQuery .FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)", Verdict.PrimaryKey),
        new("IncidentService.cs", "var incident = await scoped.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)", Verdict.PrimaryKey),
        new("IncidentService.cs", "var incident = await scoped.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)", Verdict.PrimaryKey),
        new("IncidentService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == incident.ServiceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("IncidentService.cs", "… await incidentRepo.GetQueryable() .AsNoTracking() .Where(i => i.Id == dto.Id) .Select(i => i.IsNotificationSuppressed) .FirstOrDefaultAsync(cancellationToken)", Verdict.PrimaryKey),
        new("IncidentService.cs", "… i.Id == incidentId && !i.IsDeleted) .Select(i => new { i.Title, i.Severity, i.ServiceId, i.IsNotificationSuppressed }) .FirstOrDefaultAsync(cancellationToken)", Verdict.PrimaryKey),
        new("IncidentService.cs", "… ationStartedAt, i.LastEscalationStepAt, i.IsEscalationActive, i.EscalationCyclesCompleted, }) .FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)", Verdict.PrimaryKey),
        new("IntegrationRepository.cs", "… QueryFilters() .Include(i => i.WebhookTemplate) .Include(i => i.Service) .FirstOrDefaultAsync(i => i.WebhookToken == token && !i.IsDeleted, cancellationToken)", Verdict.NeedsReview),
        new("IntegrationService.cs", "var entity = await repo.GetQueryable() .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("IntegrationService.cs", "var entity = await repo.GetQueryable() .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("IntegrationService.cs", "… ble() .Include(i => i.Service) .Include(i => i.Team) .Include(i => i.WebhookTemplate) .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("IntegrationService.cs", "… ble() .Include(i => i.Service) .Include(i => i.Team) .Include(i => i.WebhookTemplate) .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, cancellationToken)", Verdict.PrimaryKey,
            "UpdateAsync and BindServiceAsync load the row the same way; both are keyed on the primary key."),
        new("IntegrationService.cs", "… ing() .Include(i => i.Service) .Include(i => i.Team) .Include(i => i.WebhookTemplate) .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("JaegerTracingQueryService.cs", "var root = trace.Spans.FirstOrDefault(span => ParentOf(span) is not { } parent || !ids.Contains(parent))", Verdict.InMemory),
        new("JaegerTracingQueryService.cs", "var root = trace.Spans.FirstOrDefault(span => ParentOf(span) is not { } parent || !ids.Contains(parent)) ?? trace.Spans.OrderBy(span => span.StartTime).First()", Verdict.Ordered),
        new("JaegerTracingQueryService.cs", "var trace = (payload?.Data ?? []).FirstOrDefault()", Verdict.InMemory),
        new("JaegerTracingQueryService.cs", "… private static string? ParentOf(JaegerSpan span) => span.References?.FirstOrDefault(reference => string.Equals(reference.RefType, , StringComparison.Ordinal))", Verdict.InMemory),
        new("LocalizationService.cs", "return _cultures.FirstOrDefault(c => c.Name.Equals(cultureName, StringComparison.OrdinalIgnoreCase))", Verdict.InMemory),
        new("LocalizationService.cs", "var zone = _zones.FirstOrDefault(z => z.Id.Equals(timezoneId, StringComparison.OrdinalIgnoreCase))", Verdict.InMemory),
        new("MaintenanceWindowService.cs", "var matchingWindow = activeWindows.FirstOrDefault(m => { if (m.AppliesToAllServices) return true; return ParseAffectedServiceIds(m).Contains(serviceId); })", Verdict.InMemory),
        new("Messages.cs", "… (FallbackLanguage, out var english) ? english : loaded.OrderBy(locale => locale.Key, StringComparer.Ordinal) .Select(locale => locale.Value) .FirstOrDefault()", Verdict.InMemory),
        new("InboxMessageProcessor.cs", "… es, string messageType) => services.GetServices<ICalluMessageHandler>() .FirstOrDefault(h => string.Equals(h.WireName, messageType, StringComparison.Ordinal))", Verdict.InMemory),
        new("InboxRetrySweep.cs", "… es, string messageType) => services.GetServices<ICalluMessageHandler>() .FirstOrDefault(h => string.Equals(h.WireName, messageType, StringComparison.Ordinal))", Verdict.InMemory),
        new("InboxMessageProcessor.cs", "var existing = await db.InboxEntries.FirstOrDefaultAsync(e => e.Id == messageId, cancellationToken)", Verdict.PrimaryKey),
        new("InboxRetrySweep.cs", "var row = await db.InboxEntries.FirstOrDefaultAsync(e => e.Id == claimed.Id, cancellationToken)", Verdict.PrimaryKey),
        new("InboxRetrySweep.cs", "var row = await db.InboxEntries.FirstOrDefaultAsync(e => e.Id == messageId, cancellationToken)", Verdict.PrimaryKey),
        new("OutboxDispatcher.cs", "var row = await db.OutboxEntries.FirstOrDefaultAsync(e => e.Id == rowId, cancellationToken)", Verdict.PrimaryKey),
        new("NotificationDispatcher.cs", "return await _dbContext.Notifications .IgnoreQueryFilters() .AsNoTracking() .FirstOrDefaultAsync(n => n.DedupeKey == attempted.DedupeKey, cancellationToken)", Verdict.NeedsReview),
        new("NotificationDispatcher.cs", "… ities.Notification>() .Where(e => !ReferenceEquals(e.Entity, attempted) && e.Entity.DedupeKey == attempted.DedupeKey) .Select(e => e.Entity) .FirstOrDefault()", Verdict.InMemory),
        new("NotificationPreferenceRepository.cs", "return await _dbSet .Where(np => np.UserId == userId) .OrderBy(np => np.CreatedAt) .ThenBy(np => np.Id) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered, "Ordered, not keyed: UserId has no unique index, so duplicates remain possible and the read is consistent rather than arbitrary. Adding the index needs expand -> dedupe -> constrain across releases."),
        new("NotificationRepository.cs", "var notification = await _dbSet .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken)", Verdict.PrimaryKey),
        new("OnCallOverrideRepository.cs", "… > o.ScheduleId == scheduleId && o.IsActive && o.StartUtc <= now && o.EndUtc > now) .OrderByDescending(o => o.StartUtc) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered),
        new("OnCallOverrideService.cs", "var overrideEntity = await overrideRepo.FindSingleAsync(o => o.Id == overrideId && !o.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("OnCallOverrideService.cs", "var overrideEntity = await overrideRepo.FindSingleAsync(o => o.Id == overrideId && !o.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("OnCallOverrideService.cs", "…  == scheduleId && !o.IsDeleted && o.IsActive && o.StartUtc <= at && o.EndUtc > at) .OrderByDescending(o => o.StartUtc) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered),
        new("OnCallOverrideService.cs", "… = await overrideRepo.GetQueryable() .AsNoTracking() .Include(o => o.Schedule) .FirstOrDefaultAsync(o => o.Id == overrideId && !o.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("OnCallOverrideService.cs", "… eduleRepo.GetQueryable() .AsNoTracking() .Where(s => s.Id == scheduleId && !s.IsDeleted) .Select(s => (Guid?)s.TeamId) .FirstOrDefaultAsync(cancellationToken)", Verdict.PrimaryKey),
        new("OnCallService.cs", "var backup = new[] { primary, secondary } .FirstOrDefault(o => o is not null && o.UserId != activeOverride!.OverrideUserId)", Verdict.InMemory),
        new("OnCallService.cs", "var primary = currentOccurrences.FirstOrDefault(o => o.IsPrimary)", Verdict.InMemory),
        new("OnCallService.cs", "var primary = currentOccurrences.FirstOrDefault(o => o.IsPrimary) ?? currentOccurrences.FirstOrDefault()", Verdict.InMemory),
        new("OnCallService.cs", "var schedule = await scheduleRepo.GetQueryable() .AsNoTracking() .FirstOrDefaultAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("OnCallService.cs", "var secondary = currentOccurrences.FirstOrDefault(o => o != primary)", Verdict.InMemory),
        new("OnCallService.cs", "… = scheduleId && !o.IsDeleted && o.IsActive && o.StartUtc <= now && o.EndUtc > now) .OrderByDescending(o => o.StartUtc) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered),
        new("OnCallService.cs", "… o.ScheduleId == scheduleId && !o.IsDeleted && o.StartUtc > now && roster.Contains(o.UserId)) .OrderBy(o => o.StartUtc) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered),
        new("Program.cs", "var detail = report.Entries.Values.FirstOrDefault()", Verdict.InMemory),
        new("PhoneChannelDispatcher.cs", "… idents.GetQueryable() .AsNoTracking() .Where(i => i.Id == incidentId) .Select(i => new { i.Status, i.AcknowledgedAt }) .FirstOrDefaultAsync(cancellationToken)", Verdict.PrimaryKey),
        new("PostmortemService.cs", "var item = await repo.GetQueryable() .Include(p => p.Incident) .FirstOrDefaultAsync(p => p.Id == id, ct)", Verdict.PrimaryKey),
        new("RefreshTokenRepository.cs", "… shAsync(string tokenHash, CancellationToken cancellationToken = default) => await _dbSet.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken)", Verdict.NeedsReview),
        new("Repository.cs", "return await _dbSet.FirstOrDefaultAsync(predicate, cancellationToken)", Verdict.NeedsReview),
        new("RotationService.cs", "var schedule = await scheduleRepo.FindSingleAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("RotationService.cs", "… nRepo.GetQueryable() .Include(r => r.Schedule) .FirstOrDefaultAsync(r => r.Id == rotationId && r.Schedule != null && !r.Schedule.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("RotationService.cs", "… nRepo.GetQueryable() .Include(r => r.Schedule) .FirstOrDefaultAsync(r => r.Id == rotationId && r.Schedule != null && !r.Schedule.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("RunbookService.cs", "var item = await repo.GetQueryable() .Include(r => r.Service) .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct)", Verdict.PrimaryKey),
        new("ScheduleMaterializer.cs", "var schedule = await scheduleRepo.GetQueryable() .AsNoTracking() .FirstOrDefaultAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("ScheduleRepository.cs", "return await _dbSet .FirstOrDefaultAsync(s => EF.Functions.ILike(s.Name, name), cancellationToken)", Verdict.NeedsReview),
        new("ScheduleRepository.cs", "return await _dbSet .Include(s => s.Rotations.OrderBy(r => r.Order).ThenBy(r => r.HandoverStartLocal)) .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)", Verdict.PrimaryKey),
        new("ScheduleRepository.cs", "… > o.ScheduleId == scheduleId && o.IsActive && o.StartUtc <= now && o.EndUtc > now) .OrderByDescending(o => o.StartUtc) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered),
        new("ScheduleRepository.cs", "… ScheduleId == scheduleId && !o.IsDeleted && o.IsPrimary && o.StartUtc <= now && o.EndUtc > now) .OrderBy(o => o.Order) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered),
        new("ScheduleRotationRepository.cs", "return await _dbSet .FirstOrDefaultAsync(r => r.Id == occurrence.RotationId, cancellationToken)", Verdict.PrimaryKey),
        new("ScheduleRotationRepository.cs", "… ScheduleId == scheduleId && !o.IsDeleted && o.IsPrimary && o.StartUtc <= now && o.EndUtc > now) .OrderBy(o => o.Order) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered),
        new("ScheduleService.cs", "var existing = stored.FirstOrDefault(r => r.Id == rotationId)", Verdict.PrimaryKey),
        new("ScheduleService.cs", "var schedule = await scheduleRepo.FindSingleAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("ScheduleService.cs", "var schedule = await scheduleRepo.FindSingleAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("ScheduleService.cs", "… dule = await scheduleRepo.GetQueryable() .Include(s => s.Rotations) .Where(s => !s.IsDeleted) .FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken)", Verdict.PrimaryKey),
        new("ScheduleService.cs", "… tQueryable() .AsNoTracking() .Include(s => s.Team) .Include(s => s.Rotations) .FirstOrDefaultAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("ServiceManagementService.cs", "var dep = await depRepo.FindSingleAsync(d => d.Id == dependencyId && !d.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("ServiceManagementService.cs", "var uptime = (await uptimeCalculator.ComputeAsync(to.AddDays(-30), to, cancellationToken)) .FirstOrDefault(r => r.ServiceId == id)", Verdict.InMemory),
        new("ServiceManagementService.cs", "var match = results.FirstOrDefault(r => r.ServiceId == serviceId)", Verdict.InMemory),
        new("ServiceManagementService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == id && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("ServiceManagementService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == id && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("ServiceManagementService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == id && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("ServiceManagementService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("ServiceManagementService.cs", "var service = await serviceRepo.GetQueryable() .Include(s => s.Team) .AsNoTracking() .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("ServiceRepository.cs", "return await _dbSet .FirstOrDefaultAsync(s => EF.Functions.ILike(s.Name, name), cancellationToken)", Verdict.NeedsReview),
        new("ServiceRepository.cs", "… _context.Services .IgnoreQueryFilters() .Include(s => s.WebhookTemplate) .FirstOrDefaultAsync(s => s.WebhookToken == token && !s.IsDeleted, cancellationToken)", Verdict.NeedsReview),
        new("ServiceStatusCascadeEngine.cs", "var target = targets.FirstOrDefault(s => s.Id == item.ServiceId)", Verdict.PrimaryKey),
        new("SetupController.cs", "UserName = request.Email, Email = request.Email, DisplayName = request.Name ?? , FirstName = request.Name?.Split( ).FirstOrDefault()", Verdict.InMemory),
        new("SetupController.cs", "var settings = await db.OrganizationSettings .FirstOrDefaultAsync(s => s.Id == Callu.Domain.Entities.OrganizationSettings.SingletonId, ct)", Verdict.PrimaryKey),
        new("SetupController.cs", "… est.Email, DisplayName = request.Name ?? , FirstName = request.Name?.Split( ).FirstOrDefault() ?? , LastName = request.Name?.Split( ).Skip(1).FirstOrDefault()", Verdict.InMemory),
        new("SipTrunkService.cs", "var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("SipTrunkService.cs", "var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("SipTrunkService.cs", "var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("SipTrunkService.cs", "var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("SipTrunkSettingsRepository.cs", "… cancellationToken = default) => await _context.SipTrunkSettings .IgnoreQueryFilters() .AsNoTracking() .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)", Verdict.PrimaryKey),
        new("SmtpSettingsRepository.cs", "return await _dbSet .OrderBy(s => s.CreatedAt) .ThenBy(s => s.Id) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered, "Ordered, plus inserting with SmtpSettings.SingletonId. No CHECK constraint pins it: an install that already holds two rows would fail to validate one and crash-loop on startup."),
        new("StatusPageComponentService.cs", "var component = await componentRepo.FindSingleAsync(c => c.Id == componentId && !c.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageComponentService.cs", "var component = await componentRepo.FindSingleAsync(c => c.Id == componentId && !c.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageComponentService.cs", "var linked = await serviceRepo.FindSingleAsync( s => s.Id == linkedServiceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageComponentService.cs", "var page = await statusPageRepo.FindSingleAsync(p => p.Id == pageId && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageRepository.cs", "return await _dbSet .FirstOrDefaultAsync(p => p.Slug == slug && !p.IsDeleted, cancellationToken)", Verdict.NeedsReview),
        new("StatusPageRepository.cs", "return await _dbSet .IgnoreQueryFilters() .Where(p => p.Slug == slug && !p.IsDeleted && p.IsPublic) .FirstOrDefaultAsync(cancellationToken)", Verdict.NeedsReview),
        new("StatusPageRepository.cs", "… => i.CreatedAt)) .ThenInclude(i => i.Updates.OrderByDescending(u => u.CreatedAt)) .FirstOrDefaultAsync(p => p.Slug == slug && !p.IsDeleted, cancellationToken)", Verdict.NeedsReview),
        new("StatusPageRepository.cs", "… g(i => i.CreatedAt)) .ThenInclude(i => i.Updates.OrderByDescending(u => u.CreatedAt)) .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageRepository.cs", "… nInclude(i => i.Updates.OrderByDescending(u => u.CreatedAt)) .Where(p => p.Slug == slug && !p.IsDeleted && p.IsPublic) .FirstOrDefaultAsync(cancellationToken)", Verdict.NeedsReview),
        new("StatusPageService.cs", "DateTime? resolvedAt = incident.Updates .Where(u => u.Status == ) .OrderBy(u => u.CreatedAt) .Select(u => u.CreatedAt) .Cast<DateTime?>() .FirstOrDefault()", Verdict.Ordered),
        new("StatusPageService.cs", "var existing = await subscriberRepo.FindSingleAsync( s => s.StatusPageId == pageId && s.Email == email && !s.IsDeleted, cancellationToken)", Verdict.NeedsReview),
        new("StatusPageService.cs", "var incident = await incidentRepo.FindSingleAsync(i => i.Id == incidentId && !i.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageService.cs", "var incident = await incidentRepo.FindSingleAsync(i => i.Id == incidentId && !i.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageService.cs", "var page = await statusPageRepo.FindSingleAsync( p => p.Id == pageId && !p.IsDeleted && p.IsPublic && p.AllowSubscriptions, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageService.cs", "var page = await statusPageRepo.FindSingleAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageService.cs", "var page = await statusPageRepo.FindSingleAsync(p => p.Id == incident.StatusPageId && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageService.cs", "var page = await statusPageRepo.FindSingleAsync(p => p.Id == pageId && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageService.cs", "var subscriber = await subscriberRepo.FindSingleAsync( s => s.ConfirmationTokenHash == hash && !s.IsConfirmed && !s.IsDeleted, cancellationToken)", Verdict.NeedsReview),
        new("StatusPageService.cs", "var subscriber = await subscriberRepo.FindSingleAsync( s => s.StatusPageId == pageId && s.Email == email && !s.IsDeleted, cancellationToken)", Verdict.NeedsReview),
        new("StatusPageService.cs", "var subscriber = await subscriberRepo.FindSingleAsync( s => s.UnsubscribeTokenHash == hash && !s.IsDeleted, cancellationToken)", Verdict.NeedsReview),
        new("StatusPageService.cs", "… (p => p.Components) .Include(p => p.Incidents) .Include(p => p.Subscribers) .Where(p => !p.IsDeleted) .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageSubscriberEmailSender.cs", "var incident = await incidentRepo.FindSingleAsync( i => i.Id == statusPageIncidentId && !i.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("StatusPageSubscriberEmailSender.cs", "var page = await statusPageRepo.FindSingleAsync( p => p.Id == incident.StatusPageId && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("TeamMemberRepository.cs", "return await _dbSet .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, cancellationToken)", Verdict.NeedsReview),
        new("TeamMemberRepository.cs", "return await _dbSet .IgnoreQueryFilters() .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, cancellationToken)", Verdict.NeedsReview),
        new("TeamRepository.cs", "return await _dbSet .FirstOrDefaultAsync(t => EF.Functions.ILike(t.Name, name), cancellationToken)", Verdict.NeedsReview),
        new("TeamRepository.cs", "return await _dbSet .Include(t => t.Members) .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)", Verdict.PrimaryKey),
        new("TeamService.cs", "var member = await memberRepo.FindSingleAsync(m => m.Id == memberId && m.TeamId == teamId, cancellationToken)", Verdict.PrimaryKey),
        new("TeamService.cs", "var member = await memberRepo.FindSingleAsync(m => m.Id == memberId && m.TeamId == teamId, cancellationToken)", Verdict.PrimaryKey),
        new("TeamService.cs", "var team = await teamRepo.FindSingleAsync(t => t.Id == teamId && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("TeamService.cs", "var team = await teamRepo.FindSingleAsync(t => t.Id == teamId && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("TeamService.cs", "…  await teamRepo.GetQueryable() .Include(t => t.Members) .Include(t => t.Services) .FirstOrDefaultAsync(t => t.Id == teamId && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("TeamService.cs", "… mRepo.GetQueryable() .Include(t => t.Members) .Include(t => t.Services) .Where(t => !t.IsDeleted) .FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken)", Verdict.PrimaryKey),
        new("TenantUserReadRepository.cs", "g => g.Key, g => g.First()", Verdict.InMemory),
        new("TtsTemplateService.cs", "template = await templateRepo.FindSingleAsync( t => t.IsDefault && !t.IsDeleted, ct)", Verdict.NeedsReview),
        new("TtsTemplateService.cs", "var existing = await templateRepo.FindSingleAsync( t => t.LanguageCode == request.LanguageCode && !t.IsDeleted, ct)", Verdict.NeedsReview),
        new("TtsTemplateService.cs", "var template = await templateRepo.FindSingleAsync( t => t.LanguageCode == languageCode && !t.IsDeleted, ct)", Verdict.NeedsReview),
        new("TtsTemplateService.cs", "var template = await templateRepo.FindSingleAsync( t => t.LanguageCode == languageCode && !t.IsDeleted, ct)", Verdict.NeedsReview),
        new("TtsTemplateService.cs", "var template = await templateRepo.FindSingleAsync( t => t.LanguageCode == languageCode && !t.IsDeleted, ct)", Verdict.NeedsReview),
        new("UserContactRepository.cs", "… oken = default) => await context.Users .AsNoTracking() .Where(u => u.Id == userId && !u.IsDeleted) .Select(ToSnapshot) .FirstOrDefaultAsync(cancellationToken)", Verdict.PrimaryKey),
        new("UserManagementService.cs", "UserName = email, Email = email, DisplayName = displayName, FirstName = displayName.Split( ).FirstOrDefault()", Verdict.InMemory),
        new("UserManagementService.cs", "UserName = email, Email = email, DisplayName = email.Split( ).First()", Verdict.InMemory),
        new("UserManagementService.cs", "var role = roles.FirstOrDefault()", Verdict.InMemory),
        new("UserManagementService.cs", "var userName = user.DisplayName ?? email.Split( ).First()", Verdict.InMemory),
        new("UserManagementService.cs", "var userName = user.DisplayName ?? user.Email!.Split( ).First()", Verdict.InMemory),
        new("UserManagementService.cs", "… = email, DisplayName = displayName, FirstName = displayName.Split( ).FirstOrDefault() ?? displayName, LastName = displayName.Split( ).Skip(1).FirstOrDefault()", Verdict.InMemory),
        new("UserPushDeviceRepository.cs", "… tByTokenAsync(string pushToken, CancellationToken cancellationToken = default) => _dbSet.FirstOrDefaultAsync(d => d.PushToken == pushToken, cancellationToken)", Verdict.NeedsReview,
            "UserPushDevices.PushToken carries a unique index filtered on IsDeleted = false, which is the same filter the read runs under, so two live rows cannot share a token — but the uniqueness comes from an index rather than the primary key, so it stays NeedsReview until the index is re-read."),
        new("VideoConferenceService.cs", "var existing = await roomRepo.FindSingleAsync( r => r.IncidentId == incidentId && r.Status == ConferenceRoomStatus.Active, ct)", Verdict.NeedsReview),
        new("VideoConferenceService.cs", "var incident = await incidentRepo.GetQueryable() .Include(i => i.Team) .FirstOrDefaultAsync(i => i.Id == incidentId, ct)", Verdict.PrimaryKey),
        new("VideoConferenceService.cs", "var participant = await participantRepo.GetQueryable() .FirstOrDefaultAsync(p => p.ParticipantToken == participantToken, ct)", Verdict.NeedsReview),
        new("VideoConferenceService.cs", "var participant = await participantRepo.GetQueryable() .Include(p => p.ConferenceRoom) .FirstOrDefaultAsync(p => p.ParticipantToken == participantToken, ct)", Verdict.NeedsReview),
        new("VideoConferenceService.cs", "var room = await roomRepo.GetQueryable() .Include(r => r.Participants) .FirstOrDefaultAsync(r => r.Id == roomId, ct)", Verdict.PrimaryKey),
        new("VideoConferenceService.cs", "var userParticipant = room.Participants.FirstOrDefault(p => p.UserId == userId)", Verdict.InMemory),
        new("VideoConferenceService.cs", "… , (ConferenceRoom?)null); } var existingRoom = await roomRepo.FindSingleAsync( r => r.IncidentId == incidentId && r.Status == ConferenceRoomStatus.Active, ct)", Verdict.NeedsReview),
        new("VideoConferenceService.cs", "… ait roomRepo.GetQueryable() .Include(r => r.Participants) .FirstOrDefaultAsync(r => r.IncidentId == incidentId && r.Status == ConferenceRoomStatus.Active, ct)", Verdict.NeedsReview),
        new("VideoConferenceService.cs", "… ait roomRepo.GetQueryable() .Include(r => r.Participants) .FirstOrDefaultAsync(r => r.IncidentId == incidentId && r.Status == ConferenceRoomStatus.Active, ct)", Verdict.NeedsReview),
        new("VideoConferenceService.cs", "… icipantRepo.GetQueryable() .Include(p => p.ConferenceRoom) .ThenInclude(r => r.Incident) .FirstOrDefaultAsync(p => p.ParticipantToken == participantToken, ct)", Verdict.NeedsReview),
        new("VoiceCallCoalescingGuard.cs", "… n => n.DeliveryStatus == NotificationDeliveryStatus.Delivered ? n.SentAt : n.LastAttemptAt) .OrderByDescending(t => t) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered),
        new("VoiceCallRetryQuartzJob.cs", "… dentId, CancellationToken ct) => db.Incidents .AsNoTracking() .Where(i => i.Id == incidentId) .Select(i => (IncidentStatus?)i.Status) .FirstOrDefaultAsync(ct)", Verdict.PrimaryKey),
        new("VoximplantCallReadPersistence.cs", "var i = await context.Incidents .AsNoTracking() .FirstOrDefaultAsync(x => x.Id == incidentId && !x.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("VoximplantController.cs", "private ICommunicationProviderLifecycle GetVoximplantLifecycle() => lifecycles.First(l => l.ProviderType.Equals( , StringComparison.OrdinalIgnoreCase))", Verdict.InMemory),
        new("VoximplantManagementService.cs", "return response.Result?.FirstOrDefault()", Verdict.InMemory),
        new("VoximplantManagementService.cs", "var session = response.Result?.FirstOrDefault()", Verdict.InMemory),
        new("VoximplantManagementService.cs", "var provider = await providerRepo.FindSingleAsync(p => p.Id == providerId && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("VoximplantProviderLifecycle.cs", "var calluApp = apps.Result?.FirstOrDefault(a => a.ApplicationName.StartsWith(CalluAppName, StringComparison.OrdinalIgnoreCase))", Verdict.InMemory),
        new("VoximplantProviderLifecycle.cs", "var provider = await context.CommunicationProviders .AsNoTracking() .FirstOrDefaultAsync(p => p.Id == providerId && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("VoximplantProviderLifecycle.cs", "var provider = await context.CommunicationProviders .FirstOrDefaultAsync(p => p.Id == providerId && !p.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("VoximplantProviderLifecycle.cs", "var resourceName = assembly.GetManifestResourceNames() .FirstOrDefault(n => n.EndsWith( , StringComparison.OrdinalIgnoreCase))", Verdict.InMemory),
        new("VoximplantProviderLifecycle.cs", "var rule = rules?.FirstOrDefault(r => r.RuleName.Equals(name, StringComparison.OrdinalIgnoreCase))", Verdict.InMemory),
        new("VoximplantProviderLifecycle.cs", "var scenario = scenarios?.FirstOrDefault(s => s.ScenarioName.Equals(name, StringComparison.OrdinalIgnoreCase))", Verdict.InMemory),
        new("VoximplantProviderLifecycle.cs", "var script = response.Result? .FirstOrDefault(s => s.ScenarioId == provConfig.IncidentCallScenarioId)", Verdict.InMemory),
        new("VoximplantProviderLifecycle.cs", "var systemUser = users.Result?.FirstOrDefault(u => u.UserName.Equals(SystemUserName, StringComparison.OrdinalIgnoreCase))", Verdict.InMemory),
        new("VoximplantProviderLifecycle.cs", "var voxUser = users.Result?.FirstOrDefault(u => u.UserName.Equals(username, StringComparison.OrdinalIgnoreCase))", Verdict.InMemory),
        new("VoximplantVoiceCallbackPersistence.cs", "var existingSessionLog = await context.CallLogs .FirstOrDefaultAsync( c => c.CallToken == sessionId, cancellationToken)", Verdict.NeedsReview),
        new("VoximplantVoiceCallbackPersistence.cs", "var room = await context.ConferenceRooms .AsNoTracking() .FirstOrDefaultAsync(r => r.VoximplantConferenceId == callback.ConferenceId, cancellationToken)", Verdict.NeedsReview),
        new("VoximplantVoiceCallbackPersistence.cs", "… id.Empty) { logger.LogWarning( ); return pagedNobody; } var incident = await context.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)", Verdict.PrimaryKey),
        // The phone lookup that used to be here is no longer a single-row read: a phone number is
        // not unique, and the acknowledgement it attributes is now recorded in the audit trail, so
        // it reads two rows and declines to name an actor when both match.
        new("VoximplantVoiceCallbackPersistence.cs", "… okens .AsNoTracking() .Where(t => t.CallDataJson.Contains(incidentId.ToString())) .OrderByDescending(t => t.CreatedAt) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered),
        new("WebhookCaptureService.cs", "var capture = await captureRepo.FindSingleAsync(c => c.Id == captureId && !c.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookCaptureService.cs", "var capture = await captureRepo.FindSingleAsync(c => c.Id == captureId && !c.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookCaptureService.cs", "var capture = await captureRepo.FindSingleAsync(c => c.Id == captureId && !c.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookCaptureService.cs", "var capture = await captureRepo.FindSingleAsync(c => c.Id == captureId && !c.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookConfigService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookConfigService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookConfigService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookConfigService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookConfigService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookConfigService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookConfigService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookConfigService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookConfigService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookConfigService.cs", "var service = await serviceRepo.GetQueryable() .Include(s => s.WebhookTemplate) .FirstOrDefaultAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookProcessingService.cs", "… entStatus.Resolved && i.Status != IncidentStatus.Closed && !i.IsDeleted) .OrderBy(i => i.StartedAt) .ThenBy(i => i.Id) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered,
            "The fuzzy-title window can hold several live incidents; the oldest is the one the rest duplicate, and which one adopts the newest external id has to be repeatable across deliveries."),
        new("WebhookProcessingService.cs", "… dentStatus.Resolved && i.Status != IncidentStatus.Closed && !i.IsDeleted) .OrderBy(i => i.StartedAt).ThenBy(i => i.Id) .FirstOrDefaultAsync(cancellationToken)", Verdict.Ordered,
            "Creation rejects a duplicate external id with a read-then-write, so two active incidents can share one; the external system resolving \"the\" incident has to reach the oldest, not an arbitrary row."),
        new("WebhookTemplateRepository.cs", "return await _dbSet .FirstOrDefaultAsync(t => EF.Functions.ILike(t.Name, name) && !t.IsDeleted, cancellationToken)", Verdict.NeedsReview),
        new("WebhookTemplateService.cs", "var capture = await captureRepo.FindSingleAsync(c => c.Id == captureId && !c.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookTemplateService.cs", "var integration = await integrationRepo.FindSingleAsync( i => i.Id == integrationId && !i.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookTemplateService.cs", "var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookTemplateService.cs", "var template = await templateRepo.FindSingleAsync(t => t.Id == templateId && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookTemplateService.cs", "var template = await templateRepo.FindSingleAsync(t => t.Id == templateId && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookTemplateService.cs", "var template = await templateRepo.FindSingleAsync(t => t.Id == templateId && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhookTemplateService.cs", "var template = await templateRepo.FindSingleAsync(t => t.Id == templateId && !t.IsDeleted, cancellationToken)", Verdict.PrimaryKey),
        new("WebhooksController.cs", "h => h.Key, h => h.Value.FirstOrDefault()", Verdict.InMemory),
        new("WebhooksController.cs", "… string? FirstNonEmptyHeaderValue(string name) => Request.Headers.TryGetValue(name, out var values) ? values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))", Verdict.InMemory),
    ];

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // The scan.

    // FindSingleAsync is in the list because it is this codebase's own name for the operation.
    private static readonly string[] SingleRowReads =
    [
        "FirstOrDefaultAsync", "FirstAsync", "SingleOrDefaultAsync", "SingleAsync",
        "FirstOrDefault", "First", "SingleOrDefault", "Single",
        "FindSingleAsync"
    ];

    private static readonly Regex ReadCall = new(
        @"\.\s*(?<op>" + string.Join("|", SingleRowReads) + @")\s*(?:<[^;()<>]*>)?\s*\(",
        RegexOptions.Compiled);

    private const int KeyLength = 160;

    private const string Elided = "… ";

    private static IEnumerable<(string File, int Line, string Statement)> FoundReads()
    {
        foreach (var path in SourceScanner.ProductFiles(includeMigrations: false).OrderBy(p => p, StringComparer.Ordinal))
        {
            var code = SourceScanner.Code(path);
            var file = Path.GetFileName(path);

            foreach (Match match in ReadCall.Matches(code))
                yield return (file, LineOf(code, match.Index), Statement(code, match.Index));
        }
    }

    private static int LineOf(string code, int index) =>
        code.Take(index).Count(c => c == '\n') + 1;

    // Tail-first, so the read and the operators in front of it (Where, OrderBy) stay in the key.
    private static string Statement(string code, int callIndex)
    {
        var start = StatementStart(code, callIndex);

        var end = code.IndexOf('(', callIndex);
        var depth = 0;
        for (; end < code.Length; end++)
        {
            if (code[end] == '(') depth++;
            else if (code[end] == ')' && --depth == 0) { end++; break; }
        }

        var raw = code[start..Math.Min(end, code.Length)];
        var normalised = Regex.Replace(raw, @"\s+", " ").Trim();

        return normalised.Length <= KeyLength
            ? normalised
            : Elided + normalised[^(KeyLength - Elided.Length)..];
    }

    // Skips balanced bracket groups, so an initializer or lambda inside a chain is not the start.
    private static int StatementStart(string code, int callIndex)
    {
        var depth = 0;

        for (var i = callIndex - 1; i >= 0; i--)
        {
            switch (code[i])
            {
                case ')' or '}' or ']':
                    depth++;
                    break;

                case '(' or '{' or '[':
                    if (depth == 0) return i + 1;
                    depth--;
                    break;

                case ';':
                    if (depth == 0) return i + 1;
                    break;
            }
        }

        return 0;
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // THE GUARD.

    [Fact]
    public void EverySingleRowRead_IsAccountedForInTheInventory()
    {
        var found = FoundReads().ToList();

        var listed = Inventory
            .GroupBy(r => (r.File, r.Statement))
            .ToDictionary(g => g.Key, g => g.Count());

        var actual = found
            .GroupBy(r => (r.File, r.Statement))
            .ToDictionary(g => g.Key, g => g.Count());

        var unlisted = found
            .Where(r => !listed.ContainsKey((r.File, r.Statement)))
            .Select(r => $"{r.File}:{r.Line} — {r.Statement}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var vanished = listed.Keys
            .Where(k => !actual.ContainsKey(k))
            .Select(k => $"{k.File} — {k.Statement}")
            .Order(StringComparer.Ordinal)
            .ToList();

        var miscounted = actual
            .Where(kv => listed.TryGetValue(kv.Key, out var n) && n != kv.Value)
            .Select(kv => $"{kv.Key.File} — {kv.Key.Statement} (found {kv.Value}, listed {listed[kv.Key]})")
            .Order(StringComparer.Ordinal)
            .ToList();

        if (unlisted.Count == 0 && vanished.Count == 0 && miscounted.Count == 0)
            return;

        var message = new StringBuilder();

        if (unlisted.Count > 0)
            message.AppendLine(
                $"{unlisted.Count} single-row read(s) in the product are not in this file's inventory:\n  "
                + string.Join("\n  ", unlisted)
                + "\n\nEvery single-row read has to say which case it is. Pick a verdict and add it:\n"
                + "  Ordered        — the query orders before taking one row (must contain an OrderBy)\n"
                + "  PrimaryKey     — keyed on Id, so at most one row can match (must contain `Id ==`)\n"
                + "  InMemory       — LINQ over an already-materialised list; not a database read\n"
                + "  NeedsReview    — keyed on something else, and whether the schema makes it unique has\n"
                + "                   not been checked. Honest, and the next thing to look at.\n"
                + "  KnownViolation — it really can match several rows and nothing orders (pin it below)\n\n"
                + "PrimaryKey means the PRIMARY KEY, not a column that looks unique. The read on "
                + "NotificationPreferences.UserId has no index at all: it looks keyed, it is not, and the "
                + "paging path then reads an arbitrary row.\n");

        if (vanished.Count > 0)
            message.AppendLine(
                "These inventory entries no longer match anything in the product — the query was "
                + "rewritten or removed, and a stale entry is a verdict nobody has re-checked:\n  "
                + string.Join("\n  ", vanished) + "\n");

        if (miscounted.Count > 0)
            message.AppendLine(
                "These statements appear a different number of times than the inventory says:\n  "
                + string.Join("\n  ", miscounted) + "\n");

        message.AppendLine("Paste-ready inventory for the current tree (verdicts still need setting):\n");
        message.AppendLine(PasteReady(found));

        Assert.Fail(message.ToString());
    }

    [Fact]
    public void EveryOrderedVerdict_ReallyOrders()
    {
        var lying = Inventory
            .Where(r => r.Verdict == Verdict.Ordered)
            .Where(r => !r.Statement.Contains("OrderBy", StringComparison.Ordinal))
            .Select(r => $"{r.File} — {r.Statement}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(lying.Count == 0,
            "These reads are filed as Ordered and the statement contains no OrderBy: "
            + string.Join("; ", lying)
            + ". Either the ordering was removed — in which case the read is now arbitrary and the "
            + "verdict has to change — or the verdict was wrong when it was written.");
    }

    [Fact]
    public void EveryPrimaryKeyVerdict_ReallyKeysOnId()
    {
        var lying = Inventory
            .Where(r => r.Verdict == Verdict.PrimaryKey)
            .Where(r => !Regex.IsMatch(r.Statement, @"\bId\s*=="))
            .Select(r => $"{r.File} — {r.Statement}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(lying.Count == 0,
            "These reads are filed as keyed by the primary key and the statement has no `Id ==` in it: "
            + string.Join("; ", lying)
            + ".\n\nA foreign key is not a primary key and a column that reads like a singleton is not "
            + "one either — NotificationPreferences.UserId has no index at all. If the "
            + "uniqueness comes from a unique INDEX rather than the primary key, the verdict is "
            + "NeedsReview until somebody has read the index definition and said so.");
    }

    [Fact]
    public void EveryKnownViolationVerdict_NamesAPinnedDefect()
    {
        Assert.All(Inventory.Where(r => r.Verdict == Verdict.KnownViolation), r =>
        {
            var cited = KnownViolations
                .Where(v => r.Why.Contains(v.Slug, StringComparison.Ordinal))
                .ToList();

            Assert.True(cited.Count > 0,
                $"{r.File} — {r.Statement} is filed as a known violation and its justification names "
                + "none of the slugs pinned in KnownViolations. An allowlist entry with no owner is a "
                + "permanent exemption.");
        });
    }

    [Fact]
    public void EveryPinnedDefect_AppearsInTheInventory()
    {
        var justifications = Inventory
            .Where(r => r.Verdict == Verdict.KnownViolation)
            .Select(r => r.Why)
            .ToList();

        Assert.All(KnownViolations, v =>
            Assert.Contains(justifications, why => why.Contains(v.Slug, StringComparison.Ordinal)));
    }

    private static string PasteReady(IEnumerable<(string File, int Line, string Statement)> found)
    {
        // Grouped, not ToDictionary: the same statement legitimately appears twice in one file (the
        // main assertion compares occurrence COUNTS), and this path only runs when the guard is
        // already failing — throwing here replaced the fix instructions with an ArgumentException.
        var known = Inventory
            .GroupBy(r => (r.File, r.Statement))
            .ToDictionary(g => g.Key, g => g.First());
        var builder = new StringBuilder();

        foreach (var group in found
            .GroupBy(r => (r.File, r.Statement))
            .OrderBy(g => g.Key.File, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Statement, StringComparer.Ordinal))
        {
            var verdict = known.TryGetValue(group.Key, out var existing) ? existing.Verdict : Verdict.NeedsReview;
            var why = existing is { Why.Length: > 0 } ? $", \"{Escape(existing.Why)}\"" : "";

            for (var i = 0; i < group.Count(); i++)
                builder.AppendLine($"        new(\"{group.Key.File}\", \"{Escape(group.Key.Statement)}\", Verdict.{verdict}{why}),");
        }

        return builder.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", @"\\").Replace("\"", "\\\"");

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reads known to be broken, pinned individually so a fix forces an edit here.

    private sealed record PinnedViolation(string Slug, string File, string Fragment, string Why);

    private static readonly PinnedViolation[] KnownViolations =
    [
        // Empty: every arbitrary single-row read now orders, and the duplicated active-policy read is
        // behind IEscalationPolicyRepository.GetActiveForTeamAsync. Entries are deleted rather than
        // annotated, because this array is what a reader trusts to still be broken.
    ];

    /// <summary>
    /// A Fact over the array rather than a Theory with hardcoded entries: an emptied list has to read
    /// as nothing left to check, not as failures for entries somebody correctly deleted.
    /// </summary>
    [Fact]
    public void EachKnownViolation_IsStillWhereItWasPinned()
    {
        Assert.All(KnownViolations, pinned =>
        {
            var file = SourceScanner.ProductFiles(includeMigrations: false)
                .SingleOrDefault(f => Path.GetFileName(f) == pinned.File);

            Assert.NotNull(file);

            var code = SourceScanner.Code(file!);

            Assert.Contains(pinned.Fragment, code, StringComparison.Ordinal);
            Assert.Matches(ReadCall, code);
        });
    }

    [Fact]
    public void EveryKnownViolation_NamesTheDefectAndWhyItIsStillOpen()
    {
        Assert.All(KnownViolations, v =>
        {
            Assert.Matches(@"^[a-z0-9]+(?:-[a-z0-9]+){1,6}$", v.Slug);
            Assert.True(v.Why.Length >= 80,
                $"{v.Slug}'s justification is {v.Why.Length} characters. This list is what the next "
                + "person works from; an entry that does not say what the read decides and why it is "
                + "still open is a file name on a list.");
        });
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // The detector's control groups.

    [Theory]
    [InlineData("var x = await q.FirstOrDefaultAsync(p => p.Id == id, ct);", true)]
    [InlineData("var x = await q.SingleAsync(ct);", true)]
    [InlineData("var x = await repo.FindSingleAsync(p => p.TeamId == teamId, ct);", true)]
    [InlineData("var x = list.First();", true)]
    [InlineData("var x = await q.FirstOrDefaultAsync<Thing>(ct);", true)]
    [InlineData("var x = await q.ToListAsync(ct);", false)]
    [InlineData("var x = await q.AnyAsync(p => p.Id == id, ct);", false)]
    [InlineData("var first = q.FirstName;", false)]
    public void TheScanRecognisesASingleRowRead(string code, bool expected)
    {
        Assert.Equal(expected, ReadCall.IsMatch(code));
    }

    [Fact]
    public void RemovingAnOrderBy_ChangesTheKey()
    {
        const string ordered = "var p = await q.OrderBy(x => x.Priority).FirstOrDefaultAsync(x => x.IsActive, ct);";
        const string unordered = "var p = await q.FirstOrDefaultAsync(x => x.IsActive, ct);";

        var a = Statement(ordered, ordered.IndexOf(".FirstOrDefaultAsync", StringComparison.Ordinal));
        var b = Statement(unordered, unordered.IndexOf(".FirstOrDefaultAsync", StringComparison.Ordinal));

        Assert.NotEqual(a, b);
        Assert.Contains("OrderBy", a, StringComparison.Ordinal);
    }

    [Fact]
    public void TheKeyIsTheWholeStatement_NotTheLine()
    {
        const string chained = """
            var policy = await policyRepo.GetQueryable()
                .Where(p => p.TeamId == teamId)
                .OrderBy(p => p.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            """;

        var statement = Statement(chained, chained.IndexOf(".FirstOrDefaultAsync", StringComparison.Ordinal));

        Assert.Contains("Where(p => p.TeamId == teamId)", statement, StringComparison.Ordinal);
        Assert.Contains("OrderBy", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectInitializerInsideAChain_DoesNotTruncateTheKey()
    {
        const string chained =
            "var row = await context.CallTokens.Where(t => t.Token == token)"
            + ".Select(t => new Projection { Data = t.CallDataJson }).FirstOrDefaultAsync(ct);";

        var statement = Statement(chained, chained.IndexOf(".FirstOrDefaultAsync", StringComparison.Ordinal));

        Assert.StartsWith("var row = await context.CallTokens", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void TheScan_ActuallyFindsTheProductsReads()
    {
        var found = FoundReads().ToList();

        Assert.NotEmpty(found);

        foreach (var violation in KnownViolations)
            Assert.Contains(found, r => r.File == violation.File);
    }
}
