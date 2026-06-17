using System.Security.Cryptography;
using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Infrastructure.Providers.Voximplant.Models;
using Callu.Shared.Models.Communication;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Service for managing Voximplant platform resources via the Management API.
/// Reads provider config from DB, creates a management client, and delegates calls.
/// </summary>
public class VoximplantManagementService(
    ICommunicationProviderRepository providerRepo,
    ITeamMemberRepository teamMemberRepo,
    Microsoft.AspNetCore.Identity.UserManager<Callu.Infrastructure.Identity.ApplicationUser> userManager,
    IHttpClientFactory httpClientFactory,
    ProviderSecretProtector secretProtector,
    ILogger<VoximplantManagementService> logger) : IVoximplantManagementService
{
    public async Task<VoxAccountInfoDto> GetAccountInfoAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(providerId, cancellationToken);
        var client = CreateClient(config);
        
        var response = await client.GetAccountInfoAsync();
        ThrowIfError(response);
        
        return new VoxAccountInfoDto
        {
            AccountId = config.AccountId,
            AccountName = response.AccountInfo?.AccountName ?? "",
            AccountEmail = response.AccountInfo?.AccountEmail ?? "",
            Balance = response.AccountInfo?.LiveBalance ?? 0,
            Currency = response.AccountInfo?.Currency ?? "USD",
            Active = response.AccountInfo?.Active ?? false
        };
    }

    public async Task<List<VoxApplicationDto>> GetApplicationsAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.GetApplicationsAsync();
        
        ThrowIfError(response);
        
        return (response.Result ?? new())
            .Select(a => new VoxApplicationDto
            {
                ApplicationId = a.ApplicationId,
                ApplicationName = a.ApplicationName,
                Modified = ParseDate(a.Modified),
                SecureRecordStorage = a.SecureRecordStorage
            })
            .ToList();
    }
    
    public async Task<VoxApplicationDto> CreateApplicationAsync(Guid providerId, CreateVoxApplicationRequest request, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.AddApplicationAsync(request.ApplicationName);
        
        ThrowIfError(response);
        
        return new VoxApplicationDto
        {
            ApplicationId = response.ApplicationId,
            ApplicationName = response.ApplicationName ?? request.ApplicationName
        };
    }
    
    public async Task DeleteApplicationAsync(Guid providerId, long applicationId, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.DelApplicationAsync(applicationId);
        ThrowIfError(response);
    }

    public async Task<List<VoxUserDto>> GetUsersAsync(Guid providerId, long applicationId, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.GetUsersAsync(applicationId);
        
        ThrowIfError(response);
        
        return (response.Result ?? new())
            .Select(u => new VoxUserDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                DisplayName = u.UserDisplayName,
                Active = u.UserActive,
                CustomData = u.UserCustomData
            })
            .ToList();
    }
    
    public async Task<VoxUserDto> CreateUserAsync(Guid providerId, CreateVoxUserRequest request, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.AddUserAsync(
            request.ApplicationId,
            request.UserName,
            request.Password,
            request.DisplayName
        );
        
        ThrowIfError(response);
        
        return new VoxUserDto
        {
            UserId = response.UserId,
            UserName = request.UserName,
            DisplayName = request.DisplayName,
            Active = true
        };
    }
    
    public async Task DeleteUserAsync(Guid providerId, long userId, long applicationId, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.DelUserAsync(userId, applicationId);
        ThrowIfError(response);
    }

    public async Task<List<VoxScenarioDto>> GetScenariosAsync(Guid providerId, long? applicationId = null, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.GetScenariosAsync(applicationId: applicationId);
        
        ThrowIfError(response);
        
        return (response.Result ?? new())
            .Select(s => new VoxScenarioDto
            {
                ScenarioId = s.ScenarioId,
                ScenarioName = s.ScenarioName,
                Modified = ParseDate(s.Modified)
            })
            .ToList();
    }
    
    public async Task<VoxScenarioDto> CreateScenarioAsync(Guid providerId, CreateVoxScenarioRequest request, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.AddScenarioAsync(
            request.Name,
            request.Script,
            applicationId: request.ApplicationId,
            rewrite: request.Rewrite,
            ruleId: request.RuleId
        );
        
        ThrowIfError(response);
        
        return new VoxScenarioDto
        {
            ScenarioId = response.ScenarioId,
            ScenarioName = request.Name
        };
    }
    
    public async Task<string?> GetScenarioScriptAsync(Guid providerId, long scenarioId, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.GetScenariosAsync(scenarioId: scenarioId, withScript: true);
        
        ThrowIfError(response);
        
        return response.Result?.FirstOrDefault()?.ScenarioScript;
    }
    
    public async Task UpdateScenarioAsync(Guid providerId, UpdateVoxScenarioRequest request, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.SetScenarioInfoAsync(
            request.ScenarioId,
            name: request.Name,
            script: request.Script
        );
        ThrowIfError(response);
    }
    
    public async Task DeleteScenarioAsync(Guid providerId, long scenarioId, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.DelScenarioAsync(scenarioId);
        ThrowIfError(response);
    }
    
    public async Task BindScenarioAsync(Guid providerId, long ruleId, long[] scenarioIds, bool bind = true, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.BindScenarioAsync(ruleId, scenarioIds);
        ThrowIfError(response);
    }

    public async Task<List<VoxRuleDto>> GetRulesAsync(Guid providerId, long applicationId, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.GetRulesAsync(applicationId, withScenarios: true);
        
        ThrowIfError(response);
        
        return (response.Result ?? new())
            .Select(r => new VoxRuleDto
            {
                RuleId = r.RuleId,
                RuleName = r.RuleName,
                RulePattern = r.RulePattern,
                RulePatternExclude = r.RulePatternExclude,
                VideoConference = r.VideoConference,
                Modified = ParseDate(r.Modified),
                Scenarios = r.Scenarios?.Select(s => new VoxScenarioDto
                {
                    ScenarioId = s.ScenarioId,
                    ScenarioName = s.ScenarioName,
                    Modified = ParseDate(s.Modified)
                }).ToList() ?? new()
            })
            .ToList();
    }
    
    public async Task<VoxRuleDto> CreateRuleAsync(Guid providerId, CreateVoxRuleRequest request, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.AddRuleAsync(
            request.ApplicationId,
            request.Name,
            request.Pattern,
            request.ScenarioIds,
            request.VideoConference
        );
        
        ThrowIfError(response);
        
        return new VoxRuleDto
        {
            RuleId = response.RuleId,
            RuleName = request.Name,
            RulePattern = request.Pattern,
            VideoConference = request.VideoConference
        };
    }
    
    public async Task DeleteRuleAsync(Guid providerId, long ruleId, long applicationId, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        var response = await client.DelRuleAsync(ruleId, applicationId);
        ThrowIfError(response);
    }

    public async Task<VoxUserSyncResult> SyncUsersAsync(Guid providerId, long applicationId, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(providerId, cancellationToken);
        int created = 0, deleted = 0, unchanged = 0;

        var teamMembers = await teamMemberRepo.GetAllAsync(cancellationToken);
        var distinctUserIds = teamMembers.Select(m => m.UserId).Distinct().ToList();
        
        var calluUsers = new List<(string UserId, string DisplayName)>();
        foreach (var userId in distinctUserIds)
        {
            var user = await userManager.FindByIdAsync(userId);
            var displayName = user != null
                ? Callu.Shared.Extensions.StringExtensions.FormatDisplayName(user.FirstName, user.LastName, user.Email) ?? userId
                : userId;
            calluUsers.Add((userId, displayName));
        }

        var voxUsersResponse = await client.GetUsersAsync(applicationId);
        ThrowIfError(voxUsersResponse);
        var voxUsers = voxUsersResponse.Result ?? [];

        var calluUsernames = calluUsers.Select(u => SanitizeUsername(u.UserId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var voxUsernames = voxUsers.ToDictionary(u => u.UserName, u => u, StringComparer.OrdinalIgnoreCase);

        foreach (var calluUser in calluUsers)
        {
            var username = SanitizeUsername(calluUser.UserId);
            if (!voxUsernames.ContainsKey(username))
            {
                try
                {
                    var password = GenerateSecurePassword();
                    var response = await client.AddUserAsync(applicationId, username, password, calluUser.DisplayName);
                    if (response.Error != null)
                        logger.LogWarning("Failed to create Vox user {Username}: {Error}", username, response.Error.Msg);
                    else
                        created++;
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "Error creating Vox user {Username}", username);
                }
            }
            else
            {
                unchanged++;
            }
        }

        foreach (var voxUser in voxUsers)
        {
            if (!calluUsernames.Contains(voxUser.UserName))
            {
                try
                {
                    await client.DelUserAsync(voxUser.UserId, applicationId);
                    deleted++;
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "Error deleting Vox user {Username}", voxUser.UserName);
                }
            }
        }
        
        logger.LogInformation("Voximplant user sync for provider {ProviderId}, app {AppId}: created={Created}, deleted={Deleted}, unchanged={Unchanged}",
            providerId, applicationId, created, deleted, unchanged);
        
        return new VoxUserSyncResult
        {
            Created = created,
            Deleted = deleted,
            Unchanged = unchanged,
        };
    }

    private async Task<VoximplantManagementClient> CreateClientAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(providerId, cancellationToken);
        return CreateClient(config);
    }
    
    private VoximplantManagementClient CreateClient(VoximplantConfig config)
    {
        var httpClient = httpClientFactory.CreateClient("Voximplant");
        return new VoximplantManagementClient(httpClient, config.AccountId, secretProtector.Unprotect(config.ApiKey), logger);
    }
    
    private async Task<VoximplantConfig> GetConfigAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var provider = await providerRepo.FindSingleAsync(p => p.Id == providerId && !p.IsDeleted, cancellationToken);
        
        if (provider == null)
            throw new KeyNotFoundException($"Provider not found: {providerId}");
        
        if (provider.ProviderType != "voximplant")
            throw new InvalidOperationException($"Provider {providerId} is not a Voximplant provider");
        
        return JsonSerializer.Deserialize<VoximplantConfig>(provider.ConfigJson ?? "{}") 
            ?? throw new InvalidOperationException("Failed to deserialize Voximplant config");
    }
    
    private static void ThrowIfError(VoxBaseResponse response)
    {
        if (response.Error != null)
            throw new InvalidOperationException($"Voximplant API error [{response.Error.Code}]: {response.Error.Msg}");
    }
    
    private static DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;
        return DateTime.TryParse(dateStr, out var dt) ? dt : null;
    }
    
    private static string SanitizeUsername(string userId)
    {
        var sanitized = userId.ToLowerInvariant()
            .Replace("@", "-at-")
            .Replace(".", "-");
        
        if (sanitized.Length > 0 && !char.IsLetterOrDigit(sanitized[0]))
            sanitized = "u" + sanitized;
        
        if (sanitized.Length > 50)
            sanitized = sanitized[..50];
        
        return sanitized;
    }
    
    private static string GenerateSecurePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%&*";
        const string all = upper + lower + digits + special;

        Span<char> password = stackalloc char[16];
        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[3] = special[RandomNumberGenerator.GetInt32(special.Length)];
        RandomNumberGenerator.GetItems<char>(all, password[4..]);
        RandomNumberGenerator.Shuffle(password);
        return new string(password);
    }
}
