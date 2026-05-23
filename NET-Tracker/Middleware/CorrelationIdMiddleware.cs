namespace NET_Tracker.Middleware
{
    /// <summary>
    /// Middleware that ensures every HTTP request has a correlation ID for distributed tracing.
    /// Phase 11: Distributed Tracing Integration.
    ///
    /// Behavior:
    /// - Reads X-Correlation-ID from incoming request if present (preserves caller's ID)
    /// - Generates a new GUID if no correlation ID exists
    /// - Stores the ID in HttpContext.Items for downstream access
    /// - Adds X-Correlation-ID to the response headers
    ///
    /// This enables tracing a single operation across multiple microservices.
    /// </summary>
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;

        /// <summary>
        /// The HTTP header name used for correlation IDs.
        /// Industry standard: X-Correlation-ID or X-Request-ID.
        /// </summary>
        public const string CorrelationIdHeader = "X-Correlation-ID";

        /// <summary>
        /// The HttpContext.Items key where the correlation ID is stored.
        /// Access in controllers: HttpContext.Items["CorrelationId"]
        /// </summary>
        public const string CorrelationIdContextKey = "CorrelationId";

        public CorrelationIdMiddleware(
            RequestDelegate next,
            ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Processes the request: extract or generate correlation ID, propagate it.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            // Extract or generate correlation ID
            var correlationId = ExtractOrGenerateCorrelationId(context);

            // Store in context for downstream middleware/controllers
            context.Items[CorrelationIdContextKey] = correlationId;

            // Add to response headers (so callers can correlate their request)
            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
                {
                    context.Response.Headers[CorrelationIdHeader] = correlationId;
                }
                return Task.CompletedTask;
            });

            // Add to logging scope for structured logging correlation
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                [CorrelationIdContextKey] = correlationId
            }))
            {
                await _next(context);
            }
        }

        /// <summary>
        /// Extracts correlation ID from request headers or generates a new one.
        /// Validates format to prevent header injection attacks.
        /// </summary>
        private string ExtractOrGenerateCorrelationId(HttpContext context)
        {
            // Check for existing correlation ID in common header names
            var headerNames = new[]
            {
                CorrelationIdHeader,
                "X-Request-ID",
                "X-Trace-ID",
                "TraceId"
            };

            foreach (var headerName in headerNames)
            {
                if (context.Request.Headers.TryGetValue(headerName, out var existingId))
                {
                    var id = existingId.ToString().Trim();
                    // Validate: must be non-empty and reasonable length (prevent injection)
                    if (!string.IsNullOrWhiteSpace(id) && id.Length <= 128)
                    {
                        _logger.LogDebug("Using existing correlation ID from header {Header}: {CorrelationId}",
                            headerName, id);
                        return id;
                    }
                }
            }

            // Generate a new correlation ID
            var newId = Guid.NewGuid().ToString();
            _logger.LogDebug("Generated new correlation ID: {CorrelationId}", newId);
            return newId;
        }
    }

    /// <summary>
    /// Extension method to easily add correlation ID middleware to the pipeline.
    /// </summary>
    public static class CorrelationIdMiddlewareExtensions
    {
        /// <summary>
        /// Adds the CorrelationIdMiddleware to the application pipeline.
        /// Should be added very early in the pipeline, before logging middleware.
        ///
        /// Usage:
        ///   app.UseCorrelationId();
        /// </summary>
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        {
            return app.UseMiddleware<CorrelationIdMiddleware>();
        }
    }
}
