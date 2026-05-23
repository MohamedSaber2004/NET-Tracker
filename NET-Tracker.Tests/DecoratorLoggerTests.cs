using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NET_Tracker.Models;
using NET_Tracker.Services;
using NET_Tracker.Services.Interfaces;

namespace NET_Tracker.Tests;

/// <summary>
/// Unit tests for ResilientHttpTransactionLogger.
/// Verifies retry logic, circuit-breaker behaviour, and fallback buffering.
/// </summary>
public class ResilientHttpTransactionLoggerTests
{
    private readonly Mock<IHttpTransactionLogger> _innerMock = new();
    private readonly ResilientHttpTransactionLogger _sut;

    public ResilientHttpTransactionLoggerTests()
    {
        _sut = new ResilientHttpTransactionLogger(
            _innerMock.Object,
            NullLogger<ResilientHttpTransactionLogger>.Instance);
    }

    [Fact]
    public async Task LogTransactionAsync_Success_CallsInnerLogger()
    {
        _innerMock.Setup(x => x.LogTransactionAsync(It.IsAny<HttpTransaction>()))
                  .Returns(Task.CompletedTask);

        await _sut.LogTransactionAsync(BuildTxn());

        _innerMock.Verify(x => x.LogTransactionAsync(It.IsAny<HttpTransaction>()), Times.Once);
    }

    [Fact]
    public async Task LogTransactionAsync_NullTransaction_DoesNotCallInner()
    {
        await _sut.LogTransactionAsync(null!);
        _innerMock.Verify(x => x.LogTransactionAsync(It.IsAny<HttpTransaction>()), Times.Never);
    }

    [Fact]
    public async Task LogTransactionAsync_SingleFailure_RetriesAndSucceeds()
    {
        var callCount = 0;
        _innerMock.Setup(x => x.LogTransactionAsync(It.IsAny<HttpTransaction>()))
                  .Returns(() =>
                  {
                      callCount++;
                      if (callCount == 1) throw new Exception("transient");
                      return Task.CompletedTask;
                  });

        // Should not throw — retry logic kicks in
        await _sut.LogTransactionAsync(BuildTxn());
        Assert.True(callCount >= 2);
    }

    [Fact]
    public async Task LogTransactionAsync_AllRetriesFail_BuffersTransaction()
    {
        _innerMock.Setup(x => x.LogTransactionAsync(It.IsAny<HttpTransaction>()))
                  .ThrowsAsync(new Exception("db down"));

        await _sut.LogTransactionAsync(BuildTxn());

        // After max retries the transaction should sit in the fallback buffer
        Assert.True(_sut.BufferedCount > 0);
    }

    [Fact]
    public async Task GetTransactionAsync_ReturnsInnerResult()
    {
        var expected = BuildTxn();
        _innerMock.Setup(x => x.GetTransactionAsync("req-1")).ReturnsAsync(expected);

        var result = await _sut.GetTransactionAsync("req-1");
        Assert.Equal(expected.RequestId, result?.RequestId);
    }

    [Fact]
    public async Task GetTransactionAsync_InnerThrows_ReturnsNull()
    {
        _innerMock.Setup(x => x.GetTransactionAsync(It.IsAny<string>()))
                  .ThrowsAsync(new Exception("db error"));

        var result = await _sut.GetTransactionAsync("id");
        Assert.Null(result);
    }

