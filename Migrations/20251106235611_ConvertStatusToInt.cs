using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenCloud.Migrations
{
    public partial class ConvertStatusToInt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1️⃣ Convert existing string values to numeric strings first
            migrationBuilder.Sql(@"
                UPDATE ""DatabaseInstances""
                SET ""Status"" = CASE
                    WHEN ""Status"" = 'Active' THEN '1'
                    WHEN ""Status"" = 'Inactive' THEN '2'
                    WHEN ""Status"" = 'Deleted' THEN '3'
                    ELSE '0'
                END;
            ");

            // 2️⃣ Explicitly cast the column to integer using USING clause
            migrationBuilder.Sql(@"
                ALTER TABLE ""DatabaseInstances""
                ALTER COLUMN ""Status"" TYPE integer USING ""Status""::integer;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1️⃣ Convert numeric values back to text for rollback
            migrationBuilder.Sql(@"
                UPDATE ""DatabaseInstances""
                SET ""Status"" = CASE
                    WHEN ""Status"" = 1 THEN 'Active'
                    WHEN ""Status"" = 2 THEN 'Inactive'
                    WHEN ""Status"" = 3 THEN 'Deleted'
                    ELSE 'Unknown'
                END;
            ");

            // 2️⃣ Change the column type back to string (varchar)
            migrationBuilder.Sql(@"
                ALTER TABLE ""DatabaseInstances""
                ALTER COLUMN ""Status"" TYPE character varying(50);
            ");
        }
    }
}