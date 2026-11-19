using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingTracker.Persistence.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "erasure_log",
                columns: table => new
                {
                    SubjectHash = table.Column<string>(type: "text", nullable: false),
                    ErasedOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_erasure_log", x => x.SubjectHash);
                });

            migrationBuilder.CreateTable(
                name: "sign_in_challenges",
                columns: table => new
                {
                    Nonce = table.Column<string>(type: "text", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sign_in_challenges", x => x.Nonce);
                });

            migrationBuilder.CreateTable(
                name: "user_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectHash = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    CreatedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    LastSeenOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    LastUsedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    AbsoluteExpiryUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RotatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_sessions_user_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_sessions_AccountId",
                table: "device_sessions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_device_sessions_FamilyId",
                table: "device_sessions",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_device_sessions_TokenHash",
                table: "device_sessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sign_in_challenges_ExpiresAtUtc",
                table: "sign_in_challenges",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_SubjectHash",
                table: "user_accounts",
                column: "SubjectHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_sessions");

            migrationBuilder.DropTable(
                name: "erasure_log");

            migrationBuilder.DropTable(
                name: "sign_in_challenges");

            migrationBuilder.DropTable(
                name: "user_accounts");
        }
    }
}
