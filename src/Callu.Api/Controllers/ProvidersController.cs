using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/providers")]
[Authorize(Policy = Policies.CanManageIntegrations)]
public class ProvidersController(
    ICommunicationProviderService providerService,
    ISipTrunkService sipTrunkService,
    ITtsTemplateService ttsTemplateService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetProviders(CancellationToken ct)
    {
        var providers = await providerService.GetProvidersAsync(ct);
        return Ok(providers);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProvider(Guid id, CancellationToken ct)
    {
        var provider = await providerService.GetProviderAsync(id, ct);
        if (provider == null) return NotFound();
        return Ok(provider);
    }

    [HttpPost]
    public async Task<IActionResult> CreateProvider([FromBody] CreateProviderRequest request, CancellationToken ct)
    {
        var provider = await providerService.CreateProviderAsync(request, ct);
        return CreatedAtAction(nameof(GetProvider), new { id = provider.Id }, provider);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateProvider(Guid id, [FromBody] UpdateProviderRequest request, CancellationToken ct)
    {
        await providerService.UpdateProviderAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteProvider(Guid id, CancellationToken ct)
    {
        await providerService.DeleteProviderAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/test-sms")]
    public async Task<IActionResult> TestSms(Guid id, [FromBody] TestSmsRequest request, CancellationToken ct)
    {
        var result = await providerService.SendTestSmsAsync(id, request.To, request.Message, ct);
        return Ok(result);
    }

    [HttpGet("sip-trunks")]
    public async Task<IActionResult> GetSipTrunks(CancellationToken ct)
    {
        var trunks = await sipTrunkService.GetTrunksAsync(ct);
        return Ok(trunks);
    }

    [HttpGet("sip-trunks/{id:guid}")]
    public async Task<IActionResult> GetSipTrunk(Guid id, CancellationToken ct)
    {
        var trunk = await sipTrunkService.GetTrunkAsync(id, ct);
        if (trunk == null) return NotFound();
        return Ok(trunk);
    }

    [HttpPost("sip-trunks")]
    public async Task<IActionResult> CreateSipTrunk([FromBody] CreateSipTrunkRequest request, CancellationToken ct)
    {
        var trunk = await sipTrunkService.CreateTrunkAsync(request, ct);
        return Ok(trunk);
    }

    [HttpPut("sip-trunks/{id:guid}")]
    public async Task<IActionResult> UpdateSipTrunk(Guid id, [FromBody] UpdateSipTrunkRequest request, CancellationToken ct)
    {
        await sipTrunkService.UpdateTrunkAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("sip-trunks/{id:guid}")]
    public async Task<IActionResult> DeleteSipTrunk(Guid id, CancellationToken ct)
    {
        await sipTrunkService.DeleteTrunkAsync(id, ct);
        return NoContent();
    }

    [HttpGet("tts-templates")]
    public async Task<IActionResult> GetTtsTemplates(CancellationToken ct)
    {
        var templates = await ttsTemplateService.GetAllAsync(ct);
        return Ok(templates);
    }

    [HttpGet("tts-templates/{languageCode}")]
    public async Task<IActionResult> GetTtsTemplate(string languageCode, CancellationToken ct)
    {
        var template = await ttsTemplateService.GetByLanguageAsync(languageCode, ct);
        if (template == null) return NotFound();
        return Ok(template);
    }

    [HttpPost("tts-templates")]
    public async Task<IActionResult> SaveTtsTemplate([FromBody] TtsTemplateSaveRequest request, CancellationToken ct)
    {
        await ttsTemplateService.SaveAsync(request, ct);
        return Ok(new { message = Messages.Get("providers.templateSaved") });
    }

    [HttpDelete("tts-templates/{languageCode}")]
    public async Task<IActionResult> DeleteTtsTemplate(string languageCode, CancellationToken ct)
    {
        await ttsTemplateService.DeleteAsync(languageCode, ct);
        return NoContent();
    }

    /// <summary>
    /// Returns the built-in default TTS messages for a given language.
    /// Used by the frontend to prefill the template editor when creating/resetting.
    /// </summary>
    [HttpGet("tts-templates/defaults/{languageCode}")]
    public IActionResult GetTtsDefaults(string languageCode)
    {
        var defaults = ttsTemplateService.GetDefaultsForLanguage(languageCode);
        return Ok(defaults);
    }

    /// <summary>
    /// Returns all TTS message key descriptors (key, label, group, description).
    /// Used by the frontend to dynamically render the template editor.
    /// </summary>
    [HttpGet("tts-templates/keys")]
    public IActionResult GetTtsKeys()
    {
        return Ok(TtsDefaults.AllKeys);
    }

    /// <summary>
    /// Lists all known communication capabilities and their descriptions
    /// </summary>
    [HttpGet("capabilities")]
    public IActionResult GetCapabilities()
    {
        var capabilities = Enum.GetValues<CommunicationCapability>()
            .Where(c => c != CommunicationCapability.None)
            .Select(c => new
            {
                Id = c.ToString(),
                Name = FormatCapabilityName(c),
                Description = GetCapabilityDescription(c)
            });

        return Ok(capabilities);
    }

    private static string FormatCapabilityName(CommunicationCapability c) => c switch
    {
        CommunicationCapability.VoiceCalls => "Voice Calls",
        CommunicationCapability.Sms => "SMS",
        CommunicationCapability.WhatsApp => "WhatsApp",
        CommunicationCapability.VideoConference => "Video Conference",
        CommunicationCapability.TTS => "Text-to-Speech",
        CommunicationCapability.ASR => "Automatic Speech Recognition",
        CommunicationCapability.Recording => "Recording",
        CommunicationCapability.VoicemailDetection => "Voicemail Detection",
        _ => c.ToString()
    };

    private static string GetCapabilityDescription(CommunicationCapability c) => c switch
    {
        CommunicationCapability.VoiceCalls => "Make and receive voice calls through SIP trunks",
        CommunicationCapability.Sms => "Send and receive SMS messages",
        CommunicationCapability.WhatsApp => "WhatsApp messaging integration",
        CommunicationCapability.VideoConference => "Multi-party video conferencing",
        CommunicationCapability.TTS => "Convert text to natural speech",
        CommunicationCapability.ASR => "Convert speech to text in real-time",
        CommunicationCapability.Recording => "Call recording and storage",
        CommunicationCapability.VoicemailDetection => "Detect and handle voicemail",
        _ => "Communication capability"
    };
}
