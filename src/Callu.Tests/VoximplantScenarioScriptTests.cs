using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Callu.Application.Services;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;

namespace Callu.Tests;

/// <summary>Pins what the API depends on the VoxEngine script doing, against the script's source.</summary>
public class VoximplantScenarioScriptTests
{
    private static string IncidentCallScript()
    {
        var assembly = typeof(VoximplantProviderLifecycle).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(".Scripts.callu-incident-call.js", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The script carries the contract-version marker the provisioning check reads back out.</summary>
    [Fact]
    public void TheScriptCarriesTheContractVersion_TheProvisioningCheckLooksFor()
    {
        var marker = $"CALLU_SCRIPT_CONTRACT = \"{VoximplantProviderLifecycle.ScriptContractVersion}\"";
        Assert.Contains(marker, IncidentCallScript(), StringComparison.Ordinal);
    }

    /// <summary>The script sends the per-call token that binds a status callback to its incident.</summary>
    [Fact]
    public void TheScriptSendsThePerCallToken()
    {
        Assert.Contains("X-Call-Token", IncidentCallScript(), StringComparison.Ordinal);
    }

    /// <summary>The press-2 announcement is chosen from whether anybody was actually paged, not from the status code.</summary>
    [Fact]
    public void TheScriptChoosesThePressTwoAnnouncement_FromWhetherAnybodyWasActuallyPaged()
    {
        var script = IncidentCallScript();

        Assert.Contains("escalation_paged", script, StringComparison.Ordinal);
        Assert.Contains("escalation_failed", script, StringComparison.Ordinal);
    }

    /// <summary>The other end of that wire: the API has to actually send the field the script reads.</summary>
    [Fact]
    public void TheCallbackEndpoint_ReportsWhetherThePressTwoPagedAnybody()
    {
        var result = typeof(ICallDataService)
            .GetMethod(nameof(ICallDataService.ProcessCallbackAsync))!
            .ReturnType;

        Assert.Equal(typeof(Task<VoxCallbackResult>), result);

        // The controller's contract with the script, spelled the way the script spells it.
        var controller = SourceOf("Callu.Api", "Controllers", "VoximplantCallbackController.cs");
        Assert.Contains("escalation_paged", controller, StringComparison.Ordinal);
        Assert.Contains(
            nameof(VoxCallbackResult.EscalationPagedSomeone), controller, StringComparison.Ordinal);
    }

    /// <summary>The attempt id has to survive the whole round trip, or the double-dial guard stays blind here.</summary>
    // Four hops with no type system between them: the API serves it, the script reads that exact key,
    // the script sends it back under the key the DTO binds, and the DTO carries it.
    [Fact]
    public void TheAttemptId_TravelsFromTheCallDataResponseBackIntoTheCallback()
    {
        var script = SourceOf("Callu.Infrastructure", "Providers", "Voximplant", "Scripts", "callu-incident-call.js");
        var controller = SourceOf("Callu.Api", "Controllers", "VoximplantCallbackController.cs");

        Assert.Contains("attempt_id = callData.AttemptId", controller, StringComparison.Ordinal);
        Assert.Contains("incidentData.attempt_id", script, StringComparison.Ordinal);
        Assert.Contains("attempt_id:", script, StringComparison.Ordinal);

        var bound = typeof(VoxCallbackRequest)
            .GetProperty(nameof(VoxCallbackRequest.AttemptId))!
            .GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
            .Cast<JsonPropertyNameAttribute>()
            .Single();

        Assert.Equal("attempt_id", bound.Name);
    }

    /// <summary>Reads a source file out of the repository, since there is no type system across this gap.</summary>
    private static string SourceOf(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !directory.EnumerateDirectories("Callu.Api").Any())
            directory = directory.Parent;

        Assert.NotNull(directory);

        var path = Path.Combine([directory!.FullName, .. relativePath]);
        Assert.True(File.Exists(path), $"Expected to find {path}");

        return File.ReadAllText(path);
    }

    /// <summary>The honest-branch TTS keys resolve in every shipped language, or the responder hears the raw key read aloud.</summary>
    [Theory]
    [InlineData("ack_failed")]
    [InlineData("escalation_failed")]
    public void TheFailurePromptsAreDefinedForEveryShippedLanguage(string key)
    {
        TtsDefaults.Initialize(Path.Combine(AppContext.BaseDirectory, "Resources", "TtsDefaults"));

        Assert.Contains(TtsDefaults.AllKeys, k => k.Key == key);
        Assert.Contains(key, IncidentCallScript(), StringComparison.Ordinal);

        var languages = TtsDefaults.GetAvailableLanguages();
        Assert.NotEmpty(languages);

        foreach (var language in languages)
        {
            var defaults = TtsDefaults.GetDefaults(language);
            Assert.True(
                defaults.TryGetValue(key, out var message) && !string.IsNullOrWhiteSpace(message),
                $"TTS default '{key}' is missing for {language}.");
        }
    }

    /// <summary>The conference script's own cap matches the one the API sends it.</summary>
    [Fact]
    public void TheConferenceScriptFallsBackToTheSameParticipantCap_TheApiSends()
    {
        var declared = Regex.Match(ConferenceScript(), @"var\s+maxParticipants\s*=\s*(?<cap>\d+)\s*;");

        Assert.True(declared.Success, "callu-conference.js no longer declares a maxParticipants default.");
        Assert.Equal(
            new VoxCallData().MaxParticipants,
            int.Parse(declared.Groups["cap"].Value));
    }

    private static string ConferenceScript() => Script("callu-conference.js");

    private static string Script(string fileName)
    {
        var assembly = typeof(VoximplantProviderLifecycle).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith($".Scripts.{fileName}", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
