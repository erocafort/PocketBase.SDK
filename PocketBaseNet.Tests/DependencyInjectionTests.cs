using Microsoft.Extensions.DependencyInjection;
using PocketBaseNet.Abstractions;
using PocketBaseNet.DependencyInjection;

namespace PocketBaseNet.Tests;

public class DependencyInjectionTests
{
    private static ServiceProvider Build(Action<IServiceCollection>? extra = null, ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        var services = new ServiceCollection();
        extra?.Invoke(services);
        services.AddPocketBase(o => o.BaseUrl = "http://127.0.0.1:8090", lifetime);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void ResolvesClientAndServices()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IPocketBaseClient>();
        Assert.Equal("/api/collections/posts/records", client.Collection("posts").BaseCrudPath);
        Assert.Same(client.Collection("posts"), client.Collection("posts"));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBatchService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICollectionsService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISettingsService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFilesService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ILogsService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBackupsService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICronsService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISqlService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IHealthService>());
    }

    [Fact]
    public void ScopedAuthStore_IsSharedWithinScope_AndIsolatedBetweenScopes()
    {
        using var sp = Build();
        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();

        var a = scope1.ServiceProvider.GetRequiredService<IPocketBaseClient>();
        var b = scope1.ServiceProvider.GetRequiredService<IPocketBaseTransport>();
        var other = scope2.ServiceProvider.GetRequiredService<IPocketBaseClient>();

        a.AuthStore.Save("token-user-1");

        Assert.Equal("token-user-1", b.AuthStore.Token);
        Assert.Equal("", other.AuthStore.Token);
    }

    [Fact]
    public void SingletonAuthStore_IsShared()
    {
        using var sp = Build(lifetime: ServiceLifetime.Singleton);

        sp.GetRequiredService<IPocketBaseClient>().AuthStore.Save("t");

        Assert.Equal("t", sp.GetRequiredService<IPocketBaseClient>().AuthStore.Token);
    }

    [Fact]
    public void Realtime_SharesTheAuthStoreLifetime()
    {
        using var sp = Build();
        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();

        var realtime = scope1.ServiceProvider.GetRequiredService<IRealtimeService>();

        // the transient clients of the same scope share the realtime subscriptions
        Assert.Same(realtime, scope1.ServiceProvider.GetRequiredService<IPocketBaseClient>().Realtime);
        Assert.Same(realtime, scope1.ServiceProvider.GetRequiredService<IPocketBaseClient>().Realtime);
        Assert.NotSame(realtime, scope2.ServiceProvider.GetRequiredService<IRealtimeService>());

        using var singleton = Build(lifetime: ServiceLifetime.Singleton);
        Assert.Same(singleton.GetRequiredService<IRealtimeService>(), singleton.GetRequiredService<IPocketBaseClient>().Realtime);
    }

    [Fact]
    public void CustomAuthStore_IsRespected()
    {
        var custom = new AuthStore();
        using var sp = Build(s => s.AddSingleton<IAuthStore>(custom));
        using var scope = sp.CreateScope();

        Assert.Same(custom, scope.ServiceProvider.GetRequiredService<IPocketBaseClient>().AuthStore);
    }

    [Fact]
    public void InvalidConfiguration_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddPocketBase(_ => { }));
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddPocketBase(o => o.BaseUrl = "/relative"));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPocketBase(o => o.BaseUrl = "http://h", ServiceLifetime.Transient));
    }

    [Fact]
    public void FileUrl_EscapesAndAddsQuery()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();

        var record = RecordModel.FromJson("{\"id\":\"abc\",\"collectionId\":\"posts\"}");
        var url = scope.ServiceProvider.GetRequiredService<IFilesService>().GetURL(record, "my file.png", new PocketBaseNet.Abstractions.FileOptions { Thumb = "100x100" });
        Assert.Equal("http://127.0.0.1:8090/api/files/posts/abc/my%20file.png?thumb=100x100", url);
    }

    [Fact]
    public void StandaloneClient_WithoutDI()
    {
        var client = PocketBaseClient.Create(new PocketBaseOptions { BaseUrl = "http://127.0.0.1:8090" });
        Assert.Equal("http://127.0.0.1:8090/api/health", client.BuildUrl("/api/health"));
    }
}
