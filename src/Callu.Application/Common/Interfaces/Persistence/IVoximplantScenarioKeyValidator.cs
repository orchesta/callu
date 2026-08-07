namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// Validates VoxEngine scenario API keys against persisted Voximplant provider config (factory-scoped DB access).
/// </summary>
public interface IVoximplantScenarioKeyValidator
{
    Task<bool> ValidateAsync(string apiKey, CancellationToken cancellationToken = default);
}
