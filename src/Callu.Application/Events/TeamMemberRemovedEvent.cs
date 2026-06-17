namespace Callu.Application.Events;

/// <summary>
/// Fired when a team member is removed — active provider may delete the corresponding user
/// </summary>
public record TeamMemberRemovedEvent(string UserId, Guid TeamId) : ICommunicationEvent;
