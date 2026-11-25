using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IPocketBaseClient"/> and the per-domain services
    /// (<see cref="IPocketBaseTransport"/>, <see cref="IBatchService"/>, <see cref="ICollectionsService"/>,
    /// <see cref="ISettingsService"/>, <see cref="IFilesService"/>, <see cref="ILogsService"/>,
    /// <see cref="IBackupsService"/>, <see cref="ICronsService"/>, <see cref="ISqlService"/>,
    /// <see cref="IHealthService"/>, <see cref="IRealtimeService"/>).
    /// Record services are per collection: use <c>client.Collection("name")</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the client options (<see cref="PocketBaseOptions.BaseUrl"/> is required).</param>
    /// <param name="authStoreLifetime">
    /// Lifetime of the <see cref="IAuthStore"/> (who "owns" the authenticated session):
    /// <list type="bullet">
    /// <item><see cref="ServiceLifetime.Scoped"/> (default): one session per scope, e.g. per HTTP request in ASP.NET Core,
    /// so the auth state of one user is never shared with another.</item>
    /// <item><see cref="ServiceLifetime.Singleton"/>: one session for the whole app (desktop/MAUI apps, workers using a single account).</item>
    /// </list>
    /// To use a custom store (e.g. persistent), register your own <see cref="IAuthStore"/> before calling this method.
    /// The <see cref="IRealtimeService"/> (subscriptions + SSE connection) is registered with the same lifetime.
    /// </param>
    /// <returns>The <see cref="IHttpClientBuilder"/> of the underlying client, to add handlers/policies
    /// (the .NET equivalent of the JS SDK <c>beforeSend</c>/<c>afterSend</c> hooks).</returns>
    public static IHttpClientBuilder AddPocketBase(
        this IServiceCollection services,
        Action<PocketBaseOptions> configure,
        ServiceLifetime authStoreLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        if (authStoreLifetime == ServiceLifetime.Transient)
            throw new ArgumentException("The auth store can't be transient: the authenticated state would be lost between services.", nameof(authStoreLifetime));

        var options = new PocketBaseOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new InvalidOperationException("PocketBase BaseUrl is required.");
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException($"PocketBase BaseUrl must be an absolute url: '{options.BaseUrl}'.");

        services.AddSingleton(options);
        services.TryAdd(new ServiceDescriptor(typeof(IAuthStore), typeof(AuthStore), authStoreLifetime));

        var builder = services.AddHttpClient<IPocketBaseTransport, PocketBaseTransport>();

        // The realtime service holds the subscriptions and the SSE connection, so it shares the auth session lifetime
        // (the realtime client is authorized with the auth store token). It resolves a fresh transport on each use.
        services.TryAdd(new ServiceDescriptor(typeof(IRealtimeService),
            sp => new RealtimeService(() => sp.GetRequiredService<IPocketBaseTransport>()),
            authStoreLifetime));

        // Transient: the typed HttpClient is transient; holding it in a singleton
        // would pin the HttpMessageHandler forever (stale DNS, no handler rotation).
        services.AddTransient<IPocketBaseClient, PocketBaseClient>();
        services.AddTransient<IBatchService>(sp => sp.GetRequiredService<IPocketBaseClient>().CreateBatch());
        services.AddTransient<ICollectionsService>(sp => sp.GetRequiredService<IPocketBaseClient>().Collections);
        services.AddTransient<ISettingsService>(sp => sp.GetRequiredService<IPocketBaseClient>().Settings);
        services.AddTransient<IFilesService>(sp => sp.GetRequiredService<IPocketBaseClient>().Files);
        services.AddTransient<ILogsService>(sp => sp.GetRequiredService<IPocketBaseClient>().Logs);
        services.AddTransient<IBackupsService>(sp => sp.GetRequiredService<IPocketBaseClient>().Backups);
        services.AddTransient<ICronsService>(sp => sp.GetRequiredService<IPocketBaseClient>().Crons);
        services.AddTransient<ISqlService>(sp => sp.GetRequiredService<IPocketBaseClient>().Sql);
        services.AddTransient<IHealthService>(sp => sp.GetRequiredService<IPocketBaseClient>().Health);

        return builder;
    }
}
