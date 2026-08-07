using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class ScheduleOccurrenceRepository(ApplicationDbContext context, ILogger<ScheduleOccurrenceRepository> logger)
    : Repository<ScheduleOccurrence>(context, logger), IScheduleOccurrenceRepository;
