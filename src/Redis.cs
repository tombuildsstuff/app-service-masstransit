using Azure.Core;
using Microsoft.Azure.StackExchangeRedis;
using StackExchange.Redis;

namespace Orders;

public static class Redis
{
    // -----------------------------------------------------------------------
    // Connects to Azure Cache for Redis over TLS, in one of two ways:
    //
    //   * With an access key, when REDIS_KEY is set.
    //   * Otherwise as the app's managed identity: the
    //     Microsoft.Azure.StackExchangeRedis extension fetches an Entra token
    //     for the identity, signs in with the identity's object ID as the
    //     username and the token as the password, and re-authenticates before
    //     the token expires.
    // -----------------------------------------------------------------------
    public static async Task<IConnectionMultiplexer> ConnectAsync(string host, int port, string? key,
        TokenCredential credential, ILogger logger)
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { { host, port } },
            Ssl = true,
            AbortOnConnectFail = false,
        };

        if (key is { Length: > 0 })
        {
            options.Password = key;
            logger.LogInformation("Connecting to Redis at {Host}:{Port} with an access key", host, port);
        }
        else
        {
            await options.ConfigureForAzureWithTokenCredentialAsync(credential);
            logger.LogInformation("Connecting to Redis at {Host}:{Port} with an Entra token", host, port);
        }

        return await ConnectionMultiplexer.ConnectAsync(options);
    }
}
