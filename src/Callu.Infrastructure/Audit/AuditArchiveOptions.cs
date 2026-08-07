namespace Callu.Infrastructure.Audit;

/// <summary>Binds to the <c>Callu:AuditArchive</c> section; an empty path turns archiving off.</summary>
public class AuditArchiveOptions
{
    public const string SectionName = "Callu:AuditArchive";

    public string? Path { get; set; }

    /// <summary>How old a day must be before it is archived, so a day still being written to is left alone.</summary>
    public int AfterDays { get; set; } = 2;

    /// <summary>Ceiling on days per run, so the first run on an old install does not go on forever.</summary>
    public int MaxDaysPerRun { get; set; } = 31;

    public bool Enabled => !string.IsNullOrWhiteSpace(Path);
}
