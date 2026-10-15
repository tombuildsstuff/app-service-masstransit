using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using MassTransit.AzureServiceBusTransport.Configuration;

namespace Orders;

public static class ServiceBus
{
    // -----------------------------------------------------------------------
    // Builds MassTransit's host settings from the namespace's endpoint URL.
    //
    // MassTransit uses two clients: the ServiceBusClient sends and receives
    // messages over AMQP, and the ServiceBusAdministrationClient creates the
    // queues, topics and subscriptions over HTTPS. Both take only a hostname,
    // so the administration client always connects on port 443. That's right
    // on Azure, but Locally serves the HTTPS endpoint on its own port, so
    // when the endpoint names a port the administration client is pointed at it.
    // -----------------------------------------------------------------------
    public static HostSettings HostSettings(Uri endpoint, TokenCredential credential)
    {
        var adminOptions = new ServiceBusAdministrationClientOptions();
        if (!endpoint.IsDefaultPort)
        {
            adminOptions.AddPolicy(new UsePortPolicy(endpoint.Port), HttpPipelinePosition.PerRetry);
        }

        return new HostSettings
        {
            ServiceUri = new Uri($"sb://{endpoint.Host}"),
            TokenCredential = credential,
            ServiceBusClient = new ServiceBusClient(endpoint.Host, credential),
            ServiceBusAdministrationClient = new ServiceBusAdministrationClient(endpoint.Host, credential, adminOptions),
        };
    }

    private class UsePortPolicy(int port) : HttpPipelineSynchronousPolicy
    {
        public override void OnSendingRequest(HttpMessage message) => message.Request.Uri.Port = port;
    }
}
