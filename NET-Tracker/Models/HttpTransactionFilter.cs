using System;
using System.Collections.Generic;

namespace NET_Tracker.Models
{
    /// <summary>
    /// Filter/search parameters for querying HTTP transactions.
    /// Supports flexible filtering by various criteria.
    /// </summary>
    public class HttpTransactionFilter
    {
        /// <summary>
        /// Filter by specific request ID for single transaction lookup.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// Filter by HTTP method (GET, POST, PUT, DELETE, PATCH).
        /// </summary>
        public string? Method { get; set; }

        /// <summary>
        /// Filter by URL/endpoint path.
        /// Supports partial matching (contains).
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// Minimum HTTP status code to include in results.
        /// </summary>
        public int? StatusCodeMin { get; set; }

        /// <summary>
        /// Maximum HTTP status code to include in results.
        /// </summary>
        public int? StatusCodeMax { get; set; }

        /// <summary>
        /// Filter by exact status code.
        /// Overrides StatusCodeMin/Max if set.
        /// </summary>
        public int? StatusCode { get; set; }

        /// <summary>
        /// Minimum duration in milliseconds.
        /// Useful for finding slow requests.
        /// </summary>
        public long? DurationMinMs { get; set; }

        /// <summary>
        /// Maximum duration in milliseconds.
        /// </summary>
        public long? DurationMaxMs { get; set; }

        /// <summary>
        /// Filter by success/failure status.
        /// True = successful requests only, False = failed requests only.
        /// </summary>
        public bool? Success { get; set; }

        /// <summary>
        /// Filter by authenticated user ID.
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// Filter by client IP address.
        /// </summary>
        public string? IpAddress { get; set; }

        /// <summary>
        /// Start date for date range filtering.
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// End date for date range filtering.
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Free text search across URL, RequestId, and other fields.
        /// </summary>
        public string? SearchText { get; set; }

        /// <summary>
        /// Include request body in results.
        /// Set to false to reduce data transfer for large result sets.
        /// </summary>
        public bool IncludeRequestBody { get; set; } = true;

        /// <summary>
        /// Include response body in results.
        /// Set to false to reduce data transfer for large result sets.
        /// </summary>
        public bool IncludeResponseBody { get; set; } = true;

        /// <summary>
        /// Sort order for results.
        /// Options: "newest", "oldest", "slowest", "fastest", "successful", "failed"
        /// </summary>
        public string? SortBy { get; set; } = "newest";

        /// <summary>
        /// Page number for pagination (1-based).
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size for pagination.
        /// Default: 50, Max: 1000
        /// </summary>
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// Validates the filter parameters for reasonable values.
        /// </summary>
        public bool IsValid(out List<string> errors)
        {
            errors = new List<string>();

            if (PageNumber < 1)
                errors.Add("PageNumber must be >= 1");

            if (PageSize < 1)
                errors.Add("PageSize must be >= 1");

            if (PageSize > 1000)
                errors.Add("PageSize cannot exceed 1000");

            if (StatusCodeMin.HasValue && (StatusCodeMin < 100 || StatusCodeMin > 599))
                errors.Add("StatusCodeMin must be between 100 and 599");

            if (StatusCodeMax.HasValue && (StatusCodeMax < 100 || StatusCodeMax > 599))
                errors.Add("StatusCodeMax must be between 100 and 599");

            if (DurationMinMs.HasValue && DurationMinMs < 0)
                errors.Add("DurationMinMs cannot be negative");

            if (DurationMaxMs.HasValue && DurationMaxMs < 0)
                errors.Add("DurationMaxMs cannot be negative");

            if (StartDate.HasValue && EndDate.HasValue && StartDate > EndDate)
                errors.Add("StartDate cannot be after EndDate");

            return errors.Count == 0;
        }
    }
}
