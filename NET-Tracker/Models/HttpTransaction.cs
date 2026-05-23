using System;
using System.Collections.Generic;

namespace NET_Tracker.Models
{
    /// <summary>
    /// Represents a complete HTTP request/response transaction log entry.
    /// This entity captures the full lifecycle of an HTTP interaction for debugging and monitoring.
    /// </summary>
    public class HttpTransaction
    {
        /// <summary>
        /// Unique identifier for the HTTP transaction record.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Request ID used for correlation across logs.
        /// This ID is propagated through the entire request/response cycle.
        /// </summary>
        public string? RequestId { get; set; }

        #region Request Details

        /// <summary>
        /// HTTP method (GET, POST, PUT, DELETE, PATCH, etc.)
        /// </summary>
        public string? Method { get; set; }

        /// <summary>
        /// Complete URL including scheme, host, path, and query string.
        /// Example: https://localhost:5001/api/users?filter=active
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// Query string parameters extracted from the URL.
        /// </summary>
        public string? QueryString { get; set; }

        /// <summary>
        /// HTTP request headers as key-value pairs.
        /// Stored as JSON string for flexibility.
        /// </summary>
        public string? RequestHeaders { get; set; }

        /// <summary>
        /// Raw request body content.
        /// Can contain JSON, XML, form data, or other content types.
        /// </summary>
        public string? RequestBody { get; set; }

        /// <summary>
        /// Content-Type header value (e.g., "application/json", "application/x-www-form-urlencoded").
        /// </summary>
        public string? ContentType { get; set; }

        /// <summary>
        /// Size of the request body in bytes.
        /// </summary>
        public int RequestSize { get; set; }

        #endregion

        #region Response Details

        /// <summary>
        /// HTTP status code (200, 201, 400, 401, 404, 500, etc.)
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// HTTP response headers as key-value pairs.
        /// Stored as JSON string for flexibility.
        /// </summary>
        public string? ResponseHeaders { get; set; }

        /// <summary>
        /// Raw response body content returned to the client.
        /// </summary>
        public string? ResponseBody { get; set; }

        /// <summary>
        /// Size of the response body in bytes.
        /// </summary>
        public int ResponseSize { get; set; }

        #endregion

        #region Metadata & Performance

        /// <summary>
        /// Server timestamp when the request was received.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Total time taken to process the request in milliseconds.
        /// This includes middleware processing, controller execution, and response preparation.
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Client IP address that made the request.
        /// </summary>
        public string? IpAddress { get; set; }

        /// <summary>
        /// User-Agent header from the request.
        /// Identifies the client application/browser.
        /// </summary>
        public string? UserAgent { get; set; }

        /// <summary>
        /// Authenticated user ID if available.
        /// Null for unauthenticated requests.
        /// </summary>
        public string? UserId { get; set; }

        #endregion

        #region Status & Error Information

        /// <summary>
        /// Indicates whether the request was processed successfully.
        /// True if status code < 400 and no exception occurred.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if an exception occurred during processing.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Stack trace of the exception if one occurred.
        /// Useful for debugging production issues.
        /// </summary>
        public string? StackTrace { get; set; }

        #endregion

        /// <summary>
        /// Creates a summary representation of the transaction.
        /// </summary>
        public override string ToString()
        {
            var status = Success ? "✓" : "✗";
            return $"[{status}] {Method} {Url} -> {StatusCode} ({DurationMs}ms)";
        }
    }
}
