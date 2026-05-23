using Microsoft.AspNetCore.Mvc;
using NET_Tracker.Services;
using NET_Tracker.Services.Interfaces;

namespace NET_Tracker.Controllers
{
    /// <summary>
    /// Health check controller for monitoring system status and dependencies.
    /// Provides endpoints for verifying the HTTP logging service is operational.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class HealthController : ControllerBase
    {
        private readonly IHttpTransactionLogger _logger;
        private readonly ILogger<HealthController> _appLogger;

        public HealthController(
            IHttpTransactionLogger logger,
            ILogger<HealthController> appLogger)
        {
            _logger = logger;
            _appLogger = appLogger;
        }

        /// <summary>
        /// Gets overall system health status.
        /// Checks database connectivity and logging service availability.
        /// </summary>
        /// <returns>Health status</returns>
        /// <response code="200">System is healthy</response>
        /// <response code="503">System is unhealthy</response>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<HealthCheckResponse>> Get()
        {
            try
            {
                var isHealthy = await _logger.IsHealthyAsync();

                var response = new HealthCheckResponse
                {
                    Status = isHealthy ? "healthy" : "unhealthy",
                    Timestamp = DateTime.UtcNow,
                    Services = new
                    {
                        Database = isHealthy ? "operational" : "unavailable",
                        HttpLogging = isHealthy ? "operational" : "unavailable"
                    }
                };

                if (!isHealthy)
                {
                    return StatusCode(503, response);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Health check failed");

                return StatusCode(503, new HealthCheckResponse
                {
                    Status = "unhealthy",
                    Timestamp = DateTime.UtcNow,
                    Error = ex.Message,
                    Services = new
                    {
                        Database = "unavailable",
                        HttpLogging = "unavailable"
                    }
                });
            }
        }

        /// <summary>
        /// Gets detailed health information.
        /// Includes version, uptime, and service details.
        /// </summary>
        /// <returns>Detailed health information</returns>
        [HttpGet("detailed")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DetailedHealthCheckResponse>> GetDetailed()
        {
            try
            {
                var isHealthy = await _logger.IsHealthyAsync();
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

                var response = new DetailedHealthCheckResponse
                {
                    Status = isHealthy ? "healthy" : "unhealthy",
                    Timestamp = DateTime.UtcNow,
                    Version = "1.0.0", // TODO: Get from assembly version
                    UptimeSeconds = (long)uptime.TotalSeconds,
                    Services = new DetailedServiceStatus
                    {
                        Database = new ServiceDetail
                        {
                            Status = isHealthy ? "operational" : "unavailable",
                            LastChecked = DateTime.UtcNow,
                            Message = isHealthy ? "Database connection successful" : "Database connection failed"
                        },
                        HttpLogging = new ServiceDetail
                        {
                            Status = isHealthy ? "operational" : "unavailable",
                            LastChecked = DateTime.UtcNow,
                            Message = isHealthy ? "HTTP logging service is operational" : "HTTP logging service is unavailable"
                        }
                    }
                };

                if (!isHealthy)
                {
                    return StatusCode(503, response);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Detailed health check failed");

                return StatusCode(503, new DetailedHealthCheckResponse
                {
                    Status = "unhealthy",
                    Timestamp = DateTime.UtcNow,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Liveness probe for Kubernetes or similar orchestration.
        /// Returns 200 if application is running.
        /// </summary>
        /// <returns>Liveness status</returns>
        [HttpGet("live")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<object>> Live()
        {
            return await Task.FromResult(Ok(new { status = "alive", timestamp = DateTime.UtcNow }));
        }

        /// <summary>
        /// Readiness probe for Kubernetes or similar orchestration.
        /// Returns 200 if application is ready to serve requests.
        /// </summary>
        /// <returns>Readiness status</returns>
        [HttpGet("ready")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<object>> Ready()
        {
            try
            {
                var isHealthy = await _logger.IsHealthyAsync();

                if (!isHealthy)
                {
                    return StatusCode(503, new { status = "not ready", reason = "database unavailable" });
                }

                return Ok(new { status = "ready", timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Readiness check failed");
                return StatusCode(503, new { status = "not ready", reason = ex.Message });
            }
        }
    }

    /// <summary>
    /// Basic health check response.
    /// </summary>
    public class HealthCheckResponse
    {
        public string Status { get; set; }
        public DateTime Timestamp { get; set; }
        public object Services { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Detailed health check response with version and uptime.
    /// </summary>
    public class DetailedHealthCheckResponse
    {
        public string Status { get; set; }
        public DateTime Timestamp { get; set; }
        public string Version { get; set; }
        public long UptimeSeconds { get; set; }
        public DetailedServiceStatus Services { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Detailed status for each service.
    /// </summary>
    public class DetailedServiceStatus
    {
        public ServiceDetail Database { get; set; }
        public ServiceDetail HttpLogging { get; set; }
    }

    /// <summary>
    /// Individual service status details.
    /// </summary>
    public class ServiceDetail
    {
        public string Status { get; set; }
        public DateTime LastChecked { get; set; }
        public string Message { get; set; }
    }
}
