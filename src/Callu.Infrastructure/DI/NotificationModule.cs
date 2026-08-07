using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.DI;

/// <summary>
/// Notification services — channel dispatchers, orchestrator, queries
/// </summary>
internal static class NotificationModule
{
    internal static IServiceCollection AddNotificationModule(this IServiceCollection services)
    {
        services.AddScoped<Application.Services.INotificationChannelDispatcher, Services.EmailChannelDispatcher>();
        services.AddScoped<Application.Services.INotificationChannelDispatcher, Services.SmsChannelDispatcher>();
        services.AddScoped<Application.Services.INotificationChannelDispatcher, Services.VoiceCallChannelDispatcher>();

        services.AddScoped<Application.Services.INotificationDispatcher, Services.NotificationDispatcher>();

        // Voice-call flood control: opt-in via Callu:Notifications:VoiceCallCooldownSeconds.
        services.AddOptions<Application.Services.VoiceCallCoalescingOptions>()
            .BindConfiguration("Callu:Notifications");
        services.AddScoped<Application.Services.IVoiceCallCoalescingGuard, Services.VoiceCallCoalescingGuard>();

        services.AddScoped<Application.Services.INotificationQueryService, Services.NotificationQueryService>();

        services.AddScoped<Application.Services.INotificationChannelService, Services.NotificationChannelService>();

        services.AddHostedService<BackgroundJobs.NotificationReaperBackgroundService>();

        return services;
    }
}
