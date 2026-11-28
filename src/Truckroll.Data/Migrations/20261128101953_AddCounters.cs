using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Truckroll.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Counters",
                columns: table => new
                {
                    Name = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Counters", x => x.Name);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Job numbers come from this row from now on (#50). It starts at the highest number
            // already on a job, so a database with history carries on where it was; an empty one
            // starts at 21000 and its first job is J-21001. Written out here rather than shared
            // with JobNumbers, because a migration has to keep doing what it did when it shipped.
            migrationBuilder.Sql("""
                INSERT INTO Counters (Name, Value)
                SELECT 'JobNumber', GREATEST(21000, COALESCE(MAX(CAST(SUBSTRING(Number, 3) AS UNSIGNED)), 0))
                FROM Jobs WHERE Number REGEXP '^J-[0-9]+$';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Counters");
        }
    }
}
