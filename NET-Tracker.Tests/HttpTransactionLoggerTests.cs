using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NET_Tracker.Configuration;
using NET_Tracker.Data;
using NET_Tracker.Models;
using NET_Tracker.Services;

namespace NET_Tracker.Tests;

/// <summary>
/// Unit tests for HttpTransactionLogger — the core database-backed logger.
/// Uses EF Core InMemory provider so no real SQL Server is needed.
/// </summary>
public class HttpTransactionLoggerTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly HttpTransactionLogger _sut;

    public HttpTransactionLoggerTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new ApplicationDbContext(dbOptions);

        var loggingOptions = Options.Create(new HttpLoggingOptions
        {
            LogRequestBody  = true,
            LogResponseBody = true,
            LogHeaders      = true,
            LogSensitiveData = false,
            MaxBodySize     = 1024 * 1024,
            SensitivePatterns = new() { "password", "token", "authorization" }
        });

        _sut = new HttpTransactionLogger(
            _dbContext,
            NullLogger<HttpTransactionLogger>.Instance,
            loggingOptions);
    }

    // ── GenerateRequestId ────────────────────────────────────────────────────

    [Fact]
    public void GenerateRequestId_ReturnsNonEmptyString()
    {
        var id = _sut.GenerateRequestId();
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public void GenerateRequestId_ReturnsUniqueValues()
    {
        var id1 = _sut.GenerateRequestId();
        var id2 = _sut.GenerateRequestId();
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void GenerateRequestId_ReturnsValidGuid()
    {
        var id = _sut.GenerateRequestId();
        Assert.True(Guid.TryParse(id, out _));
    }

    // ── LogTransactionAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task LogTransactionAsync_SavesTransactionToDatabase()
    {
        var txn = BuildSampleTransaction();
        await _sut.LogTransactionAsync(txn);

        var saved = await _dbContext.HttpTransactions.FirstOrDefaultAsync();
        Assert.NotNull(saved);
        Assert.Equal(txn.RequestId, saved.RequestId);
    }

    [Fact]
    public async Task LogTransactionAsync_NullTransaction_DoesNotThrow()
    {
        // Should silently ignore null (graceful degradation)
        await _sut.LogTransactionAsync(null!);
        Assert.Equal(0, await _dbContext.HttpTransactions.CountAsync());
    }

    [Fact]
    public async Task LogTransactionAsync_MasksSensitiveHeaders()
    {
        var txn = BuildSampleTransaction();
        txn.RequestHeaders = "{\"Authorization\":\"Bearer secret-token\",\"Content-Type\":\"application/json\"}";

        await _sut.LogTransactionAsync(txn);

        var saved = await _dbContext.HttpTransactions.FirstOrDefaultAsync();
        Assert.NotNull(saved);
        Assert.Contains("***MASKED***", saved!.RequestHeaders);
        Assert.DoesNotContain("secret-token", saved.RequestHeaders);
    }

    [Fact]
    public async Task LogTransactionAsync_MasksPasswordInBody()
    {
        var txn = BuildSampleTransaction();
        txn.ContentType  = "application/json";
        txn.RequestBody  = "{\"username\":\"alice\",\"password\":\"super-secret\"}";

        await _sut.LogTransactionAsync(txn);

        var saved = await _dbContext.HttpTransactions.FirstOrDefaultAsync();
        Assert.NotNull(saved);
        Assert.Contains("***MASKED***", saved!.RequestBody);
        Assert.DoesNotContain("super-secret", saved.RequestBody);
    }

    [Fact]
    public async Task LogTransactionAsync_TruncatesOversizedBody()
    {
        // Set MaxBodySize to 10 bytes so we can test truncation easily
        var opts = Options.Create(new HttpLoggingOptions { MaxBodySize = 10 });
        var sut  = new HttpTransactionLogger(_dbContext, NullLogger<HttpTransactionLogger>.Instance, opts);

        var txn = BuildSampleTransaction();
        txn.RequestBody = "AAAAAAAAAAAAAAAAAAAAAA"; // 22 chars > 10

        await sut.LogTransactionAsync(txn);

        var saved = await _dbContext.HttpTransactions.FirstOrDefaultAsync();
        Assert.NotNull(saved);
        Assert.Contains("[TRUNCATED]", saved!.RequestBody);
    }

    // ── GetTransactionAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetTransactionAsync_ReturnsCorrectTransaction()
    {
        var txn = BuildSampleTransaction();
        await _sut.LogTransactionAsync(txn);

        var result = await _sut.GetTransactionAsync(txn.RequestId);
        Assert.NotNull(result);
        Assert.Equal(txn.RequestId, result!.RequestId);
    }

    [Fact]
    public async Task GetTransactionAsync_UnknownId_ReturnsNull()
    {
        var result = await _sut.GetTransactionAsync("does-not-exist");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTransactionAsync_EmptyId_ReturnsNull()
    {
        var result = await _sut.GetTransactionAsync("");
        Assert.Null(result);
    }

    // ── SearchAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_NoFilter_ReturnsAll()
    {
        await _sut.LogTransactionAsync(BuildSampleTransaction(method: "GET"));
        await _sut.LogTransactionAsync(BuildSampleTransaction(method: "POST"));

        var results = await _sut.SearchAsync(new HttpTransactionFilter { PageSize = 100 });
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_ByMethod_FiltersCorrectly()
    {
        await _sut.LogTransactionAsync(BuildSampleTransaction(method: "GET"));
        await _sut.LogTransactionAsync(BuildSampleTransaction(method: "POST"));

        var results = await _sut.SearchAsync(new HttpTransactionFilter { Method = "POST" });
        Assert.All(results, r => Assert.Equal("POST", r.Method));
    }

    [Fact]
    public async Task SearchAsync_BySuccess_FiltersFailures()
    {
        await _sut.LogTransactionAsync(BuildSampleTransaction(statusCode: 200, success: true));
        await _sut.LogTransactionAsync(BuildSampleTransaction(statusCode: 500, success: false));

        var failures = await _sut.SearchAsync(new HttpTransactionFilter { Success = false });
        Assert.All(failures, r => Assert.False(r.Success));
    }

    [Fact]
    public async Task SearchAsync_Pagination_RespectsPageSize()
    {
        for (int i = 0; i < 10; i++)
            await _sut.LogTransactionAsync(BuildSampleTransaction());

        var page1 = await _sut.SearchAsync(new HttpTransactionFilter { PageNumber = 1, PageSize = 3 });
        Assert.Equal(3, page1.Count);
    }

    // ── GetStatisticsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetStatisticsAsync_EmptyDb_ReturnsZeroStats()
    {
        var stats = await _sut.GetStatisticsAsync();
        Assert.Equal(0, stats.TotalRequests);
        Assert.Equal(0, stats.SuccessfulRequests);
    }

    [Fact]
    public async Task GetStatisticsAsync_CalculatesSuccessRate()
    {
        await _sut.LogTransactionAsync(BuildSampleTransaction(success: true));
        await _sut.LogTransactionAsync(BuildSampleTransaction(success: true));
        await _sut.LogTransactionAsync(BuildSampleTransaction(success: false));

        var stats = await _sut.GetStatisticsAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        Assert.Equal(3,  stats.TotalRequests);
        Assert.Equal(2,  stats.SuccessfulRequests);
        Assert.Equal(1,  stats.FailedRequests);
        Assert.Equal(Math.Round((decimal)2 / 3 * 100, 10),
                     Math.Round(stats.SuccessRate, 10));
    }

    // ── DeleteOldLogsAsync ───────────────────────────────────────────────────

    [Fact(Skip = "ExecuteDeleteAsync is not supported by EF Core InMemory provider")]
    public async Task DeleteOldLogsAsync_RemovesOldEntries()
    {
        // Add an old transaction (31 days ago)
        var old = BuildSampleTransaction();
        old.Timestamp = DateTime.UtcNow.AddDays(-31);
        _dbContext.HttpTransactions.Add(old);

        // Add a recent transaction
        await _sut.LogTransactionAsync(BuildSampleTransaction());

        await _dbContext.SaveChangesAsync();

        var deleted = await _sut.DeleteOldLogsAsync(30);
        Assert.Equal(1, deleted);
        Assert.Equal(1, await _dbContext.HttpTransactions.CountAsync());
    }

    // ── IsHealthyAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task IsHealthyAsync_WithValidDb_ReturnsTrue()
    {
        var healthy = await _sut.IsHealthyAsync();
        Assert.True(healthy);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HttpTransaction BuildSampleTransaction(
        string method     = "GET",
        int    statusCode = 200,
        bool   success    = true)
        => new()
        {
            Id         = Guid.NewGuid(),
            RequestId  = Guid.NewGuid().ToString(),
            Method     = method,
            Url        = $"https://localhost/api/test",
            StatusCode = statusCode,
            Success    = success,
            DurationMs = 42,
            Timestamp  = DateTime.UtcNow,
            RequestBody  = "{\"key\":\"value\"}",
            ResponseBody = "{\"result\":\"ok\"}",
            ContentType  = "application/json",
            IpAddress    = "127.0.0.1",
            UserAgent    = "TestAgent/1.0",
            QueryString  = "",
            RequestHeaders  = "{}",
            ResponseHeaders = "{}",
            UserId = "",
            ErrorMessage = "",
            StackTrace = ""
        };

    public void Dispose() => _dbContext.Dispose();
}
