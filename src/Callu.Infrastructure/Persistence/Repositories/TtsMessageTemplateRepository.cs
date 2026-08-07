using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class TtsMessageTemplateRepository(ApplicationDbContext context, ILogger<TtsMessageTemplateRepository> logger)
    : Repository<TtsMessageTemplate>(context, logger), ITtsMessageTemplateRepository
{
}
