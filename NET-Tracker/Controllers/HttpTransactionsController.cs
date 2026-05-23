using Microsoft.AspNetCore.Mvc;
using NET_Tracker.Models;
using NET_Tracker.Services;
using NET_Tracker.Services.Interfaces;

namespace NET_Tracker.Controllers
{
    /// <summary>
    /// REST API controller for accessing HTTP transaction logs.
    /// Provides endpoints for searching, filtering, and retrieving logged HTTP requests and responses.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class HttpTransactionsController : ControllerBase
    {
        private readonly IHttpTransactionLogger _logger;
        private readonly ILogger<HttpTransactionsController> _appLogger;

        public HttpTransactionsController(
            IHttpTransactionLogger logger,
            ILogger<HttpTransactionsController> appLogger)
        {
            _logger = logger;
            _appLogger = appLogger;
        }

        /// <summary>
        /// Retrieves a single HTTP transaction by request ID.
        /// </summary>
        /// <param name="requestId">The unique request ID to retrieve</param>
        /// <returns>The HTTP transaction details</returns>
        /// <response code="200">Transaction found and returned</response>
        /// <response code="404">Transaction not found</response>
        [HttpGet("{requestId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<HttpTransaction>> GetTransaction(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return BadRequest(new { error = "RequestId is required" });
            }

            try
            {
                var transaction = await _logger.GetTransactionAsync(requestId);

                if (transaction == null)
                {
                    return NotFound(new { error = $"Transaction with RequestId '{requestId}' not found" });
                }

                return Ok(transaction);
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error retrieving transaction {RequestId}", requestId);
                return StatusCode(500, new { error = "An error occurred while retrieving the transaction" });
            }
        }

        /// <summary>
        /// Searches HTTP transactions with flexible filtering and pagination.
        /// </summary>
        /// <param name="filter">Search filter criteria</param>
        /// <returns>Paginated list of matching transactions</returns>
        /// <response code="200">Search results returned</response>
        /// <response code="400">Invalid filter parameters</response>
        [HttpPost("search")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SearchResponse<HttpTransaction>>> Search(HttpTransactionFilter filter)
        {
            if (filter == null)
            {
                filter = new HttpTransactionFilter();
            }

            // Validate filter
            if (!filter.IsValid(out var errors))
            {
                return BadRequest(new
                {
                    error = "Invalid filter parameters",
                    details = errors
                });
            }

            try
            {
                var (data, totalCount) = await _logger.SearchAsync(filter);

                return Ok(new SearchResponse<HttpTransaction>
                {
                    Data = data,
                    PageNumber = filter.PageNumber,
                    PageSize = filter.PageSize,
                    TotalCount = totalCount,
                    HasMore = (filter.PageNumber * filter.PageSize) < totalCount

                });
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error searching transactions");
                return StatusCode(500, new { error = "An error occurred while searching transactions" });
            }
        }

        /// <summary>
        /// Gets all failed HTTP transactions (status code >= 400).
        /// </summary>
        /// <param name="pageNumber">Page number for pagination (default: 1)</param>
        /// <param name="pageSize">Number of results per page (default: 50, max: 500)</param>
        /// <returns>List of failed transactions</returns>
        [HttpGet("failed")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<SearchResponse<HttpTransaction>>> GetFailedTransactions(
            int pageNumber = 1,
            int pageSize = 50)
        {
            try
            {
                var filter = new HttpTransactionFilter
                {
                    Success = false,
                    PageNumber = pageNumber,
                    PageSize = Math.Min(pageSize, 500),
                    SortBy = "newest"
                };

                var (data, totalCount) = await _logger.SearchAsync(filter);

                return Ok(new SearchResponse<HttpTransaction>
                {
                    Data = data,
                    PageNumber = pageNumber,
                    PageSize = filter.PageSize,
                    TotalCount = totalCount,
                    HasMore = (pageNumber * filter.PageSize) < totalCount
                });
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error retrieving failed transactions");
                return StatusCode(500, new { error = "An error occurred while retrieving failed transactions" });
            }
        }

        /// <summary>
        /// Gets slow HTTP transactions (duration > threshold).
        /// </summary>
        /// <param name="thresholdMs">Duration threshold in milliseconds (default: 1000)</param>
        /// <param name="pageNumber">Page number for pagination</param>
        /// <param name="pageSize">Results per page</param>
        /// <returns>List of slow transactions sorted by duration</returns>
        [HttpGet("slow")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<SearchResponse<HttpTransaction>>> GetSlowTransactions(
            long thresholdMs = 1000,
            int pageNumber = 1,
            int pageSize = 50)
        {
            try
            {
                var filter = new HttpTransactionFilter
                {
                    DurationMinMs = thresholdMs,
                    PageNumber = pageNumber,
                    PageSize = Math.Min(pageSize, 500),
                    SortBy = "slowest"
                };

                var (data, totalCount) = await _logger.SearchAsync(filter);

                return Ok(new SearchResponse<HttpTransaction>
                {
                    Data = data,
                    PageNumber = pageNumber,
                    PageSize = filter.PageSize,
                    TotalCount = totalCount,
                    HasMore = (pageNumber * filter.PageSize) < totalCount
                });
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error retrieving slow transactions");
                return StatusCode(500, new { error = "An error occurred while retrieving slow transactions" });
            }
        }

        /// <summary>
        /// Gets HTTP transactions by HTTP method (GET, POST, PUT, DELETE, etc.).
        /// </summary>
        /// <param name="method">HTTP method to filter by (GET, POST, PUT, DELETE, PATCH)</param>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Results per page</param>
        /// <returns>Transactions with the specified method</returns>
        [HttpGet("by-method/{method}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<SearchResponse<HttpTransaction>>> GetByMethod(
            string method,
            int pageNumber = 1,
            int pageSize = 50)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                return BadRequest(new { error = "Method is required" });
            }

            try
            {
                var filter = new HttpTransactionFilter
                {
                    Method = method.ToUpper(),
                    PageNumber = pageNumber,
                    PageSize = Math.Min(pageSize, 500),
                    SortBy = "newest"
                };

                var (data, totalCount) = await _logger.SearchAsync(filter);

                return Ok(new SearchResponse<HttpTransaction>
                {
                    Data = data,
                    PageNumber = pageNumber,
                    PageSize = filter.PageSize,
                    TotalCount = totalCount,
                    HasMore = (pageNumber * filter.PageSize) < totalCount
                });
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error retrieving transactions by method {Method}", method);
                return StatusCode(500, new { error = "An error occurred while retrieving transactions" });
            }
        }

        /// <summary>
        /// Gets HTTP transactions by status code.
        /// </summary>
        /// <param name="statusCode">HTTP status code (200, 404, 500, etc.)</param>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Results per page</param>
        /// <returns>Transactions with the specified status code</returns>
        [HttpGet("by-status/{statusCode}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<SearchResponse<HttpTransaction>>> GetByStatusCode(
            int statusCode,
            int pageNumber = 1,
            int pageSize = 50)
        {
            if (statusCode < 100 || statusCode > 599)
            {
                return BadRequest(new { error = "StatusCode must be between 100 and 599" });
            }

            try
            {
                var filter = new HttpTransactionFilter
                {
                    StatusCode = statusCode,
                    PageNumber = pageNumber,
                    PageSize = Math.Min(pageSize, 500),
                    SortBy = "newest"
                };

                var (data, totalCount) = await _logger.SearchAsync(filter);

                return Ok(new SearchResponse<HttpTransaction>
                {
                    Data = data,
                    PageNumber = pageNumber,
                    PageSize = filter.PageSize,
                    TotalCount = totalCount,
                    HasMore = (pageNumber * filter.PageSize) < totalCount
                });
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error retrieving transactions by status {StatusCode}", statusCode);
                return StatusCode(500, new { error = "An error occurred while retrieving transactions" });
            }
        }

        /// <summary>
        /// Gets recent HTTP transactions (last 24 hours by default).
        /// </summary>
        /// <param name="hoursBack">Number of hours to look back (default: 24)</param>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Results per page</param>
        /// <returns>Recent transactions</returns>
        [HttpGet("recent")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<SearchResponse<HttpTransaction>>> GetRecent(
            int hoursBack = 24,
            int pageNumber = 1,
            int pageSize = 50)
        {
            if (hoursBack < 1)
            {
                return BadRequest(new { error = "HoursBack must be at least 1" });
            }

            try
            {
                var filter = new HttpTransactionFilter
                {
                    StartDate = DateTime.UtcNow.AddHours(-hoursBack),
                    EndDate = DateTime.UtcNow,
                    PageNumber = pageNumber,
                    PageSize = Math.Min(pageSize, 500),
                    SortBy = "newest"
                };

                var (data, totalCount) = await _logger.SearchAsync(filter);

                return Ok(new SearchResponse<HttpTransaction>
                {
                    Data = data,
                    PageNumber = pageNumber,
                    PageSize = filter.PageSize,
                    TotalCount = totalCount,
                    HasMore = (pageNumber * filter.PageSize) < totalCount
                });
            }
            catch (Exception ex)
            {
                _appLogger.LogError(ex, "Error retrieving recent transactions");
                return StatusCode(500, new { error = "An error occurred while retrieving recent transactions" });
            }
        }
    }

    /// <summary>
    /// Generic response wrapper for search results with pagination info.
    /// </summary>
    public class SearchResponse<T>
    {
        public List<T> Data { get; set; } = new();
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public bool HasMore { get; set; }
    }
}
