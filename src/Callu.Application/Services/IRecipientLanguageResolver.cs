namespace Callu.Application.Services;

/// <summary>Decides which language a given person is written to and spoken to in.</summary>
public interface IRecipientLanguageResolver
{
    /// <summary>Always returns a language this installation can speak.</summary>
    Task<string> ResolveAsync(string? userId, CancellationToken cancellationToken = default);
}
