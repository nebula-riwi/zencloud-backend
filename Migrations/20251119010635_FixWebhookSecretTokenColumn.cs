using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenCloud.Migrations
{
    /// <inheritdoc />
    public partial class FixWebhookSecretTokenColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""WebhookConfigurations"" 
                ALTER COLUMN ""SecretToken"" TYPE character varying(500);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
