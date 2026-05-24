using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NET_Tracker.Configuration;
using NET_Tracker.Data;
using NET_Tracker.Models;
using NET_Tracker.Services;
using Xunit;

namespace NET_Tracker.Tests;

public class MaskingTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly HttpTransactionLogger _sut;

    public MaskingTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"MaskingTestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new ApplicationDbContext(dbOptions);

        var loggingOptions = Options.Create(new HttpLoggingOptions
        {
            LogSensitiveData = false,
            SensitivePatterns = new() { "password", "token", "secret" }
        });

        _sut = new HttpTransactionLogger(
            _dbContext,
            NullLogger<HttpTransactionLogger>.Instance,
            loggingOptions);
    }

    [Fact]
    public async Task LogTransactionAsync_MasksNestedPasswordInBody()
    {
        var txn = new HttpTransaction
        {
            RequestId = "test-nested",
            ContentType = "application/json",
            RequestBody = "{\"user\":{\"username\":\"alice\",\"password\":\"super-secret\"}}",
            Timestamp = DateTime.UtcNow
        };

        await _sut.LogTransactionAsync(txn);

        var saved = await _dbContext.HttpTransactions.FirstOrDefaultAsync(x => x.RequestId == "test-nested");
        Assert.NotNull(saved);
        Assert.Contains("***MASKED***", saved!.RequestBody);
        Assert.DoesNotContain("super-secret", saved.RequestBody);
    }

    [Fact]
    public async Task LogTransactionAsync_MasksMultipleSensitiveFields()
    {
        var txn = new HttpTransaction
        {
            RequestId = "test-multiple",
            ContentType = "application/json",
            RequestBody = "{\"auth\":{\"token\":\"t123\"},\"credentials\":{\"password\":\"p123\"}}",
            Timestamp = DateTime.UtcNow
        };

        await _sut.LogTransactionAsync(txn);

        var saved = await _dbContext.HttpTransactions.FirstOrDefaultAsync(x => x.RequestId == "test-multiple");
        Assert.NotNull(saved);
        Assert.Contains("\"token\":\"***MASKED***\"", saved!.RequestBody);
        Assert.Contains("\"password\":\"***MASKED***\"", saved.RequestBody);
    }

    public void Dispose() => _dbContext.Dispose();
}
