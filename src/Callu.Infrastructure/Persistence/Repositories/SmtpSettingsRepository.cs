using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class SmtpSettingsRepository(ApplicationDbContext context, ILogger<SmtpSettingsRepository> logger)
    : Repository<SmtpSettings>(context, logger), ISmtpSettingsRepository
{
    public async Task<SmtpSettings?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(cancellationToken);
    }
}
