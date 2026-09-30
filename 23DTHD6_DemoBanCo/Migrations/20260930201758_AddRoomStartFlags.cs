using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _23DTHD6_DemoBanCo.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomStartFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStartingMatch",
                table: "Rooms",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsStartingMatch",
                table: "Rooms");
        }
    }
}
