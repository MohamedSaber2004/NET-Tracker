using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using NET_Tracker.Configuration;

namespace NET_Tracker.Filters
{
    /// <summary>
    /// Action filter to restrict access to the dashboard UI and APIs based on configuration.
    /// If EnableDashboardUI is false, all requests to the dashboard will return 404.
    /// </summary>
    public class DashboardAccessFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var options = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<HttpLoggingOptions>>().Value;

            if (!options.EnableDashboardUI)
            {
                // Return 404 to keep the application behavior consistent with a non-existent resource
                context.Result = new NotFoundResult();
                return;
            }

            // Check if remote access is allowed
            if (!options.AllowRemoteDashboardAccess)
            {
                var host = context.HttpContext.Request.Host.Host;
                var isLocal = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                              host.Equals("127.0.0.1") ||
                              host.Equals("::1");

                if (!isLocal)
                {
                    // Return 404 for hosted URL when remote access is denied
                    context.Result = new NotFoundResult();
                    return;
                }
            }

            base.OnActionExecuting(context);
        }
    }
}
