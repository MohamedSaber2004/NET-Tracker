using Microsoft.Extensions.Options;
using NET_Tracker.Configuration;
using NET_Tracker.Services.Interfaces;

namespace NET_Tracker.Services
{
    /// <summary>
    /// Background hosted service that periodically cleans up old HTTP transaction logs.
    /// Runs on a configurable schedule (cron-based interval) to enforce retention policies.
    /// This is Phase 8: Performance Optimization - automated data lifecycle management.
    /// </summary>
    public class HttpLogsCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<HttpLogsCleanupService> _logger;
        private readonly HttpLoggingOptions _options;

        // Cleanup check interval - run every hour, actual cleanup respects schedule
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

        public HttpLogsCleanupService(
            IServiceProvider serviceProvider,
            ILogger<HttpLogsCleanupService> logger,
            IOptions<HttpLoggingOptions> options)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? new HttpLoggingOptions();
        }

        /// <summary>
        /// Main background loop. Wakes up every hour and checks if cleanup is due.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("HTTP Logs Cleanup Service started. Retention policy: {DaysToKeep} days",
                _options.Retention.DaysToKeep);

            // Wait a bit after startup before first run
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_options.Retention.AutoCleanup)
                {
                    await RunCleanupAsync(stoppingToken);
                }
                else
                {
                    _logger.LogDebug("Auto-cleanup is disabled. Skipping retention enforcement.");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }

            _logger.LogInformation("HTTP Logs Cleanup Service stopped.");
        }

        /// <summary>
        /// Performs the actual log cleanup using a scoped service.
        /// </summary>
        private async Task RunCleanupAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Running HTTP transaction log cleanup (keeping {Days} days)...",
                    _options.Retention.DaysToKeep);

                // Use scoped service to get a fresh DbContext per operation
                await using var scope = _serviceProvider.CreateAsyncScope();
                var transactionLogger = scope.ServiceProvider.GetRequiredService<IHttpTransactionLogger>();

                var deletedCount = await transactionLogger.DeleteOldLogsAsync(_options.Retention.DaysToKeep);

                if (deletedCount > 0)
                {
                    _logger.LogInformation("Cleanup complete: deleted {Count} HTTP transaction logs older than {Days} days.",
                        deletedCount, _options.Retention.DaysToKeep);
                }
                else
                {
                    _logger.LogDebug("Cleanup ran: no logs older than {Days} days found.", _options.Retention.DaysToKeep);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown in progress
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HTTP log cleanup failed. Will retry in {Hours} hour(s).",
                    CheckInterval.TotalHours);
            }
        }
    }
}
