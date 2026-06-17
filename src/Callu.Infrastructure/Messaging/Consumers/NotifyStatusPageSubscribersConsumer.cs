using Callu.Application.Contracts.Messages;
using Callu.Application.Services;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Messaging.Consumers;

public sealed class NotifyStatusPageSubscribersConsumer(
    IStatusPageSubscriberEmailSender sender,
    ILogger<NotifyStatusPageSubscribersConsumer> logger)
    : IConsumer<NotifyStatusPageSubscribers>
{
    public async Task Consume(ConsumeContext<NotifyStatusPageSubscribers> context)
    {
        logger.LogInformation("NotifyStatusPageSubscribers: incident {IncidentId}", context.Message.StatusPageIncidentId);
        await sender.SendForIncidentAsync(context.Message.StatusPageIncidentId, context.CancellationToken);
    }
}
