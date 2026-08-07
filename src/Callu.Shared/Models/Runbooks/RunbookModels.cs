namespace Callu.Shared.Models.Runbooks;

public class RunbookDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    public string AuthorId { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public DateTime? LastUsedAt { get; set; }
    public int UsageCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateRunbookRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid? ServiceId { get; set; }
    public List<string> Tags { get; set; } = [];
}

public class UpdateRunbookRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid? ServiceId { get; set; }
    public List<string> Tags { get; set; } = [];
}
