using System;
using System.Collections.Generic;

namespace NET_Tracker.Configuration
{
    /// <summary>
    /// Configuration options for HTTP request/response logging system.
    /// Binds to appsettings.json under "HttpRequestResponseLogging" section.
    /// </summary>
    public class HttpLoggingOptions
    {
        /// <summary>
        /// Enable or disable the entire HTTP logging system.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Enable or disable the built-in dashboard UI.
        /// Set to false on public hosting if you only want to use the logging system without exposing logs.
        /// </summary>
        public bool EnableDashboardUI { get; set; } = true;

        /// <summary>
        /// Log the request body content.
        /// Set to false for large binary uploads to reduce storage.
        /// </summary>
        public bool LogRequestBody { get; set; } = true;

        /// <summary>
        /// Log the response body content.
        /// Set to false for large binary downloads to reduce storage.
        /// </summary>
        public bool LogResponseBody { get; set; } = true;

        /// <summary>
        /// Log HTTP request and response headers.
        /// </summary>
        public bool LogHeaders { get; set; } = true;

        /// <summary>
        /// Maximum body size to log (in bytes).
        /// Bodies larger than this will be truncated.
        /// Default: 1 MB
        /// </summary>
        public int MaxBodySize { get; set; } = 1024 * 1024; // 1 MB

        /// <summary>
        /// HTTP paths to exclude from logging.
        /// Useful for excluding health checks, metrics, static files, etc.
        /// </summary>
        public List<string> ExcludePaths { get; set; } = new()
        {
            "/health",
            "/metrics",
            "/swagger",
            "/api/swagger"
        };

        /// <summary>
        /// HTTP methods to exclude from logging (optional).
        /// Useful for excluding HEAD or OPTIONS requests if needed.
        /// </summary>
        public List<string> ExcludeMethods { get; set; } = new();

        /// <summary>
        /// Include query string parameters in logged URLs.
        /// Set to false if query strings contain sensitive data.
        /// </summary>
        public bool IncludeQueryString { get; set; } = true;

        /// <summary>
        /// Log sensitive data (passwords, tokens, PII, etc.) without masking.
        /// Should be FALSE in production for security/compliance.
        /// </summary>
        public bool LogSensitiveData { get; set; } = false;

        /// <summary>
        /// Regular expression patterns or keywords to identify sensitive data.
        /// Matching fields will be masked as "***MASKED***".
        /// </summary>
        public List<string> SensitivePatterns { get; set; } = new()
        {
            "password",
            "apikey",
            "api_key",
            "token",
            "authorization",
            "ssn",
            "creditcard",
            "credit_card",
            "secret",
            "private_key",
            "privatekey"
        };

        /// <summary>
        /// Storage configuration for HTTP transaction logs.
        /// </summary>
        public StorageOptions Storage { get; set; } = new();

        /// <summary>
        /// Data retention configuration.
        /// </summary>
        public RetentionOptions Retention { get; set; } = new();

        /// <summary>
        /// Performance tuning options.
        /// </summary>
        public PerformanceOptions Performance { get; set; } = new();

        /// <summary>
        /// Validates the configuration options.
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (MaxBodySize <= 0)
                errors.Add("MaxBodySize must be greater than 0");

            if (MaxBodySize > 100 * 1024 * 1024) // 100 MB
                errors.Add("MaxBodySize should not exceed 100 MB");

            if (Storage == null)
                errors.Add("Storage configuration is required");
            else if (string.IsNullOrWhiteSpace(Storage.Type))
                errors.Add("Storage.Type is required");

            if (Retention == null)
                errors.Add("Retention configuration is required");
            else if (Retention.DaysToKeep < 1)
                errors.Add("Retention.DaysToKeep must be at least 1");

            return errors.Count == 0;
        }
    }

    /// <summary>
    /// Storage configuration for HTTP transaction logs.
    /// </summary>
    public class StorageOptions
    {
        /// <summary>
        /// Storage type: "Database", "FileSystem", or "AzureBlobStorage"
        /// </summary>
        public string Type { get; set; } = "Database";

        /// <summary>
        /// Database connection string (used when Type = "Database")
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// File system directory for logs (used when Type = "FileSystem")
        /// Example: "Logs/Http" or "/var/log/myapp/http"
        /// </summary>
        public string LogsDirectory { get; set; } = "Logs/Http";

        /// <summary>
        /// Azure Blob Storage connection string (used when Type = "AzureBlobStorage")
        /// </summary>
        public string AzureBlobConnection { get; set; }

        /// <summary>
        /// Azure Blob Storage container name
        /// </summary>
        public string AzureBlobContainer { get; set; } = "http-logs";

        /// <summary>
        /// Database provider type (when using Database storage)
        /// Options: "SqlServer", "PostgreSQL", "MySql", "Sqlite"
        /// </summary>
        public string DatabaseProvider { get; set; } = "SqlServer";

        /// <summary>
        /// Whether to use a separate logging database to avoid impacting application DB performance.
        /// </summary>
        public bool UseSeparateDatabase { get; set; } = false;
    }

    /// <summary>
    /// Data retention and cleanup configuration.
    /// </summary>
    public class RetentionOptions
    {
        /// <summary>
        /// Number of days to keep HTTP transaction logs.
        /// Default: 30 days
        /// </summary>
        public int DaysToKeep { get; set; } = 30;

        /// <summary>
        /// Cron expression for automatic cleanup schedule.
        /// Default: 2 AM daily (0 2 * * *)
        /// </summary>
        public string CleanupSchedule { get; set; } = "0 2 * * *";

        /// <summary>
        /// Whether to automatically cleanup old logs.
        /// </summary>
        public bool AutoCleanup { get; set; } = true;

        /// <summary>
        /// Archive old logs instead of deleting them.
        /// </summary>
        public bool ArchiveOldLogs { get; set; } = false;

        /// <summary>
        /// Directory to archive old logs (if ArchiveOldLogs is true)
        /// </summary>
        public string ArchiveDirectory { get; set; } = "Logs/Http/Archive";
    }

    /// <summary>
    /// Performance tuning options.
    /// </summary>
    public class PerformanceOptions
    {
        /// <summary>
        /// Use asynchronous/queued logging to avoid blocking requests.
        /// Recommended for high-traffic applications.
        /// </summary>
        public bool UseAsyncLogging { get; set; } = true;

        /// <summary>
        /// Maximum queue size for async logging.
        /// If queue exceeds this size, old entries are discarded.
        /// </summary>
        public int MaxQueueSize { get; set; } = 10000;

        /// <summary>
        /// Enable caching of frequently accessed transactions.
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// Cache duration in minutes.
        /// </summary>
        public int CacheDurationMinutes { get; set; } = 60;

        /// <summary>
        /// Enable compression of body content for storage.
        /// Reduces storage size but adds CPU overhead.
        /// </summary>
        public bool CompressBodyContent { get; set; } = true;

        /// <summary>
        /// Batch database inserts for better performance.
        /// </summary>
        public bool UseBatchInserts { get; set; } = true;

        /// <summary>
        /// Batch size for database inserts.
        /// </summary>
        public int BatchInsertSize { get; set; } = 100;
    }
}
