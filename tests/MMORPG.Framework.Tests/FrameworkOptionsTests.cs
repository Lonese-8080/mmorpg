using Xunit;
using MMORPG.Framework;
using MMORPG.Framework.Observability;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Tests;

/// <summary>
/// FrameworkOptions 和 FrameworkBootstrap 单元测试
/// </summary>
public class FrameworkOptionsTests : IDisposable
{
    public FrameworkOptionsTests()
    {
        Logger.Initialize(new LogOptions { MinLevel = LogLevel.Debug, EnableConsole = false, EnableFile = false });
        FrameworkBootstrap.ResetConfiguration();
    }

    public void Dispose()
    {
        FrameworkBootstrap.ResetConfiguration();
    }

    [Fact]
    public void DefaultOptions_AllDisabled()
    {
        var opt = new FrameworkOptions();
        Assert.False(opt.EnableMetrics);
        Assert.False(opt.EnableHealthChecks);
        Assert.False(opt.EnableCrashReporting);
        Assert.False(opt.EnableGlobalRateLimiting);
        Assert.False(opt.EnableSessionRateLimiting);
        Assert.False(opt.EnableConfigHotReload);
    }

    [Fact]
    public void ProductionOptions_AllCoreEnabled()
    {
        var opt = FrameworkOptions.CreateProduction();
        Assert.True(opt.EnableMetrics);
        Assert.True(opt.EnableHealthChecks);
        Assert.True(opt.EnableCrashReporting);
        Assert.True(opt.EnableGlobalRateLimiting);
        Assert.True(opt.EnableSessionRateLimiting);
        Assert.True(opt.EnableConfigHotReload);
        Assert.True(opt.GlobalRateLimitMessagesPerSecond > 0);
    }

    [Fact]
    public void Configure_WithAllOptions_EnablesModules()
    {
        var opt = FrameworkOptions.CreateProduction();
        opt.EnableGlobalRateLimiting = false;
        opt.EnableConfigHotReload = false;

        FrameworkBootstrap.Configure(opt);

        Assert.True(FrameworkBootstrap.IsConfigured);
        Assert.True(MetricsCollector.Instance.IsEnabled);
        Assert.True(CrashReporting.IsEnabled);
    }

    [Fact]
    public void Configure_NullOptions_SilentIgnore()
    {
        FrameworkBootstrap.Configure(null!);
        Assert.False(FrameworkBootstrap.IsConfigured);
    }

    [Fact]
    public void Configure_SecondCall_IsIdempotent()
    {
        var opt = FrameworkOptions.CreateProduction();
        opt.EnableGlobalRateLimiting = false;
        opt.EnableConfigHotReload = false;

        FrameworkBootstrap.Configure(opt);
        var firstConfigured = FrameworkBootstrap.IsConfigured;

        FrameworkBootstrap.Configure(opt);
        var secondConfigured = FrameworkBootstrap.IsConfigured;

        Assert.True(firstConfigured);
        Assert.True(secondConfigured);
    }

    [Fact]
    public void ResetConfiguration_ClearsAll()
    {
        var opt = FrameworkOptions.CreateProduction();
        opt.EnableGlobalRateLimiting = false;
        opt.EnableConfigHotReload = false;

        FrameworkBootstrap.Configure(opt);
        Assert.True(FrameworkBootstrap.IsConfigured);

        FrameworkBootstrap.ResetConfiguration();
        Assert.False(FrameworkBootstrap.IsConfigured);
        Assert.False(MetricsCollector.Instance.IsEnabled);
        Assert.False(CrashReporting.IsEnabled);
    }

    [Fact]
    public void DebugOptions_RateLimitingDisabled()
    {
        var opt = FrameworkOptions.CreateDebug();
        Assert.False(opt.EnableGlobalRateLimiting);
        Assert.False(opt.EnableSessionRateLimiting);
    }

    [Fact]
    public void CrashReportDirectory_CanBeCustomized()
    {
        var opt = new FrameworkOptions
        {
            EnableCrashReporting = true,
            CrashReportDirectory = "/custom/crash/dir"
        };
        Assert.Equal("/custom/crash/dir", opt.CrashReportDirectory);
    }
}
