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
        // Ordered: no constraint pins this to one row, and an unordered read moves after every UPDATE
        // (LastTestedAt relocates the tuple), so the sender and the settings screen could disagree.
        return await _dbSet
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
