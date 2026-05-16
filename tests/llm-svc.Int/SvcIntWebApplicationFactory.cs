using LlmSdk.Proxy;
using LlmSvc.Int.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LlmSvc.Int;

internal sealed class SvcIntWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly FakeModelProvider? _fakeProvider;

    public SvcIntWebApplicationFactory(FakeModelProvider? fakeProvider = null)
    {
        _fakeProvider = fakeProvider;
    }

    public static SvcIntWebApplicationFactory CreateFake(FakeModelProvider provider) => new(provider);

    public static SvcIntWebApplicationFactory CreateLive() => new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (_fakeProvider is null)
        {
            return;
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthProvider>();
            services.RemoveAll<IModelProvider>();
            services.AddSingleton<IAuthProvider>(_fakeProvider);
            services.AddSingleton<IModelProvider>(_fakeProvider);
        });
    }
}
