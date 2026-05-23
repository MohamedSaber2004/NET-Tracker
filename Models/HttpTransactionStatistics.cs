using System;
using System.Collections.Generic;

namespace NET_Tracker.Models
{
    /// <summary>
    /// Aggregated statistics about HTTP transactions.
    /// Used for dashboards, monitoring, and performance analysis.
    /// </summary>
    public class HttpTransactionStatistics
    {
        /// <summary>
        /// Total number of HTTP transactions in the specified time range.
        /// </summary>
        public int TotalRequests { get; set; }

        /// <summary>
        /// Number of successful transactions (status code < 400).
        /// </summary>
        public int SuccessfulRequests { get; set; }

        /// <summary>
        /// Number of failed transactions (status code >= 400).
        /// </summary>
        public int FailedRequests { get; set; }

        /// <summary>
        /// Percentage of successful requests out of total.
        /// Calculated as (SuccessfulRequests / TotalRequests) * 100
        /// </summary>
        public decimal SuccessRate { get; set; }

        /// <summary>
        /// Average duration of requests in milliseconds.
        /// </summary>
        public double AverageDurationMs { get; set; }

        /// <summary>
        /// Minimum duration in milliseconds.
        /// </summary>
        public long MinDurationMs { get; set; }

        /// <summary>
        /// Maximum duration in milliseconds.
        /// </summary>
        public long MaxDurationMs { get; set; }

        /// <summary>
        /// Median duration in milliseconds.
        /// Useful for understanding typical request performance.
        /// </summary>
        public long MedianDurationMs { get; set; }

        /// <summary>
        /// 95th percentile duration in milliseconds.
        /// Indicates performance for slow requests.
        /// </summary>
        public long P95DurationMs { get; set; }

        /// <summary>
        /// 99th percentile duration in milliseconds.
        /// Indicates performance for very slow requests.
        /// </summary>
        public long P99DurationMs { get; set; }

        /// <summary>
        /// Total bytes transferred in requests.
        /// </summary>
        public long TotalRequestBytes { get; set; }

        /// <summary>
        /// Total bytes transferred in responses.
        /// </summary>
        public long TotalResponseBytes { get; set; }

        /// <summary>
        /// Average request size in bytes.
        /// </summary>
        public long AverageRequestBytes { get; set; }

        /// <summary>
        /// Average response size in bytes.
        /// </summary>
        public long AverageResponseBytes { get; set; }

        /// <summary>
        /// Breakdown of requests by HTTP method.
        /// Key: HTTP method (GET, POST, etc.)
        /// Value: Count of requests using that method
        /// </summary>
        public Dictionary<string, int> RequestsByMethod { get; set; } = new();

        /// <summary>
        /// Breakdown of responses by status code.
        /// Key: Status code (200, 400, 500, etc.)
        /// Value: Count of responses with that status
        /// </summary>
        public Dictionary<int, int> ResponsesByStatusCode { get; set; } = new();

        /// <summary>
        /// Breakdown of requests by endpoint/URL.
        /// Key: Endpoint URL
        /// Value: Count of requests to that endpoint
        /// </summary>
        public Dictionary<string, int> RequestsByEndpoint { get; set; } = new();

        /// <summary>
        /// Breakdown of requests by content type.
        /// Key: Content-Type header value
        /// Value: Count of requests with that content type
        /// </summary>
        public Dictionary<string, int> RequestsByContentType { get; set; } = new();

        /// <summary>
        /// Top slowest requests in the time range.
        /// Limited to a reasonable number (e.g., 10).
        /// </summary>
        public List<HttpTransactionSummary> SlowestRequests { get; set; } = new();

        /// <summary>
        /// Most recent failed requests.
        /// Limited to a reasonable number (e.g., 10).
        /// </summary>
        public List<HttpTransactionSummary> RecentFailures { get; set; } = new();

        /// <summary>
        /// Most frequently accessed endpoints.
        /// Limited to a reasonable number (e.g., 10).
        /// </summary>
        public List<EndpointStatistic> MostAccessedEndpoints { get; set; } = new();

        /// <summary>
        /// Time period covered by these statistics.
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// Time period covered by these statistics.
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// When these statistics were calculated.
        /// </summary>
        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Summary representation of the statistics.
        /// </summary>
        public override string ToString()
        {
            return $"Requests: {TotalRequests}, Success Rate: {SuccessRate:F2}%, Avg Duration: {AverageDurationMs:F0}ms";
        }
    }

    /// <summary>
    /// Summary information about a single HTTP transaction for display in statistics.
    /// Contains only essential fields for performance reasons.
    /// </summary>
    public class HttpTransactionSummary
    {
        public string RequestId { get; set; }
        public string Method { get; set; }
        public string Url { get; set; }
        public int StatusCode { get; set; }
        public long DurationMs { get; set; }
        public DateTime Timestamp { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Statistics for a single endpoint.
    /// </summary>
    public class EndpointStatistic
    {
        public string Endpoint { get; set; }
        public int RequestCount { get; set; }
        public double AverageDurationMs { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public decimal SuccessRate { get; set; }
    }
}
