using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Incidents;
using Callu.Shared.Models.Teams;
using Callu.Shared.Models.Services;
using Callu.Shared.Models.Schedules;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Escalations;
using Callu.Shared.Models.Settings;
using Callu.Shared.Models.Webhooks;
using Callu.Shared.Models.Auth;
using Callu.Shared.Models.Notifications;
using Mapster;
using Microsoft.Extensions.DependencyInjection;
using ServiceEntity = Callu.Domain.Entities.Service;

namespace Callu.Infrastructure.Mapping;

/// <summary>
/// Mapster configuration for entity to DTO mappings
/// </summary>
public static class MappingConfig
{
    public static IServiceCollection AddMappingConfig(this IServiceCollection services)
    {
        ConfigureIncidentMappings();
        ConfigureTeamMappings();
        ConfigureServiceMappings();
        ConfigureScheduleMappings();
        ConfigureEscalationMappings();
        ConfigureCommunicationMappings();
        ConfigureSettingsMappings();
        ConfigureWebhookMappings();
        ConfigureUserMappings();
        
        return services;
    }

    private static void ConfigureIncidentMappings()
    {
        TypeAdapterConfig<Incident, IncidentListItemDto>.NewConfig();
        TypeAdapterConfig<Incident, IncidentDto>.NewConfig();

        TypeAdapterConfig<CreateIncidentRequest, Incident>.NewConfig()
            .Map(dest => dest.Id, _ => Guid.NewGuid())
            .Map(dest => dest.Severity, src => ParseSeverity(src.Severity))
            .Map(dest => dest.Status, _ => IncidentStatus.Open)
            .Map(dest => dest.StartedAt, _ => DateTime.UtcNow)
            .Map(dest => dest.CreatedAt, _ => DateTime.UtcNow);

        TypeAdapterConfig<IncidentNote, IncidentNoteDto>.NewConfig();
    }

    private static IncidentSeverity ParseSeverity(string? severity)
    {
        if (string.IsNullOrEmpty(severity)) return IncidentSeverity.Medium;
        return Enum.TryParse<IncidentSeverity>(severity, out var s) ? s : IncidentSeverity.Medium;
    }

    private static void ConfigureTeamMappings()
    {
        TypeAdapterConfig<Team, TeamDto>.NewConfig()
            .Map(dest => dest.MemberCount, src => src.Members.Count);
        
        TypeAdapterConfig<Team, TeamDetailDto>.NewConfig()
            .Map(dest => dest.MemberCount, src => src.Members.Count)
            .Ignore(dest => dest.Members);
        
        TypeAdapterConfig<TeamMember, TeamMemberDto>.NewConfig()
            .Map(dest => dest.Role, src => src.Role ?? "Member")
            .Map(dest => dest.JoinedAt, src => src.CreatedAt)
            .Ignore(dest => dest.Name!)
            .Ignore(dest => dest.Email!)
            .Ignore(dest => dest.Initials!);
    }

    private static void ConfigureServiceMappings()
    {
        TypeAdapterConfig<ServiceEntity, ServiceDto>.NewConfig();
        TypeAdapterConfig<ServiceEntity, ServiceListDto>.NewConfig();

        TypeAdapterConfig<ServiceDependency, ServiceDependencyDto>.NewConfig();
    }

    private static void ConfigureScheduleMappings()
    {
        TypeAdapterConfig<Schedule, ScheduleDto>.NewConfig()
            .Map(dest => dest.RotationCount, src => src.Rotations.Count);
        
        TypeAdapterConfig<Schedule, ScheduleDetailDto>.NewConfig()
            .Map(dest => dest.RotationCount, src => src.Rotations.Count)
            .Ignore(dest => dest.Rotations);
        
        TypeAdapterConfig<ScheduleRotation, ScheduleRotationDto>.NewConfig()
            .Ignore(dest => dest.UserName!)
            .Ignore(dest => dest.UserInitials!);

        TypeAdapterConfig<OnCallOverride, OnCallOverrideDto>.NewConfig()
            .Ignore(dest => dest.OverrideUserName!)
            .Ignore(dest => dest.OverrideUserInitials!)
            .Ignore(dest => dest.OriginalUserName!);
    }
    
    private static void ConfigureEscalationMappings()
    {
        TypeAdapterConfig<EscalationPolicy, EscalationDto>.NewConfig()
            .Map(dest => dest.TeamName, src => src.Team != null ? src.Team.Name : null)
            .Map(dest => dest.StepCount, src => src.Steps.Count);

        TypeAdapterConfig<EscalationPolicy, EscalationDetailDto>.NewConfig()
            .Map(dest => dest.TeamName, src => src.Team != null ? src.Team.Name : null)
            .Map(dest => dest.StepCount, src => src.Steps.Count)
            .Ignore(dest => dest.Steps);

        TypeAdapterConfig<EscalationStep, EscalationStepDto>.NewConfig()
            .Map(dest => dest.ScheduleName, src => src.Schedule != null ? src.Schedule.Name : null)
            .Map(dest => dest.NotifyUserIds, src => src.TargetedUsers.Select(u => u.UserId))
            .Ignore(dest => dest.NotifyUserNames);
    }
    
    private static void ConfigureCommunicationMappings()
    {
        TypeAdapterConfig<SipTrunkSettings, SipTrunkDto>.NewConfig();

        TypeAdapterConfig<CommunicationProvider, CommunicationProviderDto>.NewConfig();

        TypeAdapterConfig<CallLog, CallLogDto>.NewConfig();

        TypeAdapterConfig<TtsMessageTemplate, TtsTemplateDto>.NewConfig();
    }
    
    private static void ConfigureSettingsMappings()
    {
        TypeAdapterConfig<SmtpSettings, SmtpSettingsDto>.NewConfig()
            .Map(dest => dest.HasPassword, src => !string.IsNullOrEmpty(src.Password));
    }
    
    private static void ConfigureWebhookMappings()
    {
        TypeAdapterConfig<WebhookTemplate, WebhookTemplateDto>.NewConfig()
            .Map(dest => dest.UsageCount, src => src.Services.Count);

        TypeAdapterConfig<WebhookCapture, WebhookCaptureDto>.NewConfig()
            .Map(dest => dest.BodySize, src => src.Body != null ? src.Body.Length : 0)
            .Map(dest => dest.Status, src => src.Status.ToString());
    }
    
    
    private static void ConfigureUserMappings()
    {
        TypeAdapterConfig<Infrastructure.Identity.ApplicationUser, UserDto>.NewConfig()
            .Map(dest => dest.IsActive, src => !src.IsDeleted)
            .Ignore(dest => dest.Role!);
    }
}
