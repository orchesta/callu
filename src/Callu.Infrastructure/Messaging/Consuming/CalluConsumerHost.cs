using System.Text;
using Callu.Infrastructure.Messaging.Broker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Callu.Infrastructure.Messaging.Consuming;

/// <summary>Consumes the queues on the Worker: dedupe, handle, commit, then ack — in that order.</summary>
public sealed class CalluConsumerHost(
    IInboxMessageProcessor processor,
    ICalluBrokerConnection broker,
    ILogger<CalluConsumerHost> logger) : BackgroundService
{
    internal const ushort Prefetch = 1;
    internal static readonly TimeSpan BreakerBaseDelay = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan BreakerMaxDelay = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var breakerAttempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilStoppedAsync(stoppingToken);
                breakerAttempt = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                breakerAttempt++;
                var delay = Backoff(breakerAttempt);

                // Nothing is dropped while this is open: the broker keeps the messages and redelivers.
                logger.LogError(ex,
                    "Consumer host stopped consuming (attempt {Attempt}); retrying in {Delay}. "
                    + "Escalation triggers wait in the queue until it recovers", breakerAttempt, delay);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static TimeSpan Backoff(int attempt)
    {
        var scaled = BreakerBaseDelay * Math.Pow(2, Math.Min(attempt - 1, 5));
        return scaled > BreakerMaxDelay ? BreakerMaxDelay : scaled;
    }

    private async Task ConsumeUntilStoppedAsync(CancellationToken stoppingToken)
    {
        await using var channel = await broker.CreateConsumeChannelAsync(Prefetch, stoppingToken);
        await DeclareTopologyAsync(channel, stoppingToken);

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = stoppingToken.Register(() => stopped.TrySetResult());

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnDeliveryAsync(channel, delivery);

        foreach (var queue in CalluTopology.QueueRoutingKeys.Keys)
        {
            await channel.BasicConsumeAsync(queue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
            logger.LogInformation("Consuming {Queue}", queue);
        }

        await stopped.Task;
    }

    private static async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            CalluTopology.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            CalluTopology.DeadLetterExchange, ExchangeType.Topic, durable: true, autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);

        foreach (var (queue, routingKey) in CalluTopology.QueueRoutingKeys)
        {
            await channel.QueueDeclareAsync(
                queue, durable: true, exclusive: false, autoDelete: false,
                arguments: CalluTopology.MainQueueArguments(), cancellationToken: cancellationToken);
            await channel.QueueBindAsync(queue, CalluTopology.Exchange, routingKey, cancellationToken: cancellationToken);

            var deadLetterQueue = CalluTopology.DeadLetterQueues[queue];
            await channel.QueueDeclareAsync(
                deadLetterQueue, durable: true, exclusive: false, autoDelete: false,
                arguments: null, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(
                deadLetterQueue, CalluTopology.DeadLetterExchange, routingKey, cancellationToken: cancellationToken);
        }
    }

    /// <summary>Thin adapter: the ordering lives in the processor, this only maps its verdict to AMQP.</summary>
    private async Task OnDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        var messageType = delivery.BasicProperties.Type;
        var payload = Encoding.UTF8.GetString(delivery.Body.Span);

        if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId) || string.IsNullOrEmpty(messageType))
        {
            logger.LogError(
                "Rejecting a delivery with no usable message id or type (id {MessageId}, type {MessageType})",
                delivery.BasicProperties.MessageId, messageType);
            await RejectAsync(channel, delivery.DeliveryTag);
            return;
        }

        var traceParent = TraceParentOf(delivery);
        using var activity = MessagingActivity.StartConsume(messageType, traceParent);

        var outcome = await processor.ProcessAsync(
            messageId, messageType, payload, traceParent, CancellationToken.None);

        switch (outcome)
        {
            case DeliveryOutcome.Ack:
                await AckAsync(channel, delivery.DeliveryTag);
                break;
            case DeliveryOutcome.DeadLetter:
                await RejectAsync(channel, delivery.DeliveryTag);
                break;
            default:
                await NackAsync(channel, delivery.DeliveryTag);
                break;
        }
    }

    private static string? TraceParentOf(BasicDeliverEventArgs delivery) =>
        delivery.BasicProperties.Headers is { } headers
        && headers.TryGetValue("traceparent", out var value)
        && value is byte[] raw
            ? Encoding.UTF8.GetString(raw)
            : null;

    // Acks run on CancellationToken.None: the work is already committed, and a cancelled ack would
    // hand the same committed message back for redelivery.
    private static Task AckAsync(IChannel channel, ulong deliveryTag) =>
        channel.BasicAckAsync(deliveryTag, multiple: false, CancellationToken.None).AsTask();

    private static Task NackAsync(IChannel channel, ulong deliveryTag) =>
        channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true, CancellationToken.None).AsTask();

    private static Task RejectAsync(IChannel channel, ulong deliveryTag) =>
        channel.BasicRejectAsync(deliveryTag, requeue: false, CancellationToken.None).AsTask();
}
