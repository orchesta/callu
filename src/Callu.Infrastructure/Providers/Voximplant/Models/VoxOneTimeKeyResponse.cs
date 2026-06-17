namespace Callu.Infrastructure.Providers.Voximplant.Models;

/// <summary>
/// Response from Voximplant CreateOneTimeLoginKey API. "result" is the one-time login
/// key string for Web SDK authentication, bound by the base <see cref="VoxBaseResponse.Result"/>
/// (JsonElement?). Do NOT re-declare a `new string? Result` here: shadowing makes STJ bind the
/// derived property and leave the base null, so VoxBaseResponse.IsSuccess is always false and
/// the key is silently discarded. Read the key via Result.GetString() at the call site.
/// </summary>
public class VoxOneTimeKeyResponse : VoxBaseResponse
{
}
