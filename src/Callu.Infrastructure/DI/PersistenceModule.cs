using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Mapping;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Seeding;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Persistence.UnitOfWork;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Callu.Infrastructure.DI;

/// <summary>
/// Persistence infrastructure — DbContext, Identity, repositories, UoW, transactions, seeding
/// </summary>
internal static class PersistenceModule
{
    internal static IServiceCollection AddPersistenceModule(this IServiceCollection services, string connectionString, bool enableParameterLogging)
    {
        services.AddNpgsqlDataSource(connectionString, builder =>
        {
            if (enableParameterLogging)
                builder.EnableParameterLogging();
            builder.UseNodaTime();
        });

        services.AddScoped<Persistence.Interceptors.AuditableEntityInterceptor>();

        // The scoped context deliberately does NOT retry: it carries transactions and the bus outbox,
        // and replaying on the same change tracker duplicates rows and strands outbox messages.
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
            var auditInterceptor = sp.GetRequiredService<Persistence.Interceptors.AuditableEntityInterceptor>();
            options.UseNpgsql(dataSource, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                npgsqlOptions.UseNodaTime();
            })
            .AddInterceptors(auditInterceptor)
            .ConfigureWarnings(w => w.Throw(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        // Factory contexts DO retry — no shared transaction or outbox, so replaying one call is safe.
        // Registered by hand: AddDbContextFactory TryAdds the options above, so retry would be dead code.
        services.AddScoped<IDbContextFactory<ApplicationDbContext>>(sp =>
        {
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
            var auditInterceptor = sp.GetRequiredService<Persistence.Interceptors.AuditableEntityInterceptor>();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(dataSource, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    npgsqlOptions.UseNodaTime();
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(2),
                        errorCodesToAdd: null);
                })
                .AddInterceptors(auditInterceptor)
                // Hand-built options resolve no ILoggerFactory on their own. Without this, contexts
                // from the factory log nothing at all — including the retry warnings that are the
                // only evidence the resilience configured just above ever engaged.
                .UseApplicationServiceProvider(sp)
                .Options;

            return new ResilientDbContextFactory(options);
        });

        services.AddSingleton<NodaTime.IClock>(NodaTime.SystemClock.Instance);
        services.AddSingleton<NodaTime.IDateTimeZoneProvider>(NodaTime.DateTimeZoneProviders.Tzdb);

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 12;
            options.Password.RequiredUniqueChars = 4;

            options.User.RequireUniqueEmail = true;

            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        services.AddScoped<ITransactionManager, TransactionManager>();

        services.AddMappingConfig();

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        services.AddScoped<IIncidentRepository, IncidentRepository>();
        services.AddScoped<IIncidentNoteRepository, IncidentNoteRepository>();
        services.AddScoped<IIncidentTimelineEventRepository, IncidentTimelineEventRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<ITeamMemberRepository, TeamMemberRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IScheduleRepository, ScheduleRepository>();
        services.AddScoped<IScheduleRotationRepository, ScheduleRotationRepository>();
        services.AddScoped<IScheduleOccurrenceRepository, ScheduleOccurrenceRepository>();
        services.AddScoped<IOnCallOverrideRepository, OnCallOverrideRepository>();
        services.AddScoped<IEscalationPolicyRepository, EscalationPolicyRepository>();
        services.AddScoped<IEscalationStepRepository, EscalationStepRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IServiceDependencyRepository, ServiceDependencyRepository>();
        services.AddScoped<ICallLogRepository, CallLogRepository>();
        services.AddScoped<IIntegrationRepository, IntegrationRepository>();
        services.AddScoped<IWebhookTemplateRepository, WebhookTemplateRepository>();
        services.AddScoped<IWebhookCaptureRepository, WebhookCaptureRepository>();
        services.AddScoped<ICommunicationProviderRepository, CommunicationProviderRepository>();
        services.AddScoped<ICapabilityProviderMappingRepository, CapabilityProviderMappingRepository>();
        services.AddScoped<ISipTrunkSettingsRepository, SipTrunkSettingsRepository>();
        services.AddScoped<ISmtpSettingsRepository, SmtpSettingsRepository>();
        services.AddScoped<IFirebaseSettingsRepository, FirebaseSettingsRepository>();
        services.AddScoped<IUserPushDeviceRepository, UserPushDeviceRepository>();
        services.AddScoped<IOrganizationSettingsRepository, OrganizationSettingsRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<IConferenceRoomRepository, ConferenceRoomRepository>();
        services.AddScoped<IConferenceParticipantRepository, ConferenceParticipantRepository>();
        services.AddScoped<ITtsMessageTemplateRepository, TtsMessageTemplateRepository>();
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<IStatusPageRepository, StatusPageRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserContactRepository, UserContactRepository>();
        services.AddScoped<ITenantUserReadRepository, TenantUserReadRepository>();

        services.AddScoped<IDbSeeder, DbSeeder>();

        services.AddScoped<ICalluVoiceCallbackPersistence, Persistence.CalluVoice.CalluVoiceCallbackPersistence>();

        services.AddSingleton<Email.SmtpPasswordProtector>();
        services.AddSingleton<Push.FirebaseCredentialProtector>();
        services.AddSingleton<Push.FcmAccessTokenProvider>();

        return services;
    }

    /// <summary>
    /// Hands out contexts built from the retrying options above. Each call gets a fresh context, so
    /// a replayed operation never inherits a previous attempt's tracked entities.
    /// </summary>
    private sealed class ResilientDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}
