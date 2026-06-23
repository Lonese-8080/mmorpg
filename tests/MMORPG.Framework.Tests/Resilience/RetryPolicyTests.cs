using Xunit;
using MMORPG.Framework.Resilience;

namespace MMORPG.Framework.Tests.Resilience;

[Collection("Observability")]
public class RetryPolicyTests
{
    [Fact]
    public async Task ExecuteAsync_SuccessOnFirstTry_ReturnsResult()
    {
        var policy = new RetryPolicy("test");
        var callCount = 0;

        var result = await policy.ExecuteAsync(() =>
        {
            callCount++;
            return Task.FromResult("success");
        });

        Assert.Equal("success", result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessOnFirstTry_NoReturn_Completes()
    {
        var policy = new RetryPolicy("test");
        var callCount = 0;

        await policy.ExecuteAsync(() =>
        {
            callCount++;
            return Task.CompletedTask;
        });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_FailsThenSucceeds_RetriesAndReturns()
    {
        var options = new RetryOptions
        {
            MaxRetryCount = 3,
            BaseDelayMs = 10,
            UseJitter = false
        };
        var policy = new RetryPolicy("test", options);
        var callCount = 0;

        var result = await policy.ExecuteAsync(() =>
        {
            callCount++;
            if (callCount < 3)
                throw new InvalidOperationException("fail");
            return Task.FromResult("recovered");
        });

        Assert.Equal("recovered", result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_AlwaysFails_ThrowsLastException()
    {
        var options = new RetryOptions
        {
            MaxRetryCount = 2,
            BaseDelayMs = 10,
            UseJitter = false
        };
        var policy = new RetryPolicy("test", options);
        var callCount = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(() =>
            {
                callCount++;
                throw new InvalidOperationException($"fail-{callCount}");
            }));

        Assert.Equal("fail-3", exception.Message);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRetryReturnsFalse_DoesNotRetry()
    {
        var options = new RetryOptions
        {
            MaxRetryCount = 5,
            BaseDelayMs = 10,
            UseJitter = false,
            ShouldRetry = ex => ex is TimeoutException
        };
        var policy = new RetryPolicy("test", options);
        var callCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(() =>
            {
                callCount++;
                throw new InvalidOperationException("not retryable");
            }));

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRetryReturnsTrue_Retries()
    {
        var options = new RetryOptions
        {
            MaxRetryCount = 2,
            BaseDelayMs = 10,
            UseJitter = false,
            ShouldRetry = ex => ex is TimeoutException
        };
        var policy = new RetryPolicy("test", options);
        var callCount = 0;

        await Assert.ThrowsAsync<TimeoutException>(() =>
            policy.ExecuteAsync(() =>
            {
                callCount++;
                throw new TimeoutException("timeout");
            }));

        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_OnRetryCallback_CalledOnRetry()
    {
        var retryAttempts = new List<int>();
        var retryDelays = new List<int>();
        var options = new RetryOptions
        {
            MaxRetryCount = 2,
            BaseDelayMs = 10,
            UseJitter = false,
            OnRetry = (attempt, delay, ex) =>
            {
                retryAttempts.Add(attempt);
                retryDelays.Add(delay);
            }
        };
        var policy = new RetryPolicy("test", options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(() => throw new InvalidOperationException("fail")));

        Assert.Equal(2, retryAttempts.Count);
        Assert.Equal(1, retryAttempts[0]);
        Assert.Equal(2, retryAttempts[1]);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationToken_CancelsImmediately()
    {
        var policy = new RetryPolicy("test");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var callCount = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync(() =>
            {
                callCount++;
                return Task.FromResult("should not run");
            }, cts.Token));

        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_BrokenCircuitException_DoesNotRetry()
    {
        var options = new RetryOptions
        {
            MaxRetryCount = 5,
            BaseDelayMs = 10,
            UseJitter = false
        };
        var policy = new RetryPolicy("test", options);
        var callCount = 0;

        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            policy.ExecuteAsync(() =>
            {
                callCount++;
                throw new BrokenCircuitException("circuit open");
            }));

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_ExponentialBackoff_IncreasesDelay()
    {
        var delays = new List<int>();
        var options = new RetryOptions
        {
            MaxRetryCount = 3,
            BaseDelayMs = 20,
            ExponentialBase = 2,
            MaxDelayMs = 1000,
            UseJitter = false,
            OnRetry = (attempt, delay, ex) => delays.Add(delay)
        };
        var policy = new RetryPolicy("test", options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(() => throw new InvalidOperationException("fail")));

        Assert.Equal(3, delays.Count);
        Assert.Equal(20, delays[0]);
        Assert.Equal(40, delays[1]);
        Assert.Equal(80, delays[2]);
    }

    [Fact]
    public async Task ExecuteAsync_MaxDelay_CapsDelay()
    {
        var delays = new List<int>();
        var options = new RetryOptions
        {
            MaxRetryCount = 5,
            BaseDelayMs = 100,
            ExponentialBase = 2,
            MaxDelayMs = 250,
            UseJitter = false,
            OnRetry = (attempt, delay, ex) => delays.Add(delay)
        };
        var policy = new RetryPolicy("test", options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(() => throw new InvalidOperationException("fail")));

        Assert.True(delays.All(d => d <= 250), $"所有延迟应该 <= 250ms，实际: {string.Join(", ", delays)}");
    }

    [Fact]
    public async Task ExecuteAsync_WithJitter_DelayIsWithinRange()
    {
        var delays = new List<int>();
        var options = new RetryOptions
        {
            MaxRetryCount = 10,
            BaseDelayMs = 100,
            ExponentialBase = 1,
            MaxDelayMs = 1000,
            UseJitter = true,
            JitterPercentage = 0.3,
            OnRetry = (attempt, delay, ex) => delays.Add(delay)
        };
        var policy = new RetryPolicy("test", options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(() => throw new InvalidOperationException("fail")));

        Assert.All(delays, d =>
        {
            Assert.True(d >= 1, "延迟至少为 1ms");
            Assert.True(d <= 130, $"带抖动的延迟应该在 70-130ms 范围内，实际: {d}");
        });
    }
}
