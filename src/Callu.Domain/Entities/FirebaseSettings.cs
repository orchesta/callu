using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

public class FirebaseSettings : BaseEntity
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    public const int MaxProjectIdLength = 128;
    public const int MaxCredentialLength = 32000;

    // The project id is interpolated into the FCM URL path, so anything outside this shape could
    // point a send at another endpoint.
    private static readonly Regex ProjectIdShape =
        new("^[a-z0-9][a-z0-9-]{2,126}[a-z0-9]$", RegexOptions.CultureInvariant);

    public static bool IsValidProjectId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ProjectIdShape.IsMatch(value);

    [StringLength(MaxProjectIdLength)]
    public string? ProjectId { get; set; }

    [StringLength(MaxCredentialLength)]
    public string? ServiceAccountJson { get; set; }

    public bool IsConfigured { get; set; }

    public DateTime? LastTestedAt { get; set; }

    [StringLength(500)]
    public string? LastTestResult { get; set; }
}
