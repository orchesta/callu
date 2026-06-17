using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Infrastructure.Providers.Voximplant.Models;
using Callu.Shared.Models.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>
/// Voximplant-specific lifecycle: auto-provisions callu application, scenarios, rules,
/// system user, and syncs team members as Voximplant users.
/// </summary>
public class VoximplantProviderLifecycle : ICommunicationProviderLifecycle
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IOrganizationSettingsService _organizationSettingsService;
    private readonly ProviderSecretProtector _secretProtector;
    private readonly ILogger<VoximplantProviderLifecycle> _logger;

    private const string CalluAppName = "callu";
    private const string SystemUserName = "callu-system";

    public string ProviderType => "voximplant";

    public VoximplantProviderLifecycle(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IOrganizationSettingsService organizationSettingsService,
        ProviderSecretProtector secretProtector,
        ILogger<VoximplantProviderLifecycle> logger)
    {
        _contextFactory = contextFactory;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _organizationSettingsService = organizationSettingsService;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    public async Task<ProvisioningResult> ProvisionAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var calluApiUrl = (await _organizationSettingsService.GetPublicBaseUrlAsync(cancellationToken))?.TrimEnd('/');
        if (string.IsNullOrEmpty(calluApiUrl) || calluApiUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return new ProvisioningResult
            {
                Success = false,
                Error = "Public Base URL must be a publicly reachable address (Settings → Organization). " +
                        "Voximplant calls it from the cloud; localhost is unreachable."
            };
        }
        
        var created = new List<string>();
        var existing = new List<string>();
        
        try
        {
            var (client, config) = await CreateClientAsync(providerId, cancellationToken);

            var accountResponse = await client.GetAccountInfoAsync();
            var accountName = accountResponse.IsSuccess ? accountResponse.AccountInfo?.AccountName ?? string.Empty : string.Empty;

            var apps = await client.GetApplicationsAsync();
            ThrowIfError(apps);
            
            var calluApp = apps.Result?.FirstOrDefault(a => 
                a.ApplicationName.StartsWith(CalluAppName, StringComparison.OrdinalIgnoreCase));
            
            long applicationId;
            string applicationName;
            if (calluApp != null)
            {
                applicationId = calluApp.ApplicationId;
                applicationName = calluApp.ApplicationName;
                existing.Add($"Application: {calluApp.ApplicationName}");
            }
            else
            {
                var addAppResponse = await client.AddApplicationAsync(CalluAppName);
                ThrowIfError(addAppResponse);
                applicationId = addAppResponse.ApplicationId;
                applicationName = addAppResponse.ApplicationName ?? CalluAppName;
                created.Add($"Application: {CalluAppName}");
            }

            var scenarioApiKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

            var scenarios = await client.GetScenariosAsync(applicationId: applicationId);
            ThrowIfError(scenarios);
            
            var incidentScript = InjectScriptVariables(GetIncidentCallScript(), calluApiUrl, scenarioApiKey);
            long incidentScenarioId = await EnsureScenario(client, scenarios.Result, 
                "callu-incident-call", incidentScript, applicationId, created, existing);
            
            var conferenceScript = InjectScriptVariables(GetConferenceScript(), calluApiUrl, scenarioApiKey);
            long conferenceScenarioId = await EnsureScenario(client, scenarios.Result,
                "callu-conference", conferenceScript, applicationId, created, existing);

            var rules = await client.GetRulesAsync(applicationId);
            ThrowIfError(rules);
            
            // Patterns must not overlap: inbound Web SDK conference joins dial "callu-<id>" and
            // must reach the conference rule, not the incident rule (both run outbound by rule_id).
            long incidentRuleId = await EnsureRule(client, rules.Result,
                "incident-call-rule", "[0-9+]+", false, new[] { incidentScenarioId }, applicationId, created, existing);

            long conferenceRuleId = await EnsureRule(client, rules.Result,
                "conference-rule", "callu-.*", true, new[] { conferenceScenarioId }, applicationId, created, existing);

            var users = await client.GetUsersAsync(applicationId);
            ThrowIfError(users);
            
            long systemUserId;
            var systemUser = users.Result?.FirstOrDefault(u => 
                u.UserName.Equals(SystemUserName, StringComparison.OrdinalIgnoreCase));
            
            if (systemUser != null)
            {
                systemUserId = systemUser.UserId;
                existing.Add($"User: {SystemUserName}");
            }
            else
            {
                var password = GenerateSecurePassword();
                var addUserResponse = await client.AddUserAsync(applicationId, SystemUserName, password, "Callu System");
                ThrowIfError(addUserResponse);
                systemUserId = addUserResponse.UserId;
                created.Add($"User: {SystemUserName}");
            }

            config.ApplicationName = applicationName;
            config.AccountName = accountName;
            await SaveProvisioningConfig(providerId, config, new ProvisioningConfig
            {
                ApplicationId = applicationId,
                IncidentCallScenarioId = incidentScenarioId,
                ConferenceScenarioId = conferenceScenarioId,
                IncidentCallRuleId = incidentRuleId,
                ConferenceRuleId = conferenceRuleId,
                SystemUserId = systemUserId,
                ScenarioApiKey = scenarioApiKey,
                LastProvisionedAt = DateTime.UtcNow
            }, cancellationToken);
            
            VoximplantProviderLifecycleLog.ProvisioningComplete(_logger, providerId, string.Join(", ", created), string.Join(", ", existing));
            
            return new ProvisioningResult
            {
                Success = true,
                CreatedResources = created,
                ExistingResources = existing
            };
        }
        catch (HttpRequestException ex)
        {
            VoximplantProviderLifecycleLog.ProvisioningFailed(_logger, ex, providerId);
            return new ProvisioningResult
            {
                Success = false,
                Error = ex.Message,
                CreatedResources = created,
                ExistingResources = existing
            };
        }
    }

    public async Task<ProvisioningStatus> GetStatusAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var resources = new List<ProvisioningResource>();
        var issues = new List<string>();
        
        try
        {
            var (client, config) = await CreateClientAsync(providerId, cancellationToken);

            VoxAccountInfoDto? accountInfo = null;
            try
            {
                var accountResponse = await client.GetAccountInfoAsync();
                if (accountResponse.IsSuccess && accountResponse.AccountInfo != null)
                {
                    accountInfo = new VoxAccountInfoDto
                    {
                        AccountId = config.AccountId,
                        AccountName = accountResponse.AccountInfo.AccountName ?? "",
                        AccountEmail = accountResponse.AccountInfo.AccountEmail ?? "",
                        Balance = accountResponse.AccountInfo.LiveBalance,
                        Currency = accountResponse.AccountInfo.Currency ?? "USD",
                        Active = accountResponse.AccountInfo.Active
                    };
                }
            }
            catch (HttpRequestException ex)
            {
                issues.Add($"Cannot reach Voximplant API: {ex.Message}");
            }

            var provConfig = config.Provisioning;
            
            if (provConfig == null)
            {
                return new ProvisioningStatus
                {
                    IsProvisioned = false,
                    AccountInfo = accountInfo,
                    Resources = resources,
                    Issues = { "Not provisioned yet. Click 'Provision' to set up." }
                };
            }

            resources.Add(new ProvisioningResource("callu", "Application", provConfig.ApplicationId > 0, provConfig.ApplicationId));
            resources.Add(new ProvisioningResource("callu-incident-call", "Scenario", provConfig.IncidentCallScenarioId > 0, provConfig.IncidentCallScenarioId));
            resources.Add(new ProvisioningResource("callu-conference", "Scenario", provConfig.ConferenceScenarioId > 0, provConfig.ConferenceScenarioId));
            resources.Add(new ProvisioningResource("incident-call-rule", "Rule", provConfig.IncidentCallRuleId > 0, provConfig.IncidentCallRuleId));
            resources.Add(new ProvisioningResource("conference-rule", "Rule", provConfig.ConferenceRuleId > 0, provConfig.ConferenceRuleId));
            resources.Add(new ProvisioningResource("callu-system", "User", provConfig.SystemUserId > 0, provConfig.SystemUserId));

            int voxUserCount = 0;
            if (provConfig.ApplicationId > 0)
            {
                try
                {
                    var usersResponse = await client.GetUsersAsync(provConfig.ApplicationId);
                    if (usersResponse.Result != null)
                        voxUserCount = usersResponse.Result.Count;
                }
                catch { }
            }
            
            int calluUserCount = await GetCalluUserCountAsync(cancellationToken);
            
            return new ProvisioningStatus
            {
                IsProvisioned = resources.All(r => r.Exists),
                AccountInfo = accountInfo,
                Resources = resources,
                ProviderUserCount = voxUserCount,
                CalluUserCount = calluUserCount,
                UsersInSync = voxUserCount >= calluUserCount,
                Issues = issues
            };
        }
        catch (HttpRequestException ex)
        {
            VoximplantProviderLifecycleLog.ProvisioningStatusFailed(_logger, ex, providerId);
            return new ProvisioningStatus
            {
                IsProvisioned = false,
                Issues = { $"Error: {ex.Message}" }
            };
        }
    }

    public async Task OnTeamMemberAddedAsync(Guid providerId, string userId, string displayName, CancellationToken cancellationToken = default)
    {
        try
        {
            var (client, config) = await CreateClientAsync(providerId, cancellationToken);
            var provConfig = config.Provisioning;

            if (provConfig?.ApplicationId is null or 0)
            {
                VoximplantProviderLifecycleLog.SkippingUserCreation(_logger, userId);
                return;
            }

            var username = SanitizeUsername(userId);
            var password = GenerateSecurePassword();

            var response = await client.AddUserAsync(provConfig.ApplicationId, username, password, displayName);
            
            if (response.Error != null)
                VoximplantProviderLifecycleLog.VoxUserCreationFailed(_logger, username, response.Error.Msg);
            else
                VoximplantProviderLifecycleLog.VoxUserCreated(_logger, username, response.UserId);
        }
        catch (HttpRequestException ex)
        {
            VoximplantProviderLifecycleLog.VoxUserCreationError(_logger, ex, userId);
        }
    }
    
    public async Task OnTeamMemberRemovedAsync(Guid providerId, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (client, config) = await CreateClientAsync(providerId, cancellationToken);
            var provConfig = config.Provisioning;

            if (provConfig?.ApplicationId is null or 0) return;

            var username = SanitizeUsername(userId);
            var users = await client.GetUsersAsync(provConfig.ApplicationId);

            var voxUser = users.Result?.FirstOrDefault(u =>
                u.UserName.Equals(username, StringComparison.OrdinalIgnoreCase));
            
            if (voxUser != null)
            {
                await client.DelUserAsync(voxUser.UserId, provConfig.ApplicationId);
                VoximplantProviderLifecycleLog.VoxUserDeleted(_logger, username, voxUser.UserId);
            }
        }
        catch (HttpRequestException ex)
        {
            VoximplantProviderLifecycleLog.VoxUserDeletionError(_logger, ex, userId);
        }
    }
    
    public async Task<SyncResult> SyncUsersAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        int usersCreated = 0, usersDeleted = 0, usersUnchanged = 0;
        var errors = new List<string>();
        
        try
        {
            var (client, config) = await CreateClientAsync(providerId, cancellationToken);
            var provConfig = config.Provisioning;

            if (provConfig?.ApplicationId is null or 0)
            {
                return new SyncResult
                {
                    Success = false,
                    Errors = { "Voximplant not provisioned. Run provisioning first." }
                };
            }

            var calluUsers = await GetAllTeamMembersAsync(cancellationToken);

            var voxUsersResponse = await client.GetUsersAsync(provConfig.ApplicationId);
            ThrowIfError(voxUsersResponse);
            var voxUsers = voxUsersResponse.Result ?? new();

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
                        var response = await client.AddUserAsync(
                            provConfig.ApplicationId, username, password, calluUser.DisplayName ?? username);

                        if (response.Error != null)
                            errors.Add($"Failed to create {username}: {response.Error.Msg}");
                        else
                            usersCreated++;
                    }
                    catch (HttpRequestException ex)
                    {
                        errors.Add($"Error creating {username}: {ex.Message}");
                    }
                }
                else
                {
                    usersUnchanged++;
                }
            }

            foreach (var voxUser in voxUsers)
            {
                if (voxUser.UserName.Equals(SystemUserName, StringComparison.OrdinalIgnoreCase))
                    continue;
                    
                if (!calluUsernames.Contains(voxUser.UserName))
                {
                    try
                    {
                        await client.DelUserAsync(voxUser.UserId, provConfig.ApplicationId);
                        usersDeleted++;
                    }
                    catch (HttpRequestException ex)
                    {
                        errors.Add($"Error deleting {voxUser.UserName}: {ex.Message}");
                    }
                }
            }
            
            VoximplantProviderLifecycleLog.UserSyncComplete(_logger, usersCreated, usersDeleted, usersUnchanged);
            
            return new SyncResult
            {
                Success = errors.Count == 0,
                UsersCreated = usersCreated,
                UsersDeleted = usersDeleted,
                UsersUnchanged = usersUnchanged,
                Errors = errors
            };
        }
        catch (HttpRequestException ex)
        {
            VoximplantProviderLifecycleLog.UserSyncFailed(_logger, ex, providerId);
            return new SyncResult
            {
                Success = false,
                UsersCreated = usersCreated,
                UsersDeleted = usersDeleted,
                UsersUnchanged = usersUnchanged,
                Errors = { ex.Message }
            };
        }
    }

    private async Task<long> EnsureScenario(
        VoximplantManagementClient client, List<VoxScenarioInfoType>? scenarios,
        string name, string script, long applicationId,
        List<string> created, List<string> existing)
    {
        var scenario = scenarios?.FirstOrDefault(s => 
            s.ScenarioName.Equals(name, StringComparison.OrdinalIgnoreCase));
        
        if (scenario != null)
        {
            VoximplantProviderLifecycleLog.ScenarioFound(_logger, name, scenario.ScenarioId, script.Length);
            var updateResponse = await client.SetScenarioInfoAsync(scenario.ScenarioId, script: script);
            if (updateResponse.Error != null)
            {
                VoximplantProviderLifecycleLog.ScenarioUpdateFailed(_logger, name, updateResponse.Error.Msg);
            }
            else
            {
                VoximplantProviderLifecycleLog.ScenarioUpdated(_logger, name);
            }
            ThrowIfError(updateResponse);
            existing.Add($"Scenario: {name} (updated)");
            return scenario.ScenarioId;
        }
        
        VoximplantProviderLifecycleLog.ScenarioCreating(_logger, name, script.Length);
        var response = await client.AddScenarioAsync(name, script, applicationId: applicationId);
        ThrowIfError(response);
        created.Add($"Scenario: {name}");
        return response.ScenarioId;
    }
    
    private async Task<long> EnsureRule(
        VoximplantManagementClient client, List<VoxRuleInfoType>? rules,
        string name, string pattern, bool videoConference, long[] scenarioIds, long applicationId,
        List<string> created, List<string> existing)
    {
        var rule = rules?.FirstOrDefault(r =>
            r.RuleName.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (rule != null)
        {
            var needsPattern = rule.RulePattern != pattern;
            var needsVideo = rule.VideoConference != videoConference;

            if (needsPattern || needsVideo)
            {
                var patchResponse = await client.SetRuleInfoAsync(
                    rule.RuleId,
                    pattern: needsPattern ? pattern : null,
                    videoConference: needsVideo ? videoConference : null);
                if (patchResponse.Error != null)
                {
                    _logger.LogWarning(
                        "Failed to reconcile rule {Rule} (pattern/video_conference): {Err}. Video conferences may fail until this is corrected manually.",
                        name, patchResponse.Error.Msg);
                }
                else
                {
                    _logger.LogInformation(
                        "Reconciled rule {Rule}: pattern='{Pattern}', video_conference={Flag}",
                        name, pattern, videoConference);
                }
                existing.Add($"Rule: {name} (reconciled → pattern='{pattern}', video_conference={videoConference})");
            }
            else
            {
                existing.Add($"Rule: {name}");
            }
            return rule.RuleId;
        }

        var response = await client.AddRuleAsync(applicationId, name, pattern, scenarioIds, videoConference);
        ThrowIfError(response);
        created.Add($"Rule: {name}");
        return response.RuleId;
    }
    
    private async Task<(VoximplantManagementClient client, VoximplantConfigWithProvisioning config)> CreateClientAsync(
        Guid providerId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        
        var provider = await context.CommunicationProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == providerId && !p.IsDeleted, cancellationToken);
        
        if (provider == null)
            throw new KeyNotFoundException($"Provider not found: {providerId}");
        
        if (provider.ProviderType != "voximplant")
            throw new InvalidOperationException($"Provider {providerId} is not a Voximplant provider");
        
        var config = JsonSerializer.Deserialize<VoximplantConfigWithProvisioning>(provider.ConfigJson ?? "{}",
            VoximplantJsonOptions.Read)
            ?? throw new InvalidOperationException("Failed to deserialize Voximplant config");
        
        VoximplantProviderLifecycleLog.ConfigLoaded(_logger,
            config.AccountId > 0, !string.IsNullOrEmpty(config.ApiKey));

        var httpClient = _httpClientFactory.CreateClient("Voximplant");
        var client = new VoximplantManagementClient(httpClient, config.AccountId, _secretProtector.Unprotect(config.ApiKey), _logger);

        return (client, config);
    }
    
    private async Task SaveProvisioningConfig(Guid providerId, VoximplantConfigWithProvisioning config, 
        ProvisioningConfig provisioningConfig, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        
        var provider = await context.CommunicationProviders
            .FirstOrDefaultAsync(p => p.Id == providerId && !p.IsDeleted, cancellationToken);
        
        if (provider == null) return;

        config.Provisioning = provisioningConfig;
        config.Provisioning.ScenarioApiKey = _secretProtector.Protect(provisioningConfig.ScenarioApiKey);
        config.IncidentCallRuleId = provisioningConfig.IncidentCallRuleId;
        config.ConferenceRuleId = provisioningConfig.ConferenceRuleId;

        provider.ConfigJson = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    
    private async Task<int> GetCalluUserCountAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TeamMembers
            .Where(m => !m.IsDeleted)
            .Select(m => m.UserId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    private async Task<List<CalluTeamUser>> GetAllTeamMembersAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.TeamMembers
            .Where(m => !m.IsDeleted)
            .Join(context.Users.AsNoTracking(),
                tm => tm.UserId,
                u => u.Id,
                (tm, u) => new CalluTeamUser
                {
                    UserId = tm.UserId,
                    DisplayName = u.DisplayName ?? u.UserName ?? tm.UserId
                })
            .Distinct()
            .ToListAsync(cancellationToken);
    }
    
    private static void ThrowIfError(VoxBaseResponse response)
    {
        if (response.Error != null)
            throw new InvalidOperationException($"Voximplant API error [{response.Error.Code}]: {response.Error.Msg}");
    }
    
    private static string SanitizeUsername(string userId) => VoximplantUserNaming.Sanitize(userId);
    
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

    private static string LoadEmbeddedScript(string scriptName)
    {
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Providers", "Voximplant", "Scripts", scriptName);

        if (File.Exists(localPath))
        {
            return File.ReadAllText(localPath);
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".Scripts.{scriptName}", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded script not found: {scriptName}");
        
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Cannot read embedded script: {scriptName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    
    private static string GetIncidentCallScript() => LoadEmbeddedScript("callu-incident-call.js");
    private static string GetConferenceScript() => LoadEmbeddedScript("callu-conference.js");
    
    /// <summary>
    /// Injects the Callu API URL and Scenario API Key into a script template.
    /// Replaces {{CALLU_API_URL}} and {{CALLU_API_KEY}} placeholders.
    /// </summary>
    private static string InjectScriptVariables(string script, string apiUrl, string scenarioApiKey)
    {
        return script
            .Replace("{{CALLU_API_URL}}", apiUrl)
            .Replace("{{CALLU_API_KEY}}", scenarioApiKey);
    }
}

