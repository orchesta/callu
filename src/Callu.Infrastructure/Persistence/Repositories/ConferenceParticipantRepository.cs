using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class ConferenceParticipantRepository(ApplicationDbContext context, ILogger<ConferenceParticipantRepository> logger)
    : Repository<ConferenceParticipant>(context, logger), IConferenceParticipantRepository
{
}
