using System.Threading.Channels;
using NET_Tracker.Models;
using NET_Tracker.Services.Interfaces;

namespace NET_Tracker.Services
{
    /// <summary>
    /// High-performance queue-based HTTP transaction logger.
    /// Uses System.Threading.Channels to decouple logging from the request pipeline,
    /// achieving near-zero overhead on active requests.
    ///
    /// IMPORTANT: This class MUST be registered as a Singleton.
    /// It uses IServiceScopeFactory to create fresh DI scopes for each DB write,
    /// preventing the "captured disposed DbContext" bug that occurs when a Scoped
    /// DbContext is used from a background Task that outlives the request scope.
    /// </summary>
    public class QueuedHttpTransactionLogger : IHttpTransactionLogger, IDisposable
    {
        private readonly Channel<HttpTransaction> _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<QueuedHttpTransactionLogger> _logger;
        private readonly Task _backgroundTask;
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// Initializes the queued logger with a bounded channel to prevent unbounded memory growth.
        /// </summary>
        /// <param name="scopeFactory">Used to create fresh DI scopes in the background loop.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="maxQueueSize">Maximum number of pending log entries (default: 10,000).</param>
        public QueuedHttpTransactionLogger(
            IServiceScopeFactory scopeFactory,
            ILogger<QueuedHttpTransactionLogger> logger,
            int maxQueueSize = 10_000)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Bounded channel — drops oldest items if full (back-pressure handling)
            _channel = Channel.CreateBounded<HttpTransaction>(new BoundedChannelOptions(maxQueueSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

            // Start background consumer immediately
            _backgroundTask = Task.Run(ProcessQueueAsync);
        }

        /// <inheritdoc />
        public string GenerateRequestId() => Guid.NewGuid().ToString();

        /// <summary>
        /// Enqueues the transaction for async processing. Returns immediately (non-blocking).
        /// </summary>
        public async Task LogTransactionAsync(HttpTransaction transaction)
        {
            if (transaction == null) return;

            // Non-blocking write — if channel is full, DropOldest mode handles it
            if (!_channel.Writer.TryWrite(transaction))
            {
                // Fallback: try async write with short timeout
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                    await _channel.Writer.WriteAsync(transaction, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Queue full - dropping transaction {RequestId}", transaction.RequestId);
                }
            }
        }

        /// <inheritdoc />
        public async Task<HttpTransaction?> GetTransactionAsync(string requestId)
        {
            using var scope = _scopeFactory.CreateScope();
            var inner = scope.ServiceProvider.GetRequiredService<HttpTransactionLogger>();
            return await inner.GetTransactionAsync(requestId);
        }

        /// <inheritdoc />
        public async Task<(List<HttpTransaction> Data, int TotalCount)> SearchAsync(HttpTransactionFilter filter)
        {
            using var scope = _scopeFactory.CreateScope();
            var inner = scope.ServiceProvider.GetRequiredService<HttpTransactionLogger>();
            return await inner.SearchAsync(filter);
        }

        /// <inheritdoc />
        public async Task<HttpTransactionStatistics> GetStatisticsAsync(
            DateTime? startDate = null, DateTime? endDate = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var inner = scope.ServiceProvider.GetRequiredService<HttpTransactionLogger>();
            return await inner.GetStatisticsAsync(startDate, endDate);
        }

        /// <inheritdoc />
        public async Task<int> DeleteOldLogsAsync(int daysToKeep)
        {
            using var scope = _scopeFactory.CreateScope();
            var inner = scope.ServiceProvider.GetRequiredService<HttpTransactionLogger>();
            return await inner.DeleteOldLogsAsync(daysToKeep);
        }

        /// <inheritdoc />
        public async Task<bool> IsHealthyAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var inner = scope.ServiceProvider.GetRequiredService<HttpTransactionLogger>();
            return await inner.IsHealthyAsync();
        }

        /// <summary>
        /// Gets the current number of items pending in the queue.
        /// </summary>
        public int PendingCount => _channel.Reader.Count;

        /// <summary>
        /// Background loop that reads from the channel and writes to the DB.
        /// Creates a fresh DI scope (and therefore a fresh DbContext) per batch
        /// to avoid using disposed contexts.
        /// </summary>
        private async Task ProcessQueueAsync()
        {
            _logger.LogInformation("QueuedHttpTransactionLogger background processor started.");
            var batch = new List<HttpTransaction>(capacity: 50);

            try
            {
                await foreach (var transaction in _channel.Reader.ReadAllAsync(_cts.Token))
                {
                    batch.Add(transaction);

                    // Drain all immediately available items into the same batch (up to 50)
                    while (batch.Count < 50 && _channel.Reader.TryRead(out var extra))
                        batch.Add(extra);

                    // Write the batch using a FRESH scope → fresh DbContext per batch
                    await WriteBatchAsync(batch);
                    batch.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "Queue processor stopping. Flushing {Count} remaining items...", batch.Count);

                // Flush any remaining items in the in-memory batch
                if (batch.Count > 0)
                    await WriteBatchAsync(batch);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Queue processor crashed unexpectedly.");
            }
        }

        /// <summary>
        /// Writes a batch of transactions to the DB inside a fresh DI scope.
        /// </summary>
        private async Task WriteBatchAsync(List<HttpTransaction> batch)
        {
            if (batch.Count == 0) return;

            try
            {
                // Fresh scope = fresh DbContext, no "disposed context" issues
                await using var scope = _scopeFactory.CreateAsyncScope();
                var inner = scope.ServiceProvider.GetRequiredService<HttpTransactionLogger>();

                foreach (var item in batch)
                {
                    try
                    {
                        await inner.LogTransactionAsync(item);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to persist queued transaction {RequestId}", item.RequestId);
                    }
                }

                _logger.LogDebug("Flushed batch of {Count} transaction(s) to database.", batch.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write batch of {Count} transactions.", batch.Count);
            }
        }

        /// <summary>
        /// Gracefully shuts down the background processor and flushes remaining items.
        /// </summary>
        public void Dispose()
        {
            _channel.Writer.Complete();
            _cts.Cancel();

            try { _backgroundTask.GetAwaiter().GetResult(); }
            catch { /* ignore shutdown exceptions */ }

            _cts.Dispose();
        }
    }
}
