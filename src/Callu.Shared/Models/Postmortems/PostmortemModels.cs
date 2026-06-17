namespace Callu.Shared.Models.Postmortems;

public class PostmortemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? RootCause { get; set; }
    public Guid IncidentId { get; set; }
    public string? IncidentTitle { get; set; }
    public string AuthorId { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public DateTime? PublishedAt { get; set; }
    public List<PostmortemActionItemDto> ActionItems { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class PostmortemActionItemDto
{
    public string Description { get; set; } = string.Empty;
    public string? AssigneeId { get; set; }
    public string? AssigneeName { get; set; }
    public bool IsComplete { get; set; }
    public DateTime? DueDate { get; set; }
}

public class CreatePostmortemRequest
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? RootCause { get; set; }
    public Guid IncidentId { get; set; }
    public List<PostmortemActionItemDto> ActionItems { get; set; } = [];
}

public class UpdatePostmortemRequest
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? RootCause { get; set; }
    public List<PostmortemActionItemDto> ActionItems { get; set; } = [];
}
