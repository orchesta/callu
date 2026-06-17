namespace Callu.Domain.Enums;

/// <summary>
/// Type of integration
/// </summary>
public enum IntegrationType
{
    DataDog = 1,
    Prometheus = 2,
    Grafana = 3,
    PagerDuty = 4,
    Slack = 5,
    MsTeams = 6,
    Discord = 7,
    Email = 8,
    Sms = 9,
    Webhook = 10,
    Custom = 11
}
