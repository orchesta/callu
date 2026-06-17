using System.ComponentModel.DataAnnotations;

namespace Callu.Shared.Models.Email;

/// <summary>
/// Email template DTOs — list, detail, create request, and update request
/// </summary>

public record EmailTemplateDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public bool IsSystem { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record EmailTemplateDetailDto : EmailTemplateDto
{
    public string HtmlBody { get; init; } = string.Empty;
    public string? PlainTextBody { get; init; }
    public string? Description { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public record CreateEmailTemplateRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Key { get; init; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Subject { get; init; } = string.Empty;

    [Required]
    public string HtmlBody { get; init; } = string.Empty;

    public string? PlainTextBody { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }
}

public record UpdateEmailTemplateRequest
{
    [StringLength(200)]
    public string? Name { get; init; }

    [StringLength(500)]
    public string? Subject { get; init; }

    public string? HtmlBody { get; init; }

    public string? PlainTextBody { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    public bool? IsActive { get; init; }
}
