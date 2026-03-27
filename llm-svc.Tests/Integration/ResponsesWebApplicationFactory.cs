using LlmSvc.Core.Ports;
using llm_svc.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace llm_svc.Tests.Integration;

public sealed class ResponsesWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakeModelProvider Provider { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IModelProvider>();
            services.AddSingleton<IModelProvider>(Provider);
        });
    }
}
