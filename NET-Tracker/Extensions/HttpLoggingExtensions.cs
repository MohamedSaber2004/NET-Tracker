using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NET_Tracker.Configuration;
using NET_Tracker.Data;
using NET_Tracker.Middleware;
using NET_Tracker.Services;
using NET_Tracker.Services.Interfaces;

namespace NET_Tracker.Extensions
{
    /// <summary>
    /// Extension methods for registering HTTP request/response logging services.
    /// These methods are used in Program.cs to configure the logging system.
    ///
    /// Decorator chain (innermost to outermost):
    ///   HttpTransactionLogger (DB storage)
    ///     → ResilientHttpTransactionLogger  (Phase 10: retry + circuit-breaker, always on)
    ///     → CachedHttpTransactionLogger     (Phase 8: read cache,  if Performance.EnableCaching = true)
    ///     → QueuedHttpTransactionLogger     (Phase 8: fire-and-forget writes, if Performance.UseAsyncLogging = true)
    /// </summary>
    public static class HttpLoggingExtensions
    {
        /// <summary>
        /// Adds HTTP request/response logging services to the dependency injection container.
        ///
        /// Usage in Program.cs:
        ///     builder.Services.AddHttpRequestResponseLogging(builder.Configuration);
        /// </summary>
        /// <param name="services">The service collection to register services with.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddHttpRequestResponseLogging(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            return AddNetTracker(services, configuration);
        }

        /// <summary>
        /// Adds NET-Tracker services to the dependency injection container.
        ///
        /// Usage in Program.cs:
        ///     builder.Services.AddNetTracker(builder.Configuration);
        /// </summary>
        public static IServiceCollection AddNetTracker(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // ── 1. Bind configuration ─────────────────────────────────────────────
            var configSection = configuration.GetSection("NetTracker").Exists() 
                ? configuration.GetSection("NetTracker") 
                : configuration.GetSection("HttpRequestResponseLogging");

            services.Configure<HttpLoggingOptions>(configSection);

            // ── 2. Resolve connection string ──────────────────────────────────────
            var storageConnectionString = configSection.GetSection("Storage:ConnectionString").Value;
            var defaultConnectionString = configuration.GetConnectionString("DefaultConnection");

            var connectionString = !string.IsNullOrWhiteSpace(storageConnectionString)
                ? storageConnectionString
                : defaultConnectionString;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "No connection string found. Please configure either " +
                    "'HttpRequestResponseLogging:Storage:ConnectionString' or " +
                    "'ConnectionStrings:DefaultConnection' in appsettings.json");
            }

            // ── 3. Register DbContext with retry logic ────────────────────────────
            services.AddDbContext<ApplicationDbContext>(optionsBuilder =>
            {
                optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null);
                });
            });

            // ── 4. IMemoryCache (shared) ──────────────────────────────────────────
            services.AddMemoryCache();

            // ── 5. Register the concrete base logger as Scoped ───────────────────
            //      Registered by CONCRETE TYPE so QueuedHttpTransactionLogger can
            //      resolve it directly via IServiceScopeFactory without going through
            //      the IHttpTransactionLogger chain and causing circular dependencies.
            services.AddScoped<HttpTransactionLogger>();

            // ── 6. Build the outermost decorator as a SINGLETON ──────────────────
            //
            //  WHY SINGLETON?
            //  QueuedHttpTransactionLogger starts a background Task in its constructor
            //  that drains a Channel and writes to the database. If it were Scoped,
            //  the background Task would outlive the request scope, causing the
            //  captured DbContext (Scoped) to be used AFTER it was disposed — which
            //  silently discards every transaction (exception is swallowed internally).
            //
            //  By making it Singleton and using IServiceScopeFactory, the background
            //  loop creates a FRESH scope (and therefore a FRESH DbContext) for every
            //  batch write, solving the lifetime mismatch completely.
            //
            //  Decorator chain for WRITES (middleware path):
            //    QueuedHttpTransactionLogger (Singleton)
            //      → background loop creates fresh scope
            //        → HttpTransactionLogger (fresh Scoped) → DB ✅
            //
            //  Decorator chain for READS (controller path):
            //    QueuedHttpTransactionLogger (Singleton)
            //      → creates fresh scope
            //        → HttpTransactionLogger (fresh Scoped) → DB ✅
            //
            services.AddSingleton<QueuedHttpTransactionLogger>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<HttpLoggingOptions>>().Value;
                var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
                var queuedLogger = sp.GetRequiredService<ILogger<QueuedHttpTransactionLogger>>();

                return new QueuedHttpTransactionLogger(
                    scopeFactory,
                    queuedLogger,
                    opts.Performance.MaxQueueSize);
            });

            // ── 7. Bind IHttpTransactionLogger to the Singleton outermost decorator ─
            services.AddSingleton<IHttpTransactionLogger>(sp =>
                sp.GetRequiredService<QueuedHttpTransactionLogger>());

            // ── 8. Background cleanup hosted service (log retention) ───────────────
            services.AddHostedService<HttpLogsCleanupService>();

            return services;
        }


        /// <summary>
        /// Adds the HTTP request/response logging middleware to the pipeline.
        /// Must be called early in the middleware pipeline, before other middleware.
        ///
        /// Usage in Program.cs:
        ///     app.UseHttpRequestResponseLogging(app.Configuration);
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The application builder for chaining.</returns>
        public static IApplicationBuilder UseHttpRequestResponseLogging(
            this IApplicationBuilder app,
            IConfiguration configuration)
        {
            return UseNetTracker(app, configuration);
        }

        /// <summary>
        /// Adds the NET-Tracker logging middleware to the pipeline.
        /// Must be called early in the middleware pipeline, before other middleware.
        ///
        /// Usage in Program.cs:
        ///     app.UseNetTracker(app.Configuration);
        /// </summary>
        public static IApplicationBuilder UseNetTracker(
            this IApplicationBuilder app,
            IConfiguration configuration)
        {
            var configSection = configuration.GetSection("NetTracker").Exists() 
                ? configuration.GetSection("NetTracker") 
                : configuration.GetSection("HttpRequestResponseLogging");

            var options = configSection.Get<HttpLoggingOptions>();

            if (options?.Enabled != true)
            {
                return app;
            }

            // Phase 11: Correlation ID middleware FIRST (before logging middleware)
            app.UseCorrelationId();

            // HTTP request/response logging middleware
            app.UseMiddleware<HttpRequestResponseLoggingMiddleware>();

            return app;
        }
    }
}
