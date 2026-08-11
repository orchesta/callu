using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Callu.Domain.Base;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Messaging.Persistence;

namespace Callu.Infrastructure.Persistence;

/// <summary>
/// Application database context (single-tenant).
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Incident> Incidents { get; set; } = null!;
    public DbSet<IncidentNote> IncidentNotes { get; set; } = null!;
    public DbSet<IncidentTimelineEvent> IncidentTimelineEvents { get; set; } = null!;
    public DbSet<Team> Teams { get; set; } = null!;
    public DbSet<TeamMember> TeamMembers { get; set; } = null!;
    public DbSet<Service> Services { get; set; } = null!;
    public DbSet<Schedule> Schedules { get; set; } = null!;
    public DbSet<ScheduleRotation> ScheduleRotations { get; set; } = null!;
    public DbSet<ScheduleOccurrence> ScheduleOccurrences { get; set; } = null!;
    public DbSet<OnCallOverride> OnCallOverrides { get; set; } = null!;
    public DbSet<EscalationPolicy> EscalationPolicies { get; set; } = null!;
    public DbSet<EscalationStep> EscalationSteps { get; set; } = null!;
    public DbSet<EscalationStepUser> EscalationStepUsers { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<AuditChainState> AuditChainStates { get; set; } = null!;
    public DbSet<AuditSigningKey> AuditSigningKeys { get; set; } = null!;
    public DbSet<AuditArchiveRun> AuditArchiveRuns { get; set; } = null!;

    public DbSet<ServiceDependency> ServiceDependencies { get; set; } = null!;
    public DbSet<Integration> Integrations { get; set; } = null!;

    public DbSet<NotificationPreference> NotificationPreferences { get; set; } = null!;

    public DbSet<SmtpSettings> SmtpSettings { get; set; } = null!;
    public DbSet<FirebaseSettings> FirebaseSettings { get; set; } = null!;
    public DbSet<UserPushDevice> UserPushDevices { get; set; } = null!;

    public DbSet<OrganizationSettings> OrganizationSettings { get; set; } = null!;

    public DbSet<WebhookCapture> WebhookCaptures { get; set; } = null!;
    public DbSet<WebhookTemplate> WebhookTemplates { get; set; } = null!;

    public DbSet<CommunicationProvider> CommunicationProviders { get; set; } = null!;
    public DbSet<SipTrunkSettings> SipTrunkSettings { get; set; } = null!;
    public DbSet<CapabilityProviderMapping> CapabilityProviderMappings { get; set; } = null!;

    public DbSet<ConferenceRoom> ConferenceRooms { get; set; } = null!;
    public DbSet<ConferenceParticipant> ConferenceParticipants { get; set; } = null!;

    public DbSet<TtsMessageTemplate> TtsMessageTemplates { get; set; } = null!;

    public DbSet<CallLog> CallLogs { get; set; } = null!;
    public DbSet<CallToken> CallTokens { get; set; } = null!;

    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;

    public DbSet<EmailTemplate> EmailTemplates { get; set; } = null!;

    public DbSet<StatusPage> StatusPages { get; set; } = null!;
    public DbSet<StatusPageComponent> StatusPageComponents { get; set; } = null!;
    public DbSet<StatusPageIncident> StatusPageIncidents { get; set; } = null!;
    public DbSet<StatusPageIncidentUpdate> StatusPageIncidentUpdates { get; set; } = null!;
    public DbSet<StatusPageView> StatusPageViews { get; set; } = null!;
    public DbSet<StatusPageSubscriber> StatusPageSubscribers { get; set; } = null!;

    public DbSet<AlertRule> AlertRules { get; set; } = null!;

    public DbSet<Postmortem> Postmortems { get; set; } = null!;
    public DbSet<Runbook> Runbooks { get; set; } = null!;
    public DbSet<MaintenanceWindow> MaintenanceWindows { get; set; } = null!;
    public DbSet<NotificationChannel> NotificationChannels { get; set; } = null!;
    public DbSet<WebhookDelivery> WebhookDeliveries { get; set; } = null!;

    public DbSet<OutboxEntry> OutboxEntries { get; set; } = null!;

    public DbSet<InboxEntry> InboxEntries { get; set; } = null!;
    public DbSet<NotificationChannelDelivery> NotificationChannelDeliveries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            // A filtered read would hide a row the unique index still sees, so the dedupe read says
            // "new" and the insert answers 23505.
            if (entityType.ClrType == typeof(AuditLog)
                || entityType.ClrType == typeof(OutboxEntry)
                || entityType.ClrType == typeof(InboxEntry))
                continue;

            var method = typeof(ApplicationDbContext)
                .GetMethod(nameof(ApplySoftDeleteFilter),
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType.ClrType);

            method.Invoke(this, [modelBuilder]);
        }

        modelBuilder.Entity<ApplicationUser>().HasQueryFilter(u => !u.IsDeleted);

        ConfigureIncident(modelBuilder);
        ConfigureTeam(modelBuilder);
        ConfigureService(modelBuilder);
        ConfigureIntegration(modelBuilder);
        ConfigureSchedule(modelBuilder);
        ConfigureEscalationPolicy(modelBuilder);
        ConfigureNotification(modelBuilder);
        ConfigureAuditLog(modelBuilder);
        ConfigureConference(modelBuilder);
        ConfigureTtsTemplate(modelBuilder);
        ConfigureCallLog(modelBuilder);
        ConfigureRefreshToken(modelBuilder);
        ConfigureEmailTemplate(modelBuilder);
        ConfigureStatusPage(modelBuilder);
        ConfigurePostmortem(modelBuilder);

        modelBuilder.Entity<OrganizationSettings>(entity =>
        {
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_OrganizationSettings_Singleton",
                "\"Id\" = '00000000-0000-0000-0000-000000000001'"));
        });

        modelBuilder.Entity<FirebaseSettings>(entity =>
        {
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_FirebaseSettings_Singleton",
                "\"Id\" = '00000000-0000-0000-0000-000000000003'"));
        });

        modelBuilder.Entity<UserPushDevice>(entity =>
        {
            entity.HasIndex(e => e.PushToken)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<CapabilityProviderMapping>(entity =>
        {
            // One live route per channel. The filter is the predicate the registry reads with, so a
            // row an operator switched off stays ignorable instead of becoming an unresolvable conflict.
            entity.HasIndex(e => e.Capability)
                .IsUnique()
                .HasFilter("\"IsEnabled\" AND \"IsDeleted\" = false");
        });

        modelBuilder.Entity<OutboxEntry>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt, e.CreatedAt });
            entity.HasIndex(e => e.SentAt);
        });

        modelBuilder.Entity<InboxEntry>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt });
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.HasIndex(e => new { e.IncidentId, e.AttemptedAt });
            entity.HasIndex(e => e.NextRetryAt)
                .HasFilter("\"Status\" = 'Retrying'");
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<NotificationChannelDelivery>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => e.NextRetryAt)
                .HasFilter("\"Status\" = 'Retrying'");

            // This table grows per incident per channel, so reading a channel's history has to be an
            // index scan rather than a sort over everything ever delivered.
            entity.HasIndex(e => new { e.ChannelId, e.AttemptedAt });
        });

        ConfigurePostgreSql(modelBuilder);
    }

    private void ApplySoftDeleteFilter<T>(ModelBuilder modelBuilder) where T : BaseEntity
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => !e.IsDeleted);
    }

    private static void ConfigureIncident(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Severity);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.Status, e.IsDeleted });
            entity.HasIndex(e => new { e.Status, e.StartedAt });
            entity.HasIndex(e => e.ServiceId);

            entity.HasIndex(e => new { e.ServiceId, e.ExternalAlertId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false AND \"ExternalAlertId\" IS NOT NULL AND \"Status\" NOT IN (3, 4)");

            entity.HasOne(e => e.Service)
                .WithMany(s => s.Incidents)
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Team)
                .WithMany(t => t.Incidents)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IncidentNote>(entity =>
        {
            entity.HasIndex(e => e.IncidentId);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.Incident)
                .WithMany(i => i.Notes)
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IncidentTimelineEvent>(entity =>
        {
            entity.HasIndex(e => e.IncidentId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.EventType);

            entity.HasOne(e => e.Incident)
                .WithMany(i => i.TimelineEvents)
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureTeam(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique().HasFilter("\"IsDeleted\" = false");
        });

        modelBuilder.Entity<TeamMember>(entity =>
        {
            entity.HasIndex(e => new { e.TeamId, e.UserId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.HasOne(e => e.Team)
                .WithMany(t => t.Members)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureService(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Service>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique().HasFilter("\"IsDeleted\" = false");
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.WebhookToken).IsUnique().HasFilter("\"IsDeleted\" = false");

            entity.HasOne(e => e.Team)
                .WithMany(t => t.Services)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.WebhookTemplate)
                .WithMany(t => t.Services)
                .HasForeignKey(e => e.WebhookTemplateId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.ProviderId);

            entity.Ignore(e => e.WebhookEnabled);
        });

        modelBuilder.Entity<WebhookCapture>(entity =>
        {
            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => e.IntegrationId);
            entity.HasIndex(e => e.CapturedAt);
            entity.HasIndex(e => e.Status);

            // Cascade is stated on both owners: EF's default for an optional FK is ClientSetNull,
            // which would silently stop deleting captures with their endpoint.
            entity.HasOne(e => e.Service)
                .WithMany(s => s.WebhookCaptures)
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Integration)
                .WithMany()
                .HasForeignKey(e => e.IntegrationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookTemplate>(entity =>
        {
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.IsBuiltIn);
        });

        modelBuilder.Entity<ServiceDependency>(entity =>
        {
            entity.HasIndex(e => new { e.ServiceId, e.DependsOnServiceId }).IsUnique().HasFilter("\"IsDeleted\" = false");

            entity.HasOne(e => e.Service)
                .WithMany(s => s.Dependencies)
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DependsOnService)
                .WithMany(s => s.DependentServices)
                .HasForeignKey(e => e.DependsOnServiceId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureIntegration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Integration>(entity =>
        {
            entity.HasIndex(e => e.WebhookToken).IsUnique().HasFilter("\"IsDeleted\" = false");
            entity.HasIndex(e => e.ServiceId);

            entity.HasOne(e => e.WebhookTemplate)
                .WithMany()
                .HasForeignKey(e => e.WebhookTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureSchedule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Schedule>(entity =>
        {
            entity.HasIndex(e => e.Name);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduleRotation>(entity =>
        {
            entity.HasIndex(e => new { e.ScheduleId, e.HandoverStartLocal });
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.Schedule)
                .WithMany(s => s.Rotations)
                .HasForeignKey(e => e.ScheduleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduleOccurrence>(entity =>
        {
            entity.HasIndex(e => new { e.ScheduleId, e.StartUtc, e.EndUtc });
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.StartUtc });

            entity.HasOne(e => e.Schedule)
                .WithMany(s => s.Occurrences)
                .HasForeignKey(e => e.ScheduleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Rotation)
                .WithMany(r => r.Occurrences)
                .HasForeignKey(e => e.RotationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureEscalationPolicy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EscalationPolicy>(entity =>
        {
            entity.HasIndex(e => e.Name);

            // Declared rather than left to the foreign key: adding the filtered index below would
            // otherwise make EF drop the automatic one, and reads that include inactive policies
            // would lose it.
            entity.HasIndex(e => e.TeamId);

            // Which policy pages a team has to be one row, not a tiebreak. The filter is the exact
            // predicate the paging read uses; a team-less policy is NULL here and never collides.
            // Named, so it sits alongside the plain foreign-key index rather than replacing it —
            // reads that also want inactive policies still need that one.
            entity.HasIndex(e => e.TeamId, "IX_EscalationPolicies_TeamId_Active")
                .IsUnique()
                .HasFilter("\"IsActive\" AND \"IsDeleted\" = false");

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EscalationStep>(entity =>
        {
            entity.HasIndex(e => new { e.EscalationPolicyId, e.Level })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.HasOne(e => e.EscalationPolicy)
                .WithMany(p => p.Steps)
                .HasForeignKey(e => e.EscalationPolicyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Schedule)
                .WithMany()
                .HasForeignKey(e => e.ScheduleId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EscalationStepUser>(entity =>
        {
            entity.HasKey(e => new { e.EscalationStepId, e.UserId });

            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.EscalationStep)
                .WithMany(s => s.TargetedUsers)
                .HasForeignKey(e => e.EscalationStepId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(e => !e.EscalationStep.IsDeleted);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OnCallOverride>(entity =>
        {
            entity.HasIndex(e => e.ScheduleId);
            entity.HasIndex(e => new { e.StartUtc, e.EndUtc });
            entity.HasIndex(e => e.OverrideUserId);

            entity.HasOne(e => e.Schedule)
                .WithMany()
                .HasForeignKey(e => e.ScheduleId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureNotification(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.IsRead);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.UserId, e.IsRead });

            entity.HasIndex(e => e.DedupeKey)
                .IsUnique()
                .HasFilter("\"DedupeKey\" IS NOT NULL");

            entity.HasIndex(e => new { e.DeliveryStatus, e.NextRetryAt });

            // The unconfirmed-call sweep runs every two minutes over a table that grows per page, and
            // "Delivered" matches nearly all of it; this is what keeps that an index scan.
            entity.HasIndex(e => new { e.Type, e.UnconfirmedCallReportedAt, e.LastAttemptAt });

            entity.HasOne(e => e.Incident)
                .WithMany(i => i.Notifications)
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditChainState>();

        modelBuilder.Entity<AuditSigningKey>(entity =>
        {
            entity.HasIndex(e => e.KeyId).IsUnique();
            // "The key to sign with" has to be one row, not whichever row came back first. Indexed on
            // IsDeleted because the filter already fixes it to false: unique over a constant is how
            // Postgres is told at most one row may match, and a unique index over the nullable
            // RetiredAt would not, since it treats NULLs as distinct from each other.
            entity.HasIndex(e => e.IsDeleted)
                .IsUnique()
                .HasFilter("\"RetiredAt\" IS NULL AND \"IsDeleted\" = false");
        });

        modelBuilder.Entity<AuditArchiveRun>(entity => entity.HasIndex(e => e.Day).IsUnique());

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(e => e.ActorId);
            entity.HasIndex(e => e.Action);
            entity.HasIndex(e => e.EventName);
            entity.HasIndex(e => e.ResourceType);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.ResourceType, e.ResourceId });

            // Verification walks the chain in sequence order and retention has to delete a prefix
            // of it, so both scan this column over a table that grows with every incident.
            // Unique because the sealer assigns the next number in memory: two of them running at once
            // would hand out the same one, and a chain with a repeated sequence can never verify again.
            entity.HasIndex(e => e.Sequence)
                .IsUnique()
                .HasFilter("\"Sequence\" IS NOT NULL");

            // The sealer reads only the rows it has not sealed yet, which are a small tail.
            entity.HasIndex(e => new { e.CreatedAt, e.Id })
                .HasFilter("\"Sequence\" IS NULL");
        });
    }

    private static void ConfigureConference(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConferenceRoom>(entity =>
        {
            entity.HasIndex(e => e.RoomToken).IsUnique();
            entity.HasIndex(e => e.IncidentId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ExpiresAt);

            entity.HasIndex(e => e.IncidentId)
                .IsUnique()
                .HasFilter("\"Status\" = 0")
                .HasDatabaseName("IX_ConferenceRooms_IncidentId_Active");

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConferenceParticipant>(entity =>
        {
            entity.HasIndex(e => e.ParticipantToken).IsUnique();
            entity.HasIndex(e => e.ConferenceRoomId);
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.ConferenceRoom)
                .WithMany(r => r.Participants)
                .HasForeignKey(e => e.ConferenceRoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureTtsTemplate(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TtsMessageTemplate>(entity =>
        {
            entity.HasIndex(e => e.LanguageCode).IsUnique().HasFilter("\"IsDeleted\" = false");
        });
    }

    private static void ConfigureCallLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CallLog>(entity =>
        {
            entity.HasIndex(e => e.IncidentId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.InitiatedAt);

            entity.HasIndex(e => e.NextRetryAt);

            // One attempt, one row: the callback dedupes on this token and two deliveries can arrive at once.
            entity.HasIndex(e => e.CallToken)
                .IsUnique()
                .HasFilter("\"CallToken\" IS NOT NULL AND \"IsDeleted\" = false");

            // Read by the double-dial guard on every retry, so it has to be an index scan.
            entity.HasIndex(e => e.AttemptId)
                .HasFilter("\"AttemptId\" IS NOT NULL");

            entity.HasOne(e => e.Incident)
                .WithMany(i => i.CallLogs)
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CallToken>(entity =>
        {
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
        });
    }

    private static void ConfigureRefreshToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.FamilyId);
            entity.HasIndex(e => e.ExpiresAt);

            entity.Ignore(e => e.IsExpired);
            entity.Ignore(e => e.IsRevoked);
            entity.Ignore(e => e.IsActive);
        });
    }

    private static void ConfigureEmailTemplate(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailTemplate>(entity =>
        {
            entity.HasIndex(e => e.Key).IsUnique().HasFilter("\"IsDeleted\" = false");
            entity.HasIndex(e => e.IsSystem);
        });
    }

    private static void ConfigureStatusPage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StatusPage>(entity =>
        {
            entity.HasIndex(e => e.Slug).IsUnique().HasFilter("\"IsDeleted\" = false");
            entity.HasIndex(e => e.IsPublic);
        });

        modelBuilder.Entity<StatusPageComponent>(entity =>
        {
            entity.HasIndex(e => e.StatusPageId);
            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => new { e.StatusPageId, e.DisplayOrder });

            entity.HasOne(e => e.StatusPage)
                .WithMany(p => p.Components)
                .HasForeignKey(e => e.StatusPageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StatusPageIncident>(entity =>
        {
            entity.HasIndex(e => e.StatusPageId);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.StatusPage)
                .WithMany(p => p.Incidents)
                .HasForeignKey(e => e.StatusPageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StatusPageIncidentUpdate>(entity =>
        {
            entity.HasIndex(e => e.StatusPageIncidentId);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.Incident)
                .WithMany(i => i.Updates)
                .HasForeignKey(e => e.StatusPageIncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePostmortem(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Postmortem>(entity =>
        {
            entity.HasIndex(e => e.IncidentId)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurePostgreSql(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(BaseEntity.RowVersion))
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureNewEntityIds();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        EnsureNewEntityIds();
        return base.SaveChanges();
    }

    private void EnsureNewEntityIds()
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.Id == Guid.Empty)
            {
                entry.Entity.Id = Guid.NewGuid();
            }
        }
    }
}
