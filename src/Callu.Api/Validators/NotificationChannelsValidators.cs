using Callu.Domain.Entities;
using Callu.Shared.Models.Notifications;
using FluentValidation;

namespace Callu.Api.Validators;

public class CreateNotificationChannelRequestValidator : AbstractValidator<CreateNotificationChannelRequest>
{
    private static readonly HashSet<string> Severities = ["Low", "Medium", "High", "Critical"];

    public CreateNotificationChannelRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChannelType)
            .Must(t => Enum.TryParse<NotificationChannelType>(t, ignoreCase: true, out _))
            .WithMessage("Invalid channel type.");
        RuleFor(x => x.MinimumSeverity)
            .Must(s => s == null || Severities.Contains(s))
            .WithMessage("Minimum severity must be Low, Medium, High, or Critical.");
        RuleFor(x => x)
            .Must(x => x.NotifyOnIncidentCreated || x.NotifyOnIncidentAcknowledged || x.NotifyOnIncidentResolved)
            .WithMessage("Select at least one lifecycle trigger.");
        RuleFor(x => x).Custom(ValidateCreateConfiguration);
    }

    private static void ValidateCreateConfiguration(
        CreateNotificationChannelRequest req,
        ValidationContext<CreateNotificationChannelRequest> ctx)
    {
        if (!Enum.TryParse<NotificationChannelType>(req.ChannelType, ignoreCase: true, out var type))
            return;

        var c = req.Configuration ?? [];
        switch (type)
        {
            case NotificationChannelType.Slack:
            case NotificationChannelType.MicrosoftTeams:
                RequireHttpUrl(c, "webhookUrl", ctx);
                break;
            case NotificationChannelType.Email:
                if (!c.TryGetValue("to", out var to) || string.IsNullOrWhiteSpace(to))
                    ctx.AddFailure(nameof(req.Configuration), "Configuration 'to' (email address) is required.");
                else if (!to.Contains('@'))
                    ctx.AddFailure(nameof(req.Configuration), "Configuration 'to' must be a valid email address.");
                break;
            case NotificationChannelType.Webhook:
                RequireHttpUrl(c, "url", ctx);
                if (c.TryGetValue("method", out var m) &&
                    !m.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                    !m.Equals("PUT", StringComparison.OrdinalIgnoreCase))
                    ctx.AddFailure(nameof(req.Configuration), "Configuration 'method' must be POST or PUT.");
                break;
        }
    }

    private static void RequireHttpUrl(
        Dictionary<string, string> c,
        string key,
        ValidationContext<CreateNotificationChannelRequest> ctx)
    {
        if (!c.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
        {
            ctx.AddFailure(nameof(CreateNotificationChannelRequest.Configuration), $"{key} is required.");
            return;
        }

        if (!Uri.TryCreate(v, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            ctx.AddFailure(nameof(CreateNotificationChannelRequest.Configuration), $"{key} must be a valid http(s) URL.");
    }
}

public class UpdateNotificationChannelRequestValidator : AbstractValidator<UpdateNotificationChannelRequest>
{
    private static readonly HashSet<string> Severities = ["Low", "Medium", "High", "Critical"];

    public UpdateNotificationChannelRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MinimumSeverity)
            .Must(s => s == null || Severities.Contains(s))
            .WithMessage("Minimum severity must be Low, Medium, High, or Critical.");
        RuleFor(x => x)
            .Must(x => x.NotifyOnIncidentCreated || x.NotifyOnIncidentAcknowledged || x.NotifyOnIncidentResolved)
            .WithMessage("Select at least one lifecycle trigger.");
        RuleFor(x => x.Configuration).Custom(ValidateUpdateConfiguration);
    }

    private static void ValidateUpdateConfiguration(
        Dictionary<string, string>? c,
        ValidationContext<UpdateNotificationChannelRequest> ctx)
    {
        c ??= [];
        foreach (var key in new[] { "webhookUrl", "url" })
        {
            if (!c.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) continue;
            if (!Uri.TryCreate(v, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                ctx.AddFailure(nameof(UpdateNotificationChannelRequest.Configuration), $"{key} must be a valid http(s) URL.");
        }

        if (c.TryGetValue("to", out var to) && !string.IsNullOrWhiteSpace(to) && !to.Contains('@'))
            ctx.AddFailure(nameof(UpdateNotificationChannelRequest.Configuration), "Configuration 'to' must be a valid email address.");

        if (c.TryGetValue("method", out var m) &&
            !string.IsNullOrWhiteSpace(m) &&
            !m.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            !m.Equals("PUT", StringComparison.OrdinalIgnoreCase))
            ctx.AddFailure(nameof(UpdateNotificationChannelRequest.Configuration), "Configuration 'method' must be POST or PUT.");
    }
}
