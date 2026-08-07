namespace Callu.Application.Events;

/// <summary>
/// Fired when a team member is added — active provider may create a corresponding user
/// </summary>
public record TeamMemberAddedEvent(string UserId, string DisplayName, Guid TeamId) : ICommunicationEvent;
