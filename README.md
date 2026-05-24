# NET-Tracker 🚀

[![NuGet](https://img.shields.io/nuget/v/NetTracker.Core.svg)](https://www.nuget.org/packages/NetTracker.Core)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**NET-Tracker** is a high-performance, plug-and-play HTTP request/response tracking library for ASP.NET Core 8.0. It provides a real-time dashboard to monitor, search, and analyze every HTTP transaction handled by your application with **zero boilerplate**.

---

## 🌟 Why NET-Tracker?

In modern distributed systems, understanding what's happening inside your HTTP pipeline is critical. NET-Tracker captures the full lifecycle of every request—including headers, bodies, and timing—without slowing down your application.

- **Zero Configuration:** Drop it in and it just works.
- **Performance First:** Uses `System.Threading.Channels` for non-blocking, async logging.
- **Enterprise Ready:** Built-in resilience with circuit breakers and retries.
- **Security Focused:** Automatic PII/sensitive data masking for JSON payloads.

---

## ✨ Key Features

- 📊 **Real-time Dashboard:** A modern UI to browse and inspect transactions as they happen.
- 🔍 **Advanced Filtering:** Search by URL, Method, Status Code, IP, or even search within request bodies.
- ⚡ **Async Logging Pipeline:** Decoupled writes ensure that logging never blocks your active requests.
- 🛡️ **Resilience Decorators:** Gracefully handles database downtime using retry patterns.
- 🔐 **Data Masking:** Automatically masks passwords, tokens, and keys (configurable patterns).
- 🧹 **Retention Policies:** Automatic cleanup keeps your database size under control.
- 📈 **Aggregated Analytics:** View P95/P99 latencies, top endpoints, and error distributions.

---

## 🚀 Quick Start (v3.0.3+)

### 1. Install Package
```bash
dotnet add package NetTracker.Core
```

### 2. Configure `appsettings.json`
Add the `NetTracker` block to your configuration:
```json
{
  "NetTracker": {
    "Enabled": true,
    "Storage": {
      "ConnectionString": "Server=localhost;Database=TrackerDb;Trusted_Connection=True;"
    },
    "Retention": {
      "DaysToKeep": 30,
      "AutoCleanup": true
    }
  }
}
```

### 3. Register Services
Update your `Program.cs` to include the tracker:

```csharp
var builder = WebApplication.CreateBuilder(args);

// ✅ Register NET-Tracker
builder.Services.AddNetTracker(builder.Configuration);

var app = builder.Build();

// ✅ Add Middleware (Place before UseRouting)
app.UseNetTracker(app.Configuration);

app.MapControllers();
app.Run();
```

### 4. Access the Dashboard
Navigate to `/Tracker` in your browser to see your live traffic!

---

## 🏗️ Architecture Overview

NET-Tracker uses a decoupled decorator architecture to ensure maximum reliability and performance:

1.  **Middleware:** Intercepts the `HttpContext` and streams data to the queue.
2.  **Queued Logger (Singleton):** An in-memory buffer that prevents request blocking.
3.  **Resilient Decorator:** Wraps the storage logic with retries and a circuit breaker.
4.  **SQL Logger (Scoped):** Persists the data to SQL Server using Entity Framework Core.

---

## 📋 Requirements

- **Framework:** .NET 8.0+
- **Database:** SQL Server (LocalDB, Express, or Azure SQL)
- **UI:** Compatible with all modern browsers (Chrome, Edge, Firefox, Safari)

---

## 🔗 Project Links

- **NuGet Package:** [NetTracker.Core](https://www.nuget.org/packages/NetTracker.Core)
- **GitHub Repo:** [MohamedSaber2004/NET-Tracker](https://github.com/MohamedSaber2004/NET-Tracker)

---

## 📄 License

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for details.
