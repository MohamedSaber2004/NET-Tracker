using Microsoft.AspNetCore.Mvc;
using NET_Tracker.Filters;
using NET_Tracker.Models;
using NET_Tracker.Services;
using NET_Tracker.Services.Interfaces;

namespace NET_Tracker.Controllers
{
    /// <summary>
    /// REST API controller for HTTP transaction statistics and analytics.
    /// Provides endpoints for aggregated metrics, performance analysis, and trending data.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    [DashboardAccessFilter]
    public class StatisticsController : ControllerBase
    {
        private readonly IHttpTransactionLogger _logger;
        private readonly ILogger<StatisticsController> _appLogger;

        public StatisticsController(
            IHttpTransactionLogger logger,
            ILogger<StatisticsController> appLogger)
        {
            _logger = logger;
            _appLogger = appLogger;
        }

        /// <summary>
        /// Gets aggregated statistics for HTTP transactions.
        /// Includes success rate, performance metrics (P95, P99), and breakdowns by method/status.
        /// </summary>
        /// <param name="startDate">Start date for statistics (default: 7 days ago)</param>
        /// <param name="endDate">End date for statistics (default: now)</param>
        /// <returns>Aggregated statistics</returns>
        /// <response code="200">Statistics calculated and returned</response>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<HttpTransactionStatistics>> GetStatistics(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                // Validate dates
                if (startDate.HasValue && endDate.HasValue && startDate > endDate)
                {
                    return BadRequest(new { error = "StartDate must be before EndDate" });
                }

