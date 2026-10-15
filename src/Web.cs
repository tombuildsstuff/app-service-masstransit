using MassTransit;
using StackExchange.Redis;

namespace Orders;

public static class Web
{
    // The IDs of the most recent orders, newest first. The web app writes these
    // to Redis itself with StackExchange.Redis; everything else about an order
    // lives in the saga, which it asks over Service Bus.
    private static readonly RedisKey RecentOrders = "orders:recent";
    private const int RecentOrderCount = 20;

    public static void Map(WebApplication app)
    {
        app.MapGet("/", () => Results.Content(Page, "text/html"));

        // Places an order: publishes OrderSubmitted, which starts the saga.
        app.MapPost("/orders", async (HttpRequest request, IPublishEndpoint publish, IConnectionMultiplexer redis) =>
        {
            var form = await request.ReadFormAsync();
            var customer = form["customer"].ToString().Trim();
            var item = form["item"].ToString().Trim();
            if (customer.Length == 0 || item.Length == 0 || !int.TryParse(form["quantity"], out var quantity) || quantity < 1)
            {
                return Results.BadRequest("A customer, an item and a quantity of at least 1 are required.");
            }

            var orderId = NewId.NextGuid();
            await publish.Publish(new OrderSubmitted(orderId, customer, item, quantity, DateTimeOffset.UtcNow));

            var db = redis.GetDatabase();
            await db.ListLeftPushAsync(RecentOrders, orderId.ToString());
            await db.ListTrimAsync(RecentOrders, 0, RecentOrderCount - 1);

            return Results.Redirect("/");
        });

        // Asks the saga for the status of each recent order, using MassTransit's
        // request/response: the request is published, and the saga replies to
        // this app's own temporary queue.
        app.MapGet("/api/orders", async (IConnectionMultiplexer redis, IRequestClient<GetOrderStatus> client,
            CancellationToken cancellationToken) =>
        {
            var ids = await redis.GetDatabase().ListRangeAsync(RecentOrders);
            var orders = await Task.WhenAll(ids.Select(async id =>
            {
                var orderId = Guid.Parse(id.ToString());
                try
                {
                    var response = await client.GetResponse<OrderStatus, OrderNotFound>(new GetOrderStatus(orderId),
                        cancellationToken);
                    return response.Is(out Response<OrderStatus>? status)
                        ? (object)status.Message
                        : new { OrderId = orderId, State = "Pending" };
                }
                catch (RequestTimeoutException)
                {
                    return new { OrderId = orderId, State = "No reply" };
                }
            }));

            return Results.Ok(orders);
        });
    }

    private const string Page = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Orders - MassTransit on Locally</title>
          <style>
            body { font-family: system-ui, sans-serif; max-width: 60rem; margin: 2rem auto; padding: 0 1rem; color: #1f2328; }
            form { display: flex; gap: .5rem; flex-wrap: wrap; margin-bottom: 1.5rem; }
            input, button { font: inherit; padding: .4rem .6rem; }
            table { border-collapse: collapse; width: 100%; }
            th, td { text-align: left; padding: .4rem .6rem; border-bottom: 1px solid #d0d7de; }
            .Submitted, .Pending { color: #9a6700; } .Fulfilled { color: #1a7f37; } .Faulted, .No.reply { color: #cf222e; }
            small { color: #59636e; }
          </style>
        </head>
        <body>
          <h1>Orders</h1>
          <p>Place an order and watch the saga move it along. Order an item with <code>fail</code> in its name to see it retried and then faulted.</p>
          <form method="post" action="/orders">
            <input name="customer" placeholder="Customer" required>
            <input name="item" placeholder="Item" required>
            <input name="quantity" type="number" min="1" value="1" required>
            <button>Place order</button>
          </form>
          <table>
            <thead><tr><th>Order</th><th>Customer</th><th>Item</th><th>Qty</th><th>State</th><th>Detail</th></tr></thead>
            <tbody id="orders"><tr><td colspan="6"><small>Loading...</small></td></tr></tbody>
          </table>
          <script>
            async function refresh() {
              const body = document.getElementById("orders");
              try {
                const orders = await (await fetch("/api/orders")).json();
                body.replaceChildren(...orders.map(o => {
                  const tr = document.createElement("tr");
                  const cells = [o.orderId.slice(0, 8), o.customer ?? "", o.item ?? "", o.quantity ?? "", o.state, o.reason ?? ""];
                  for (const value of cells) {
                    const td = document.createElement("td");
                    td.textContent = value;
                    tr.appendChild(td);
                  }
                  tr.children[4].className = o.state;
                  return tr;
                }));
                if (orders.length === 0) body.innerHTML = '<tr><td colspan="6"><small>No orders yet.</small></td></tr>';
              } catch (e) {
                body.innerHTML = '<tr><td colspan="6"><small>Couldn\'t load orders.</small></td></tr>';
              }
            }
            refresh();
            setInterval(refresh, 2000);
          </script>
        </body>
        </html>
        """;
}
