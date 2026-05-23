using NET_Tracker.Models;
using NET_Tracker.Services.Interfaces;

namespace NET_Tracker.Services
{
    /// <summary>
    /// Resilient decorator for HTTP transaction logger (Phase 10: Error Handling & Resilience).
    /// Implements graceful degradation: if logging fails, the application continues normally.
    /// Features:
    /// - Retry logic with exponential backoff
    /// - Circuit breaker pattern to avoid hammering a failing database
    /// - Fallback to in-memory buffer when circuit is open
    /// - Automatic circuit reset after a cooldown period
    /// </summary>
    public class ResilientHttpTransactionLogger : IHttpTransactionLogger
    {
        private readonly IHttpTransactionLogger _innerLogger;
        private readonly ILogger<ResilientHttpTransactionLogger> _logger;

        // Circuit breaker state
        private int _consecutiveFailures = 0;
        private DateTime _circuitOpenedAt = DateTime.MinValue;
        private CircuitState _circuitState = CircuitState.Closed;
        private readonly object _circuitLock = new();

        // Configuration
        private const int FailureThreshold = 5;           // Open circuit after 5 failures
        private static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromSeconds(30);
        private const int MaxRetries = 2;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

        // Fallback buffer (holds transactions when circuit is open)
        private readonly Queue<HttpTransaction> _fallbackBuffer = new();
        private const int MaxFallbackBufferSize = 500;

        public enum CircuitState { Closed, Open, HalfOpen }

        public ResilientHttpTransactionLogger(
            IHttpTransactionLogger innerLogger,
            ILogger<ResilientHttpTransactionLogger> logger)
        {
            _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string GenerateRequestId() => _innerLogger.GenerateRequestId();

        /// <summary>
        /// Logs with retry and circuit breaker. If the circuit is open, buffers the transaction.
        /// </summary>
        public async Task LogTransactionAsync(HttpTransaction transaction)
        {
            if (transaction == null) return;

            if (IsCircuitOpen())
            {
                BufferTransaction(transaction);
                return;
            }

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    await _innerLogger.LogTransactionAsync(transaction);
                    OnSuccess();
                    
                    // Try to drain buffer on success
                    await DrainBufferAsync();
                    return;
                }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    _logger.LogWarning(ex,
                        "Log attempt {Attempt}/{MaxRetries} failed for {RequestId}. Retrying...",
                        attempt + 1, MaxRetries + 1, transaction.RequestId);
                    await Task.Delay(RetryDelay * (attempt + 1)); // Exponential backoff
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "All {MaxRetries} retry attempts failed for {RequestId}. Recording failure.",
                        MaxRetries + 1, transaction.RequestId);
                    OnFailure();
                    BufferTransaction(transaction);
                }
            }
        }

        /// <inheritdoc />
        public async Task<HttpTransaction> GetTransactionAsync(string requestId)
        {
            try { return await _innerLogger.GetTransactionAsync(requestId); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve transaction {RequestId}", requestId);
                return null!;
            }
        }

        /// <inheritdoc />
        public async Task<(List<HttpTransaction> Data, int TotalCount)> SearchAsync(HttpTransactionFilter filter)
        {
            try { return await _innerLogger.SearchAsync(filter); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search transactions");
                return (new List<HttpTransaction>(), 0);
            }
        }

        /// <inheritdoc />
        public async Task<HttpTransactionStatistics> GetStatisticsAsync(
            DateTime? startDate = null, DateTime? endDate = null)
        {
            try { return await _innerLogger.GetStatisticsAsync(startDate, endDate); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get statistics");
                return new HttpTransactionStatistics();
            }
        }

        /// <inheritdoc />
        public async Task<int> DeleteOldLogsAsync(int daysToKeep)
        {
            try { return await _innerLogger.DeleteOldLogsAsync(daysToKeep); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete old logs");
                return 0;
            }
        }

        /// <inheritdoc />
        public async Task<bool> IsHealthyAsync()
        {
            try { return await _innerLogger.IsHealthyAsync(); }
            catch { return false; }
        }

        /// <summary>Gets the current circuit breaker state (for monitoring).</summary>
        public CircuitState CurrentState => _circuitState;

        /// <summary>Gets the number of transactions buffered due to circuit open.</summary>
        public int BufferedCount => _fallbackBuffer.Count;

        // ─── Circuit Breaker Logic ──────────────────────────────────────

        private bool IsCircuitOpen()
        {
            lock (_circuitLock)
            {
                if (_circuitState == CircuitState.Open)
                {
                    // Check if cooldown period has elapsed
                    if (DateTime.UtcNow - _circuitOpenedAt >= CircuitBreakDuration)
                    {
                        _logger.LogInformation("Circuit breaker transitioning to HALF-OPEN state (testing connectivity)");
                        _circuitState = CircuitState.HalfOpen;
                        return false; // Allow one attempt through
                    }
                    return true; // Still open
                }
                return false;
            }
        }

        private void OnSuccess()
        {
            lock (_circuitLock)
            {
                if (_circuitState == CircuitState.HalfOpen)
                {
                    _logger.LogInformation("Circuit breaker CLOSED - connectivity restored");
                }
                _consecutiveFailures = 0;
                _circuitState = CircuitState.Closed;
            }
        }

        private void OnFailure()
        {
            lock (_circuitLock)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= FailureThreshold || _circuitState == CircuitState.HalfOpen)
                {
                    _circuitState = CircuitState.Open;
                    _circuitOpenedAt = DateTime.UtcNow;
                    _logger.LogWarning(
                        "Circuit breaker OPEN after {Failures} failures. Will reset in {Seconds}s",
                        _consecutiveFailures, CircuitBreakDuration.TotalSeconds);
                }
            }
        }

        private void BufferTransaction(HttpTransaction transaction)
        {
            lock (_fallbackBuffer)
            {
                if (_fallbackBuffer.Count >= MaxFallbackBufferSize)
                    _fallbackBuffer.Dequeue(); // Drop oldest

                _fallbackBuffer.Enqueue(transaction);
                _logger.LogDebug("Transaction {RequestId} buffered ({Count}/{Max})",
                    transaction.RequestId, _fallbackBuffer.Count, MaxFallbackBufferSize);
            }
        }

        private async Task DrainBufferAsync()
        {
            List<HttpTransaction> toFlush;
            lock (_fallbackBuffer)
            {
                if (_fallbackBuffer.Count == 0) return;
                toFlush = new List<HttpTransaction>(_fallbackBuffer);
                _fallbackBuffer.Clear();
            }

            _logger.LogInformation("Draining {Count} buffered transactions...", toFlush.Count);
            foreach (var txn in toFlush)
            {
                try { await _innerLogger.LogTransactionAsync(txn); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to drain buffered transaction {RequestId}", txn.RequestId);
                    // Re-buffer on failure
                    BufferTransaction(txn);
                    break;
                }
            }
        }
    }
}
