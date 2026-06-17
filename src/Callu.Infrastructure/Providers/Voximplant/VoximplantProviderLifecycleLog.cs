using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>
/// Source-generated log methods for VoximplantProviderLifecycle — zero-allocation, compile-time template validation.
/// </summary>
internal static partial class VoximplantProviderLifecycleLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Voximplant provisioning complete for provider {ProviderId}. Created: {Created}, Existing: {Existing}")]
    public static partial void ProvisioningComplete(ILogger logger, Guid providerId, string created, string existing);

    [LoggerMessage(Level = LogLevel.Error, Message = "Voximplant provisioning failed for provider {ProviderId}")]
    public static partial void ProvisioningFailed(ILogger logger, Exception ex, Guid providerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get provisioning status for provider {ProviderId}")]
    public static partial void ProvisioningStatusFailed(ILogger logger, Exception ex, Guid providerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Voximplant not provisioned, skipping user creation for {UserId}")]
    public static partial void SkippingUserCreation(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create Vox user {Username}: {Error}")]
    public static partial void VoxUserCreationFailed(ILogger logger, string username, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created Vox user {Username} (ID: {VoxUserId})")]
    public static partial void VoxUserCreated(ILogger logger, string username, long voxUserId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error creating Vox user for {UserId}")]
    public static partial void VoxUserCreationError(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted Vox user {Username} (ID: {VoxUserId})")]
    public static partial void VoxUserDeleted(ILogger logger, string username, long voxUserId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error deleting Vox user for {UserId}")]
    public static partial void VoxUserDeletionError(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User sync complete. Created: {Created}, Deleted: {Deleted}, Unchanged: {Unchanged}")]
    public static partial void UserSyncComplete(ILogger logger, int created, int deleted, int unchanged);

    [LoggerMessage(Level = LogLevel.Error, Message = "User sync failed for provider {ProviderId}")]
    public static partial void UserSyncFailed(ILogger logger, Exception ex, Guid providerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scenario '{Name}' found (ID: {Id}), updating script ({ScriptLen} chars)...")]
    public static partial void ScenarioFound(ILogger logger, string name, long id, int scriptLen);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to update scenario '{Name}': {Error}")]
    public static partial void ScenarioUpdateFailed(ILogger logger, string name, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scenario '{Name}' updated successfully")]
    public static partial void ScenarioUpdated(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scenario '{Name}' not found, creating new ({ScriptLen} chars)...")]
    public static partial void ScenarioCreating(ILogger logger, string name, int scriptLen);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Decrypted config JSON: {ConfigJson}")]
    public static partial void DecryptedConfig(ILogger logger, string configJson);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Voximplant config loaded: AccountId set={HasAccountId}, ApiKey set={HasApiKey}")]
    public static partial void ConfigLoaded(ILogger logger, bool hasAccountId, bool hasApiKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to decrypt config, returning raw")]
    public static partial void DecryptConfigFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse config JSON, returning raw")]
    public static partial void ParseConfigFailed(ILogger logger, Exception ex);
}
