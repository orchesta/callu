namespace Callu.Api.Middleware;

/// <summary>Retired pass-through: callback-to-incident binding is now the per-call <c>X-Call-Token</c>, and this
/// warns once if <c>Voximplant:Signature:RequireSignature</c> is still set. Safe to delete with that config section.</summary>
public sealed class VoximplantSignatureMiddleware(
    RequestDelegate next,
    Microsoft.Extensions.Options.IOptionsMonitor<VoximplantSignatureOptions> optionsMonitor,
    ILogger<VoximplantSignatureMiddleware> logger)
{
    private static int _warned;

    public Task InvokeAsync(HttpContext context)
    {
        if (optionsMonitor.CurrentValue.RequireSignature &&
            Interlocked.Exchange(ref _warned, 1) == 0)
        {
            logger.LogError(
                "Voximplant:Signature:RequireSignature is set but signature verification has been " +
                "removed — it was unimplementable (the signing secret would have to ship inside the " +
                "VoxEngine script, alongside the scenario key it was supposed to backstop) and the " +
                "shipped scripts never signed their requests, so enabling it only rejected genuine " +
                "callbacks. Callbacks are now bound to their incident by a per-call X-Call-Token. " +
                "Remove the Voximplant:Signature section from your configuration.");
        }

        return next(context);
    }
}

/// <summary>
/// Retained only so an existing <c>Voximplant:Signature</c> configuration section still binds
/// instead of failing. Both values are ignored. See <see cref="VoximplantSignatureMiddleware"/>.
/// </summary>
public sealed class VoximplantSignatureOptions
{
    public const string SectionName = "Voximplant:Signature";

    public bool RequireSignature { get; set; }

    public string? Secret { get; set; }
}
