using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NET_Tracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HttpTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    QueryString = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestHeaders = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequestSize = table.Column<int>(type: "int", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ResponseHeaders = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseSize = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StackTrace = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HttpTransactions", x => x.Id);
                },
                comment: "HTTP request/response transaction logs for debugging and monitoring.");

            migrationBuilder.CreateIndex(
                name: "IX_HttpTransactions_DurationMs",
                table: "HttpTransactions",
                column: "DurationMs");

            migrationBuilder.CreateIndex(
                name: "IX_HttpTransactions_Method_StatusCode",
                table: "HttpTransactions",
                columns: new[] { "Method", "StatusCode" });

            migrationBuilder.CreateIndex(
                name: "IX_HttpTransactions_RequestId",
                table: "HttpTransactions",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_HttpTransactions_Success",
                table: "HttpTransactions",
                column: "Success");

            migrationBuilder.CreateIndex(
                name: "IX_HttpTransactions_Timestamp",
                table: "HttpTransactions",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_HttpTransactions_Timestamp_Success",
                table: "HttpTransactions",
                columns: new[] { "Timestamp", "Success" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HttpTransactions");
        }
    }
}
