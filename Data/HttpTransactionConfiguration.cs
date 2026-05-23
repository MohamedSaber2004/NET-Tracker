using Microsoft.EntityFrameworkCore;
using NET_Tracker.Models;

namespace NET_Tracker.Data
{
    /// <summary>
    /// Database context for HTTP transaction logging.
    /// Extends the application's main DbContext to include HTTP logging entities.
    /// </summary>
    public interface IHttpLoggingDbContext
    {
        /// <summary>
        /// DbSet for HTTP transaction logs.
        /// </summary>
        DbSet<HttpTransaction> HttpTransactions { get; set; }

        /// <summary>
        /// Saves changes asynchronously.
        /// </summary>
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Configuration for HTTP transaction entity mapping to database schema.
    /// This class configures how HttpTransaction entities are mapped to database tables.
    /// </summary>
    public class HttpTransactionConfiguration
    {
        /// <summary>
        /// Configures the HTTP transaction entity for Entity Framework Core.
        /// Call this in your DbContext.OnModelCreating method.
        /// </summary>
        public static void Configure(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<HttpTransaction>();

            // Primary key
            entity.HasKey(x => x.Id);

            // Properties configuration
            entity.Property(x => x.RequestId)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Method)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(x => x.Url)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(x => x.QueryString)
                .HasMaxLength(2000);

            entity.Property(x => x.ContentType)
                .HasMaxLength(256);

            entity.Property(x => x.IpAddress)
                .HasMaxLength(45);

            entity.Property(x => x.UserAgent)
                .HasMaxLength(500);

            entity.Property(x => x.UserId)
                .HasMaxLength(256);

            entity.Property(x => x.ErrorMessage)
                .HasColumnType("nvarchar(max)");

            entity.Property(x => x.StackTrace)
                .HasColumnType("nvarchar(max)");

            entity.Property(x => x.RequestHeaders)
                .HasColumnType("nvarchar(max)");

            entity.Property(x => x.RequestBody)
                .HasColumnType("nvarchar(max)");

            entity.Property(x => x.ResponseHeaders)
                .HasColumnType("nvarchar(max)");

            entity.Property(x => x.ResponseBody)
                .HasColumnType("nvarchar(max)");

            // Indexes for common queries
            entity.HasIndex(x => x.RequestId)
                .IsUnique(false)
                .HasDatabaseName("IX_HttpTransactions_RequestId");

            entity.HasIndex(x => x.Timestamp)
                .IsUnique(false)
                .HasDatabaseName("IX_HttpTransactions_Timestamp");

            entity.HasIndex(x => new { x.Method, x.StatusCode })
                .IsUnique(false)
                .HasDatabaseName("IX_HttpTransactions_Method_StatusCode");

            entity.HasIndex(x => x.Success)
                .IsUnique(false)
                .HasDatabaseName("IX_HttpTransactions_Success");

            entity.HasIndex(x => x.DurationMs)
                .IsUnique(false)
                .HasDatabaseName("IX_HttpTransactions_DurationMs");

            entity.HasIndex(x => new { x.Timestamp, x.Success })
                .IsUnique(false)
                .HasDatabaseName("IX_HttpTransactions_Timestamp_Success");

            // Table naming
            entity.ToTable("HttpTransactions");

            // Annotations
            entity.Metadata.SetComment("HTTP request/response transaction logs for debugging and monitoring.");
        }
    }
}
