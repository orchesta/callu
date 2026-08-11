using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Domain.Entities;
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Email;
using Callu.Shared.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Email Template service implementation — DB-managed templates with preview and test-send
/// </summary>
public partial class EmailTemplateService(
    IEmailTemplateRepository emailTemplateRepo,
    IEmailService emailService,
    ITransactionManager transactionManager,
    IDbEmailTemplateResolver templateResolver,
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
        if (template is null) return null;
        var dto = template.Adapt<EmailTemplateDetailDto>();
        return dto with
        {
            Variables = Email.TemplateVariableRenderer
                .ExtractVariables(template.HtmlBody + " " + template.Subject)
        };
    }

    public async Task<EmailTemplateDto> CreateAsync(CreateEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var dto = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existing = await emailTemplateRepo.GetByKeyAsync(request.Key, cancellationToken);
            if (existing != null)
            {
                throw new ConflictException($"Email template with key '{request.Key}' already exists.");
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

        // Post-commit: the live pipeline caches templates for 5 min.
        await templateResolver.InvalidateAsync(dto.Key, cancellationToken);
        return dto;
    }

    public async Task<EmailTemplateDetailDto?> UpdateAsync(Guid id, UpdateEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var dto = await transactionManager.ExecuteInTransactionAsync(async () =>
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

        if (dto is not null)
            await templateResolver.InvalidateAsync(dto.Key, cancellationToken);
        return dto;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        string? deletedKey = null;
        var deleted = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
            if (template == null) return false;

            if (template.IsSystem)
            {
                throw new BusinessRuleException("System templates cannot be deleted.");
            }

            template.IsDeleted = true;
            deletedKey = template.Key;
            return true;
        }, cancellationToken);

        if (deleted && deletedKey is not null)
            await templateResolver.InvalidateAsync(deletedKey, cancellationToken);
        return deleted;
    }

    public async Task<string> PreviewAsync(Guid id, Dictionary<string, string> variables, CancellationToken cancellationToken = default)
    {
        var template = await emailTemplateRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (template == null)
        {
            throw new NotFoundException("EmailTemplate", id);
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
            logger.LogError(ex, "Failed to send test email for template {TemplateId} to {Recipient}", id, PiiRedactor.Email(recipientEmail));
            return (false, ex.Message);
        }
    }

    // Shared with the live send pipeline so the editor's preview escapes HTML identically.
    private static string ReplaceVariables(string content, Dictionary<string, string> variables) =>
        Email.TemplateVariableRenderer.ReplaceVariables(content, variables);

    private static List<string> ExtractVariables(string content) =>
        Email.TemplateVariableRenderer.ExtractVariables(content);
}
