using Azure.Core;
using Azure.Identity;
using MassTransit;
using Orders;
using StackExchange.Redis;

// ---------------------------------------------------------------------------
// One image, two apps: the ROLE app setting picks which one this container is.
//
//   * web    - the page where orders are placed and their progress is shown.
//   * worker - hosts the order saga and the fulfilment consumer.
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);
var role = Required("ROLE");
if (role is not ("web" or "worker"))
{
    throw new InvalidOperationException($"ROLE must be 'web' or 'worker', not '{role}'.");
}

builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

// The app's user-assigned managed identity. App Service exposes it to the
// container through the managed identity endpoint (the IDENTITY_ENDPOINT and
// IDENTITY_HEADER environment variables), and AZURE_CLIENT_ID picks which of
// the web app's identities to issue tokens for. Service Bus is always reached
// with this credential, and Redis is too unless it's given an access key.
TokenCredential credential = new ManagedIdentityCredential(
    ManagedIdentityId.FromUserAssignedClientId(Required("AZURE_CLIENT_ID")));

using var loggerFactory = LoggerFactory.Create(l => l.AddConsole());
var redis = await Redis.ConnectAsync(Required("REDIS_HOST"), int.Parse(Required("REDIS_PORT")),
    Environment.GetEnvironmentVariable("REDIS_KEY"), credential, loggerFactory.CreateLogger("Redis"));
builder.Services.AddSingleton(redis);

builder.Services.AddMassTransit(x =>
{
    // Queues are named after their consumers in kebab-case: "fulfil-order",
    // "order-state", and "fulfil-order_error" for messages that failed.
    x.SetKebabCaseEndpointNameFormatter();

    if (role == "worker")
    {
        // The saga's instances are stored in Redis, through the same
        // Entra-authenticated connection the app uses everywhere else.
        x.AddSagaStateMachine<OrderStateMachine, OrderState>()
            .RedisRepository(r =>
            {
                r.ConnectionFactory(_ => redis);
                r.KeyPrefix = "saga";
            });

        x.AddConsumer<FulfilOrderConsumer, FulfilOrderConsumerDefinition>();
    }
    else
    {
        x.AddRequestClient<GetOrderStatus>(RequestTimeout.After(s: 5));
    }

    x.UsingAzureServiceBus((context, cfg) =>
    {
        // Terraform hands over the namespace's endpoint URL.
        cfg.Host(ServiceBus.HostSettings(new Uri(Required("SERVICEBUS_ENDPOINT")), credential));

        // Creates a queue for each consumer and saga registered above, plus the
        // topics and subscriptions that route published events into them.
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

// Reports unhealthy until the bus has connected to Service Bus and started
// its receive endpoints, so App Service won't route traffic to it before then.
app.MapHealthChecks("/healthz");

if (role == "web")
{
    Web.Map(app);
}
else
{
    app.MapGet("/", () => "The worker is running: it hosts the order saga and the fulfilment consumer.");
}

app.Run();

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"The {name} environment variable must be set.");
