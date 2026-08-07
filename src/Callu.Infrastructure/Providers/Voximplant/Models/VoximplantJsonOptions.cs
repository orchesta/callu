using System.Text.Json;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

/// <summary>The single case-insensitive options instance every reader of a Voximplant ConfigJson blob
/// must use, so no reader silently binds null.</summary>
internal static class VoximplantJsonOptions
{
    public static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };
}
