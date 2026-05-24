# NET-Tracker Project Context

## Project Overview
**NET-Tracker** is a high-performance, plug-and-play HTTP request/response tracking library for ASP.NET Core 8.0. It provides a real-time dashboard to monitor, search, and analyze every HTTP transaction handled by the application with zero boilerplate.

### Main Technologies
- **Target Framework:** .NET 8.0
- **Web Framework:** ASP.NET Core MVC
- **ORM:** Entity Framework Core 8.0
- **Database:** SQL Server
- **Async Processing:** `System.Threading.Channels` for non-blocking logging.
- **Resilience:** Circuit breaker and retry patterns for database protection.
- **Frontend:** ASP.NET Core MVC Views with Bootstrap and jQuery.

---

## Building and Running

### Prerequisites
- .NET 8.0 SDK
- SQL Server (LocalDB or full instance)

### Key Commands
- **Build:** `dotnet build` from the root directory.
- **Run:** `dotnet run --project NET-Tracker/NET-Tracker.csproj`
- **Test:** `dotnet test` from the root directory.
- **Database Migrations:** Applied automatically on startup in `Program.cs`. To add a new migration:
  `dotnet ef migrations add <MigrationName> --project NET-Tracker --startup-project NET-Tracker`

### Accessing the Dashboard
Once running, the dashboard is typically accessible at `/Tracker` (default route) or as configured in `appsettings.json`.

---

## Architecture and Design

### Logging Pipeline
1. **`HttpRequestResponseLoggingMiddleware`**: Intercepts requests/responses, captures headers, bodies, and timing.
2. **`QueuedHttpTransactionLogger` (Singleton)**: Uses a `Channel<HttpTransaction>` to decouple logging from the request thread.
3. **`ResilientHttpTransactionLogger` (Decorator)**: Implements retries and a circuit breaker to handle database failures gracefully.
4. **`HttpTransactionLogger` (Scoped)**: Performs the actual EF Core persistence to SQL Server.

### Key Features
- **Async Queueing:** Fire-and-forget logging prevents performance impact on active requests.
- **Data Masking:** Automatically masks sensitive JSON keys (e.g., passwords, tokens).
- **Auto Cleanup:** `HttpLogsCleanupService` runs in the background to enforce retention policies (days to keep and max rows).
- **Selective Projection:** Search queries only fetch necessary columns to reduce database and network load.
- **Correlation ID:** Uses `X-Request-ID` to track transactions throughout their lifecycle.

---

## Development Conventions

### Coding Style
- **Decorator Pattern:** Extensively used for logging services to add resilience and caching layers.
- **Dependency Injection:** Services are registered via extension methods in `NET_Tracker.Extensions.HttpLoggingExtensions`.
- **Async/Await:** All I/O operations (database, streams) are fully asynchronous.
- **Separation of Concerns:** Distinct layers for Middleware, Services (Interfaces/Implementations), Models, and Data.

### Testing Practices
- **Unit Tests:** Located in `NET-Tracker.Tests`.
- **Mocking:** Uses `Moq` for service abstraction testing.
- **Decorator Testing:** Specifically tests the resilience and caching logic in isolation.

### Directory Structure
- `NET-Tracker/`: Main project directory.
  - `Middleware/`: Core logging middleware.
  - `Services/`: Implementation of logging logic and decorators.
  - `Data/`: EF Core DbContext and configurations.
  - `Models/`: Data transfer objects and filter entities.
  - `Views/`: Razor views for the dashboard.
- `NET-Tracker.Tests/`: XUnit test project.

---

## ⚡ Advanced Workflows & Shortcuts

### 1. Performance Testing & Optimization
To test the performance of any endpoint or the dashboard itself:
- **Shortcut:** Call `GET /api/statistics/performance` to get real-time P50, P95, and P99 latency metrics.
- **UI Benchmarking:** Use browser DevTools to monitor the size of the `search` API response. If JSON payloads exceed 1MB, verify that `Response Compression` is active in `Program.cs`.
- **Database Bottlenecks:** Inspect `HttpTransactionLogger.SearchAsync`. Ensure that any new filters are supported by indexes in `HttpTransactionConfiguration.cs`.
- **Optimization Rule:** Always use `.Select()` projection in `SearchAsync` to avoid fetching `nvarchar(max)` body columns for list views.

### 2. Security Auditing
To audit the security of the tracker or the host application:
- **PII/Sensitive Data:** Verify `MaskSensitiveData` in `HttpTransactionLogger.cs`. Ensure all patterns (e.g., "password", "token", "secret", "key", "authorization") are included in `HttpLoggingOptions.SensitivePatterns`.
- **Config Audit:** Ensure `LogSensitiveData` is set to `false` in `appsettings.json` for production environments.
- **Access Control:** Check `HttpTransactionsController` and `StatisticsController` for `[Authorize]` attributes. The dashboard should not be exposed publicly without auth.
- **Path Exclusion:** Verify that health checks and auth endpoints are listed in `ExcludePaths` to prevent logging credentials.

### 3. Implementation Workflow: New Features
When asked to "Make a new feature", follow this execution roadmap:
1.  **Research:** Map the feature to existing Models (e.g., `HttpTransaction`) or create a new one.
2.  **Logic:** Implement the business logic in a new or existing Service. Prefer **Decorators** for cross-cutting concerns (e.g., caching, resilience).
3.  **API:** Add a specialized endpoint in `HttpTransactionsController` or a new controller.
4.  **UI:** Update Razor views in `Views/Tracker/` or `Views/Statistics/`. Use `site.js` for AJAX-based updates.
5.  **Validation:** Add a new test class in `NET-Tracker.Tests` to verify the feature end-to-end.

### 4. Refactoring Guidelines
- **Decorator Chain:** When adding global logic, implement `IHttpTransactionLogger` and wrap the existing chain in `HttpLoggingExtensions.cs`.
- **Async Consistency:** Ensure `CancellationToken` is passed through all service methods down to the EF Core `ToListAsync()` calls.
- **DRY Logic:** Move common query filters in `SearchAsync` to reusable `IQueryable` extension methods.
- **Projection:** If a new view is added, create a dedicated DTO and use `.Select()` or AutoMapper `.ProjectTo()` to minimize data transfer.

### 5. Roadmap for Prompt Generation
Use these templates to generate prompts for specific types of feature development:

- **New Storage Provider:** `"Implement a PostgreSQL storage provider for NET-Tracker. Create a new `PostgreSqlHttpTransactionLogger` implementing `IHttpTransactionLogger`, update `HttpLoggingExtensions` to support a 'Postgres' storage type, and provide the necessary DDL/Migration logic."`
- **Advanced Analytics:** `"Add a 'Trending Endpoints' feature. Implement a new method in `HttpTransactionLogger` to calculate endpoint volume change over the last 24h vs previous 24h. Add a corresponding API endpoint and a chart in the Statistics view using Chart.js."`
- **Real-time Notifications:** `"Implement Slack/Teams notifications for 500 errors. Create a `NotificationDecorator` for `IHttpTransactionLogger` that checks for `Success == false` and status >= 500, then sends an async webhook request."`