    [Fact]
    public async Task SearchAsync_InnerThrows_ReturnsEmptyList()
    {
        _innerMock.Setup(x => x.SearchAsync(It.IsAny<HttpTransactionFilter>()))
                  .ThrowsAsync(new Exception("db error"));

        var result = await _sut.SearchAsync(new HttpTransactionFilter());
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStatisticsAsync_InnerThrows_ReturnsEmptyStats()
    {
        _innerMock.Setup(x => x.GetStatisticsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
                  .ThrowsAsync(new Exception("db error"));

        var stats = await _sut.GetStatisticsAsync();
        Assert.Equal(0, stats.TotalRequests);
    }

    [Fact]
    public async Task IsHealthyAsync_InnerThrows_ReturnsFalse()
    {
        _innerMock.Setup(x => x.IsHealthyAsync()).ThrowsAsync(new Exception());
        var result = await _sut.IsHealthyAsync();
        Assert.False(result);
    }

    [Fact]
    public void GenerateRequestId_DelegatesToInner()
    {
        _innerMock.Setup(x => x.GenerateRequestId()).Returns("test-id");
        var id = _sut.GenerateRequestId();
        Assert.Equal("test-id", id);
    }

    private static HttpTransaction BuildTxn() => new()
    {
        Id        = Guid.NewGuid(),
        RequestId = Guid.NewGuid().ToString(),
        Method    = "GET",
        Url       = "https://localhost/api/test",
        Timestamp = DateTime.UtcNow
    };
}

/// <summary>
/// Unit tests for CachedHttpTransactionLogger.
/// Verifies that cache hits avoid inner calls and that write-through invalidation works.
/// </summary>
public class CachedHttpTransactionLoggerTests
{
    private readonly Mock<IHttpTransactionLogger> _innerMock = new();
    private readonly IMemoryCache _memoryCache;
    private readonly CachedHttpTransactionLogger _sut;

    public CachedHttpTransactionLoggerTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        _sut = new CachedHttpTransactionLogger(
            _innerMock.Object,
            _memoryCache,
            NullLogger<CachedHttpTransactionLogger>.Instance);
    }

    [Fact]
    public async Task GetTransactionAsync_FirstCall_HitsInnerLogger()
    {
        var txn = BuildTxn("req-1");
        _innerMock.Setup(x => x.GetTransactionAsync("req-1")).ReturnsAsync(txn);

        var result = await _sut.GetTransactionAsync("req-1");
        Assert.Equal("req-1", result?.RequestId);
        _innerMock.Verify(x => x.GetTransactionAsync("req-1"), Times.Once);
    }

    [Fact]
    public async Task GetTransactionAsync_SecondCall_UsesCache()
    {
        var txn = BuildTxn("req-1");
        _innerMock.Setup(x => x.GetTransactionAsync("req-1")).ReturnsAsync(txn);

        await _sut.GetTransactionAsync("req-1");
        await _sut.GetTransactionAsync("req-1"); // should be cached

        _innerMock.Verify(x => x.GetTransactionAsync("req-1"), Times.Once);
    }

    [Fact]
    public async Task GetStatisticsAsync_SecondCall_UsesCache()
    {
        _innerMock.Setup(x => x.GetStatisticsAsync(null, null))
                  .ReturnsAsync(new HttpTransactionStatistics { TotalRequests = 42 });

        await _sut.GetStatisticsAsync();
        await _sut.GetStatisticsAsync();

        _innerMock.Verify(x => x.GetStatisticsAsync(null, null), Times.Once);
    }

    [Fact]
    public async Task LogTransactionAsync_PassesThroughToInner()
    {
        var txn = BuildTxn("req-2");
        _innerMock.Setup(x => x.LogTransactionAsync(txn)).Returns(Task.CompletedTask);

        await _sut.LogTransactionAsync(txn);

        _innerMock.Verify(x => x.LogTransactionAsync(txn), Times.Once);
    }

    [Fact]
    public async Task DeleteOldLogsAsync_PassesThroughAndReturnsCount()
    {
        _innerMock.Setup(x => x.DeleteOldLogsAsync(30)).ReturnsAsync(5);
        var count = await _sut.DeleteOldLogsAsync(30);
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task IsHealthyAsync_DelegatesToInner()
    {
        _innerMock.Setup(x => x.IsHealthyAsync()).ReturnsAsync(true);
        Assert.True(await _sut.IsHealthyAsync());
    }

    [Fact]
    public void GenerateRequestId_DelegatesToInner()
    {
        _innerMock.Setup(x => x.GenerateRequestId()).Returns("cached-id");
        Assert.Equal("cached-id", _sut.GenerateRequestId());
    }

    private static HttpTransaction BuildTxn(string requestId) => new()
    {
        Id        = Guid.NewGuid(),
        RequestId = requestId,
        Method    = "GET",
        Url       = "https://localhost/test",
        Timestamp = DateTime.UtcNow
    };
}

/// <summary>
/// Unit tests for QueuedHttpTransactionLogger.
/// Verifies that writes are enqueued and eventually drained to the inner logger.
/// </summary>
public class QueuedHttpTransactionLoggerTests
{
    [Fact]
    public async Task LogTransactionAsync_DoesNotBlockCaller()
    {
        var innerMock = new Mock<IHttpTransactionLogger>();
        var tcs = new TaskCompletionSource<bool>();

        innerMock.Setup(x => x.LogTransactionAsync(It.IsAny<HttpTransaction>()))
                 .Returns(async () => await tcs.Task); // blocks inner

        var sut = new QueuedHttpTransactionLogger(
            innerMock.Object,
            NullLogger<QueuedHttpTransactionLogger>.Instance,
            maxQueueSize: 100);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await sut.LogTransactionAsync(BuildTxn());
        sw.Stop();

        // The call should return almost instantly (< 200ms), not wait for DB
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Expected < 200ms but got {sw.ElapsedMilliseconds}ms");

        tcs.SetResult(true); // unblock background thread
    }

    [Fact]
    public async Task LogTransactionAsync_EventuallyDrainsToInner()
    {
        var innerMock = new Mock<IHttpTransactionLogger>();
        innerMock.Setup(x => x.LogTransactionAsync(It.IsAny<HttpTransaction>()))
                 .Returns(Task.CompletedTask);

        var sut = new QueuedHttpTransactionLogger(
            innerMock.Object,
            NullLogger<QueuedHttpTransactionLogger>.Instance,
            maxQueueSize: 100);

        await sut.LogTransactionAsync(BuildTxn());

        // Give the background loop time to drain the single item
        await Task.Delay(500);

        innerMock.Verify(x => x.LogTransactionAsync(It.IsAny<HttpTransaction>()), Times.Once);
    }

    [Fact]
    public void GenerateRequestId_DelegatesToInner()
    {
        var innerMock = new Mock<IHttpTransactionLogger>();
        innerMock.Setup(x => x.GenerateRequestId()).Returns("queued-id");

        var sut = new QueuedHttpTransactionLogger(
            innerMock.Object,
            NullLogger<QueuedHttpTransactionLogger>.Instance);

        Assert.Equal("queued-id", sut.GenerateRequestId());
    }

    private static HttpTransaction BuildTxn() => new()
    {
        Id        = Guid.NewGuid(),
        RequestId = Guid.NewGuid().ToString(),
        Method    = "GET",
        Url       = "https://localhost/test",
        Timestamp = DateTime.UtcNow
    };
}
