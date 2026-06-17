using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Localization;
using Callu.Shared.Models.Webhooks;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Webhook template service implementation
/// </summary>
public class WebhookTemplateService(
    IWebhookTemplateRepository templateRepo,
    IWebhookCaptureRepository captureRepo,
    IServiceRepository serviceRepo,
    ITransactionManager transactionManager,
    IWebhookPayloadParser payloadParser) : IWebhookTemplateService
{
    public async Task<IEnumerable<WebhookTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var templates = await templateRepo.GetQueryable()
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .OrderByDescending(t => t.IsBuiltIn)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var result = new List<WebhookTemplateDto>();
        
        foreach (var t in templates)
        {
            var usageCount = await serviceRepo.GetQueryable()
                .CountAsync(s => s.WebhookTemplateId == t.Id && !s.IsDeleted, cancellationToken);
            
            result.Add(t.Adapt<WebhookTemplateDto>() with { UsageCount = usageCount });
        }

        return result;
    }

    public async Task<WebhookTemplateDto?> GetTemplateByIdAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        var template = await templateRepo.FindSingleAsync(t => t.Id == templateId && !t.IsDeleted, cancellationToken);
        if (template == null) return null;

        var usageCount = await serviceRepo.GetQueryable()
            .CountAsync(s => s.WebhookTemplateId == templateId && !s.IsDeleted, cancellationToken);

        return template.Adapt<WebhookTemplateDto>() with { UsageCount = usageCount };
    }

    public async Task<WebhookTemplateDto> CreateTemplateAsync(CreateWebhookTemplateRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var template = new WebhookTemplate
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                Name = request.Name,
                Description = request.Description,
                FieldMappings = request.FieldMappings,
                StateMapping = request.StateMapping,
                SamplePayload = request.SamplePayload,
                DataLanguage = request.DataLanguage ?? "en-US",
                IsBuiltIn = false,
                IsActive = true
            };

            await templateRepo.AddAsync(template, cancellationToken);

            return template.Adapt<WebhookTemplateDto>();
        }, cancellationToken);
    }

    public async Task<WebhookTemplateDto> CreateTemplateFromCaptureAsync(Guid captureId, CreateWebhookTemplateRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var capture = await captureRepo.FindSingleAsync(c => c.Id == captureId && !c.IsDeleted, cancellationToken);
            
            if (capture == null)
                throw new ArgumentException("Capture not found", nameof(captureId));

            var template = new WebhookTemplate
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                Name = request.Name,
                Description = request.Description,
                FieldMappings = request.FieldMappings,
                StateMapping = request.StateMapping,
                SamplePayload = capture.Body,
                DataLanguage = request.DataLanguage ?? "en-US",
                IsBuiltIn = false,
                IsActive = true
            };

            await templateRepo.AddAsync(template, cancellationToken);
            
            capture.Status = WebhookCaptureStatus.UsedForTemplate;

            var service = await serviceRepo.FindSingleAsync(s => s.Id == capture.ServiceId && !s.IsDeleted, cancellationToken);
            if (service != null)
            {
                service.WebhookTemplateId = template.Id;
            }

            return template.Adapt<WebhookTemplateDto>();
        }, cancellationToken);
    }

    public async Task<bool> UpdateTemplateAsync(Guid templateId, UpdateWebhookTemplateRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var template = await templateRepo.FindSingleAsync(t => t.Id == templateId && !t.IsDeleted, cancellationToken);
            if (template == null) return false;
            if (template.IsBuiltIn) return false;

            if (request.Name != null) template.Name = request.Name;
            if (request.Description != null) template.Description = request.Description;
            if (request.FieldMappings != null) template.FieldMappings = request.FieldMappings;
            if (request.StateMapping != null) template.StateMapping = request.StateMapping;
            if (request.SamplePayload != null) template.SamplePayload = request.SamplePayload;
            if (request.DataLanguage != null) template.DataLanguage = request.DataLanguage;
            if (request.IsActive.HasValue) template.IsActive = request.IsActive.Value;

            return true;
        }, cancellationToken);
    }

    public async Task<bool> DeleteTemplateAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var template = await templateRepo.FindSingleAsync(t => t.Id == templateId && !t.IsDeleted, cancellationToken);
            if (template == null) return false;
            if (template.IsBuiltIn) return false;

            var services = await serviceRepo.FindAsync(s => s.WebhookTemplateId == templateId && !s.IsDeleted, cancellationToken);
            foreach (var service in services)
            {
                service.WebhookTemplateId = null;
            }

            template.IsDeleted = true;
            return true;
        }, cancellationToken);
    }

    public async Task<WebhookTemplateTestResult> TestTemplateAsync(Guid templateId, string samplePayload, CancellationToken cancellationToken = default)
    {
        var template = await templateRepo.FindSingleAsync(t => t.Id == templateId && !t.IsDeleted, cancellationToken);
        
        if (template == null)
        {
            return new WebhookTemplateTestResult
            {
                Success = false,
                ErrorMessage = Messages.Get("webhooks.templateNotFound")
            };
        }

        var validationResult = payloadParser.Validate(samplePayload, template);

        return new WebhookTemplateTestResult
        {
            Success = validationResult.IsValid,
            ErrorMessage = validationResult.IsValid ? null : string.Join("; ", validationResult.Errors),
            MappedFields = validationResult.ExtractedFields
        };
    }
}
