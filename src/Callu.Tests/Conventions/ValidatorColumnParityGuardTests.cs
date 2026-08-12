using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using Callu.Api.Controllers;
using Callu.Domain.Entities;
using FluentValidation;
using FluentValidation.Validators;

namespace Callu.Tests.Conventions;

/// <summary>
/// Every request-DTO string field has a length bound, and no bound is wider than the column it is
/// written to.
/// </summary>
public class ValidatorColumnParityGuardTests
{
    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // The DTO surface, and where each DTO's fields end up.

    private static readonly Assembly SharedAssembly = typeof(Callu.Shared.Models.Incidents.CreateIncidentRequest).Assembly;

    private static IEnumerable<Type> RequestDtos() =>
        SharedAssembly.GetTypes()
            .Where(t => t is { IsPublic: true, IsAbstract: false }
                        && t.Name.EndsWith("Request", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    private sealed record Mapping(string Dto, Type? Entity, string Why);

    private const string Identity =
        "Writes to ApplicationUser, which lives in Callu.Infrastructure/Identity, not Callu.Domain — the "
        + "column bounds come from ASP.NET Identity's own schema, so there is no [StringLength] here to "
        + "compare against. The have-a-bound half still applies: the widths live in "
        + "Callu.Shared/Validation/IdentityFieldLengths.cs, which ApplicationUser's own [StringLength] "
        + "attributes read too, so the two cannot drift. What this "
        + "guard still cannot see is a field whose value is DERIVED into a narrower column — "
        + "DisplayName is varchar(100) and is built from FirstName + LastName, both bounded at 100; "
        + "that one is clamped at the write site instead.";

    private const string Command =
        "An action command: its fields are arguments, not columns. Nothing it carries is persisted "
        + "verbatim, so there is no column bound to compare against.";

    private const string ProviderPayload =
        "A payload for an external provider API (Voximplant / SMS / TTS), not a row. The bound that "
        + "matters is the provider's, which this guard cannot see; the have-a-bound half still applies.";

    private static readonly Mapping[] Mappings =
    [
        // ── Persisted: mapped to the entity whose columns the DTO's fields become ────────────────────
        new("AddComponentRequest", typeof(StatusPageComponent), "Creates a status page component row."),
        new("AddIncidentUpdateRequest", typeof(StatusPageIncidentUpdate), "Appends an update to a status page incident."),
        new("AddMemberRequest", typeof(TeamMember), "Creates a team membership row."),
        new("CreateAlertRuleRequest", typeof(AlertRule), "Creates an alert rule row."),
        new("CreateConferenceRequest", typeof(ConferenceRoom), "Creates a conference room row."),
        new("CreateConferenceRoomRequest", typeof(ConferenceRoom), "Creates a conference room row."),
        new("CreateEmailTemplateRequest", typeof(EmailTemplate), "Creates an email template row."),
        new("CreateEscalationRequest", typeof(EscalationPolicy), "Creates an escalation policy row."),
        new("CreateEscalationStepRequest", typeof(EscalationStep), "Creates an escalation step row."),
        new("CreateIncidentNoteRequest", typeof(IncidentNote), "Creates an incident note row."),
        new("CreateIncidentRequest", typeof(Incident), "Creates an incident row — the alert-ingest path."),
        new("CreateIntegrationRequest", typeof(Integration), "Creates an inbound integration row."),
        new("CreateMaintenanceWindowRequest", typeof(MaintenanceWindow), "Creates a maintenance window row."),
        new("UpdateMaintenanceWindowRequest", typeof(MaintenanceWindow), "Updates a maintenance window row."),
        new("CreateNotificationChannelRequest", typeof(NotificationChannel), "Creates a notification channel row."),
        new("CreateOverrideRequest", typeof(OnCallOverride), "Creates an on-call override row."),
        new("CreatePostmortemRequest", typeof(Postmortem), "Creates a postmortem row — the operator's own writing."),
        new("CreateProviderRequest", typeof(CommunicationProvider), "Creates a communication provider row."),
        new("CreateRotationRequest", typeof(ScheduleRotation), "Creates a schedule rotation row."),
        new("CreateRunbookRequest", typeof(Runbook), "Creates a runbook row — the operator's own writing."),
        new("CreateScheduleRequest", typeof(Schedule), "Creates a schedule row."),
        new("CreateServiceDependencyRequest", typeof(ServiceDependency), "Creates a service dependency row."),
        new("CreateServiceActionRequest", typeof(ServiceAction), "Creates a service action row."),
        new("CreateServiceRequest", typeof(Service), "Creates a service row."),
        new("CreateSipTrunkRequest", typeof(SipTrunkSettings), "Creates the SIP trunk settings row."),
        new("CreateStatusIncidentRequest", typeof(StatusPageIncident), "Creates a status page incident row."),
        new("CreateStatusPageRequest", typeof(StatusPage), "Creates a status page row."),
        new("CreateTeamRequest", typeof(Team), "Creates a team row."),
        new("CreateWebhookTemplateRequest", typeof(WebhookTemplate), "Creates a webhook template row."),
        new("SubscribeRequest", typeof(StatusPageSubscriber), "Creates a status page subscriber row."),
        new("TtsTemplateSaveRequest", typeof(TtsMessageTemplate), "Creates or updates a TTS message template row."),
        new("UpdateAlertRuleRequest", typeof(AlertRule), "Updates an alert rule row."),
        new("UpdateComponentRequest", typeof(StatusPageComponent), "Updates a status page component row."),
        new("UpdateEmailTemplateRequest", typeof(EmailTemplate), "Updates an email template row."),
        new("UpdateEscalationRequest", typeof(EscalationPolicy), "Updates an escalation policy row."),
        new("UpdateIncidentNoteRequest", typeof(IncidentNote), "Updates an incident note row."),
        new("UpdateIncidentRequest", typeof(Incident), "Updates an incident row."),
        new("UpdateIntegrationRequest", typeof(Integration), "Updates an inbound integration row."),
        new("UpdateMemberRoleRequest", typeof(TeamMember), "Updates a team membership row."),
        new("UpdateNotificationChannelRequest", typeof(NotificationChannel), "Updates a notification channel row."),
        new("UpdateOrganizationSettingsRequest", typeof(OrganizationSettings), "Updates the organization settings singleton row."),
        new("UpdateOverrideRequest", typeof(OnCallOverride), "Updates an on-call override row."),
        new("UpdatePostmortemRequest", typeof(Postmortem), "Updates a postmortem row — the operator's own writing."),
        new("UpdateProviderRequest", typeof(CommunicationProvider), "Updates a communication provider row."),
        new("UpdateRotationRequest", typeof(ScheduleRotation), "Updates a schedule rotation row."),
        new("UpdateRunbookRequest", typeof(Runbook), "Updates a runbook row — the operator's own writing."),
        new("UpdateScheduleRequest", typeof(Schedule), "Updates a schedule row."),
        new("UpdateServiceActionRequest", typeof(ServiceAction), "Updates a service action row."),
        new("UpdateServiceRequest", typeof(Service), "Updates a service row."),
        new("SetCapabilityRouteRequest", typeof(CapabilityProviderMapping), "Pins one capability to one provider; its only field becomes ProviderId."),
        new("UpdateSipTrunkRequest", typeof(SipTrunkSettings), "Updates the SIP trunk settings row."),
        new("UpdateSmtpSettingsRequest", typeof(SmtpSettings), "Updates the SMTP settings row."),
        new("UpdateFirebaseSettingsRequest", typeof(FirebaseSettings), "Updates the Firebase settings singleton row."),
        new("RegisterPushDeviceRequest", typeof(UserPushDevice), "Creates or refreshes a push device row."),
        new("UnregisterPushDeviceRequest", typeof(UserPushDevice), "Names the push device row to soft-delete."),
        new("UpdateStatusPageRequest", typeof(StatusPage), "Updates a status page row."),
        new("UpdateStepRequest", typeof(EscalationStep), "Updates an escalation step row."),
        new("UpdateTeamRequest", typeof(Team), "Updates a team row."),
        new("UpdateWebhookTemplateRequest", typeof(WebhookTemplate), "Updates a webhook template row."),

        // ── Not mapped, on purpose, with the reason ──────────────────────────────────────────────────
        new("TtsPreviewRequest", null,
            "Renders sample speech so an operator can hear a template before a call carries it. Nothing "
            + "is persisted: the text goes to the voice service, the audio comes back, and neither is "
            + "written to any table. The bounds it does carry are the voice service's own limits, kept "
            + "below them so an over-long request is a field error here rather than a refusal there."),
        new("TtsPreviewSegmentRequest", null,
            "One segment of that sample, with the language to speak it in. Same as its parent: nothing "
            + "reaches a column, so there is no width to compare against. Its bounds exist to keep a "
            + "preview from spending the synthesizer's CPU without limit, not to protect a column."),
        new("AcceptInvitationRequest", null, Identity),
        new("AdminUpdateUserRequest", null, Identity),
        new("ChangePasswordRequest", null, Identity),
        new("ChangeRoleRequest", null, Identity),
        new("ForgotPasswordRequest", null, Identity),
        new("InitialSetupRequest", null, Identity),
        new("InviteUserRequest", null, Identity),
        new("LoginRequest", null, Identity),
        new("RefreshRequest", null,
            "The optional body for refresh / logout when the HttpOnly cookie is not sent. The token is "
            + "hashed and compared against RefreshToken.TokenHash; the plaintext itself is never "
            + "written, so there is no column to compare a bound against. It carries one anyway, well "
            + "above the 88 characters an issued token occupies."),
        new("ResetPasswordRequest", null, Identity),
        new("UpdateProfileRequest", null, Identity),

        new("BindIntegrationServiceRequest", null, Command + " Carries only a target service id; nothing it holds becomes a column value."),
        new("EscalateRequest", null, Command + " The reason does reach IncidentTimelineEvent.Description, which is a bounded column."),
        new("ReassignRequest", null, Command),
        new("ReorderStepsRequest", null, Command),
        new("SetProviderRequest", null, Command),
        new("SetSignatureRequest", null, Command + " The secret is stored under a differently-named column, so a name-based map finds nothing."),
        new("SetTemplateRequest", null, Command),
        new("TestNotificationRequest", null, Command + " Named like the Notification entity and deliberately NOT mapped to it: it sends a test, it does not write a row."),
        new("ToggleListeningModeRequest", null, Command),
        new("PreviewEmailTemplateRequest", null, Command + " Renders a preview; nothing is persisted."),
        new("TraceSearchRequest", null, Command + " A Jaeger query, not a write."),

        new("MakeCallRequest", null, ProviderPayload),
        new("SendSmsRequest", null, ProviderPayload),
        new("SendTestEmailRequest", null, ProviderPayload),
        new("TestEmailRequest", null, ProviderPayload),
        new("TestSmsRequest", null, ProviderPayload),
        new("TestPayloadRequest", null, ProviderPayload),
        new("PreviewWebhookTemplateRequest", null, Command + " Sample payload and mappings are parsed and discarded; nothing is persisted."),
        new("TTSRequest", null, ProviderPayload),
        new("CreateVoxApplicationRequest", null, ProviderPayload),
        new("CreateVoxRuleRequest", null, ProviderPayload),
        new("CreateVoxScenarioRequest", null, ProviderPayload),
        new("CreateVoxUserRequest", null, ProviderPayload),
        new("UpdateVoxScenarioRequest", null, ProviderPayload),
        new("VoxCallbackRequest", null, ProviderPayload + " Inbound, from Voximplant."),
        new("CalluVoiceCallbackRequest", null,
            "Inbound call status from callu-voice. Nothing on it is written: the call and the number "
            + "come out of the sealed token in the callback URL and are clamped there, and the status "
            + "is mapped through the status table rather than stored."),

        new("SaveSchedulePlanRequest", null,
            "Composite: one request writes Schedule AND its ScheduleRotation children, so there is no "
            + "single target entity for a name-based map. Its own string fields are still held to the "
            + "have-a-bound half; the nested rotations are out of scope (see the type summary).")
    ];

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reading the bounds.

    private static IEnumerable<PropertyInfo> StringProperties(Type dto) =>
        dto.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.CanRead)
            .OrderBy(p => p.Name, StringComparer.Ordinal);

    // Instantiated directly rather than resolved: building the API's container would drag in the host.
    private static readonly Lazy<Dictionary<Type, IValidator>> Validators = new(() =>
    {
        var assemblies = new[]
        {
            typeof(Callu.Application.Validators.CreateEscalationRequestValidator).Assembly,
            typeof(HealthController).Assembly,
            SharedAssembly
        }.Distinct();

        var found = new Dictionary<Type, IValidator>();

        foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
        {
            if (type is not { IsClass: true, IsAbstract: false }) continue;
            if (type.GetConstructor(Type.EmptyTypes) is null) continue;

            var validated = ValidatedType(type);
            if (validated is null) continue;

            found[validated] = (IValidator)Activator.CreateInstance(type)!;
        }

        return found;
    });

    private static Type? ValidatedType(Type type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                return t.GetGenericArguments()[0];

        return null;
    }

    // A DataAnnotation counts: ASP.NET model validation enforces it on a bound DTO, so it is a 400.
    private static int? BoundOn(Type owner, PropertyInfo property)
    {
        var bounds = new List<int>();

        var annotation = property.GetCustomAttribute<StringLengthAttribute>()?.MaximumLength
                         ?? property.GetCustomAttribute<MaxLengthAttribute>()?.Length;
        if (annotation is > 0) bounds.Add(annotation.Value);

        // Records carry the annotation on the constructor parameter — the place MVC reads it from.
        var parameter = owner.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
        var parameterAnnotation = parameter?.GetCustomAttribute<StringLengthAttribute>()?.MaximumLength
                                  ?? parameter?.GetCustomAttribute<MaxLengthAttribute>()?.Length;
        if (parameterAnnotation is > 0) bounds.Add(parameterAnnotation.Value);

        if (Validators.Value.TryGetValue(owner, out var validator))
            bounds.AddRange(validator.CreateDescriptor()
                .GetValidatorsForMember(property.Name)
                .Select(x => x.Validator)
                .OfType<ILengthValidator>()
                .Select(v => v.Max)
                .Where(max => max > 0));

        return bounds.Count == 0 ? null : bounds.Min();
    }

    private static int? ColumnBoundFor(Type entity, string propertyName)
    {
        var property = entity.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        if (property is null || property.PropertyType != typeof(string)) return null;

        return property.GetCustomAttribute<StringLengthAttribute>()?.MaximumLength
               ?? property.GetCustomAttribute<MaxLengthAttribute>()?.Length;
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // THE INVENTORIES. Both are a to-do list, not a set of exemptions.

    private sealed record Unbounded(string Dto, string Property, string Why);

    private const string IngestPath =
        "The monitoring-ingest DTO. Adding rules here is not enough on its own: on the ingest path an "
        + "over-long Alertmanager summary must be TRUNCATED, because a 400 is never retried and the "
        + "alert is simply lost. Product code, and a design decision with it.";

    private const string TextColumn =
        "Deliberately unbounded, and it must stay that way: the column behind it is PostgreSQL `text`, "
        + "so a long writeup was never at risk of 22001. Inventing a limit here would reject work the "
        + "schema accepts, which is why Title/RootCause/Description are bounded and these are not. "
        + "Verified against migration 20260715133340_Initial (Postmortems.Content, Runbooks.Content).";

    private const string EscalationReason =
        "Lands in IncidentTimelineEvent.Description, a varchar(1000) the writer clamps rather than the "
        + "validator, so an operator's long manual-escalation reason is one of the ways that column "
        + "overflows. Bounding it here and clamping there are both wanted; both are product code.";

    private const string PersistedTail =
        "The long tail: the field becomes a bounded column, so a long enough value is a 22001 and a "
        + "500. Individually small, and best swept as one batch rather than one DTO at a time — which "
        + "is why they are listed together here instead of fixed piecemeal.";

    private const string NotPersisted =
        "No Callu.Domain column behind it (see Mappings) — it is a provider payload, an action argument "
        + "or an Identity field, so this is not the 22001 path. Still unbounded, which means an "
        + "arbitrarily large body reaches whatever consumes it. Lower priority, listed.";

    private static readonly Unbounded[] KnownUnbounded =
    [
        // The monitoring-ingest path.
        new("CreateIncidentRequest", "DataLanguage", IngestPath),
        new("CreateIncidentRequest", "ExternalAlertId", IngestPath),
        new("CreateIncidentRequest", "Severity", IngestPath),
        new("CreateIntegrationRequest", "Type", PersistedTail),

        // The operator's own writing, and the one place unbounded is correct.
        new("CreatePostmortemRequest", "Content", TextColumn),
        new("CreateRunbookRequest", "Content", TextColumn),
        new("UpdatePostmortemRequest", "Content", TextColumn),
        new("UpdateRunbookRequest", "Content", TextColumn),

        // Reaches the timeline's own clamped column.
        new("EscalateRequest", "Reason", EscalationReason),

        // The long tail: fields that DO become columns.
        new("AddComponentRequest", "HealthCheckBody", PersistedTail),
        new("AddComponentRequest", "HealthCheckFieldMappings", PersistedTail),
        new("AddComponentRequest", "HealthCheckStateMapping", PersistedTail),
        new("AddIncidentUpdateRequest", "Message", PersistedTail),
        new("AddMemberRequest", "Role", PersistedTail),
        new("AddMemberRequest", "UserId", PersistedTail),
        new("CreateAlertRuleRequest", "Description", PersistedTail),
        new("CreateAlertRuleRequest", "Name", PersistedTail),
        new("CreateConferenceRequest", "Name", PersistedTail),
        new("CreateEmailTemplateRequest", "HtmlBody", PersistedTail),
        new("CreateEmailTemplateRequest", "PlainTextBody", PersistedTail),
        new("CreateMaintenanceWindowRequest", "Mode", PersistedTail),
        new("UpdateMaintenanceWindowRequest", "Mode", PersistedTail),
        new("CreateNotificationChannelRequest", "ChannelType", PersistedTail),
        new("CreateNotificationChannelRequest", "MinimumSeverity", PersistedTail),
        new("CreateOverrideRequest", "OriginalUserId", PersistedTail),
        new("CreateOverrideRequest", "OverrideUserId", PersistedTail),
        new("CreateRotationRequest", "UserId", PersistedTail),
        new("CreateScheduleRequest", "Description", PersistedTail),
        new("CreateScheduleRequest", "Name", PersistedTail),
        new("CreateScheduleRequest", "Timezone", PersistedTail),
        new("CreateServiceDependencyRequest", "Description", PersistedTail),
        new("CreateServiceRequest", "Color", PersistedTail),
        new("CreateServiceRequest", "Environment", PersistedTail),
        new("CreateServiceRequest", "Icon", PersistedTail),
        new("CreateServiceRequest", "Type", PersistedTail),
        new("CreateSipTrunkRequest", "CallerId", PersistedTail),
        new("CreateSipTrunkRequest", "DisplayName", PersistedTail),
        new("CreateSipTrunkRequest", "Password", PersistedTail),
        new("CreateWebhookTemplateRequest", "DataLanguage", PersistedTail),
        new("CreateWebhookTemplateRequest", "Description", PersistedTail),
        new("CreateWebhookTemplateRequest", "FieldMappings", PersistedTail),
        new("CreateWebhookTemplateRequest", "SamplePayload", PersistedTail),
        new("CreateWebhookTemplateRequest", "StateMapping", PersistedTail),
        new("SaveSchedulePlanRequest", "Description", PersistedTail),
        new("SaveSchedulePlanRequest", "Name", PersistedTail),
        new("SaveSchedulePlanRequest", "Timezone", PersistedTail),
        new("UpdateAlertRuleRequest", "Description", PersistedTail),
        new("UpdateAlertRuleRequest", "Name", PersistedTail),
        new("UpdateComponentRequest", "HealthCheckBody", PersistedTail),
        new("UpdateComponentRequest", "HealthCheckFieldMappings", PersistedTail),
        new("UpdateComponentRequest", "HealthCheckStateMapping", PersistedTail),
        new("UpdateEmailTemplateRequest", "HtmlBody", PersistedTail),
        new("UpdateEmailTemplateRequest", "PlainTextBody", PersistedTail),
        new("UpdateIncidentRequest", "Severity", PersistedTail),
        new("UpdateIncidentRequest", "Status", PersistedTail),
        new("UpdateMemberRoleRequest", "Role", PersistedTail),
        new("UpdateNotificationChannelRequest", "MinimumSeverity", PersistedTail),
        new("UpdateOrganizationSettingsRequest", "BaseUrl", PersistedTail),
        new("UpdateOrganizationSettingsRequest", "DefaultCulture", PersistedTail),
        new("UpdateOrganizationSettingsRequest", "DefaultTimezone", PersistedTail),
        new("UpdateOrganizationSettingsRequest", "OrganizationName", PersistedTail),
        new("UpdateOverrideRequest", "OverrideUserId", PersistedTail),
        new("UpdateScheduleRequest", "Description", PersistedTail),
        new("UpdateScheduleRequest", "Name", PersistedTail),
        new("UpdateScheduleRequest", "Timezone", PersistedTail),
        new("UpdateServiceRequest", "Color", PersistedTail),
        new("UpdateServiceRequest", "Environment", PersistedTail),
        new("UpdateServiceRequest", "Icon", PersistedTail),
        new("UpdateServiceRequest", "Status", PersistedTail),
        new("UpdateServiceRequest", "Type", PersistedTail),
        new("UpdateSipTrunkRequest", "CallerId", PersistedTail),
        new("UpdateSipTrunkRequest", "DisplayName", PersistedTail),
        new("UpdateSipTrunkRequest", "Password", PersistedTail),
        new("UpdateSmtpSettingsRequest", "FromAddress", PersistedTail),
        new("UpdateSmtpSettingsRequest", "FromName", PersistedTail),
        new("UpdateSmtpSettingsRequest", "Password", PersistedTail),
        new("UpdateSmtpSettingsRequest", "ReplyToAddress", PersistedTail),
        new("UpdateSmtpSettingsRequest", "Username", PersistedTail),
        new("UpdateTeamRequest", "Color", PersistedTail),
        new("UpdateTeamRequest", "Description", PersistedTail),
        new("UpdateWebhookTemplateRequest", "DataLanguage", PersistedTail),
        new("UpdateWebhookTemplateRequest", "Description", PersistedTail),
        new("UpdateWebhookTemplateRequest", "FieldMappings", PersistedTail),
        new("UpdateWebhookTemplateRequest", "SamplePayload", PersistedTail),
        new("UpdateWebhookTemplateRequest", "StateMapping", PersistedTail),

        // Not written to a Callu.Domain column (see Mappings) — a lesser, but still real, gap.
        new("CreateVoxApplicationRequest", "ApplicationName", NotPersisted),
        new("CreateVoxRuleRequest", "Name", NotPersisted),
        new("CreateVoxRuleRequest", "Pattern", NotPersisted),
        new("CreateVoxScenarioRequest", "Name", NotPersisted),
        new("CreateVoxScenarioRequest", "Script", NotPersisted),
        new("CreateVoxUserRequest", "DisplayName", NotPersisted),
        new("CreateVoxUserRequest", "Password", NotPersisted),
        new("CreateVoxUserRequest", "UserName", NotPersisted),
        new("MakeCallRequest", "CallerId", NotPersisted),
        new("MakeCallRequest", "CustomData", NotPersisted),
        new("MakeCallRequest", "DataLanguage", NotPersisted),
        new("MakeCallRequest", "Description", NotPersisted),
        new("MakeCallRequest", "Destination", NotPersisted),
        new("MakeCallRequest", "IncidentTitle", NotPersisted),
        new("MakeCallRequest", "Language", NotPersisted),
        new("MakeCallRequest", "ServiceName", NotPersisted),
        new("MakeCallRequest", "Severity", NotPersisted),
        new("MakeCallRequest", "VoiceId", NotPersisted),
        new("ReassignRequest", "TargetUserId", NotPersisted),
        new("SendSmsRequest", "SenderId", NotPersisted),
        new("SendSmsRequest", "To", NotPersisted),
        new("SendTestEmailRequest", "Email", NotPersisted),
        new("SendTestEmailRequest", "RecipientEmail", NotPersisted),
        new("SetProviderRequest", "ProviderId", NotPersisted),
        new("SetSignatureRequest", "HeaderName", NotPersisted),
        new("SetSignatureRequest", "Secret", NotPersisted),
        new("TTSRequest", "Language", NotPersisted),
        new("TTSRequest", "Text", NotPersisted),
        new("TTSRequest", "VoiceId", NotPersisted),
        new("TestEmailRequest", "RecipientEmail", NotPersisted),
        new("TestNotificationRequest", "Message", NotPersisted),
        new("PreviewWebhookTemplateRequest", "FieldMappings", NotPersisted),
        new("PreviewWebhookTemplateRequest", "SamplePayload", NotPersisted),
        new("PreviewWebhookTemplateRequest", "StateMapping", NotPersisted),
        new("TestPayloadRequest", "SamplePayload", NotPersisted),
        new("TestSmsRequest", "Message", NotPersisted),
        new("TestSmsRequest", "To", NotPersisted),
        new("TraceSearchRequest", "Operation", NotPersisted),
        new("TraceSearchRequest", "Service", NotPersisted),
        new("UpdateVoxScenarioRequest", "Name", NotPersisted),
        new("UpdateVoxScenarioRequest", "Script", NotPersisted),
        // A bound here would be a 400, and callu-voice does not retry a 4xx: the acknowledgement is
        // simply lost. Neither field is written — an over-long status maps to unrecognised, which keeps
        // the retry chain paging, and an over-long call id cannot match the token and is refused.
        new("CalluVoiceCallbackRequest", "CallId",
            "Compared with the call id sealed into the callback token and never stored; the stored "
            + "CallToken is clamped from the token, not from this body."),
        new("CalluVoiceCallbackRequest", "Status",
            "Mapped through the callu-voice status table and never stored; anything the table does not "
            + "know becomes a failed call, which keeps the retry chain paging."),
        new("VoxCallbackRequest", "CallSessionId", NotPersisted),
        new("VoxCallbackRequest", "CallToken", NotPersisted),
        new("VoxCallbackRequest", "ConferenceId", NotPersisted),
        new("VoxCallbackRequest", "IncidentId", NotPersisted),
        new("VoxCallbackRequest", "Status", NotPersisted),
    ];

    private sealed record TooWide(string Dto, string Property, string Why);

    private static readonly TooWide[] KnownWiderThanTheColumn =
    [
        new("CreateTeamRequest", "Color",
            "Turned up by this guard. [SafeStringLength(0, 50)] on the DTO against "
            + "Team.Color varchar(20). Reachable: the format regex allows bg-<name>-<number> with an "
            + "unbounded name, so \"bg-averyverylongcolourname-500\" passes validation at 33 characters "
            + "and PostgreSQL refuses it. Fix is one number in product code."),

        new("TtsTemplateSaveRequest", "DisplayName",
            "Turned up by this guard. The validator allows 100, "
            + "TtsMessageTemplate.DisplayName is varchar(50), and nothing else narrows it: a 60-character "
            + "language label passes validation and comes back as a 500. Fix is one number in product code."),
    ];

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // THE GUARDS.

    [Fact]
    public void EveryRequestDto_IsMappedOrExplicitlyUnmapped()
    {
        var listed = Mappings.Select(m => m.Dto).ToHashSet(StringComparer.Ordinal);
        var actual = RequestDtos().Select(t => t.Name).ToList();

        var unlisted = actual.Where(n => !listed.Contains(n)).Order(StringComparer.Ordinal).ToList();
        var vanished = listed.Where(n => !actual.Contains(n)).Order(StringComparer.Ordinal).ToList();

        Assert.True(unlisted.Count == 0,
            "These request DTOs are not in the DTO→entity map: " + string.Join(", ", unlisted)
            + ".\n\nSay which entity's columns their fields become, or say null and why not. A DTO that "
            + "is simply absent gets no column comparison and nobody notices — which is how a validator "
            + "wider than its column ships.");

        Assert.True(vanished.Count == 0,
            "These map entries no longer name a request DTO in Callu.Shared: " + string.Join(", ", vanished)
            + ". Renamed or deleted; either way the entry describes a tree that has moved.");
    }

    [Fact]
    public void EveryStringFieldOnARequest_HasALengthBound()
    {
        var allowed = KnownUnbounded.Select(u => (u.Dto, u.Property)).ToHashSet();

        var unbounded = RequestDtos()
            .SelectMany(dto => StringProperties(dto).Select(p => (Dto: dto.Name, Property: p.Name, Owner: dto, Prop: p)))
            .Where(x => BoundOn(x.Owner, x.Prop) is null)
            .Select(x => (x.Dto, x.Property))
            .ToList();

        var unlisted = unbounded.Where(x => !allowed.Contains(x)).ToList();
        var fixedSince = allowed.Where(a => !unbounded.Contains(a)).ToList();

        var message = new StringBuilder();

        if (unlisted.Count > 0)
        {
            message.AppendLine(
                $"{unlisted.Count} request DTO string field(s) have no length bound at all — no "
                + "FluentValidation MaximumLength rule and no [StringLength]/[MaxLength]:\n");

            foreach (var x in unlisted.OrderBy(x => x.Dto, StringComparer.Ordinal).ThenBy(x => x.Property, StringComparer.Ordinal))
                message.AppendLine($"  {x.Dto}.{x.Property}");

            message.AppendLine(
                "\nEach of these is a 22001 waiting for a long enough value: PostgreSQL refuses it, EF "
                + "wraps it, GlobalExceptionHandler has no arm for it, and the operator gets 500 'an "
                + "unexpected error occurred' with their work gone. Add the rule, or add an entry to "
                + "KnownUnbounded saying what the field is and why it is still unbounded.\n");

            message.AppendLine("Paste-ready:\n");
            foreach (var x in unlisted.OrderBy(x => x.Dto, StringComparer.Ordinal).ThenBy(x => x.Property, StringComparer.Ordinal))
                message.AppendLine($"        new(\"{x.Dto}\", \"{x.Property}\", \"\"),");
        }

        if (fixedSince.Count > 0)
            message.AppendLine(
                "These KnownUnbounded entries now HAVE a bound — delete them, an allowlist that lists "
                + "fixed things is one nobody reads: "
                + string.Join(", ", fixedSince.Select(x => $"{x.Dto}.{x.Property}").Order(StringComparer.Ordinal)));

        if (message.Length > 0) Assert.Fail(message.ToString());
    }

    [Fact]
    public void NoValidatorIsWiderThanTheColumnItWritesTo()
    {
        var allowed = KnownWiderThanTheColumn.Select(t => (t.Dto, t.Property)).ToHashSet();
        var offenders = new List<(string Dto, string Property, int Rule, int Column)>();

        foreach (var mapping in Mappings.Where(m => m.Entity is not null))
        {
            var dto = RequestDtos().SingleOrDefault(t => t.Name == mapping.Dto);
            if (dto is null) continue;

            foreach (var property in StringProperties(dto))
            {
                var bound = BoundOn(dto, property);
                var column = ColumnBoundFor(mapping.Entity!, property.Name);

                if (bound is { } b && column is { } c && b > c)
                    offenders.Add((mapping.Dto, property.Name, b, c));
            }
        }

        var unlisted = offenders.Where(o => !allowed.Contains((o.Dto, o.Property))).ToList();
        var fixedSince = allowed.Where(a => !offenders.Any(o => o.Dto == a.Dto && o.Property == a.Property)).ToList();

        var message = new StringBuilder();

        if (unlisted.Count > 0)
        {
            message.AppendLine(
                $"{unlisted.Count} field(s) are validated LOOSER than the column they are written to:\n");

            foreach (var o in unlisted.OrderBy(o => o.Dto, StringComparer.Ordinal).ThenBy(o => o.Property, StringComparer.Ordinal))
                message.AppendLine($"  {o.Dto}.{o.Property}: rule allows {o.Rule}, column holds {o.Column}");

            message.AppendLine(
                "\nA value between those two numbers passes validation and then fails in PostgreSQL with "
                + "22001, which the operator sees as 500 rather than 400. Tighten the rule to the column "
                + "(or widen the column with a migration, if the longer value is the point).\n");

            message.AppendLine("Paste-ready:\n");
            foreach (var o in unlisted.OrderBy(o => o.Dto, StringComparer.Ordinal).ThenBy(o => o.Property, StringComparer.Ordinal))
                message.AppendLine($"        new(\"{o.Dto}\", \"{o.Property}\", \"\"),");
        }

        if (fixedSince.Count > 0)
            message.AppendLine(
                "These KnownWiderThanTheColumn entries now fit their column — delete them: "
                + string.Join(", ", fixedSince.Select(x => $"{x.Dto}.{x.Property}").Order(StringComparer.Ordinal)));

        if (message.Length > 0) Assert.Fail(message.ToString());
    }

    [Fact]
    public void EveryUnmappedDto_SaysWhyItIsUnmapped()
    {
        Assert.All(Mappings.Where(m => m.Entity is null), m =>
            Assert.True(m.Why.Length >= 80,
                $"{m.Dto} is unmapped with a {m.Why.Length}-character reason. \"Unmapped\" is a claim "
                + "that there is no column to compare against, and a claim needs a reason — otherwise it "
                + "reads as \"nobody looked\", which is a different and worse thing."));
    }

    [Fact]
    public void EveryAllowlistEntry_SaysWhyItIsStillOpen()
    {
        Assert.All(KnownUnbounded, u => Assert.True(u.Why.Length >= 60, $"{u.Dto}.{u.Property}: {u.Why}"));
        Assert.All(KnownWiderThanTheColumn, t => Assert.True(t.Why.Length >= 60, $"{t.Dto}.{t.Property}: {t.Why}"));
    }

    // Premises and control groups.

    [Fact]
    public void TheDtoSurface_IsFound()
    {
        var dtos = RequestDtos().Select(t => t.Name).ToList();

        Assert.True(dtos.Count > 50, $"only {dtos.Count} request DTOs found — the reflection has narrowed");
        Assert.Contains(nameof(Callu.Shared.Models.Incidents.CreateIncidentRequest), dtos);
        Assert.Contains(nameof(Callu.Shared.Models.Escalations.CreateEscalationStepRequest), dtos);
    }

    [Fact]
    public void TheValidators_AreFound()
    {
        Assert.NotEmpty(Validators.Value);

        Assert.True(Validators.Value.ContainsKey(typeof(Callu.Shared.Models.Incidents.CreateIncidentRequest)),
            "CreateIncidentRequestValidator was not discovered — the validator scan is broken, and every "
            + "field in the product would read as unbounded");
    }

    [Fact]
    public void TheBoundReader_SeesAFluentValidationRule()
    {
        var dto = typeof(Callu.Shared.Models.Incidents.CreateIncidentRequest);
        var title = dto.GetProperty("Title")!;

        Assert.Equal(200, BoundOn(dto, title));
    }

    [Fact]
    public void TheBoundReader_SeesADataAnnotation()
    {
        var dto = typeof(Callu.Shared.Models.StatusPages.SubscribeRequest);
        var email = dto.GetProperty("Email")!;

        Assert.Equal(320, BoundOn(dto, email));
    }

    [Fact]
    public void TheColumnReader_SeesTheEntitysBound()
    {
        Assert.Equal(200, ColumnBoundFor(typeof(Incident), nameof(Incident.Title)));
        Assert.Equal(1000, ColumnBoundFor(typeof(IncidentTimelineEvent), nameof(IncidentTimelineEvent.Description)));

        Assert.Null(ColumnBoundFor(typeof(Incident), nameof(Incident.Id)));
        Assert.Null(ColumnBoundFor(typeof(Incident), "NoSuchProperty"));
    }

    [Fact]
    public void TheBoundReader_ReportsAnUnboundedFieldAsUnbounded()
    {
        var dto = typeof(Unvalidated);

        Assert.Null(BoundOn(dto, dto.GetProperty(nameof(Unvalidated.Narrative))!));
    }

    private sealed class Unvalidated
    {
        public string Narrative { get; init; } = string.Empty;
    }
}
