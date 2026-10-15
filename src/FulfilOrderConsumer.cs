using MassTransit;

namespace Orders;

// ---------------------------------------------------------------------------
// Fulfils an order. This stands in for the slow, failure-prone work a real
// system would do here (reserving stock, calling a courier's API, ...).
//
// An order for an item containing "fail" always throws, so you can watch
// MassTransit retry it, give up, move it to the error queue and publish a
// Fault<FulfilOrder> - which the saga turns into the Faulted state.
// ---------------------------------------------------------------------------
public class FulfilOrderConsumer(ILogger<FulfilOrderConsumer> logger) : IConsumer<FulfilOrder>
{
    public async Task Consume(ConsumeContext<FulfilOrder> context)
    {
        var order = context.Message;
        var attempt = context.GetRetryAttempt() + 1;
        logger.LogInformation("Fulfilling order {OrderId} ({Quantity} x {Item}), attempt {Attempt}",
            order.OrderId, order.Quantity, order.Item, attempt);

        await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);

        if (order.Item.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The warehouse couldn't find any '{order.Item}'.");
        }

        await context.Publish(new OrderFulfilled(order.OrderId, DateTimeOffset.UtcNow));
    }
}

public class FulfilOrderConsumerDefinition : ConsumerDefinition<FulfilOrderConsumer>
{
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<FulfilOrderConsumer> consumerConfigurator, IRegistrationContext context)
    {
        // Retry in-process three times, a second apart, before giving up.
        endpointConfigurator.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(1)));
    }
}
