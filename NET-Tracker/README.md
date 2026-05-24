# NET-Tracker — HTTP Request/Response Monitoring Dashboard for ASP.NET Core

[![NuGet](https://img.shields.io/nuget/v/NetTracker.Core.svg)](https://www.nuget.org/packages/NetTracker.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

**NET-Tracker** is a lightweight, plug-and-play HTTP tracking library for ASP.NET Core. Drop it into any existing project to get a **beautiful real-time dashboard** that lets you monitor, search, and analyze every HTTP request and response your application handles — with zero boilerplate.

---

## ✨ Features

- 📊 **Live Dashboard** — Interactive UI to browse, filter, and inspect all HTTP transactions
- 🔍 **Full-text Search** — Search by URL, method, status code, IP address, user agent, and more
- ⚡ **Async Queue Logging** — Non-blocking fire-and-forget writes keep your app fast
- 🛡️ **Resilient** — Built-in retry + circuit-breaker pattern protects against DB failures
- 🧹 **Auto Cleanup** — Configurable log retention to prevent unbounded database growth
- 🔐 **Sensitive Data Masking** — Automatically masks passwords, tokens, API keys in logs
- 📈 **Analytics** — Top endpoints, slowest requests, error rates, and percentile breakdowns
- 🩺 **Health Endpoint** — `/api/health` reports database and logging service status
- ⚙️ **Config-first** — Everything controlled from `appsettings.json` with no code changes needed

---

## 🚀 Quick Start

### 1. Install the Package

```bash
dotnet add package NetTracker.Core
```

### 2. Add Configuration to `appsettings.json`

```json
{
  "NetTracker": {
    "Enabled": true,
    "LogRequestBody": true,
    "LogResponseBody": true,
    "LogHeaders": true,
    "Storage": {
      "Type": "Database",
      "ConnectionString": "Server=localhost;Database=MyAppLogs;Integrated Security=true;TrustServerCertificate=true;"
    },
    "Retention": {
      "DaysToKeep": 30,
      "AutoCleanup": true
    },
    "Performance": {
      "UseAsyncLogging": true,
      "MaxQueueSize": 10000,
      "EnableCaching": true
    },
    "ExcludePaths": [
      "/health",
      "/swagger",
      "/favicon.ico"
    ]
  }
}
```

### 3. Register Services in `Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

// ✅ Add NET-Tracker — one line!
builder.Services.AddNetTracker(builder.Configuration);

var app = builder.Build();

// ✅ Add NET-Tracker Middleware (add before UseRouting)
app.UseNetTracker(app.Configuration);

app.UseRouting();
app.MapControllers();
app.Run();
```

### 4. Open the Dashboard

Navigate to **`your-url`** in your browser.

---

## ⚙️ Full Configuration Reference

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | `bool` | `true` | Enable or disable all tracking |
| `LogRequestBody` | `bool` | `true` | Log incoming request bodies |
| `LogResponseBody` | `bool` | `true` | Log outgoing response bodies |
| `LogHeaders` | `bool` | `true` | Log request and response headers |
| `LogBodyOnlyOnErrors` | `bool` | `false` | Save bodies only on error responses (saves storage) |
| `MaxBodySize` | `int` | `1048576` | Max body size in bytes (default 1 MB) |
| `IncludeQueryString` | `bool` | `true` | Include query string in logged URLs |
| `LogSensitiveData` | `bool` | `false` | Log sensitive data without masking |
| `ExcludePaths` | `string[]` | `["/health", ...]` | Paths to exclude from logging |
| `ExcludeMethods` | `string[]` | `[]` | HTTP methods to exclude (e.g. `OPTIONS`) |
| `Storage.ConnectionString` | `string` | — | SQL Server connection string |
| `Retention.DaysToKeep` | `int` | `30` | How many days to keep logs |
| `Retention.AutoCleanup` | `bool` | `true` | Run automatic cleanup |
| `Performance.UseAsyncLogging` | `bool` | `true` | Non-blocking async queue writes |
| `Performance.MaxQueueSize` | `int` | `10000` | Max in-memory queue size |
| `Performance.EnableCaching` | `bool` | `true` | Cache read queries in memory |

---

## 📸 Dashboard Preview

The dashboard provides:
- **Overview metrics** — Total requests, error rate, avg duration, active users
- **Transactions table** — Paginated, searchable list of all HTTP calls
- **Request detail modal** — Full headers, body, response, timing for any transaction  
- **Analytics tab** — Top endpoints and slowest requests charts
- **Performance tab** — P50/P95/P99 latency percentiles
- **Health tab** — Live database and service health status

---

## 🏗️ Architecture

```
Request → HttpRequestResponseLoggingMiddleware
              ↓
        QueuedHttpTransactionLogger   (async queue, Singleton)
              ↓ (background drain)
        ResilientHttpTransactionLogger (retry + circuit-breaker)
              ↓
        HttpTransactionLogger          (EF Core → SQL Server)
```

---

## 📋 Requirements

- .NET 8.0+
- ASP.NET Core MVC
- SQL Server (LocalDB, Express, or full)

---

## 🔗 Links

- **GitHub:** [https://github.com/MohamedSaber2004/NET-Tracker](https://github.com/MohamedSaber2004/NET-Tracker)
- **NuGet:** [https://www.nuget.org/packages/NetTracker.Core](https://www.nuget.org/packages/NetTracker.Core)

---

## 📄 License

This project is licensed under the **MIT License** — free to use, modify, and distribute.
