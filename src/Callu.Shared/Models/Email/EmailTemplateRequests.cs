namespace Callu.Shared.Models.Email;

public record PreviewEmailTemplateRequest(Dictionary<string, string> Variables);
public record SendTestEmailRequest(string Email);
