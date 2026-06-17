using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Public status page for communicating service health to customers
/// </summary>
public class StatusPage : BaseEntity
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL-friendly unique slug (e.g. "acme-status")
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Slug { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [StringLength(500)]
    public string? LogoUrl { get; set; }

    [StringLength(200)]
    public string? CustomDomain { get; set; }

    public bool IsPublic { get; set; } = true;

    /// <summary>
    /// Support email shown on the public status page
    /// </summary>
    [StringLength(200)]
    [EmailAddress]
    public string? SupportEmail { get; set; }

    /// <summary>
    /// Whether visitors can subscribe to email notifications
    /// </summary>
    public bool AllowSubscriptions { get; set; } = true;

    /// <summary>
    /// Computed overall status based on component statuses
    /// </summary>
    [StringLength(50)]
    public string OverallStatus { get; set; } = "operational";

    public virtual ICollection<StatusPageComponent> Components { get; set; } = new List<StatusPageComponent>();
    public virtual ICollection<StatusPageIncident> Incidents { get; set; } = new List<StatusPageIncident>();
    public virtual ICollection<StatusPageView> Views { get; set; } = new List<StatusPageView>();
    public virtual ICollection<StatusPageSubscriber> Subscribers { get; set; } = new List<StatusPageSubscriber>();
}
