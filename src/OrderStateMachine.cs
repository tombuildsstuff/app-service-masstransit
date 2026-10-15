using MassTransit;

namespace Orders;

// ---------------------------------------------------------------------------
// The order saga - a MassTransit state machine that tracks each order from
// submission to fulfilment (or failure).
//
// Each instance is persisted in Azure Cache for Redis by MassTransit.Redis,
// keyed by the order ID, so the saga survives restarts and can be handled by
// any worker instance. Optimistic concurrency uses the Version property.
// ---------------------------------------------------------------------------
public class OrderState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }
    public int Version { get; set; }

    public string CurrentState { get; set; } = null!;
    public string Customer { get; set; } = null!;
    public string Item { get; set; } = null!;
    public int Quantity { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Reason { get; set; }
}

public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public State Submitted { get; private set; } = null!;
    public State Fulfilled { get; private set; } = null!;
    public State Faulted { get; private set; } = null!;

    public Event<OrderSubmitted> OrderSubmitted { get; private set; } = null!;
    public Event<OrderFulfilled> OrderFulfilled { get; private set; } = null!;
    public Event<Fault<FulfilOrder>> FulfilmentFaulted { get; private set; } = null!;
    public Event<GetOrderStatus> StatusRequested { get; private set; } = null!;

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderSubmitted, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => OrderFulfilled, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => FulfilmentFaulted, x => x.CorrelateById(m => m.Message.Message.OrderId));
        Event(() => StatusRequested, x =>
        {
            x.CorrelateById(m => m.Message.OrderId);

            // The status request may arrive before the saga exists (or after it
            // has expired), so answer "not found" rather than faulting.
            x.OnMissingInstance(m => m.ExecuteAsync(ctx => ctx.RespondAsync(new OrderNotFound(ctx.Message.OrderId))));
        });

        Initially(
            When(OrderSubmitted)
                .Then(ctx =>
                {
                    ctx.Saga.Customer = ctx.Message.Customer;
                    ctx.Saga.Item = ctx.Message.Item;
                    ctx.Saga.Quantity = ctx.Message.Quantity;
                    ctx.Saga.SubmittedAt = ctx.Message.SubmittedAt;
                })
                .Publish(ctx => new FulfilOrder(ctx.Saga.CorrelationId, ctx.Saga.Item, ctx.Saga.Quantity))
                .TransitionTo(Submitted));

        During(Submitted,
            When(OrderFulfilled)
                .Then(ctx => ctx.Saga.CompletedAt = ctx.Message.FulfilledAt)
                .TransitionTo(Fulfilled),

            // MassTransit publishes Fault<FulfilOrder> once the consumer has
            // exhausted its retries; the message itself is moved to the
            // fulfil-order_error queue.
            When(FulfilmentFaulted)
                .Then(ctx =>
                {
                    ctx.Saga.CompletedAt = ctx.Message.Timestamp;
                    ctx.Saga.Reason = ctx.Message.Exceptions.FirstOrDefault()?.Message;
                })
                .TransitionTo(Faulted));

        DuringAny(
            When(StatusRequested)
                .Respond(ctx => new OrderStatus(
                    ctx.Saga.CorrelationId, ctx.Saga.CurrentState, ctx.Saga.Customer, ctx.Saga.Item,
                    ctx.Saga.Quantity, ctx.Saga.SubmittedAt, ctx.Saga.CompletedAt, ctx.Saga.Reason)));
    }
}