                var stats = await _logger.GetStatisticsAsync(startDate, endDate);

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error calculating statistics");
                return StatusCode(500, new { error = "An error occurred while calculating statistics" });
            }
        }

        /// <summary>
        /// Gets statistics for the last 24 hours.
        /// Quick endpoint for daily metrics.
        /// </summary>
        /// <returns>Statistics for last 24 hours</returns>
        [HttpGet("daily")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<HttpTransactionStatistics>> GetDailyStatistics()
        {
            try
            {
                var stats = await _logger.GetStatisticsAsync(
                    DateTime.UtcNow.AddHours(-24),
                    DateTime.UtcNow
                );

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error calculating daily statistics");
                return StatusCode(500, new { error = "An error occurred while calculating daily statistics" });
            }
        }

        /// <summary>
        /// Gets statistics for the last 7 days.
        /// Quick endpoint for weekly metrics.
        /// </summary>
        /// <returns>Statistics for last 7 days</returns>
        [HttpGet("weekly")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<HttpTransactionStatistics>> GetWeeklyStatistics()
        {
            try
            {
                var stats = await _logger.GetStatisticsAsync(
                    DateTime.UtcNow.AddDays(-7),
                    DateTime.UtcNow
                );

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error calculating weekly statistics");
                return StatusCode(500, new { error = "An error occurred while calculating weekly statistics" });
            }
        }

        /// <summary>
        /// Gets statistics for the last 30 days.
        /// Quick endpoint for monthly metrics.
        /// </summary>
        /// <returns>Statistics for last 30 days</returns>
        [HttpGet("monthly")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<HttpTransactionStatistics>> GetMonthlyStatistics()
        {
            try
            {
                var stats = await _logger.GetStatisticsAsync(
                    DateTime.UtcNow.AddDays(-30),
                    DateTime.UtcNow
                );

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error calculating monthly statistics");
                return StatusCode(500, new { error = "An error occurred while calculating monthly statistics" });
            }
        }

        /// <summary>
        /// Gets summary statistics: success rate, avg response time, error count.
        /// Lightweight endpoint for dashboard displays.
        /// </summary>
        /// <returns>Summary statistics</returns>
        [HttpGet("summary")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<SummaryStatistics>> GetSummary()
        {
            try
            {
                var stats = await _logger.GetStatisticsAsync(
                    DateTime.UtcNow.AddHours(-24),
                    DateTime.UtcNow
                );

                return Ok(new SummaryStatistics
                {
                    TotalRequests = stats.TotalRequests,
                    SuccessfulRequests = stats.SuccessfulRequests,
                    FailedRequests = stats.FailedRequests,
                    SuccessRate = stats.SuccessRate,
                    AverageDurationMs = stats.AverageDurationMs,
                    P95DurationMs = stats.P95DurationMs,
                    P99DurationMs = stats.P99DurationMs,
                    TopSlowRequest = stats.SlowestRequests.FirstOrDefault(),
                    RecentError = stats.RecentFailures.FirstOrDefault()
                });
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error calculating summary statistics");
                return StatusCode(500, new { error = "An error occurred while calculating summary statistics" });
            }
        }

        /// <summary>
        /// Gets performance metrics: response times, throughput, and percentiles.
        /// Useful for SLA monitoring.
        /// </summary>
        /// <param name="startDate">Start date (default: 24 hours ago)</param>
        /// <param name="endDate">End date (default: now)</param>
        /// <returns>Performance metrics</returns>
        [HttpGet("performance")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<PerformanceMetrics>> GetPerformanceMetrics(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.UtcNow.AddHours(-24);
                endDate ??= DateTime.UtcNow;

                var stats = await _logger.GetStatisticsAsync(startDate, endDate);

                return Ok(new PerformanceMetrics
                {
                    AverageDurationMs = stats.AverageDurationMs,
                    MedianDurationMs = stats.MedianDurationMs,
                    MinDurationMs = stats.MinDurationMs,
                    MaxDurationMs = stats.MaxDurationMs,
                    P95DurationMs = stats.P95DurationMs,
                    P99DurationMs = stats.P99DurationMs,
                    ThroughputPerSecond = stats.TotalRequests / (endDate.Value - startDate.Value).TotalSeconds,
                    SuccessRate = stats.SuccessRate
                });
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error calculating performance metrics");
                return StatusCode(500, new { error = "An error occurred while calculating performance metrics" });
            }
        }

        /// <summary>
        /// Gets error analysis: error count, types, and trends.
        /// Useful for monitoring application health.
        /// </summary>
        /// <param name="startDate">Start date (default: 24 hours ago)</param>
        /// <param name="endDate">End date (default: now)</param>
        /// <returns>Error analysis</returns>
        [HttpGet("errors")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<ErrorAnalysis>> GetErrorAnalysis(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.UtcNow.AddHours(-24);
                endDate ??= DateTime.UtcNow;

                var stats = await _logger.GetStatisticsAsync(startDate, endDate);

                return Ok(new ErrorAnalysis
                {
                    TotalErrors = stats.FailedRequests,
                    ErrorRate = (decimal)stats.FailedRequests / stats.TotalRequests * 100,
                    SuccessRate = stats.SuccessRate,
                    ErrorsByStatusCode = stats.ResponsesByStatusCode
                        .Where(x => x.Key >= 400)
                        .OrderByDescending(x => x.Value)
                        .ToDictionary(x => x.Key, x => x.Value),
                    RecentErrors = stats.RecentFailures,
                    TimeRange = new { StartDate = startDate, EndDate = endDate }
                });
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error calculating error analysis");
                return StatusCode(500, new { error = "An error occurred while calculating error analysis" });
            }
        }

        /// <summary>
        /// Gets endpoint performance breakdown.
        /// Shows which endpoints are slowest and most accessed.
        /// </summary>
        /// <param name="startDate">Start date (default: 24 hours ago)</param>
        /// <param name="endDate">End date (default: now)</param>
        /// <returns>Endpoint statistics</returns>
        [HttpGet("endpoints")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<EndpointAnalysis>> GetEndpointAnalysis(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.UtcNow.AddHours(-24);
                endDate ??= DateTime.UtcNow;

                var stats = await _logger.GetStatisticsAsync(startDate, endDate);

                return Ok(new EndpointAnalysis
                {
                    MostAccessedEndpoints = stats.MostAccessedEndpoints,
                    SlowestEndpoints = stats.MostAccessedEndpoints
                        .OrderByDescending(x => x.AverageDurationMs)
                        .Take(10)
                        .ToList(),
                    EndpointCount = stats.RequestsByEndpoint.Count,
                    TimeRange = new { StartDate = startDate, EndDate = endDate }
                });
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error calculating endpoint analysis");
                return StatusCode(500, new { error = "An error occurred while calculating endpoint analysis" });
            }
        }
    }

    /// <summary>
    /// Summary statistics for quick dashboard display.
    /// </summary>
    public class SummaryStatistics
    {
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public decimal SuccessRate { get; set; }
        public double AverageDurationMs { get; set; }
        public long P95DurationMs { get; set; }
        public long P99DurationMs { get; set; }
        public HttpTransactionSummary TopSlowRequest { get; set; }
        public HttpTransactionSummary RecentError { get; set; }
    }

    /// <summary>
    /// Performance metrics for SLA monitoring.
    /// </summary>
    public class PerformanceMetrics
    {
        public double AverageDurationMs { get; set; }
        public long MedianDurationMs { get; set; }
        public long MinDurationMs { get; set; }
        public long MaxDurationMs { get; set; }
        public long P95DurationMs { get; set; }
        public long P99DurationMs { get; set; }
        public double ThroughputPerSecond { get; set; }
        public decimal SuccessRate { get; set; }
    }

    /// <summary>
    /// Error analysis for health monitoring.
    /// </summary>
    public class ErrorAnalysis
    {
        public int TotalErrors { get; set; }
        public decimal ErrorRate { get; set; }
        public decimal SuccessRate { get; set; }
        public Dictionary<int, int> ErrorsByStatusCode { get; set; }
        public List<HttpTransactionSummary> RecentErrors { get; set; }
        public object TimeRange { get; set; }
    }

    /// <summary>
    /// Endpoint performance analysis.
    /// </summary>
    public class EndpointAnalysis
    {
        public List<EndpointStatistic> MostAccessedEndpoints { get; set; }
        public List<EndpointStatistic> SlowestEndpoints { get; set; }
        public int EndpointCount { get; set; }
        public object TimeRange { get; set; }
    }
}
