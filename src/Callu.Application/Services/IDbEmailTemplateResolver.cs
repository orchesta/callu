namespace Callu.Application.Services;

/// <summary>Subject + HTML body of a DB email template with variables substituted.</summary>
public sealed record RenderedEmailTemplate(string Subject, string HtmlBody);

/// <summary>
/// Resolves an operator-edited email template from the database, or null when there is no active row.
/// </summary>
public interface IDbEmailTemplateResolver
{
    Task<RenderedEmailTemplate?> TryRenderAsync(
        string key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken = default);

    /// <summary>Drop the cached copy after an editor create/update/delete.</summary>
    Task InvalidateAsync(string key, CancellationToken cancellationToken = default);
}
