using Xunit;
using MMORPG.Framework.Observability;

namespace MMORPG.Framework.Tests.Observability;

public class HealthCheckStatusTests
{
    [Fact]
    public void Constructor_SetsOverallStatus()
    {
        var entries = new List<HealthCheckEntry>
        {
            new HealthCheckEntry("test", HealthCheckResult.Healthy, "ok")
        };

        var status = new HealthCheckStatus(HealthCheckResult.Healthy, entries);

        Assert.Equal(HealthCheckResult.Healthy, status.OverallStatus);
    }

    [Fact]
    public void Constructor_SetsEntries()
    {
        var entries = new List<HealthCheckEntry>
        {
            new HealthCheckEntry("cpu", HealthCheckResult.Healthy, "10%"),
            new HealthCheckEntry("memory", HealthCheckResult.Degraded, "80%")
        };

        var status = new HealthCheckStatus(HealthCheckResult.Degraded, entries);

        Assert.Equal(2, status.Entries.Count);
        Assert.Equal("cpu", status.Entries[0].Name);
        Assert.Equal("memory", status.Entries[1].Name);
    }

    [Fact]
    public void Constructor_SetsTimestamp()
    {
        var before = DateTime.UtcNow;

        var status = new HealthCheckStatus(HealthCheckResult.Healthy, new List<HealthCheckEntry>());

        Assert.True(status.TimestampUtc >= before);
        Assert.True(status.TimestampUtc <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void RenderText_ContainsOverallStatus()
    {
        var entries = new List<HealthCheckEntry>
        {
            new HealthCheckEntry("tcp", HealthCheckResult.Healthy, "listening")
        };
        var status = new HealthCheckStatus(HealthCheckResult.Healthy, entries);

        var text = status.RenderText();

        Assert.Contains("Overall: Healthy", text);
    }

    [Fact]
    public void RenderText_ContainsEntryInfo()
    {
        var entries = new List<HealthCheckEntry>
        {
            new HealthCheckEntry("cpu_usage", HealthCheckResult.Healthy, "current 10%"),
            new HealthCheckEntry("memory_usage", HealthCheckResult.Degraded, "80% used")
        };
        var status = new HealthCheckStatus(HealthCheckResult.Degraded, entries);

        var text = status.RenderText();

        Assert.Contains("cpu_usage", text);
        Assert.Contains("memory_usage", text);
        Assert.Contains("current 10%", text);
        Assert.Contains("80% used", text);
    }

    [Fact]
    public void RenderText_ContainsTimestamp()
    {
        var status = new HealthCheckStatus(HealthCheckResult.Healthy, new List<HealthCheckEntry>());

        var text = status.RenderText();

        Assert.Contains("UTC", text);
    }

    [Fact]
    public void RenderJson_ContainsOverallStatus()
    {
        var entries = new List<HealthCheckEntry>
        {
            new HealthCheckEntry("test", HealthCheckResult.Healthy, "ok")
        };
        var status = new HealthCheckStatus(HealthCheckResult.Healthy, entries);

        var json = status.RenderJson();

        Assert.Contains("\"overall_status\":\"Healthy\"", json);
    }

    [Fact]
    public void RenderJson_ContainsTimestamp()
    {
        var status = new HealthCheckStatus(HealthCheckResult.Healthy, new List<HealthCheckEntry>());

        var json = status.RenderJson();

        Assert.Contains("\"timestamp\"", json);
    }

    [Fact]
    public void RenderJson_ContainsEntries()
    {
        var entries = new List<HealthCheckEntry>
        {
            new HealthCheckEntry("cpu", HealthCheckResult.Healthy, "10%"),
            new HealthCheckEntry("mem", HealthCheckResult.Degraded, "80%")
        };
        var status = new HealthCheckStatus(HealthCheckResult.Degraded, entries);

        var json = status.RenderJson();

        Assert.Contains("\"name\":\"cpu\"", json);
        Assert.Contains("\"name\":\"mem\"", json);
        Assert.Contains("\"status\":\"Healthy\"", json);
        Assert.Contains("\"status\":\"Degraded\"", json);
        Assert.Contains("\"description\":\"10%\"", json);
        Assert.Contains("\"description\":\"80%\"", json);
    }

    [Fact]
    public void RenderJson_EmptyEntries_ValidJson()
    {
        var status = new HealthCheckStatus(HealthCheckResult.Healthy, new List<HealthCheckEntry>());

        var json = status.RenderJson();

        Assert.StartsWith("{", json);
        Assert.EndsWith("}", json);
        Assert.Contains("\"entries\":[]", json);
    }

    [Fact]
    public void RenderJson_DescriptionWithQuotes_RemovesQuotes()
    {
        var entries = new List<HealthCheckEntry>
        {
            new HealthCheckEntry("test", HealthCheckResult.Unhealthy, "value \"quoted\" text")
        };
        var status = new HealthCheckStatus(HealthCheckResult.Unhealthy, entries);

        var json = status.RenderJson();

        Assert.Contains("\"description\":\"value quoted text\"", json);
    }

    [Fact]
    public void RenderText_UnhealthyStatus()
    {
        var entries = new List<HealthCheckEntry>
        {
            new HealthCheckEntry("db_connection", HealthCheckResult.Unhealthy, "connection refused")
        };
        var status = new HealthCheckStatus(HealthCheckResult.Unhealthy, entries);

        var text = status.RenderText();

        Assert.Contains("Overall: Unhealthy", text);
        Assert.Contains("db_connection", text);
        Assert.Contains("connection refused", text);
    }

    [Fact]
    public void HealthCheckEntry_Constructor_SetsProperties()
    {
        var entry = new HealthCheckEntry("test_name", HealthCheckResult.Degraded, "test description");

        Assert.Equal("test_name", entry.Name);
        Assert.Equal(HealthCheckResult.Degraded, entry.Result);
        Assert.Equal("test description", entry.Description);
    }

    [Fact]
    public void HealthCheckResult_EnumValues()
    {
        Assert.Equal(0, (int)HealthCheckResult.Healthy);
        Assert.Equal(1, (int)HealthCheckResult.Degraded);
        Assert.Equal(2, (int)HealthCheckResult.Unhealthy);
    }
}
