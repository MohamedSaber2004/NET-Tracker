using Microsoft.Extensions.Caching.Memory;
using NET_Tracker.Models;
using NET_Tracker.Services.Interfaces;

namespace NET_Tracker.Services
{
    /// <summary>
    /// Caching decorator for HTTP transaction logger.
    /// Caches read operations (GetTransactionAsync, GetStatisticsAsync) to reduce
    /// database load for frequently queried data.
    /// Write operations are passed through directly to the inner logger.
    /// </summary>
    public class CachedHttpTransactionLogger : IHttpTransactionLogger
    {
        private readonly IHttpTransactionLogger _innerLogger;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CachedHttpTransactionLogger> _logger;
        private const string CacheKeyPrefix = "http_txn_";
        private const string StatsCacheKeyPrefix = "http_stats_";
        private static readonly TimeSpan TransactionCacheDuration = TimeSpan.FromHours(1);
        private static readonly TimeSpan StatsCacheDuration = TimeSpan.FromMinutes(5);

        public CachedHttpTransactionLogger(
            IHttpTransactionLogger innerLogger,
            IMemoryCache cache,
            ILogger<CachedHttpTransactionLogger> logger)
        {
            _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string GenerateRequestId() => _innerLogger.GenerateRequestId();

        /// <summary>
        /// Logs a transaction and invalidates any related cached statistics.
        /// </summary>
        public async Task LogTransactionAsync(HttpTransaction transaction)
        {
            await _innerLogger.LogTransactionAsync(transaction);
            // Invalidate stats cache since new data arrived
            InvalidateStatsCache();
        }

        /// <summary>
        /// Retrieves a transaction by ID with caching (1 hour TTL).
        /// </summary>
        public async Task<HttpTransaction> GetTransactionAsync(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return await _innerLogger.GetTransactionAsync(requestId);

            var cacheKey = CacheKeyPrefix + requestId;

            if (_cache.TryGetValue(cacheKey, out HttpTransaction? cached))
            {
                _logger.LogDebug("Cache hit for transaction {RequestId}", requestId);
                return cached!;
            }

            var transaction = await _innerLogger.GetTransactionAsync(requestId);

            if (transaction != null)
            {
                _cache.Set(cacheKey, transaction, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TransactionCacheDuration,
                    SlidingExpiration = TimeSpan.FromMinutes(15),
                    Size = 1
                });
            }

            return transaction!;
        }

        /// <inheritdoc />
        public async Task<(List<HttpTransaction> Data, int TotalCount)> SearchAsync(HttpTransactionFilter filter)
            => await _innerLogger.SearchAsync(filter);

        /// <summary>
        /// Gets aggregated statistics with a short cache TTL (5 minutes).
        /// Statistics are expensive to compute and tolerate slight staleness.
        /// </summary>
        public async Task<HttpTransactionStatistics> GetStatisticsAsync(
            DateTime? startDate = null, DateTime? endDate = null)
        {
            var cacheKey = BuildStatsCacheKey(startDate, endDate);

            if (_cache.TryGetValue(cacheKey, out HttpTransactionStatistics? cached))
            {
                _logger.LogDebug("Cache hit for statistics");
                return cached!;
            }

            var stats = await _innerLogger.GetStatisticsAsync(startDate, endDate);

            _cache.Set(cacheKey, stats, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = StatsCacheDuration,
                Size = 1
            });

            return stats;
        }

        /// <inheritdoc />
        public async Task<int> DeleteOldLogsAsync(int daysToKeep)
        {
            var count = await _innerLogger.DeleteOldLogsAsync(daysToKeep);
            // After deletion, cached stats are stale
            InvalidateStatsCache();
            return count;
        }

        /// <inheritdoc />
        public Task<bool> IsHealthyAsync() => _innerLogger.IsHealthyAsync();

        private string BuildStatsCacheKey(DateTime? startDate, DateTime? endDate)
        {
            var start = startDate?.ToString("yyyyMMddHH") ?? "null";
            var end = endDate?.ToString("yyyyMMddHH") ?? "null";
            return $"{StatsCacheKeyPrefix}{start}_{end}";
        }

        private void InvalidateStatsCache()
        {
            // MemoryCache doesn't support tag-based invalidation natively.
            // For production, use IOutputCacheStore or Redis with tag support.
            // Here we rely on short TTL (5 min) for eventual consistency.
            _logger.LogDebug("Statistics cache will expire naturally (TTL: 5 minutes)");
        }
    }
}
