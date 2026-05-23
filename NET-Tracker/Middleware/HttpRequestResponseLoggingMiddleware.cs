using Microsoft.Extensions.Options;
using NET_Tracker.Configuration;
using NET_Tracker.Data;
using NET_Tracker.Models;
using NET_Tracker.Services.Interfaces;
using System.Diagnostics;
using System.Text;

namespace NET_Tracker.Middleware
{
    /// <summary>
    /// Middleware for logging complete HTTP request/response transactions.
    /// Intercepts all HTTP requests and responses, capturing headers, bodies, and timing information.
    /// 
    /// Must be registered early in the middleware pipeline, before routing and authorization.
    /// </summary>
    public class HttpRequestResponseLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<HttpRequestResponseLoggingMiddleware> _logger;
        private readonly HttpLoggingOptions _options;

        public HttpRequestResponseLoggingMiddleware(
            RequestDelegate next,
            ILogger<HttpRequestResponseLoggingMiddleware> logger,
            IOptions<HttpLoggingOptions> options)
        {
            _next = next;
            _logger = logger;
            _options = options.Value;
        }

        /// <summary>
        /// Middleware invocation method. Processes the HTTP request and response.
        /// </summary>
        public async Task InvokeAsync(
            HttpContext context,
            IHttpTransactionLogger transactionLogger)
        {
            // Check if logging is enabled
            if (_options?.Enabled != true)
            {
                await _next(context);
                return;
            }

            // Check if path should be excluded
            if (IsPathExcluded(context.Request.Path))
            {
                await _next(context);
                return;
            }

            // Check if method should be excluded
            if (IsMethodExcluded(context.Request.Method))
            {
                await _next(context);
                return;
            }

            // ✅ STEP 1: Generate or retrieve Request ID
            var requestId = GetOrGenerateRequestId(context, transactionLogger);
            context.Items["RequestId"] = requestId;

            // Phase 11 ── Tag the current OpenTelemetry / W3C trace Activity
            var activity = Activity.Current;
            if (activity != null)
            {
                activity.SetTag("http.request_id", requestId);
                activity.SetTag("http.method", context.Request.Method);
                activity.SetTag("http.url",
                    $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}");
                activity.SetTag("http.target", context.Request.Path.ToString());
                activity.SetTag("http.flavor",
                    context.Request.Protocol?.Replace("HTTP/", "") ?? "1.1");
                activity.SetTag("net.peer.ip",
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            }


            // ✅ STEP 2: Enable request body reading
            context.Request.EnableBuffering();

            // ✅ STEP 3: Capture request data
            var requestData = await CaptureRequestAsync(context, requestId);

            // ✅ STEP 4: Start timer
            var stopwatch = Stopwatch.StartNew();

            // ✅ STEP 5: Capture original response stream
            var originalBodyStream = context.Response.Body;

            try
            {
                using (var responseBuffer = new MemoryStream())
                {
                    context.Response.Body = responseBuffer;

                    try
                    {
                        // ✅ STEP 6: Call next middleware
                        await _next(context);
                    }
                    catch (Exception ex)
                    {
                        // ✅ STEP 7: Capture exception details
                        stopwatch.Stop();

                        await LogTransactionAsync(
                            context,
                            requestData,
                            null, // No response body on exception
                            ex,
                            stopwatch.ElapsedMilliseconds,
                            transactionLogger);

                        // Re-throw to let other middleware/error handlers process
                        throw;
                    }

                    stopwatch.Stop();

                    // ✅ STEP 8: Read response body
                    responseBuffer.Position = 0;
                    var responseBody = await new StreamReader(responseBuffer).ReadToEndAsync();

                    // ✅ STEP 9: Log the complete transaction
                    await LogTransactionAsync(
                        context,
                        requestData,
                        responseBody,
                        null, // No exception
                        stopwatch.ElapsedMilliseconds,
                        transactionLogger);

                    // ✅ STEP 10: Add Request ID to response header
                    context.Response.Headers["X-Request-ID"] = requestId;

                    // ✅ STEP 11: Copy response to original stream
                    responseBuffer.Position = 0;
                    await responseBuffer.CopyToAsync(originalBodyStream);
                }
            }
            finally
            {
                // Ensure original stream is restored
                context.Response.Body = originalBodyStream;
            }
        }

        /// <summary>
        /// Gets or generates a request ID for correlation.
        /// </summary>
        private string GetOrGenerateRequestId(HttpContext context, IHttpTransactionLogger transactionLogger)
        {
            // Check if request already has X-Request-ID header
            if (context.Request.Headers.TryGetValue("X-Request-ID", out var existingId) && !string.IsNullOrEmpty(existingId))
            {
                return existingId.ToString();
            }

            // Generate new request ID
            return transactionLogger.GenerateRequestId();
        }

        /// <summary>
        /// Captures all request details from the HTTP context.
        /// </summary>
        private async Task<HttpTransaction> CaptureRequestAsync(HttpContext context, string requestId)
        {
            var request = context.Request;
            var requestBody = "";

            // Read request body
            if (request.ContentLength > 0 && _options.LogRequestBody)
            {
                try
                {
                    request.Body.Position = 0;
                    using (var reader = new StreamReader(request.Body, Encoding.UTF8, true, 1024, true))
                    {
                        requestBody = await reader.ReadToEndAsync();
                    }
                    request.Body.Position = 0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read request body for {RequestId}", requestId);
                }
            }

            // Extract query string
            var queryString = _options.IncludeQueryString 
                ? request.QueryString.ToString() 
                : "";

            // Build full URL
            var url = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}";

            return new HttpTransaction
            {
                Id = Guid.NewGuid(),
                RequestId = requestId,
                Method = request.Method,
                Url = url,
                QueryString = queryString,
                RequestHeaders = _options.LogHeaders ? CaptureHeaders(request.Headers) : null,
                RequestBody = _options.LogRequestBody ? requestBody : null,
                ContentType = request.ContentType,
                RequestSize = (int)(request.ContentLength ?? 0),
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = request.Headers["User-Agent"].ToString(),
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Logs the complete HTTP transaction after response is ready.
        /// </summary>
        private async Task LogTransactionAsync(
            HttpContext context,
            HttpTransaction requestData,
            string? responseBody,
            Exception? exception,
            long durationMs,
            IHttpTransactionLogger transactionLogger)
        {
            try
            {
                var transaction = requestData;

                // Add response details
                transaction.StatusCode = context.Response.StatusCode;
                transaction.ResponseHeaders = _options.LogHeaders ? CaptureHeaders(context.Response.Headers) : null;
                transaction.ResponseBody = _options.LogResponseBody ? responseBody : null;
                transaction.ResponseSize = string.IsNullOrEmpty(responseBody) ? 0 : Encoding.UTF8.GetByteCount(responseBody);

                // Add timing
                transaction.DurationMs = durationMs;

                // Add status and error info
                transaction.Success = context.Response.StatusCode < 400 && exception == null;
                transaction.ErrorMessage = exception?.Message;
                transaction.StackTrace = exception?.StackTrace;

                // Phase 11 ── Enrich existing Activity with response-time attributes
                var activity = Activity.Current;
                if (activity != null)
                {
                    activity.SetTag("http.status_code", context.Response.StatusCode);
                    activity.SetTag("http.duration_ms", durationMs);
                    activity.SetTag("http.success", transaction.Success);

                    // Follow OpenTelemetry semantic convention: mark 5xx as error
                    if (context.Response.StatusCode >= 500)
                    {
                        activity.SetStatus(ActivityStatusCode.Error,
                            exception?.Message ?? $"HTTP {context.Response.StatusCode}");
                    }
                    else if (exception != null)
                    {
                        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                        
                        var tags = new ActivityTagsCollection
                        {
                            { "exception.type", exception.GetType().FullName },
                            { "exception.message", exception.Message },
                            { "exception.stacktrace", exception.StackTrace }
                        };
                        activity.AddEvent(new ActivityEvent("exception", default, tags));
                    }
                }

                // Log the transaction
                await transactionLogger.LogTransactionAsync(transaction);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log transaction {RequestId}", requestData.RequestId);
            }
        }

        /// <summary>
        /// Captures HTTP headers as a JSON-formatted string.
        /// </summary>
        private string? CaptureHeaders(IHeaderDictionary headers)
        {
            try
            {
                var headerDict = headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value.ToArray()));
                return System.Text.Json.JsonSerializer.Serialize(headerDict);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture headers");
                return null;
            }
        }

        /// <summary>
        /// Checks if the request path should be excluded from logging.
        /// </summary>
        private bool IsPathExcluded(PathString path)
        {
            if (_options?.ExcludePaths == null || _options.ExcludePaths.Count == 0)
                return false;

            var pathValue = path.Value?.ToLowerInvariant() ?? "";

            return _options.ExcludePaths.Any(excludePath =>
                pathValue.StartsWith(excludePath.ToLowerInvariant()));
        }

        /// <summary>
        /// Checks if the HTTP method should be excluded from logging.
        /// </summary>
        private bool IsMethodExcluded(string method)
        {
            if (_options?.ExcludeMethods == null || _options.ExcludeMethods.Count == 0)
                return false;

            return _options.ExcludeMethods.Contains(method, StringComparer.OrdinalIgnoreCase);
        }
    }
}
