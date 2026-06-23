using Xunit;
using MMORPG.Framework.Observability;

namespace MMORPG.Framework.Tests.Observability;

[Collection("Observability")]
public class CrashReportingTests
{
    [Fact]
    public void IsEnabled_InitiallyFalse()
    {
        Assert.False(CrashReporting.IsEnabled);
    }

    [Fact]
    public void CrashCount_NonNegative()
    {
        Assert.True(CrashReporting.CrashCount >= 0);
    }

    [Fact]
    public void CrashDirectory_DefaultValue_ContainsLogs()
    {
        Assert.Contains("crashes", CrashReporting.CrashDirectory);
    }

    [Fact]
    public void ReportException_NullException_DoesNotThrow()
    {
        CrashReporting.ReportException(null);
    }

    [Fact]
    public void ReportException_ValidException_IncreasesCount()
    {
        var before = CrashReporting.CrashCount;

        CrashReporting.ReportException(new InvalidOperationException("test crash"), "test module");

        Assert.True(CrashReporting.CrashCount > before);
    }

    [Fact]
    public void Enable_WhenDisabled_Enables()
    {
        CrashReporting.Disable();

        CrashReporting.Enable();

        Assert.True(CrashReporting.IsEnabled);

        CrashReporting.Disable();
    }

    [Fact]
    public void Enable_WhenAlreadyEnabled_DoesNotThrow()
    {
        CrashReporting.Disable();
        CrashReporting.Enable();

        CrashReporting.Enable();

        Assert.True(CrashReporting.IsEnabled);

        CrashReporting.Disable();
    }

    [Fact]
    public void Disable_WhenEnabled_Disables()
    {
        CrashReporting.Enable();

        CrashReporting.Disable();

        Assert.False(CrashReporting.IsEnabled);
    }

    [Fact]
    public void Disable_WhenAlreadyDisabled_DoesNotThrow()
    {
        CrashReporting.Disable();

        CrashReporting.Disable();

        Assert.False(CrashReporting.IsEnabled);
    }

    [Fact]
    public void ReportException_WithInnerException_DoesNotThrow()
    {
        var inner = new ArgumentException("inner error");
        var outer = new InvalidOperationException("outer error", inner);

        CrashReporting.ReportException(outer, "with inner");
    }

    [Fact]
    public void Enable_WithCustomDirectory_SetsDirectory()
    {
        CrashReporting.Disable();
        var testDir = Path.Combine(Path.GetTempPath(), "test-crashes-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        try
        {
            CrashReporting.Enable(testDir);

            Assert.True(CrashReporting.IsEnabled);

            CrashReporting.ReportException(new Exception("test"), "custom dir test");

            Assert.True(Directory.Exists(testDir));
        }
        finally
        {
            CrashReporting.Disable();
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
        }
    }
}
