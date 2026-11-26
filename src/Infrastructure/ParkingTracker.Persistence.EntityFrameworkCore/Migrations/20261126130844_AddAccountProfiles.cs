using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingTracker.Persistence.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    KeyId = table.Column<string>(type: "text", maxLength: 8, nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    UpdatedOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_profiles_user_accounts_Id",
                        column: x => x.Id,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_profiles");
        }
    }
}
