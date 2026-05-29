using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Misty.Api.Common;

internal sealed class ServiceBusSenderHealthCheck : IHealthCheck
{
    private readonly string _connectionString;
    private readonly string _topic;

    public ServiceBusSenderHealthCheck(string connectionString, string topic)
    {
        _connectionString = connectionString;
        _topic = topic;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = new ServiceBusClient(_connectionString);
            await using var sender = client.CreateSender(_topic);
            await sender.CreateMessageBatchAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message, ex);
        }
    }
}
