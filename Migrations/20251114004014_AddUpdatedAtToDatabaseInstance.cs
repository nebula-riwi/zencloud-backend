using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenCloud.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdatedAtToDatabaseInstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "WebhookConfigurations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "DatabaseInstances",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "WebhookConfigurations");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "DatabaseInstances");
        }
    }
}
