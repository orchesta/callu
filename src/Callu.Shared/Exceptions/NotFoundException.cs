namespace Callu.Shared.Exceptions;

/// <summary>
/// Exception when an entity is not found
/// </summary>
public class NotFoundException : DomainException
{
    public string EntityType { get; }
    public object? EntityId { get; }
    
    public NotFoundException(string entityType, object? entityId = null)
        : base($"{entityType} not found" + (entityId != null ? $": {entityId}" : ""), "NOT_FOUND")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
    
    public static NotFoundException For<T>(object id) => new(typeof(T).Name, id);
}