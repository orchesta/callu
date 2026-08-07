namespace Callu.Shared.Models.Maintenance;

public class MaintenanceWindowDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public List<Guid> AffectedServiceIds { get; set; } = [];
    public bool AppliesToAllServices { get; set; }
    public string Mode { get; set; } = "SuppressAlerts";
    public string CreatedById { get; set; } = string.Empty;
    public bool IsCancelled { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateMaintenanceWindowRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public List<Guid> AffectedServiceIds { get; set; } = [];
    /// <summary>Explicit apply-to-every-service toggle; either this or <see cref="AffectedServiceIds"/> must be set.</summary>
    public bool AppliesToAllServices { get; set; }
    public string Mode { get; set; } = "SuppressAlerts";
}

public class UpdateMaintenanceWindowRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public List<Guid> AffectedServiceIds { get; set; } = [];
    public bool AppliesToAllServices { get; set; }
    public string Mode { get; set; } = "SuppressAlerts";
}
