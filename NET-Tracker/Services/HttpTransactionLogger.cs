using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NET_Tracker.Configuration;
using NET_Tracker.Data;
using NET_Tracker.Models;
using NET_Tracker.Services.Interfaces;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NET_Tracker.Services
{
    /// <summary>
    /// Implementation of HTTP transaction logger using Entity Framework Core and SQL Server.
    /// Stores complete HTTP request/response data for debugging and monitoring.
    /// </summary>
    public class HttpTransactionLogger : IHttpTransactionLogger
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<HttpTransactionLogger> _logger;
        private readonly HttpLoggingOptions _options;

        public HttpTransactionLogger(
            ApplicationDbContext dbContext,
            ILogger<HttpTransactionLogger> logger,
            IOptions<HttpLoggingOptions> options)
        {
            _dbContext = dbContext;
            _logger = logger;
            _options = options.Value;
        }

        /// <summary>
        /// Generates a unique request ID for correlation across the request lifecycle.
        /// </summary>
        public string GenerateRequestId()
        {
            return Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Logs a complete HTTP transaction to the database.
        /// This method is called by the middleware after the response is prepared.
        /// </summary>
        public async Task LogTransactionAsync(HttpTransaction transaction)
        {
            if (transaction == null)
            {
                _logger.LogWarning("Attempt to log null transaction");
                return;
            }

            try
            {
                // Truncate bodies if larger than MaxBodySize
                transaction = TruncateBodies(transaction);

                // Mask sensitive data if needed
                if (!_options.LogSensitiveData)
                {
                    transaction = MaskSensitiveData(transaction);
                }

                // Drop bodies for successful requests if configured to save space
                if (_options.LogBodyOnlyOnErrors && transaction.Success)
                {
                    transaction.RequestBody = null;
                    transaction.ResponseBody = null;
                }

                // Add to database context
                _dbContext.HttpTransactions.Add(transaction);

                // Save to database
                await _dbContext.SaveChangesAsync();

                _logger.LogDebug("Logged HTTP transaction {RequestId} - {Method} {Url} -> {StatusCode} ({DurationMs}ms)",
                    transaction.RequestId, transaction.Method, transaction.Url, transaction.StatusCode, transaction.DurationMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log HTTP transaction {RequestId}", transaction.RequestId);
                // Don't throw - logging failure shouldn't break the application
            }
        }

        /// <summary>
        /// Retrieves a specific HTTP transaction by its request ID.
        /// </summary>
        public async Task<HttpTransaction> GetTransactionAsync(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return null;

            try
            {
                var transaction = await _dbContext.HttpTransactions
                    .FirstOrDefaultAsync(x => x.RequestId == requestId);

                return transaction;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve transaction {RequestId}", requestId);
                return null;
            }
        }

        /// <summary>
        /// Searches HTTP transactions using flexible filtering criteria.
        /// </summary>
        public async Task<(List<HttpTransaction> Data, int TotalCount)> SearchAsync(HttpTransactionFilter filter)
        {
            if (filter == null)
                filter = new HttpTransactionFilter();

            // Validate filter
            if (!filter.IsValid(out var errors))
            {
                _logger.LogWarning("Invalid search filter: {Errors}", string.Join(", ", errors));
                return (new List<HttpTransaction>(), 0);
            }

            try
            {
                var query = _dbContext.HttpTransactions.AsQueryable();

                // Apply filters
                if (!string.IsNullOrWhiteSpace(filter.RequestId))
                    query = query.Where(x => x.RequestId == filter.RequestId);

                if (!string.IsNullOrWhiteSpace(filter.Method))
                    query = query.Where(x => x.Method == filter.Method);

                if (!string.IsNullOrWhiteSpace(filter.Url))
                    query = query.Where(x => x.Url.Contains(filter.Url));

                if (filter.StatusCode.HasValue)
                    query = query.Where(x => x.StatusCode == filter.StatusCode);
                else
                {
                    if (filter.StatusCodeMin.HasValue)
                        query = query.Where(x => x.StatusCode >= filter.StatusCodeMin);

                    if (filter.StatusCodeMax.HasValue)
                        query = query.Where(x => x.StatusCode <= filter.StatusCodeMax);
                }

                if (filter.DurationMinMs.HasValue)
                    query = query.Where(x => x.DurationMs >= filter.DurationMinMs);

                if (filter.DurationMaxMs.HasValue)
                    query = query.Where(x => x.DurationMs <= filter.DurationMaxMs);

                if (filter.Success.HasValue)
                    query = query.Where(x => x.Success == filter.Success);

                if (!string.IsNullOrWhiteSpace(filter.UserId))
                    query = query.Where(x => x.UserId == filter.UserId);

                if (!string.IsNullOrWhiteSpace(filter.IpAddress))
                    query = query.Where(x => x.IpAddress == filter.IpAddress);

                if (filter.StartDate.HasValue)
                    query = query.Where(x => x.Timestamp >= filter.StartDate);

                if (filter.EndDate.HasValue)
                    query = query.Where(x => x.Timestamp <= filter.EndDate);

                if (!string.IsNullOrWhiteSpace(filter.SearchText))
                    query = query.Where(x =>
                        (x.Url != null && x.Url.Contains(filter.SearchText)) ||
                        (x.RequestId != null && x.RequestId.Contains(filter.SearchText)) ||
                        (x.IpAddress != null && x.IpAddress.Contains(filter.SearchText)));

                // Apply sorting
                query = ApplySorting(query, filter.SortBy);

                // Get total count BEFORE pagination
                var totalCount = await query.CountAsync();

                // Apply pagination
                var skip = (filter.PageNumber - 1) * filter.PageSize;
                var results = await query
                    .Skip(skip)
                    .Take(filter.PageSize)
                    .ToListAsync();

                return (results, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search transactions");
                return (new List<HttpTransaction>(), 0);
            }
        }

        /// <summary>
        /// Gets aggregated statistics about HTTP transactions.
        /// </summary>
        public async Task<HttpTransactionStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.UtcNow.AddDays(-7);
                endDate ??= DateTime.UtcNow;

                var transactions = await _dbContext.HttpTransactions
                    .Where(x => x.Timestamp >= startDate && x.Timestamp <= endDate)
                    .ToListAsync();

                if (transactions.Count == 0)
                {
                    return new HttpTransactionStatistics
                    {
                        StartDate = startDate.Value,
                        EndDate = endDate.Value
                    };
                }

                var stats = new HttpTransactionStatistics
                {
                    StartDate = startDate.Value,
                    EndDate = endDate.Value,
                    TotalRequests = transactions.Count,
                    SuccessfulRequests = transactions.Count(x => x.Success),
                    FailedRequests = transactions.Count(x => !x.Success),
                    SuccessRate = (decimal)transactions.Count(x => x.Success) / transactions.Count * 100,
                    AverageDurationMs = transactions.Average(x => x.DurationMs),
                    MinDurationMs = transactions.Min(x => x.DurationMs),
                    MaxDurationMs = transactions.Max(x => x.DurationMs),
                    MedianDurationMs = CalculatePercentile(transactions.Select(x => x.DurationMs).ToList(), 50),
                    P95DurationMs = CalculatePercentile(transactions.Select(x => x.DurationMs).ToList(), 95),
                    P99DurationMs = CalculatePercentile(transactions.Select(x => x.DurationMs).ToList(), 99),
                    TotalRequestBytes = transactions.Sum(x => x.RequestSize),
                    TotalResponseBytes = transactions.Sum(x => x.ResponseSize),
                    AverageRequestBytes = (long)transactions.Average(x => x.RequestSize),
                    AverageResponseBytes = (long)transactions.Average(x => x.ResponseSize),
                };

                // Group by method
                stats.RequestsByMethod = transactions
                    .GroupBy(x => x.Method ?? "UNKNOWN")
                    .ToDictionary(g => g.Key, g => g.Count());

                // Group by status code
                stats.ResponsesByStatusCode = transactions
                    .GroupBy(x => x.StatusCode)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Group by endpoint
                stats.RequestsByEndpoint = transactions
                    .GroupBy(x => x.Url ?? "unknown")
                    .ToDictionary(g => g.Key, g => g.Count());

                // Group by content type
                stats.RequestsByContentType = transactions
                    .Where(x => !string.IsNullOrEmpty(x.ContentType))
                    .GroupBy(x => x.ContentType!)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Top slowest requests
                stats.SlowestRequests = transactions
                    .OrderByDescending(x => x.DurationMs)
                    .Take(10)
                    .Select(x => new HttpTransactionSummary
                    {
                        RequestId = x.RequestId,
                        Method = x.Method,
                        Url = x.Url,
                        StatusCode = x.StatusCode,
                        DurationMs = x.DurationMs,
                        Timestamp = x.Timestamp,
                        ErrorMessage = x.ErrorMessage
                    })
                    .ToList();

                // Recent failures
                stats.RecentFailures = transactions
                    .Where(x => !x.Success)
                    .OrderByDescending(x => x.Timestamp)
                    .Take(10)
                    .Select(x => new HttpTransactionSummary
                    {
                        RequestId = x.RequestId,
                        Method = x.Method,
                        Url = x.Url,
                        StatusCode = x.StatusCode,
                        DurationMs = x.DurationMs,
                        Timestamp = x.Timestamp,
                        ErrorMessage = x.ErrorMessage
                    })
                    .ToList();

                // Most accessed endpoints
                stats.MostAccessedEndpoints = transactions
                    .GroupBy(x => x.Url ?? "unknown")
                    .Select(g => new EndpointStatistic
                    {
                        Endpoint = g.Key,
                        RequestCount = g.Count(),
                        AverageDurationMs = g.Average(x => x.DurationMs),
                        SuccessCount = g.Count(x => x.Success),
                        FailureCount = g.Count(x => !x.Success),
                        SuccessRate = (decimal)g.Count(x => x.Success) / g.Count() * 100
                    })
                    .OrderByDescending(x => x.RequestCount)
                    .Take(10)
                    .ToList();

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get statistics");
                return new HttpTransactionStatistics();
            }
        }

        /// <summary>
        /// Deletes HTTP transaction logs older than the specified number of days.
        /// </summary>
        public async Task<int> DeleteOldLogsAsync(int daysToKeep)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
                var count = await _dbContext.HttpTransactions
                    .Where(x => x.Timestamp < cutoffDate)
                    .ExecuteDeleteAsync();

                // Phase 2 Cleanup: MaxRowsToKeep Enforcement
                if (_options.Retention.MaxRowsToKeep > 0)
                {
                    var totalRows = await _dbContext.HttpTransactions.CountAsync();
                    if (totalRows > _options.Retention.MaxRowsToKeep)
                    {
                        var rowsToDelete = totalRows - _options.Retention.MaxRowsToKeep;
                        
                        // EF Core 8 ExecuteDelete works with Skip/Take on SQL Server, 
                        // but to be safe and compatible with all providers, we select the IDs to delete.
                        var idsToDelete = await _dbContext.HttpTransactions
                            .OrderBy(x => x.Timestamp)
                            .Select(x => x.Id)
                            .Take(rowsToDelete)
                            .ToListAsync();

                        if (idsToDelete.Count > 0)
                        {
                            var excessDeleted = await _dbContext.HttpTransactions
                                .Where(x => idsToDelete.Contains(x.Id))
                                .ExecuteDeleteAsync();
                            
                            count += excessDeleted;
                            _logger.LogInformation("Deleted {Count} excess rows to maintain MaxRowsToKeep limit of {Limit}.", excessDeleted, _options.Retention.MaxRowsToKeep);
                        }
                    }
                }

                _logger.LogInformation("Deleted {Count} HTTP transaction logs during cleanup (Cutoff: {CutoffDate}, MaxRows: {MaxRows}).",
                    count, cutoffDate, _options.Retention.MaxRowsToKeep);

                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete old logs");
                return 0;
            }
        }

        /// <summary>
        /// Checks if the logging service is healthy and operational.
        /// </summary>
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                return await _dbContext.Database.CanConnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return false;
            }
        }

        /// <summary>
        /// Truncates request and response bodies if they exceed MaxBodySize.
        /// </summary>
        private HttpTransaction TruncateBodies(HttpTransaction transaction)
        {
            if (_options.MaxBodySize <= 0)
                return transaction;

            const string truncatedSuffix = "...[TRUNCATED]";

            if (!string.IsNullOrEmpty(transaction.RequestBody) && transaction.RequestBody.Length > _options.MaxBodySize)
            {
                transaction.RequestBody = transaction.RequestBody.Substring(0, _options.MaxBodySize) + truncatedSuffix;
            }

            if (!string.IsNullOrEmpty(transaction.ResponseBody) && transaction.ResponseBody.Length > _options.MaxBodySize)
            {
                transaction.ResponseBody = transaction.ResponseBody.Substring(0, _options.MaxBodySize) + truncatedSuffix;
            }

            return transaction;
        }

        /// <summary>
        /// Masks sensitive data in request and response bodies and headers.
        /// </summary>
        private HttpTransaction MaskSensitiveData(HttpTransaction transaction)
        {
            // Mask request headers
            if (!string.IsNullOrEmpty(transaction.RequestHeaders))
            {
                transaction.RequestHeaders = MaskJsonHeaders(transaction.RequestHeaders);
            }

            // Mask response headers
            if (!string.IsNullOrEmpty(transaction.ResponseHeaders))
            {
                transaction.ResponseHeaders = MaskJsonHeaders(transaction.ResponseHeaders);
            }

            // Mask request body
            if (!string.IsNullOrEmpty(transaction.RequestBody) && transaction.ContentType?.Contains("json") == true)
            {
                transaction.RequestBody = MaskJsonContent(transaction.RequestBody);
            }

            // Mask response body
            if (!string.IsNullOrEmpty(transaction.ResponseBody) && transaction.ContentType?.Contains("json") == true)
            {
                transaction.ResponseBody = MaskJsonContent(transaction.ResponseBody);
            }

            return transaction;
        }

        /// <summary>
        /// Masks sensitive fields in JSON headers.
        /// </summary>
        private string MaskJsonHeaders(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var dict = doc.RootElement.Deserialize<Dictionary<string, string>>() ?? new();

                foreach (var pattern in _options.SensitivePatterns)
                {
                    var sensitiveKeys = dict.Keys
                        .Where(k => k.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var key in sensitiveKeys)
                    {
                        dict[key] = "***MASKED***";
                    }
                }

                return JsonSerializer.Serialize(dict);
            }
            catch
            {
                return json;
            }
        }

        /// <summary>
        /// Masks sensitive fields in JSON content.
        /// </summary>
        private string MaskJsonContent(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var dict = doc.RootElement.Deserialize<Dictionary<string, object>>() ?? new();

                foreach (var pattern in _options.SensitivePatterns)
                {
                    var sensitiveKeys = dict.Keys
                        .Where(k => k.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var key in sensitiveKeys)
                    {
                        dict[key] = "***MASKED***";
                    }
                }

                return JsonSerializer.Serialize(dict);
            }
            catch
            {
                return json;
            }
        }

        /// <summary>
        /// Calculates a percentile value from a list of long values.
        /// </summary>
        private long CalculatePercentile(List<long> values, int percentile)
        {
            if (values.Count == 0)
                return 0;

            var sorted = values.OrderBy(x => x).ToList();
            var index = (int)Math.Ceiling((percentile / 100.0) * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
        }

        /// <summary>
        /// Applies sorting to a transaction query.
        /// </summary>
        private IQueryable<HttpTransaction> ApplySorting(IQueryable<HttpTransaction> query, string sortBy)
        {
            return sortBy?.ToLower() switch
            {
                "oldest" => query.OrderBy(x => x.Timestamp),
                "slowest" => query.OrderByDescending(x => x.DurationMs),
                "fastest" => query.OrderBy(x => x.DurationMs),
                "successful" => query.OrderByDescending(x => x.Success),
                "failed" => query.OrderBy(x => x.Success),
                _ => query.OrderByDescending(x => x.Timestamp) // default: newest
            };
        }
    }
}
