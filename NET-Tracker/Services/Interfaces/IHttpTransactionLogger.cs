using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NET_Tracker.Models;

namespace NET_Tracker.Services.Interfaces
{
    /// <summary>
    /// Interface for HTTP transaction logging service.
    /// Defines core operations for capturing, storing, and querying HTTP request/response logs.
    /// 
    /// Implementations can use different storage backends:
    /// - Database (SQL Server, PostgreSQL, etc.)
    /// - File System (JSON Lines, CSV)
    /// - Cloud Storage (Azure Blob Storage, AWS S3)
    /// </summary>
    public interface IHttpTransactionLogger
    {
        /// <summary>
        /// Generates a unique request ID for correlation across the request lifecycle.
        /// Should be called at the start of request processing in middleware.
        /// </summary>
        /// <returns>A unique request ID string (typically GUID).</returns>
        string GenerateRequestId();

        /// <summary>
        /// Logs a complete HTTP transaction after request processing.
        /// This method should be called by middleware after response is prepared.
        /// </summary>
        /// <param name="transaction">The HTTP transaction to log.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task LogTransactionAsync(HttpTransaction transaction);

        /// <summary>
        /// Retrieves a specific HTTP transaction by its request ID.
        /// </summary>
        /// <param name="requestId">The unique request ID to look up.</param>
        /// <returns>The transaction if found; null otherwise.</returns>
        Task<HttpTransaction> GetTransactionAsync(string requestId);

        /// <summary>
        /// Searches HTTP transactions using flexible filtering criteria.
        /// Supports pagination and sorting for large result sets.
        /// </summary>
        /// <param name="filter">The filter/search parameters.</param>
        /// <returns>A list of matching transactions, potentially limited by page size.</returns>
        Task<(List<HttpTransaction> Data, int TotalCount)> SearchAsync(HttpTransactionFilter filter);

        /// <summary>
        /// Gets aggregated statistics about HTTP transactions.
        /// Useful for dashboards and monitoring.
        /// </summary>
        /// <param name="startDate">Start of the time range (UTC).</param>
        /// <param name="endDate">End of the time range (UTC).</param>
        /// <returns>Statistics object with counts, averages, and summaries.</returns>
        Task<HttpTransactionStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// Deletes HTTP transaction logs older than the specified number of days.
        /// Used for data retention policies and cleanup.
        /// </summary>
        /// <param name="daysToKeep">Number of days of logs to retain.</param>
        /// <returns>The number of records deleted.</returns>
        Task<int> DeleteOldLogsAsync(int daysToKeep);

        /// <summary>
        /// Checks if the logging service is healthy and operational.
        /// </summary>
        /// <returns>True if the service is functioning properly.</returns>
        Task<bool> IsHealthyAsync();
    }
}
