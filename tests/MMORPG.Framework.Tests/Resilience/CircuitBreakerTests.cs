using Xunit;
using MMORPG.Framework.Resilience;

namespace MMORPG.Framework.Tests.Resilience;

[Collection("Observability")]
public class CircuitBreakerTests
{
    [Fact]
    public void InitialState_IsClosed()
    {
        var cb = new CircuitBreaker("test");
        Assert.Equal(CircuitBreakerState.Closed, cb.State);
        Assert.Equal(0, cb.FailureCount);
    }

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsResult()
    {
        var cb = new CircuitBreaker("test");
        var result = await cb.ExecuteAsync(() => Task.FromResult(42));
        Assert.Equal(42, result);
        Assert.Equal(CircuitBreakerState.Closed, cb.State);
    }

    [Fact]
    public async Task ExecuteAsync_Success_NoReturn_Completes()
    {
        var cb = new CircuitBreaker("test");
        var called = false;
        await cb.ExecuteAsync(() =>
        {
            called = true;
            return Task.CompletedTask;
        });
        Assert.True(called);
    }

    [Fact]
    public async Task ExecuteAsync_Failure_BelowThreshold_StaysClosed()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 5,
            DurationOfBreakMs = 30_000,
            MinimumExecutionTimeMs = 0
        };
        var cb = new CircuitBreaker("test", options);

        for (int i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cb.ExecuteAsync(() => throw new InvalidOperationException("fail")));
        }

        Assert.Equal(CircuitBreakerState.Closed, cb.State);
        Assert.Equal(4, cb.FailureCount);
    }

    [Fact]
    public async Task ExecuteAsync_Failure_ReachesThreshold_OpensCircuit()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            DurationOfBreakMs = 30_000,
            MinimumExecutionTimeMs = 0
        };
        var cb = new CircuitBreaker("test", options);

        for (int i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cb.ExecuteAsync(() => throw new InvalidOperationException("fail")));
        }

        Assert.Equal(CircuitBreakerState.Open, cb.State);
    }

    [Fact]
    public async Task ExecuteAsync_OpenState_ThrowsBrokenCircuitException()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            DurationOfBreakMs = 30_000,
            MinimumExecutionTimeMs = 0
        };
        var cb = new CircuitBreaker("test", options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cb.ExecuteAsync(() => throw new InvalidOperationException("fail")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cb.ExecuteAsync(() => throw new InvalidOperationException("fail")));

        Assert.Equal(CircuitBreakerState.Open, cb.State);

        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            cb.ExecuteAsync(() => Task.FromResult("should not run")));
    }

    [Fact]
    public async Task ExecuteAsync_OpenState_NoReturn_ThrowsBrokenCircuitException()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            DurationOfBreakMs = 30_000,
            MinimumExecutionTimeMs = 0
        };
        var cb = new CircuitBreaker("test", options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cb.ExecuteAsync(() => throw new InvalidOperationException("fail")));

        Assert.Equal(CircuitBreakerState.Open, cb.State);

        var actionCalled = false;
        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            cb.ExecuteAsync(() =>
            {
                actionCalled = true;
                return Task.CompletedTask;
            }));
        Assert.False(actionCalled, "熔断状态下不应该执行 action");
    }

    [Fact]
    public async Task ExecuteAsync_HalfOpen_Success_ClosesCircuit()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            DurationOfBreakMs = 50,
            HalfOpenMaxAttempts = 1,
            SuccessThreshold = 1,
            MinimumExecutionTimeMs = 0
        };
        var cb = new CircuitBreaker("test", options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cb.ExecuteAsync(() => throw new InvalidOperationException("fail")));
        Assert.Equal(CircuitBreakerState.Open, cb.State);

        await Task.Delay(100);
        Assert.Equal(CircuitBreakerState.HalfOpen, cb.State);

        var result = await cb.ExecuteAsync(() => Task.FromResult("recovered"));
        Assert.Equal("recovered", result);
        Assert.Equal(CircuitBreakerState.Closed, cb.State);
    }

    [Fact]
    public async Task ExecuteAsync_HalfOpen_Failure_ReopensCircuit()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            DurationOfBreakMs = 50,
            HalfOpenMaxAttempts = 1,
            MinimumExecutionTimeMs = 0
        };
        var cb = new CircuitBreaker("test", options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cb.ExecuteAsync(() => throw new InvalidOperationException("fail")));
        Assert.Equal(CircuitBreakerState.Open, cb.State);

        await Task.Delay(100);
        Assert.Equal(CircuitBreakerState.HalfOpen, cb.State);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cb.ExecuteAsync(() => throw new InvalidOperationException("still failing")));

        Assert.Equal(CircuitBreakerState.Open, cb.State);
    }

    [Fact]
    public void Reset_FromOpen_ReturnsToClosed()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            DurationOfBreakMs = 30_000,
            MinimumExecutionTimeMs = 0
        };
        var cb = new CircuitBreaker("test", options);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            cb.ExecuteAsync(() => throw new InvalidOperationException("fail"))).Wait();
        Assert.Equal(CircuitBreakerState.Open, cb.State);

        cb.Reset();

        Assert.Equal(CircuitBreakerState.Closed, cb.State);
        Assert.Equal(0, cb.FailureCount);
    }

    [Fact]
    public async Task GetSnapshot_ReturnsCorrectState()
    {
        var cb = new CircuitBreaker("test-snapshot");
        var snapshot = cb.GetSnapshot();

        Assert.Equal("test-snapshot", snapshot.Name);
        Assert.Equal(CircuitBreakerState.Closed, snapshot.State);
        Assert.Equal(0, snapshot.FailureCount);
    }

    [Fact]
    public async Task ExecuteAsync_FastFailure_DoesNotCount()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            DurationOfBreakMs = 30_000,
            MinimumExecutionTimeMs = 1000
        };
        var cb = new CircuitBreaker("test", options);

        for (int i = 0; i < 10; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cb.ExecuteAsync(() => throw new InvalidOperationException("fast fail")));
        }

        Assert.Equal(CircuitBreakerState.Closed, cb.State);
        Assert.Equal(0, cb.FailureCount);
    }

    [Fact]
    public async Task ExecuteAsync_BrokenCircuitException_DoesNotCountAsFailure()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            DurationOfBreakMs = 30_000,
            MinimumExecutionTimeMs = 0
        };
        var cb = new CircuitBreaker("test", options);

        var innerCb = new CircuitBreaker("inner", new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            DurationOfBreakMs = 30_000,
            MinimumExecutionTimeMs = 0
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            innerCb.ExecuteAsync(() => throw new InvalidOperationException("fail")));

        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            cb.ExecuteAsync(() => innerCb.ExecuteAsync(() => Task.FromResult(1))));

        Assert.Equal(CircuitBreakerState.Closed, cb.State);
        Assert.Equal(0, cb.FailureCount);
    }
}
