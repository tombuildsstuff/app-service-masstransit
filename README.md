# Orders App: MassTransit on Azure App Service with Service Bus and Redis

Two small .NET apps that talk to each other through [MassTransit](https://masstransit.io) over Azure Service Bus, with a saga whose state lives in Azure Cache for Redis. Both apps run from one image, and the `ROLE` environment variable decides which one a container runs as. The same image runs unchanged on Azure App Service or on [Locally](https://locally.build).

* **web** is a page where you place orders. Placing one publishes an `OrderSubmitted` event. The page lists recent orders and refreshes every couple of seconds, asking the saga for each order's status over MassTransit's request/response.
* **worker** hosts the **order saga** (a MassTransit state machine) and the **fulfilment consumer**. The saga starts when an order is submitted and asks for the order to be fulfilled. It then moves to `Fulfilled` or `Faulted` depending on what happens. Order an item with `fail` in its name to see the failure path.

It's written in C# on ASP.NET Core (.NET 10), and pins MassTransit 8.x, which is Apache 2.0 licensed.

## The Code

| File | What it does |
| --- | --- |
| [`src/Program.cs`](./src/Program.cs) | Reads the configuration, sets up the managed identity credential, and registers the saga and consumer (worker) or the request client (web). |
| [`src/Contracts.cs`](./src/Contracts.cs) | The messages the two apps exchange. |
| [`src/OrderStateMachine.cs`](./src/OrderStateMachine.cs) | The order saga. |
| [`src/FulfilOrderConsumer.cs`](./src/FulfilOrderConsumer.cs) | The fulfilment consumer, and its retry policy. |
| [`src/ServiceBus.cs`](./src/ServiceBus.cs) | Connects MassTransit to Service Bus with an Entra token, and uses the namespace's port for the administration API when one is given. |
| [`src/Redis.cs`](./src/Redis.cs) | Connects to Redis with an Entra token, or with an access key if one is set. |
| [`src/Web.cs`](./src/Web.cs) | The web app's page and endpoints. |

## Configuration

Configuration comes from environment variables.

| Variable | Required | Default | Notes |
| --- | --- | --- | --- |
| `ROLE` | Yes | (none) | `web` or `worker`. |
| `PORT` | No | `8080` | HTTP server port. |
| `AZURE_CLIENT_ID` | Yes | (none) | Client ID of the user-assigned managed identity to get tokens as. |
| `SERVICEBUS_ENDPOINT` | Yes | (none) | The Service Bus namespace's endpoint URL. There's no connection string: the app always signs in with the managed identity. |
| `REDIS_HOST` | Yes | (none) | The Redis cache's hostname. |
| `REDIS_PORT` | Yes | (none) | The Redis cache's TLS port: 6380 on Azure, a dynamically allocated port on Locally. |
| `REDIS_KEY` | No | (none) | The cache's access key. If it's not set, the app signs in to Redis with the managed identity instead. |

The app gets its tokens from the managed identity endpoint App Service provides (`IDENTITY_ENDPOINT` and `IDENTITY_HEADER`), so it's meant to run on App Service rather than on its own.

## Building & Publishing the Image

The image is published to Docker Hub as [`tombuildsstuff/app-service-masstransit`](https://hub.docker.com/r/tombuildsstuff/app-service-masstransit).

Build a multi-arch image, so it runs on both Azure App Service's `linux/amd64` and Apple Silicon's `linux/arm64`, and push it. With Docker:

```bash
docker buildx build --platform linux/amd64,linux/arm64 \
  -t tombuildsstuff/app-service-masstransit:v1.0.0 \
  -t tombuildsstuff/app-service-masstransit:latest --push .
```

Or with Podman:

```bash
podman build --platform linux/amd64,linux/arm64 --manifest app-service-masstransit:v1.0.0 .
podman manifest push --all app-service-masstransit:v1.0.0 docker://docker.io/tombuildsstuff/app-service-masstransit:v1.0.0
podman manifest push --all app-service-masstransit:v1.0.0 docker://docker.io/tombuildsstuff/app-service-masstransit:latest
```

The SDK stage of the [`Dockerfile`](./Dockerfile) runs on the build machine's architecture and publishes for each target, so neither build runs the .NET SDK under emulation.

## Deploying the Infrastructure

The infrastructure that runs this image (the two App Services, Service Bus, Redis and their managed identities) lives in a separate repository, [`locallybuild/example-app-service-masstransit`](https://github.com/locallybuild/example-app-service-masstransit). It provisions everything with Terraform and imports this image into a Container Registry for App Service to pull. See that repository's README for how to deploy it against Locally or Azure.
