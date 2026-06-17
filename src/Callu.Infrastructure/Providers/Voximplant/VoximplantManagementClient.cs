using System.Text.Json;
using Callu.Infrastructure.Providers.Voximplant.Models;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>
/// HTTP client for the Voximplant Management API. When a JWT authenticator is passed,
/// requests use Bearer auth and the api_key never appears in the URL. Otherwise falls
/// back to api_key in the query string.
/// </summary>
public class VoximplantManagementClient(
    HttpClient httpClient,
    long accountId,
    string apiKey,
    ILogger logger,
    VoximplantJwtAuthenticator? jwtAuthenticator = null)
{
    private const string ApiBase = "https://api.voximplant.com/platform_api";
    private bool UseJwt => jwtAuthenticator is not null;

    public async Task<VoxAccountInfoResponse> GetAccountInfoAsync()
    {
        return await GetAsync<VoxAccountInfoResponse>("GetAccountInfo");
    }

    public async Task<VoxAddApplicationResponse> AddApplicationAsync(string applicationName)
    {
        var args = new Dictionary<string, string>
        {
            ["application_name"] = applicationName
        };
        return await GetAsync<VoxAddApplicationResponse>("AddApplication", args);
    }
    
    public async Task<VoxGetApplicationsResponse> GetApplicationsAsync(long? applicationId = null, bool withRules = false, bool withScenarios = false)
    {
        var args = new Dictionary<string, string>
        {
            ["count"] = "100"
        };
        if (applicationId.HasValue)
            args["application_id"] = applicationId.Value.ToString();
        if (withRules)
            args["with_rules"] = "true";
        if (withScenarios)
            args["with_scenarios"] = "true";
            
        return await GetAsync<VoxGetApplicationsResponse>("GetApplications", args);
    }
    
    public async Task<VoxBaseResponse> DelApplicationAsync(long applicationId)
    {
        var args = new Dictionary<string, string>
        {
            ["application_id"] = applicationId.ToString()
        };
        return await GetAsync<VoxBaseResponse>("DelApplication", args);
    }
    
    public async Task<VoxBaseResponse> SetApplicationInfoAsync(long applicationId, string? newName = null)
    {
        var args = new Dictionary<string, string>
        {
            ["application_id"] = applicationId.ToString()
        };
        if (newName != null)
            args["application_name"] = newName;
            
        return await GetAsync<VoxBaseResponse>("SetApplicationInfo", args);
    }
    
    public async Task<VoxAddUserResponse> AddUserAsync(long applicationId, string userName, string password, string displayName)
    {
        var args = new Dictionary<string, string>
        {
            ["application_id"] = applicationId.ToString(),
            ["user_name"] = userName,
            ["user_password"] = password,
            ["user_display_name"] = displayName
        };
        return await GetAsync<VoxAddUserResponse>("AddUser", args);
    }
    
    public async Task<VoxGetUsersResponse> GetUsersAsync(long applicationId)
    {
        var args = new Dictionary<string, string>
        {
            ["application_id"] = applicationId.ToString(),
            ["count"] = "100"
        };
        return await GetAsync<VoxGetUsersResponse>("GetUsers", args);
    }
    
    public async Task<VoxBaseResponse> DelUserAsync(long userId, long applicationId)
    {
        var args = new Dictionary<string, string>
        {
            ["user_id"] = userId.ToString(),
            ["application_id"] = applicationId.ToString()
        };
        return await GetAsync<VoxBaseResponse>("DelUser", args);
    }
    
    public async Task<VoxBaseResponse> SetUserInfoAsync(long userId, string? userName = null, string? displayName = null, bool? active = null, string? userPassword = null)
    {
        var args = new Dictionary<string, string>
        {
            ["user_id"] = userId.ToString()
        };
        if (userName != null)
            args["user_name"] = userName;
        if (displayName != null)
            args["user_display_name"] = displayName;
        if (active.HasValue)
            args["user_active"] = active.Value.ToString().ToLowerInvariant();
        if (userPassword != null)
            args["user_password"] = userPassword;

        return userPassword != null
            ? await PostAsync<VoxBaseResponse>("SetUserInfo", args)
            : await GetAsync<VoxBaseResponse>("SetUserInfo", args);
    }
    
    public async Task<VoxGetUsersResponse> GetUsersAsync(long applicationId, string userNameFilter)
    {
        var args = new Dictionary<string, string>
        {
            ["application_id"] = applicationId.ToString(),
            ["user_name"] = userNameFilter,
            ["count"] = "1"
        };
        return await GetAsync<VoxGetUsersResponse>("GetUsers", args);
    }
    
    /// <summary>
    /// Ensures a Voximplant user exists for a conference participant. Returns user_id,
    /// or null on failure. Password is rotated per-login by <see cref="PrepareWebSdkLoginHashAsync"/>.
    /// </summary>
    public async Task<long?> EnsureConferenceUserAsync(long applicationId, string participantToken, string displayName)
    {
        var existing = await GetUsersAsync(applicationId, participantToken);
        if (existing.Result?.Count > 0)
            return existing.Result[0].UserId;

        var password = Guid.NewGuid().ToString("N")[..16];
        var result = await AddUserAsync(applicationId, participantToken, password, displayName);
        return result.IsSuccess ? result.UserId : null;
    }

    /// <summary>
    /// Rotates the user's password to a fresh ephemeral value and returns it for a single
    /// Web SDK password login. Voximplant has no server-side one-time-key API, so the Web
    /// SDK logs in with this password directly; it is discarded after this join.
    /// </summary>
    public async Task<string?> PrepareWebSdkLoginHashAsync(long userId, string fullUserName)
    {
        var ephemeralPassword = Guid.NewGuid().ToString("N")[..16];

        var setResult = await SetUserInfoAsync(userId, userPassword: ephemeralPassword);
        if (!setResult.IsSuccess)
        {
            logger.LogWarning("Voximplant SetUserInfo (password rotation) failed for {User}: {Err}",
                fullUserName, setResult.Error?.Msg);
            return null;
        }

        return ephemeralPassword;
    }

    public async Task<VoxAddScenarioResponse> AddScenarioAsync(string scenarioName, string scenarioScript,
        long? applicationId = null, bool rewrite = false, long? ruleId = null)
    {
        var args = new Dictionary<string, string>
        {
            ["scenario_name"] = scenarioName,
            ["scenario_script"] = scenarioScript
        };
        if (applicationId.HasValue)
            args["application_id"] = applicationId.Value.ToString();
        if (rewrite)
            args["rewrite"] = "true";
        if (ruleId.HasValue)
            args["rule_id"] = ruleId.Value.ToString();
            
        return await PostAsync<VoxAddScenarioResponse>("AddScenario", args);
    }
    
    public async Task<VoxGetScenariosResponse> GetScenariosAsync(long? applicationId = null, long? scenarioId = null, bool withScript = false)
    {
        var args = new Dictionary<string, string>
        {
            ["count"] = "100"
        };
        if (applicationId.HasValue)
            args["application_id"] = applicationId.Value.ToString();
        if (scenarioId.HasValue)
            args["scenario_id"] = scenarioId.Value.ToString();
        if (withScript && scenarioId.HasValue)
            args["with_script"] = "true";
            
        return await GetAsync<VoxGetScenariosResponse>("GetScenarios", args);
    }
    
    public async Task<VoxBaseResponse> SetScenarioInfoAsync(long scenarioId, string? name = null, string? script = null)
    {
        var args = new Dictionary<string, string>
        {
            ["scenario_id"] = scenarioId.ToString()
        };
        if (name != null)
            args["scenario_name"] = name;
        if (script != null)
            args["scenario_script"] = script;
            
        return await PostAsync<VoxBaseResponse>("SetScenarioInfo", args);
    }
    
    public async Task<VoxBaseResponse> DelScenarioAsync(long scenarioId)
    {
        var args = new Dictionary<string, string>
        {
            ["scenario_id"] = scenarioId.ToString()
        };
        return await GetAsync<VoxBaseResponse>("DelScenario", args);
    }
    
    public async Task<VoxBaseResponse> BindScenarioAsync(long ruleId, long[] scenarioIds, long? applicationId = null, bool bind = true)
    {
        var args = new Dictionary<string, string>
        {
            ["rule_id"] = ruleId.ToString(),
            ["scenario_id"] = string.Join(";", scenarioIds),
            ["bind"] = bind.ToString().ToLowerInvariant()
        };
        if (applicationId.HasValue)
            args["application_id"] = applicationId.Value.ToString();
            
        return await GetAsync<VoxBaseResponse>("BindScenario", args);
    }

    public async Task<VoxAddRuleResponse> AddRuleAsync(long applicationId, string ruleName, string rulePattern,
        long[] scenarioIds, bool videoConference = false)
    {
        var args = new Dictionary<string, string>
        {
            ["application_id"] = applicationId.ToString(),
            ["rule_name"] = ruleName,
            ["rule_pattern"] = rulePattern,
            ["scenario_id"] = string.Join(";", scenarioIds)
        };
        if (videoConference)
            args["video_conference"] = "true";
            
        return await GetAsync<VoxAddRuleResponse>("AddRule", args);
    }
    
    public async Task<VoxGetRulesResponse> GetRulesAsync(long applicationId, bool withScenarios = true)
    {
        var args = new Dictionary<string, string>
        {
            ["application_id"] = applicationId.ToString(),
            ["with_scenarios"] = withScenarios.ToString().ToLowerInvariant(),
            ["count"] = "100"
        };
        return await GetAsync<VoxGetRulesResponse>("GetRules", args);
    }
    
    public async Task<VoxBaseResponse> SetRuleInfoAsync(long ruleId, string? name = null, string? pattern = null, bool? videoConference = null)
    {
        var args = new Dictionary<string, string>
        {
            ["rule_id"] = ruleId.ToString()
        };
        if (name != null)
            args["rule_name"] = name;
        if (pattern != null)
            args["rule_pattern"] = pattern;
        if (videoConference.HasValue)
            args["video_conference"] = videoConference.Value.ToString().ToLowerInvariant();
            
        return await GetAsync<VoxBaseResponse>("SetRuleInfo", args);
    }
    
    public async Task<VoxBaseResponse> DelRuleAsync(long ruleId, long applicationId)
    {
        var args = new Dictionary<string, string>
        {
            ["rule_id"] = ruleId.ToString(),
            ["application_id"] = applicationId.ToString()
        };
        return await GetAsync<VoxBaseResponse>("DelRule", args);
    }
    
    private async Task<T> GetAsync<T>(string method, Dictionary<string, string>? args = null) where T : VoxBaseResponse, new()
    {
        var url = BuildUrl(method, args);

        try
        {
            logger.LogDebug("Voximplant API GET: {Method} (auth={Auth})", method, UseJwt ? "JWT" : "apiKey");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(request);
            var response = await httpClient.SendAsync(request);
            return await ParseResponseAsync<T>(response, method);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Voximplant API call failed: {Method}", method);
            return new T { Error = new VoxApiError { Code = -1, Msg = ex.Message } };
        }
    }

    private async Task<T> PostAsync<T>(string method, Dictionary<string, string> args) where T : VoxBaseResponse, new()
    {
        var url = $"{ApiBase}/{method}";

        if (!UseJwt)
        {
            args["account_id"] = accountId.ToString();
            args["api_key"] = apiKey;
        }

        try
        {
            logger.LogDebug("Voximplant API POST: {Method} (auth={Auth})", method, UseJwt ? "JWT" : "apiKey");
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(args)
            };
            ApplyAuth(request);
            var response = await httpClient.SendAsync(request);
            return await ParseResponseAsync<T>(response, method);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Voximplant API call failed: {Method}", method);
            return new T { Error = new VoxApiError { Code = -1, Msg = ex.Message } };
        }
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (jwtAuthenticator is null) return;
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtAuthenticator.GetBearerToken());
    }

    private string BuildUrl(string method, Dictionary<string, string>? args = null)
    {
        var url = UseJwt
            ? $"{ApiBase}/{method}?account_id={accountId}"
            : $"{ApiBase}/{method}?account_id={accountId}&api_key={Uri.EscapeDataString(apiKey)}";

        if (args != null)
        {
            foreach (var kvp in args)
            {
                url += $"&{kvp.Key}={Uri.EscapeDataString(kvp.Value)}";
            }
        }

        return url;
    }
    
    private async Task<T> ParseResponseAsync<T>(HttpResponseMessage response, string method) where T : VoxBaseResponse, new()
    {
        if (!response.IsSuccessStatusCode)
        {
            return new T { Error = new VoxApiError { Code = (int)response.StatusCode, Msg = $"HTTP {response.StatusCode}" } };
        }
        
        var json = await response.Content.ReadAsStringAsync();
        logger.LogDebug("Voximplant API response for {Method}: {Json}", method, json.Length > 500 ? json[..500] + "..." : json);
        
        var result = JsonSerializer.Deserialize<T>(json, _jsonOptions);
        return result ?? new T { Error = new VoxApiError { Code = -1, Msg = "Failed to parse response" } };
    }
    
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
