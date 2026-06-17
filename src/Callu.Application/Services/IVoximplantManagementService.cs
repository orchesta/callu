using Callu.Shared.Models.Communication;

namespace Callu.Application.Services;

/// <summary>
/// Service for managing Voximplant platform resources (applications, users, scenarios, rules)
/// via the Management API
/// </summary>
public interface IVoximplantManagementService
{
    /// <summary>
    /// Gets account info from Voximplant for the given provider
    /// </summary>
    Task<VoxAccountInfoDto> GetAccountInfoAsync(Guid providerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all applications in the Voximplant account
    /// </summary>
    Task<List<VoxApplicationDto>> GetApplicationsAsync(Guid providerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new Voximplant application
    /// </summary>
    Task<VoxApplicationDto> CreateApplicationAsync(Guid providerId, CreateVoxApplicationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a Voximplant application
    /// </summary>
    Task DeleteApplicationAsync(Guid providerId, long applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all users in a Voximplant application
    /// </summary>
    Task<List<VoxUserDto>> GetUsersAsync(Guid providerId, long applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user in a Voximplant application
    /// </summary>
    Task<VoxUserDto> CreateUserAsync(Guid providerId, CreateVoxUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user from a Voximplant application
    /// </summary>
    Task DeleteUserAsync(Guid providerId, long userId, long applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists scenarios, optionally filtered by application
    /// </summary>
    Task<List<VoxScenarioDto>> GetScenariosAsync(Guid providerId, long? applicationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new scenario with the given script
    /// </summary>
    Task<VoxScenarioDto> CreateScenarioAsync(Guid providerId, CreateVoxScenarioRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the JavaScript source of a scenario
    /// </summary>
    Task<string?> GetScenarioScriptAsync(Guid providerId, long scenarioId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a scenario's name and/or script
    /// </summary>
    Task UpdateScenarioAsync(Guid providerId, UpdateVoxScenarioRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a scenario
    /// </summary>
    Task DeleteScenarioAsync(Guid providerId, long scenarioId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds or unbinds scenarios to a routing rule
    /// </summary>
    Task BindScenarioAsync(Guid providerId, long ruleId, long[] scenarioIds, bool bind = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all routing rules in an application
    /// </summary>
    Task<List<VoxRuleDto>> GetRulesAsync(Guid providerId, long applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new routing rule
    /// </summary>
    Task<VoxRuleDto> CreateRuleAsync(Guid providerId, CreateVoxRuleRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a routing rule
    /// </summary>
    Task DeleteRuleAsync(Guid providerId, long ruleId, long applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes CalluApp users with Voximplant users for the given application.
    /// Creates missing users, deletes orphaned Voximplant users.
    /// </summary>
    Task<VoxUserSyncResult> SyncUsersAsync(Guid providerId, long applicationId, CancellationToken cancellationToken = default);
}
