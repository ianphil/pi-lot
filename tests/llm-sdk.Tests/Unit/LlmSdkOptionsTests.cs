using LlmSdk.Client;

namespace LlmSdk.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class LlmSdkOptionsTests
{
    [Fact]
    public void Defaults_AreConfiguredForSdkUsage()
    {
        var options = new LlmSdkOptions();

        Assert.Null(options.DefaultModel);
        Assert.Equal(TimeSpan.FromSeconds(120), options.HttpTimeout);
    }
}
