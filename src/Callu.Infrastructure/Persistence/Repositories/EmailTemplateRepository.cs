using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class EmailTemplateRepository(ApplicationDbContext context, ILogger<EmailTemplateRepository> logger)
    : Repository<EmailTemplate>(context, logger), IEmailTemplateRepository
{
    public async Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(t => t.Key == key && !t.IsDeleted, cancellationToken);
    }
}
