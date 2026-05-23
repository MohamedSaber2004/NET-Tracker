using Microsoft.EntityFrameworkCore;
using NET_Tracker.Data;
using NET_Tracker.Extensions;

namespace NET_Tracker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            // ✅ Add NET-Tracker HTTP Request/Response Logging System
            builder.Services.AddNetTracker(builder.Configuration);

            // ✅ Apply database migrations automatically
            using (var serviceProvider = builder.Services.BuildServiceProvider())
            {
                using (var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>())
                {
                    dbContext.Database.Migrate();
                }
            }

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            // ✅ Add NET-Tracker Middleware (must be early in pipeline)
            app.UseNetTracker(app.Configuration);

            app.UseRouting();

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
