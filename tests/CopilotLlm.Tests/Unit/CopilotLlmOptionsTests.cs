using CopilotLlm.Client;

namespace CopilotLlm.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class CopilotLlmOptionsTests
{
    [Fact]
    public void Defaults_AreConfiguredForSdkUsage()
    {
        var options = new CopilotLlmOptions();

        Assert.Null(options.DefaultModel);
        Assert.Equal(TimeSpan.FromSeconds(120), options.HttpTimeout);
    }
}
