using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Domain.Entities;
using Callu.Shared.Models.Email;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Email Template service implementation — DB-managed templates with preview and test-send
/// </summary>
public partial class EmailTemplateService(
    IEmailTemplateRepository emailTemplateRepo,
    IEmailService emailService,
    ITransactionManager transactionManager,
    ILogger<EmailTemplateService> logger) : IEmailTemplateService
{
    public async Task<IEnumerable<EmailTemplateDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var templates = await emailTemplateRepo.FindAsync(t => !t.IsDeleted, cancellationToken);
        return templates
            .OrderByDescending(t => t.IsSystem)
            .ThenBy(t => t.Name)
            .Select(t => t.Adapt<EmailTemplateDto>());
    }

    public async Task<EmailTemplateDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        return template?.Adapt<EmailTemplateDetailDto>();
    }

    public async Task<EmailTemplateDto> CreateAsync(CreateEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existing = await emailTemplateRepo.GetByKeyAsync(request.Key, cancellationToken);
            if (existing != null)
            {
                throw new InvalidOperationException($"Email template with key '{request.Key}' already exists.");
            }

            var template = new EmailTemplate
            {
                Name = request.Name,
                Key = request.Key,
                Subject = request.Subject,
                HtmlBody = request.HtmlBody,
                PlainTextBody = request.PlainTextBody,
                Description = request.Description,
                IsSystem = false,
                IsActive = true
            };

            await emailTemplateRepo.AddAsync(template, cancellationToken);
            return template.Adapt<EmailTemplateDto>();
        }, cancellationToken);
    }

    public async Task<EmailTemplateDetailDto?> UpdateAsync(Guid id, UpdateEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
            if (template == null) return null;

            if (request.Name != null) template.Name = request.Name;
            if (request.Subject != null) template.Subject = request.Subject;
            if (request.HtmlBody != null) template.HtmlBody = request.HtmlBody;
            if (request.PlainTextBody != null) template.PlainTextBody = request.PlainTextBody;
            if (request.Description != null) template.Description = request.Description;
            if (request.IsActive.HasValue) template.IsActive = request.IsActive.Value;

            return template.Adapt<EmailTemplateDetailDto>();
        }, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
            if (template == null) return false;

            if (template.IsSystem)
            {
                throw new InvalidOperationException("System templates cannot be deleted.");
            }

            template.IsDeleted = true;
            return true;
        }, cancellationToken);
    }

    public async Task<string> PreviewAsync(Guid id, Dictionary<string, string> variables, CancellationToken cancellationToken = default)
    {
        var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (template == null)
        {
            throw new InvalidOperationException("Template not found.");
        }

        return ReplaceVariables(template.HtmlBody, variables);
    }

    public async Task<(bool Success, string? ErrorMessage)> SendTestAsync(Guid id, string recipientEmail, CancellationToken cancellationToken = default)
    {
        var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (template == null) return (false, "Template not found");

        try
        {
            var sampleVars = ExtractVariables(template.HtmlBody)
                .ToDictionary(v => v, v => $"[{v}]");

            var previewHtml = ReplaceVariables(template.HtmlBody, sampleVars);
            var previewSubject = ReplaceVariables(template.Subject, sampleVars);

            await emailService.SendAsync(recipientEmail, previewSubject, previewHtml, cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send test email for template {TemplateId} to {Recipient}", id, recipientEmail);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Replace {{variable}} placeholders with HTML-escaped values.
    /// User-supplied values (incident title, description, user names, etc.) must
    /// NOT be inserted raw — an attacker-controlled title like `<img src=x onerror=...>`
    /// would otherwise run in the recipient's email client.
    ///
    /// Subject lines flow through the same path, and while most clients render them
    /// as plain text, escaping is still safer (no accidental HTML entity confusion).
    /// Variable names ending in "_raw" bypass escaping — used by templates that
    /// intentionally inject safe, pre-rendered HTML (e.g. signed action buttons).
    /// </summary>
    private static string ReplaceVariables(string content, Dictionary<string, string> variables)
    {
        var result = content;
        foreach (var (key, value) in variables)
        {
            var placeholder = $"{{{{{key}}}}}";
            var replacement = key.EndsWith("_raw", StringComparison.Ordinal)
                ? value
                : WebUtility.HtmlEncode(value ?? string.Empty);
            result = result.Replace(placeholder, replacement);
        }
        return result;
    }

    /// <summary>
    /// Extract all {{variable}} names from template content
    /// </summary>
    private static List<string> ExtractVariables(string content)
    {
        var matches = VariablePattern().Matches(content);
        return matches.Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex VariablePattern();
}
