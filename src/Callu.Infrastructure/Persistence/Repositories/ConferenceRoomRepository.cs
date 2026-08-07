using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class ConferenceRoomRepository(ApplicationDbContext context, ILogger<ConferenceRoomRepository> logger)
    : Repository<ConferenceRoom>(context, logger), IConferenceRoomRepository
{
}
