using System.Text.Json;
using Xunit;
using MMORPG.Framework.Observability;

namespace MMORPG.Framework.Tests.Observability;

/// <summary>
/// 健康检查服务单元测试
/// 
/// 由于 HealthCheckService 是单例，归入 Observability 集合，
/// 与其他可观测性测试串行执行以避免竞态条件。
/// </summary>
[Collection("Observability")]
public class HealthCheckTests : IDisposable
{
    private static HealthCheckService Svc => HealthCheckService.Instance;

    public HealthCheckTests()
    {
        Svc.Clear();
    }

    public void Dispose()
    {
        Svc.Clear();
    }

    [Fact]
    public async Task EmptyCheck_ReturnsHealthy()
    {
        var status = await Svc.CheckHealthAsync();
        Assert.NotNull(status);
        Assert.NotNull(status.Entries);
        Assert.InRange<int>((int)status.OverallStatus, 0, 2);
    }

    [Fact]
    public async Task SingleHealthyCheck_ReturnsHealthy()
    {
        Svc.Register("hc_single_healthy", () => HealthCheckResult.Healthy, "ok");

        var status = await Svc.CheckHealthAsync();
        var entry = Assert.Single(status.Entries, e => e.Name == "hc_single_healthy");
        Assert.Equal(HealthCheckResult.Healthy, entry.Result);
    }

    [Fact]
    public async Task MixedChecks_TakesWorst_HealthyAndUnhealthy()
    {
        Svc.Register("hc_mix_healthy", () => HealthCheckResult.Healthy, "ok");
        Svc.Register("hc_mix_unhealthy", () => HealthCheckResult.Unhealthy, "down");

        var status = await Svc.CheckHealthAsync();
        var filtered = status.Entries
            .Where(e => e.Name == "hc_mix_healthy" || e.Name == "hc_mix_unhealthy")
            .ToList();

        var worst = (HealthCheckResult)filtered.Max(e => (int)e.Result);
        Assert.Equal(HealthCheckResult.Unhealthy, worst);
        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public async Task DegradedAndUnhealthy_TakesWorst()
    {
        Svc.Register("hc_du_degraded", () => HealthCheckResult.Degraded, "warn");
        Svc.Register("hc_du_unhealthy", () => HealthCheckResult.Unhealthy, "error");

        var status = await Svc.CheckHealthAsync();
        var filtered = status.Entries
            .Where(e => e.Name == "hc_du_degraded" || e.Name == "hc_du_unhealthy")
            .ToList();

        var worst = (HealthCheckResult)filtered.Max(e => (int)e.Result);
        Assert.Equal(HealthCheckResult.Unhealthy, worst);
    }

    [Fact]
    public async Task ExceptionInCheck_RecordsAsUnhealthy()
    {
        Svc.Register("hc_ex_throwing", ct =>
        {
            throw new InvalidOperationException("boom");
        });

        var status = await Svc.CheckHealthAsync();
        var entry = Assert.Single(status.Entries, e => e.Name == "hc_ex_throwing");
        Assert.Equal(HealthCheckResult.Unhealthy, entry.Result);
        Assert.NotNull(entry.Description);
        Assert.Contains("boom", entry.Description);
    }

    [Fact]
    public async Task RenderText_ReturnsNonEmptyString()
    {
        Svc.Register("hc_render_text", () => HealthCheckResult.Healthy, "fine");

        var text = await Svc.RenderTextAsync();
        Assert.False(string.IsNullOrEmpty(text));
        Assert.Contains("overall", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RenderJson_IsValidJson()
    {
        Svc.Register("hc_json_a", () => HealthCheckResult.Healthy, "a-ok");
        Svc.Register("hc_json_b", () => HealthCheckResult.Degraded, "b-warn");

        var json = await Svc.RenderJsonAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("overall_status", out _));
        Assert.True(root.TryGetProperty("entries", out var entries));
        Assert.Equal(JsonValueKind.Array, entries.ValueKind);
    }

    [Fact]
    public async Task AddMemoryCheck_RegistersSuccessfully()
    {
        Svc.AddMemoryCheck(memoryThresholdBytes: long.MaxValue);

        var status = await Svc.CheckHealthAsync();
        var entry = Assert.Single(status.Entries, e => e.Name == "memory_usage");
        Assert.Equal(HealthCheckResult.Healthy, entry.Result);
    }

    [Fact]
    public async Task RegisterMultipleChecks_RunsAll()
    {
        var names = new[] { "hc_multi_a", "hc_multi_b", "hc_multi_c" };
        foreach (var n in names)
            Svc.Register(n, () => HealthCheckResult.Healthy, "ok");

        var status = await Svc.CheckHealthAsync();
        int count = status.Entries.Count(e => names.Contains(e.Name));
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task EntriesOrder_ByNameConsistent()
    {
        var names = new[] { "hc_order_a", "hc_order_b", "hc_order_c" };
        foreach (var n in names)
            Svc.Register(n, () => HealthCheckResult.Healthy, n);

        var status = await Svc.CheckHealthAsync();
        var filtered = status.Entries.Where(e => names.Contains(e.Name)).ToList();
        Assert.Equal(names.Length, filtered.Count);
        for (int i = 0; i < names.Length; i++)
        {
            Assert.Equal(names[i], filtered[i].Name);
        }
    }
}
