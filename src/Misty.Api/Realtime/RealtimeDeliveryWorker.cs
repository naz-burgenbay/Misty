using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Misty.Infrastructure.Messaging;
using System.Text.Json;

namespace Misty.Api.Realtime;

// Consumes MessageCreated events from the realtime-delivery subscription and fans them out to connected SignalR clients.
public sealed class RealtimeDeliveryWorker : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly IHubContext<MistyHub> _hub;
    private readonly ILogger<RealtimeDeliveryWorker> _logger;

    public RealtimeDeliveryWorker(
        ServiceBusClient client,
        IHubContext<MistyHub> hub,
        ILogger<RealtimeDeliveryWorker> logger)
    {
        _client = client;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1,
        };

        await using var processor = _client.CreateProcessor("message-events", "realtime-delivery", opts);

        processor.ProcessMessageAsync += OnMessageAsync;
        processor.ProcessErrorAsync += OnErrorAsync;

        await processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }

        await processor.StopProcessingAsync();
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<MessageCreatedPayload>(args.Message.Body.ToString());
            if (payload is null)
            {
                await args.DeadLetterMessageAsync(args.Message, "NullPayload", "Deserialization returned null");
                return;
            }

            if (payload.ChannelId.HasValue)
                await _hub.Clients
                    .Group($"channel:{payload.ChannelId}")
                    .SendAsync("MessageCreated", payload, args.CancellationToken);
            else if (payload.ConversationId.HasValue)
                await _hub.Clients
                    .Group($"conversation:{payload.ConversationId}")
                    .SendAsync("MessageCreated", payload, args.CancellationToken);

            await args.CompleteMessageAsync(args.Message);
            _logger.LogDebug("Delivered MessageCreated {MessageId} via SignalR", payload.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process realtime-delivery message");
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus realtime-delivery processor error: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
