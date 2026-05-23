using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using NET_Tracker.Data;
using NET_Tracker.Extensions;
using System.IO.Compression;

namespace NET_Tracker
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ✅ Response Compression — reduces JSON payload sizes by ~60-80%
            // This is critical for the Transactions API which can return large JSON arrays.
            builder.Services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
                {
                    "application/json",
                    "application/javascript",
                    "text/css",
                    "text/html",
                    "text/plain"
                });
            });
            builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
                options.Level = CompressionLevel.Fastest);
            builder.Services.Configure<GzipCompressionProviderOptions>(options =>
                options.Level = CompressionLevel.Fastest);

            // Add services to the container.
            builder.Services.AddControllersWithViews()
                .AddJsonOptions(options =>
                {
                    // Serialize only non-null fields — reduces JSON payload further
                    options.JsonSerializerOptions.DefaultIgnoreCondition =
                        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                });

            // ✅ Add NET-Tracker HTTP Request/Response Logging System
            builder.Services.AddNetTracker(builder.Configuration);

            // ✅ Apply database migrations automatically
            using (var serviceProvider = builder.Services.BuildServiceProvider())
            {
                using (var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>())
                {
                    await dbContext.Database.MigrateAsync();
                }
            }

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            // ✅ Must be before UseStaticFiles and UseRouting for full effect
            app.UseResponseCompression();

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            // ✅ Add NET-Tracker Middleware (must be early in pipeline)
            app.UseNetTracker(app.Configuration);

            app.UseRouting();

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            await app.RunAsync();
        }
    }
}
