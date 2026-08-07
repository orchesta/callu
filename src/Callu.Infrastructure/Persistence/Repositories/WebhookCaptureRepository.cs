using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class WebhookCaptureRepository(ApplicationDbContext context, ILogger<WebhookCaptureRepository> logger)
    : Repository<WebhookCapture>(context, logger), IWebhookCaptureRepository
{
    public async Task<IEnumerable<WebhookCapture>> GetByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.ServiceId == serviceId && !c.IsDeleted)
            .OrderByDescending(c => c.CapturedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.CountAsync(c => c.ServiceId == serviceId && !c.IsDeleted, cancellationToken);
    }
}
