namespace Callu.Shared.Models.Webhooks;

public record SetProviderRequest(string ProviderId);
public record ToggleListeningModeRequest(bool Enabled);
public record SetTemplateRequest(Guid? TemplateId);
public record SetSignatureRequest(string Secret, string? HeaderName);
