using Xunit;
using MMORPG.Framework.Resilience;

namespace MMORPG.Framework.Tests.Resilience;

[Collection("Observability")]
public class TimeoutPolicyTests
{
    [Fact]
    public async Task ExecuteAsync_SucceedsWithinTimeout_ReturnsResult()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 5000 });

        var result = await policy.ExecuteAsync(ct => Task.FromResult("success"));

        Assert.Equal("success", result);
    }

    [Fact]
    public async Task ExecuteAsync_SucceedsWithinTimeout_NoReturn_Completes()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 5000 });
        var called = false;

        await policy.ExecuteAsync(async ct =>
        {
            called = true;
            await Task.Delay(10, ct);
        });

        Assert.True(called);
    }

    [Fact]
    public async Task ExecuteAsync_TimesOut_ThrowsTimeoutRejectedException()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 50 });

        var exception = await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            policy.ExecuteAsync(async ct =>
            {
                await Task.Delay(5000, ct);
                return "should not return";
            }));

        Assert.Contains("超时", exception.Message);
        Assert.Contains("50ms", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_TimesOut_NoReturn_ThrowsTimeoutRejectedException()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 50 });

        await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            policy.ExecuteAsync(async ct =>
            {
                await Task.Delay(5000, ct);
            }));
    }

    [Fact]
    public async Task ExecuteAsync_OnTimeoutCallback_CalledOnTimeout()
    {
        var callbackCalled = false;
        var callbackMessage = "";
        var policy = new TimeoutPolicy("test", new TimeoutOptions
        {
            TimeoutMs = 50,
            OnTimeout = msg =>
            {
                callbackCalled = true;
                callbackMessage = msg;
            }
        });

        await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            policy.ExecuteAsync(async ct =>
            {
                await Task.Delay(5000, ct);
                return "fail";
            }));

        Assert.True(callbackCalled);
        Assert.Contains("超时", callbackMessage);
    }

    [Fact]
    public async Task ExecuteAsync_BusinessException_ThrowsOriginalException()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 5000 });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(ct =>
                throw new InvalidOperationException("business error")));

        Assert.Equal("business error", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_BusinessException_NoReturn_ThrowsOriginalException()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 5000 });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(ct =>
                throw new InvalidOperationException("business error")));
    }

    [Fact]
    public async Task ExecuteAsync_ExternalCancellation_ThrowsOperationCanceledException()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 5000 });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync(async ct =>
            {
                await Task.Delay(5000, ct);
                return "should not run";
            }, cts.Token));

        Assert.NotNull(exception);
    }

    [Fact]
    public async Task ExecuteAsync_SimplifiedOverload_Succeeds()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 5000 });

        var result = await policy.ExecuteAsync(() => Task.FromResult("simplified"));

        Assert.Equal("simplified", result);
    }

    [Fact]
    public async Task ExecuteAsync_SimplifiedOverload_BusinessException_ThrowsOriginal()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 5000 });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(() =>
                throw new InvalidOperationException("simplified error")));

        Assert.Equal("simplified error", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_SimplifiedOverload_NoReturn_Succeeds()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 5000 });
        var called = false;

        await policy.ExecuteAsync(() =>
        {
            called = true;
            return Task.CompletedTask;
        });

        Assert.True(called);
    }

    [Fact]
    public async Task ExecuteAsync_SimplifiedOverload_NoReturn_BusinessException_ThrowsOriginal()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 5000 });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(() =>
                throw new InvalidOperationException("simplified no-return error")));
    }

    [Fact]
    public async Task ExecuteAsync_PassesCancellationTokenToAction()
    {
        var policy = new TimeoutPolicy("test", new TimeoutOptions { TimeoutMs = 50 });
        CancellationToken receivedToken = default;

        await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            policy.ExecuteAsync(async ct =>
            {
                receivedToken = ct;
                await Task.Delay(5000, ct);
                return "fail";
            }));

        Assert.True(receivedToken.CanBeCanceled);
    }
}
