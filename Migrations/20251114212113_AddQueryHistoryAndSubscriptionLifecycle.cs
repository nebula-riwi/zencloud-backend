using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenCloud.Migrations
{
    /// <inheritdoc />
    public partial class AddQueryHistoryAndSubscriptionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoRenewEnabled",
                table: "Subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ExpirationReminderCount",
                table: "Subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAutoRenewAttemptAt",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastAutoRenewError",
                table: "Subscriptions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastExpirationReminderSentAt",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardId",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayerId",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethodId",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DatabaseQueryHistory",
                columns: table => new
                {
                    QueryHistoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueryText = table.Column<string>(type: "text", nullable: false),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: true),
                    ExecutionTimeMs = table.Column<double>(type: "double precision", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    EngineType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseQueryHistory", x => x.QueryHistoryId);
                    table.ForeignKey(
                        name: "FK_DatabaseQueryHistory_DatabaseInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "DatabaseInstances",
                        principalColumn: "InstanceId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DatabaseQueryHistory_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseQueryHistory_InstanceId",
                table: "DatabaseQueryHistory",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseQueryHistory_UserId",
                table: "DatabaseQueryHistory",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatabaseQueryHistory");

            migrationBuilder.DropColumn(
                name: "AutoRenewEnabled",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ExpirationReminderCount",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LastAutoRenewAttemptAt",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LastAutoRenewError",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LastExpirationReminderSentAt",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "CardId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PayerId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PaymentMethodId",
                table: "Payments");
        }
    }
}
