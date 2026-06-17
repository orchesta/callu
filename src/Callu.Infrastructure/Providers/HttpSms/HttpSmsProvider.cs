using System.Text;
using System.Text.Json;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Communication;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers.HttpSms;

/// <summary>
/// Generic HTTP SMS provider. Sends an SMS by rendering an operator-defined HTTP request
/// template, so a company can plug in its own SMS API without a code change. Placeholders
/// are substituted with location-aware escaping (JSON-escape inside a JSON body, URL-encode
/// inside a URL or form body) so values containing quotes/ampersands can't break the request.
/// </summary>
public class HttpSmsProvider : BaseCommunicationProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpSmsProvider> _logger;
    private HttpSmsConfig? _config;

    public override string ProviderType => "http-sms";

    public override CommunicationCapability Capabilities => CommunicationCapability.Sms;

    public HttpSmsProvider(IHttpClientFactory httpClientFactory, ILogger<HttpSmsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public override async Task InitializeAsync(string configJson, SipTrunkSettings? sipTrunk)
    {
        await base.InitializeAsync(configJson, sipTrunk);
        _config = GetConfig<HttpSmsConfig>();
    }

    public override Task<(bool Success, string Message)> TestConnectionAsync()
    {
        if (_config == null || string.IsNullOrWhiteSpace(_config.Url))
            return Task.FromResult<(bool, string)>((false, "Provider not configured (missing URL)."));

        if (!Uri.TryCreate(_config.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Task.FromResult<(bool, string)>((false, $"Invalid URL: {_config.Url}"));

        return Task.FromResult<(bool, string)>(
            (true, $"Configuration looks valid ({_config.Method.ToUpperInvariant()} {uri.Host}). Use 'Send test SMS' to verify delivery."));
    }

    public override async Task<SmsResult> SendSmsAsync(SendSmsRequest request)
    {
        if (_config == null || string.IsNullOrWhiteSpace(_config.Url))
            return new SmsResult { Success = false, ErrorMessage = "Provider not configured" };

        try
        {
            var method = string.Equals(_config.Method, "GET", StringComparison.OrdinalIgnoreCase)
                ? HttpMethod.Get
                : HttpMethod.Post;
            var isJson = !string.Equals(_config.ContentType, "form", StringComparison.OrdinalIgnoreCase);
            var sender = !string.IsNullOrEmpty(request.SenderId) ? request.SenderId : (_config.SenderId ?? "");

            var url = Substitute(_config.Url, Uri.EscapeDataString, request.To, request.Message, sender);
            using var httpRequest = new HttpRequestMessage(method, url);

            if (_config.Headers != null)
            {
                foreach (var (name, value) in _config.Headers)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                    var rendered = StripCrlf(Substitute(value, v => v, request.To, request.Message, sender));
                    httpRequest.Headers.TryAddWithoutValidation(name, rendered);
                }
            }

            if (method == HttpMethod.Post && !string.IsNullOrEmpty(_config.BodyTemplate))
            {
                var body = isJson
                    ? Substitute(_config.BodyTemplate, JsonEscape, request.To, request.Message, sender)
                    : Substitute(_config.BodyTemplate, Uri.EscapeDataString, request.To, request.Message, sender);
                httpRequest.Content = new StringContent(
                    body, Encoding.UTF8, isJson ? "application/json" : "application/x-www-form-urlencoded");
            }

            var client = _httpClientFactory.CreateClient("HttpSms");
            var response = await client.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (EvaluateSuccess(response.IsSuccessStatusCode, responseBody))
            {
                _logger.LogInformation("HTTP SMS sent to {To} (HTTP {Status})", request.To, (int)response.StatusCode);
                return new SmsResult { Success = true, MessageId = ExtractMessageId(responseBody) };
            }

            _logger.LogWarning("HTTP SMS failed to {To}: HTTP {Status} - {Body}",
                request.To, (int)response.StatusCode, Truncate(responseBody, 300));
            return new SmsResult { Success = false, ErrorMessage = $"HTTP {(int)response.StatusCode}: {Truncate(responseBody, 300)}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP SMS request failed to {To}", request.To);
            return new SmsResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Replaces every placeholder in <paramref name="template"/>, passing each substituted
    /// VALUE through <paramref name="escape"/> so it is safe for the target location
    /// (JSON string, URL, or form field). The template's own literal characters are untouched.
    /// </summary>
    private string Substitute(string template, Func<string, string> escape, string to, string message, string sender)
    {
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;
        return template
            .Replace("{to}", escape(to ?? string.Empty))
            .Replace("{message}", escape(message ?? string.Empty))
            .Replace("{sender}", escape(sender ?? string.Empty))
            .Replace("{apiKey}", escape(_config?.ApiKey ?? string.Empty))
            .Replace("{username}", escape(_config?.Username ?? string.Empty))
            .Replace("{password}", escape(_config?.Password ?? string.Empty));
    }

    /// <summary>Escapes a value for embedding inside a JSON string literal (quotes, backslashes, control chars).</summary>
    private static string JsonEscape(string value)
    {
        var serialized = JsonSerializer.Serialize(value);
        return serialized.Length >= 2 ? serialized[1..^1] : serialized;
    }

    private static string StripCrlf(string value) => value.Replace("\r", string.Empty).Replace("\n", string.Empty);

    private bool EvaluateSuccess(bool is2xx, string body)
    {
        if (!string.Equals(_config?.SuccessMode, "jsonField", StringComparison.OrdinalIgnoreCase))
            return is2xx;

        if (string.IsNullOrWhiteSpace(_config?.SuccessField))
            return is2xx;

        var actual = ReadJsonPath(body, _config!.SuccessField!);
        return actual != null && string.Equals(actual, _config.SuccessValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private string? ExtractMessageId(string body)
    {
        if (!string.IsNullOrWhiteSpace(_config?.MessageIdPath))
        {
            var id = ReadJsonPath(body, _config!.MessageIdPath!);
            if (!string.IsNullOrEmpty(id)) return id;
        }

        var trimmed = body.Trim();
        return trimmed.Length is > 0 and <= 200 ? trimmed : null;
    }

    /// <summary>Traverses a dotted path (e.g. "data.id") into a JSON document; returns the leaf as a string.</summary>
    private static string? ReadJsonPath(string body, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var element = doc.RootElement;
            foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out var next))
                    return null;
                element = next;
            }
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
