using System.Text;
using System.Text.Json;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Communication;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers.Verimor;

/// <summary>
/// Verimor SMS provider implementation.
/// API Reference: https://github.com/verimor/SMS-API/blob/master/user_guide.md
/// Auth: username/password in JSON body (POST) or query params (GET) — NOT Basic Auth.
/// </summary>
public class VerimorProvider : BaseCommunicationProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VerimorProvider> _logger;
    private VerimorConfig? _config;
    
    private const string VerimorApiBase = "https://sms.verimor.com.tr/v2";
    
    public override string ProviderType => "verimor";
    
    public override CommunicationCapability Capabilities => CommunicationCapability.Sms;
    
    public VerimorProvider(IHttpClientFactory httpClientFactory, ILogger<VerimorProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }
    
    public override async Task InitializeAsync(string configJson, SipTrunkSettings? sipTrunk)
    {
        await base.InitializeAsync(configJson, sipTrunk);
        _config = GetConfig<VerimorConfig>();
    }
    
    /// <summary>
    /// Test connection by checking balance.
    /// Verimor balance endpoint: GET /v2/balance?username=X&amp;password=Y
    /// </summary>
    public override async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        if (_config == null)
            return (false, "Provider not configured");
            
        try
        {
            var client = _httpClientFactory.CreateClient("Verimor");

            var url = $"{VerimorApiBase}/balance" +
                      $"?username={Uri.EscapeDataString(_config.ApiUsername)}" +
                      $"&password={Uri.EscapeDataString(_config.ApiPassword)}";
            
            var response = await client.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var balance = await response.Content.ReadAsStringAsync();
                return (true, $"Connected to Verimor. Balance: {balance}");
            }
            
            var errorBody = await response.Content.ReadAsStringAsync();
            
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return (false, $"Authentication failed - check username and password. {errorBody}");
            }
            
            return (false, $"HTTP {(int)response.StatusCode}: {errorBody}");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to test Verimor connection");
            return (false, $"Connection failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Send SMS via Verimor POST JSON API.
    /// Verimor expects: { username, password, source_addr, messages: [{ msg, dest }] }
    /// Response: HTTP 200 with campaign ID on success, HTTP 400 with error code on failure.
    /// </summary>
    public override async Task<SmsResult> SendSmsAsync(SendSmsRequest request)
    {
        if (_config == null)
            return new SmsResult { Success = false, ErrorMessage = "Provider not configured" };
            
        try
        {
            var client = _httpClientFactory.CreateClient("Verimor");

            var smsPayload = new
            {
                username = _config.ApiUsername,
                password = _config.ApiPassword,
                source_addr = _config.SenderId,
                messages = new[]
                {
                    new
                    {
                        msg = request.Message,
                        dest = request.To
                    }
                }
            };
            
            var content = new StringContent(
                JsonSerializer.Serialize(smsPayload),
                Encoding.UTF8,
                "application/json");
            
            var response = await client.PostAsync($"{VerimorApiBase}/send.json", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Verimor SMS sent successfully. Campaign ID: {CampaignId}", responseBody);
                
                return new SmsResult
                {
                    Success = true,
                    MessageId = responseBody.Trim()
                };
            }
            
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Verimor SMS failed: HTTP {StatusCode} - {Body}", 
                (int)response.StatusCode, errorBody);
            
            return new SmsResult
            {
                Success = false,
                ErrorMessage = $"SMS failed: {errorBody.Trim()}"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to send SMS via Verimor to {Destination}", request.To);
            return new SmsResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}
