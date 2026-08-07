using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// SmtpSettings-specific repository interface
/// </summary>
public interface ISmtpSettingsRepository : IRepository<SmtpSettings>
{
    Task<SmtpSettings?> GetSettingsAsync(CancellationToken cancellationToken = default);
}
