using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Callu.Infrastructure.Persistence.Voximplant;

public class VoximplantCallReadPersistence(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IVoximplantCallReadPersistence
{
    public async Task<IncidentVoiceRetryInfo?> GetIncidentForVoiceRetryAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var i = await context.Incidents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == incidentId && !x.IsDeleted, cancellationToken);
        if (i is null)
            return null;
        return new IncidentVoiceRetryInfo(i.Status, i.CreatedBy, i.Title, i.Description, i.Severity);
    }
}
