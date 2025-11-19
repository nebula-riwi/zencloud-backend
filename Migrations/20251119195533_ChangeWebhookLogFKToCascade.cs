using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenCloud.Migrations
{
    /// <inheritdoc />
    public partial class ChangeWebhookLogFKToCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookLogs_WebhookConfigurations_WebhookId",
                table: "WebhookLogs");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookLogs_WebhookConfigurations_WebhookId",
                table: "WebhookLogs",
                column: "WebhookId",
                principalTable: "WebhookConfigurations",
                principalColumn: "WebhookId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookLogs_WebhookConfigurations_WebhookId",
                table: "WebhookLogs");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookLogs_WebhookConfigurations_WebhookId",
                table: "WebhookLogs",
                column: "WebhookId",
                principalTable: "WebhookConfigurations",
                principalColumn: "WebhookId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
