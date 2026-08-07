namespace Callu.Domain.Enums;

/// <summary>
/// Direction of data flow for an integration
/// </summary>
public enum IntegrationDirection
{
    /// <summary>
    /// Receives alerts from external systems (inbound webhooks)
    /// </summary>
    Inbound = 1,
    
    /// <summary>
    /// Sends feedback/notifications to external systems (outbound webhooks)
    /// </summary>
    Outbound = 2,
    
    /// <summary>
    /// Both receives and sends data
    /// </summary>
    Bidirectional = 3
}
