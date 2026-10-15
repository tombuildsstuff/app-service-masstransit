namespace Orders;

// ---------------------------------------------------------------------------
// The messages exchanged over Service Bus.
//
// MassTransit names the Service Bus entities after these types: each event that
// is published becomes a topic (e.g. "orders/ordersubmitted"), and each command
// that is sent goes to the queue of the endpoint that consumes it.
// ---------------------------------------------------------------------------

// Published by the web app when an order is placed. Starts the order saga.
public record OrderSubmitted(Guid OrderId, string Customer, string Item, int Quantity, DateTimeOffset SubmittedAt);

// Published by the saga to ask for the order to be fulfilled.
public record FulfilOrder(Guid OrderId, string Item, int Quantity);

// Published by the fulfilment consumer once the order has been fulfilled.
public record OrderFulfilled(Guid OrderId, DateTimeOffset FulfilledAt);

// Request/response: the web app asks the saga where an order has got to.
public record GetOrderStatus(Guid OrderId);

public record OrderStatus(Guid OrderId, string State, string Customer, string Item, int Quantity,
    DateTimeOffset SubmittedAt, DateTimeOffset? CompletedAt, string? Reason);

public record OrderNotFound(Guid OrderId);
