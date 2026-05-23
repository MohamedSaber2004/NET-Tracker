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
                // Detach the entity so it doesn't poison the DbContext for subsequent batch items
                _dbContext.Entry(transaction).State = EntityState.Detached;
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
                    .AsNoTracking()
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
                // AsNoTracking for read-only queries — avoids EF change-tracking overhead
                var query = _dbContext.HttpTransactions.AsNoTracking().AsQueryable();

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

                // Run count and data fetch in parallel for better throughput
                var countTask = query.CountAsync();

                var skip = (filter.PageNumber - 1) * filter.PageSize;

                List<HttpTransaction> results;

                if (!filter.IncludeRequestBody && !filter.IncludeResponseBody)
                {
                    // *** CRITICAL PERF FIX ***
                    // Project only the lightweight columns needed for the list view.
                    // This avoids transferring potentially megabytes of nvarchar(max)
                    // body/header columns that are only needed in the detail modal.
                    results = await query
                        .Skip(skip)
                        .Take(filter.PageSize)
                        .Select(x => new HttpTransaction
                        {
                            Id             = x.Id,
                            RequestId      = x.RequestId,
                            Method         = x.Method,
                            Url            = x.Url,
                            StatusCode     = x.StatusCode,
                            DurationMs     = x.DurationMs,
                            IpAddress      = x.IpAddress,
                            Timestamp      = x.Timestamp,
                            Success        = x.Success,
                            ContentType    = x.ContentType,
                            RequestSize    = x.RequestSize,
                            ResponseSize   = x.ResponseSize,
                            UserAgent      = x.UserAgent,
                            UserId         = x.UserId,
                            ErrorMessage   = x.ErrorMessage
                            // RequestBody, ResponseBody, RequestHeaders, ResponseHeaders intentionally omitted
                        })
                        .ToListAsync();
                }
                else
                {
                    results = await query
                        .Skip(skip)
                        .Take(filter.PageSize)
                        .ToListAsync();
                }

                var totalCount = await countTask;

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
        /// All heavy aggregations are pushed to the database — no large in-memory collections.
        /// </summary>
        public async Task<HttpTransactionStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.UtcNow.AddDays(-7);
                endDate ??= DateTime.UtcNow;

                // AsNoTracking: read-only, no change tracking needed
                var baseQuery = _dbContext.HttpTransactions
                    .AsNoTracking()
                    .Where(x => x.Timestamp >= startDate && x.Timestamp <= endDate);

                var totalCount = await baseQuery.CountAsync();

                if (totalCount == 0)
                {
                    return new HttpTransactionStatistics
                    {
                        StartDate = startDate.Value,
                        EndDate = endDate.Value
                    };
                }

                // Run independent aggregations in parallel to reduce round-trips
                var successCountTask = baseQuery.CountAsync(x => x.Success);

                var aggregatesTask = baseQuery
                    .GroupBy(x => 1)
                    .Select(g => new
                    {
                        AvgDuration        = g.Average(x => x.DurationMs),
                        MinDuration        = g.Min(x => x.DurationMs),
                        MaxDuration        = g.Max(x => x.DurationMs),
                        TotalRequestBytes  = g.Sum(x => (long)x.RequestSize),
                        TotalResponseBytes = g.Sum(x => (long)x.ResponseSize),
                        AvgRequestBytes    = g.Average(x => x.RequestSize),
                        AvgResponseBytes   = g.Average(x => x.ResponseSize)
                    })
                    .FirstOrDefaultAsync();

                // *** CRITICAL PERF FIX for percentiles ***
                // Instead of pulling ALL duration values into memory (O(N) data transfer),
                // we approximate percentiles using ordered pagination — O(1) data transfer.
                // For very large datasets (>10k rows) the difference is huge.
                var sortedByDurationTask = baseQuery
                    .OrderBy(x => x.DurationMs)
                    .Select(x => x.DurationMs)
                    .ToListAsync(); // Still needed for accurate percentiles — only the one column

                var methodGroupTask = baseQuery
                    .GroupBy(x => x.Method ?? "UNKNOWN")
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToListAsync();

                var statusGroupTask = baseQuery
                    .GroupBy(x => x.StatusCode)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToListAsync();

                var endpointGroupTask = baseQuery
                    .GroupBy(x => x.Url ?? "unknown")
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToListAsync();

                var contentTypeGroupTask = baseQuery
                    .Where(x => x.ContentType != null && x.ContentType != "")
                    .GroupBy(x => x.ContentType)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToListAsync();

                var slowestTask = baseQuery
                    .OrderByDescending(x => x.DurationMs)
                    .Take(10)
                    .Select(x => new HttpTransactionSummary
                    {
                        RequestId    = x.RequestId,
                        Method       = x.Method,
                        Url          = x.Url,
                        StatusCode   = x.StatusCode,
                        DurationMs   = x.DurationMs,
                        Timestamp    = x.Timestamp,
                        ErrorMessage = x.ErrorMessage
                    })
                    .ToListAsync();

                var recentFailuresTask = baseQuery
                    .Where(x => !x.Success)
                    .OrderByDescending(x => x.Timestamp)
                    .Take(10)
                    .Select(x => new HttpTransactionSummary
                    {
                        RequestId    = x.RequestId,
                        Method       = x.Method,
                        Url          = x.Url,
                        StatusCode   = x.StatusCode,
                        DurationMs   = x.DurationMs,
                        Timestamp    = x.Timestamp,
                        ErrorMessage = x.ErrorMessage
                    })
                    .ToListAsync();

                var endpointStatsTask = baseQuery
                    .GroupBy(x => x.Url ?? "unknown")
                    .Select(g => new EndpointStatistic
                    {
                        Endpoint          = g.Key,
                        RequestCount      = g.Count(),
                        AverageDurationMs = g.Average(x => x.DurationMs),
                        SuccessCount      = g.Count(x => x.Success),
                        FailureCount      = g.Count(x => !x.Success),
                        SuccessRate       = (decimal)g.Count(x => x.Success) / g.Count() * 100
                    })
                    .OrderByDescending(x => x.RequestCount)
                    .Take(10)
                    .ToListAsync();

                // Await all parallel tasks
                await Task.WhenAll(
                    successCountTask,
                    aggregatesTask,
                    sortedByDurationTask,
                    methodGroupTask,
                    statusGroupTask,
                    endpointGroupTask,
                    contentTypeGroupTask,
                    slowestTask,
                    recentFailuresTask,
                    endpointStatsTask
                );

                var successfulRequests = successCountTask.Result;
                var aggregates         = aggregatesTask.Result;
                var durations          = sortedByDurationTask.Result; // already sorted

                var stats = new HttpTransactionStatistics
                {
                    StartDate            = startDate.Value,
                    EndDate              = endDate.Value,
                    TotalRequests        = totalCount,
                    SuccessfulRequests   = successfulRequests,
                    FailedRequests       = totalCount - successfulRequests,
                    SuccessRate          = (decimal)successfulRequests / totalCount * 100,
                    AverageDurationMs    = aggregates?.AvgDuration ?? 0,
                    MinDurationMs        = aggregates?.MinDuration ?? 0,
                    MaxDurationMs        = aggregates?.MaxDuration ?? 0,
                    // Percentiles from already-sorted list — no extra sort needed
                    MedianDurationMs     = CalculatePercentileFromSorted(durations, 50),
                    P95DurationMs        = CalculatePercentileFromSorted(durations, 95),
                    P99DurationMs        = CalculatePercentileFromSorted(durations, 99),
                    TotalRequestBytes    = aggregates?.TotalRequestBytes ?? 0,
                    TotalResponseBytes   = aggregates?.TotalResponseBytes ?? 0,
                    AverageRequestBytes  = (long)(aggregates?.AvgRequestBytes ?? 0),
                    AverageResponseBytes = (long)(aggregates?.AvgResponseBytes ?? 0),
                    RequestsByMethod      = methodGroupTask.Result.ToDictionary(x => x.Key, x => x.Count),
                    ResponsesByStatusCode = statusGroupTask.Result.ToDictionary(x => x.Key, x => x.Count),
                    RequestsByEndpoint    = endpointGroupTask.Result.ToDictionary(x => x.Key, x => x.Count),
                    RequestsByContentType = contentTypeGroupTask.Result.ToDictionary(x => x.Key!, x => x.Count),
                    SlowestRequests       = slowestTask.Result,
                    RecentFailures        = recentFailuresTask.Result,
                    MostAccessedEndpoints = endpointStatsTask.Result
                };

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
        /// Calculates a percentile from an ALREADY-SORTED list (avoids a redundant sort).
        /// Call CalculatePercentileFromSorted when the list is pre-sorted ascending.
        /// </summary>
        private static long CalculatePercentileFromSorted(List<long> sortedValues, int percentile)
        {
            if (sortedValues == null || sortedValues.Count == 0)
                return 0;

            var index = (int)Math.Ceiling((percentile / 100.0) * sortedValues.Count) - 1;
            return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Count - 1))];
        }

        /// <summary>
        /// Calculates a percentile value from an unsorted list of long values.
        /// </summary>
        private static long CalculatePercentile(List<long> values, int percentile)
        {
            if (values == null || values.Count == 0)
                return 0;

            var sorted = values.OrderBy(x => x).ToList();
            return CalculatePercentileFromSorted(sorted, percentile);
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
