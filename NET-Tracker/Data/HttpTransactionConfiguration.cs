using Microsoft.EntityFrameworkCore;
using NET_Tracker.Models;

namespace NET_Tracker.Data
{
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
                .HasMaxLength(2000)
                .IsRequired(false);

            entity.Property(x => x.ContentType)
                .HasMaxLength(256)
                .IsRequired(false);

            entity.Property(x => x.IpAddress)
                .HasMaxLength(45)
                .IsRequired(false);

            entity.Property(x => x.UserAgent)
                .HasMaxLength(500)
                .IsRequired(false);

            entity.Property(x => x.UserId)
                .HasMaxLength(256)
                .IsRequired(false);

            entity.Property(x => x.ErrorMessage)
                .HasColumnType("nvarchar(max)")
                .IsRequired(false);

            entity.Property(x => x.StackTrace)
                .HasColumnType("nvarchar(max)")
                .IsRequired(false);

            entity.Property(x => x.RequestHeaders)
                .HasColumnType("nvarchar(max)")
                .IsRequired(false);

            entity.Property(x => x.RequestBody)
                .HasColumnType("nvarchar(max)")
                .IsRequired(false);

            entity.Property(x => x.ResponseHeaders)
                .HasColumnType("nvarchar(max)")
                .IsRequired(false);

            entity.Property(x => x.ResponseBody)
                .HasColumnType("nvarchar(max)")
                .IsRequired(false);

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

            // Covering index for the default list view query:
            // ORDER BY Timestamp DESC + optional filters on StatusCode/Method/Success/DurationMs.
            // Including lightweight non-key columns avoids a key lookup back to the clustered index
            // for the columns used in SELECT projections (list view omits bodies).
            entity.HasIndex(x => new { x.Timestamp, x.StatusCode, x.Method, x.Success, x.DurationMs })
                .IsUnique(false)
                .HasDatabaseName("IX_HttpTransactions_List_Covering");

            // Table naming
            entity.ToTable("HttpTransactions");

            // Annotations
            entity.Metadata.SetComment("HTTP request/response transaction logs for debugging and monitoring.");
        }
    }
}
