using Microsoft.EntityFrameworkCore;
using NET_Tracker.Models;

namespace NET_Tracker.Data
{
    /// <summary>
    /// Main application database context for NET-Tracker.
    /// Includes entities for domain models and HTTP transaction logging.
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// DbSet for HTTP transaction logs.
        /// Stores complete request/response information for debugging and monitoring.
        /// </summary>
        public DbSet<HttpTransaction> HttpTransactions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure HTTP transaction entity
            HttpTransactionConfiguration.Configure(modelBuilder);
        }
    }
}
