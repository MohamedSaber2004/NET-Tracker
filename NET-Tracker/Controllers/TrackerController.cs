using Microsoft.AspNetCore.Mvc;
using NET_Tracker.Services;
using NET_Tracker.Services.Interfaces;

namespace NET_Tracker.Controllers
{
    /// <summary>
    /// MVC controller for the HTTP Request/Response Logger Dashboard.
    /// Serves the interactive monitoring dashboard UI.
    /// </summary>
    public class TrackerController : Controller
    {
        private readonly IHttpTransactionLogger _logger;
        private readonly ILogger<TrackerController> _appLogger;

        public TrackerController(
            IHttpTransactionLogger logger,
            ILogger<TrackerController> appLogger)
        {
            _logger = logger;
            _appLogger = appLogger;
        }

        /// <summary>
        /// Renders the main dashboard view.
        /// All data is loaded via AJAX from the API endpoints.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "HTTP Request/Response Logger Dashboard";
            return await Task.FromResult(View());
        }
    }
}
