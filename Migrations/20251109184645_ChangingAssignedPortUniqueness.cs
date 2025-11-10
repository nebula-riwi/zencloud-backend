using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenCloud.Migrations
{
    /// <inheritdoc />
    public partial class ChangingAssignedPortUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DatabaseInstances_AssignedPort",
                table: "DatabaseInstances");

            migrationBuilder.AlterColumn<string>(
                name: "EngineName",
                table: "DatabaseEngines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldMaxLength: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "EngineName",
                table: "DatabaseEngines",
                type: "integer",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseInstances_AssignedPort",
                table: "DatabaseInstances",
                column: "AssignedPort",
                unique: true);
        }
    }
}
