using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// EmailTemplate-specific repository interface
/// </summary>
public interface IEmailTemplateRepository : IRepository<EmailTemplate>
{
    Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
}
