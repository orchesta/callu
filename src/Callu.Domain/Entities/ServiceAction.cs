using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>An operator-defined outbound HTTP action a responder can run from an incident.</summary>
public class ServiceAction : BaseEntity
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;
    public const int MaxUrlLength = 500;
    public const int MaxHttpMethodLength = 10;
    public const int MaxContentTypeLength = 50;
    public const int MaxHeadersLength = 4000;
    public const int MaxPayloadTemplateLength = 8000;
    public const int MaxSecretLength = 256;
    public const int MaxSignatureHeaderLength = 100;
    public const int MaxPerService = 50;

    public Guid ServiceId { get; set; }

    public virtual Service? Service { get; set; }

    [Required]
    [StringLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(MaxDescriptionLength)]
    public string? Description { get; set; }

    [Required]
    [StringLength(MaxUrlLength)]
    public string Url { get; set; } = string.Empty;

    [StringLength(MaxHttpMethodLength)]
    public string HttpMethod { get; set; } = "POST";

    [StringLength(MaxContentTypeLength)]
    public string ContentType { get; set; } = "application/json";

    /// <summary>Custom headers as a JSON object of string values.</summary>
    public string? HeadersJson { get; set; }

    /// <summary>Scriban template for the request body; optional — a GET trigger has no body.</summary>
    public string? PayloadTemplate { get; set; }

    /// <summary>HMAC secret for signing this action's requests; write-only through the API.</summary>
    [StringLength(MaxSecretLength)]
    public string? Secret { get; set; }

    [StringLength(MaxSignatureHeaderLength)]
    public string? SignatureHeader { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int DisplayOrder { get; set; } = 0;
}
