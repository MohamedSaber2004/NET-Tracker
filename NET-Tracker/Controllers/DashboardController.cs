using Microsoft.AspNetCore.Mvc;
using NET_Tracker.Services;
using NET_Tracker.Services.Interfaces;

namespace NET_Tracker.Controllers
{
    /// <summary>
    /// MVC controller for the HTTP Request/Response Logger Dashboard.
    /// Serves the interactive monitoring dashboard UI.
    /// </summary>
    public class DashboardController : Controller
    {
        private readonly IHttpTransactionLogger _logger;
        private readonly ILogger<DashboardController> _appLogger;

        public DashboardController(
            IHttpTransactionLogger logger,
            ILogger<DashboardController> appLogger)
        {
            _logger = logger;
            _appLogger = appLogger;
        }

        /// <summary>
        /// Renders the main dashboard view.
        /// All data is loaded via AJAX from the API endpoints.
        /// </summary>
        public IActionResult Index()
        {
            ViewData["Title"] = "HTTP Request/Response Logger Dashboard";
            return View();
        }
    }
}
