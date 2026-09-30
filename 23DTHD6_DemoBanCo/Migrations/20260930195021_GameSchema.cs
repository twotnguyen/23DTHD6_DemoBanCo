using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _23DTHD6_DemoBanCo.Migrations
{
    /// <inheritdoc />
    public partial class GameSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Draws",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Elo",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GoogleEmail",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGuest",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Losses",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RankedGames",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Wins",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ActiveMatchId",
                table: "Rooms",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Rooms",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "Rooms",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsReady",
                table: "RoomParticipants",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "RoomParticipants",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Sticker = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DirectConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserALowId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserBHighId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DirectMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConversationId = table.Column<int>(type: "INTEGER", nullable: false),
                    SenderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Sticker = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DirectMessages_Users_SenderId",
                        column: x => x.SenderId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Friendships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    FriendId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friendships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeLimitSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    RedUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    BlackUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TurnSide = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    EndReason = table.Column<int>(type: "INTEGER", nullable: false),
                    RedTimeLeftSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    BlackTimeLeftSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    WinnerSide = table.Column<int>(type: "INTEGER", nullable: false),
                    RedRankedGames = table.Column<int>(type: "INTEGER", nullable: false),
                    BlackRankedGames = table.Column<int>(type: "INTEGER", nullable: false),
                    RedUndoCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BlackUndoCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastMoveAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InactivityWarned = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisconnectedUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    DisconnectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AiDifficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Fen = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Matches_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OtpCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    CodeSalt = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<int>(type: "INTEGER", nullable: false),
                    BlockedUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    BlockedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomBlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchMoves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentMoveId = table.Column<int>(type: "INTEGER", nullable: true),
                    MoveNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Side = table.Column<int>(type: "INTEGER", nullable: false),
                    PieceId = table.Column<string>(type: "TEXT", nullable: false),
                    FromRow = table.Column<int>(type: "INTEGER", nullable: false),
                    FromCol = table.Column<int>(type: "INTEGER", nullable: false),
                    ToRow = table.Column<int>(type: "INTEGER", nullable: false),
                    ToCol = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedPieceId = table.Column<string>(type: "TEXT", nullable: true),
                    SanMove = table.Column<string>(type: "TEXT", nullable: false),
                    FenAfter = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchMoves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchMoves_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_ActiveMatchId",
                table: "Rooms",
                column: "ActiveMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_Visibility_Status",
                table: "Rooms",
                columns: new[] { "Visibility", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_RoomId_CreatedAt",
                table: "ChatMessages",
                columns: new[] { "RoomId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_UserId",
                table: "ChatMessages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectConversations_UserALowId_UserBHighId",
                table: "DirectConversations",
                columns: new[] { "UserALowId", "UserBHighId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessages_SenderId",
                table: "DirectMessages",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_UserId_FriendId",
                table: "Friendships",
                columns: new[] { "UserId", "FriendId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_RoomId",
                table: "Matches",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Status",
                table: "Matches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MatchMoves_MatchId",
                table: "MatchMoves",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_OtpCodes_Purpose_Target",
                table: "OtpCodes",
                columns: new[] { "Purpose", "Target" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpCodes_Purpose_Target_CreatedAt",
                table: "OtpCodes",
                columns: new[] { "Purpose", "Target", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomBlocks_RoomId_BlockedUserId",
                table: "RoomBlocks",
                columns: new[] { "RoomId", "BlockedUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "DirectConversations");

            migrationBuilder.DropTable(
                name: "DirectMessages");

            migrationBuilder.DropTable(
                name: "Friendships");

            migrationBuilder.DropTable(
                name: "MatchMoves");

            migrationBuilder.DropTable(
                name: "OtpCodes");

            migrationBuilder.DropTable(
                name: "RoomBlocks");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_ActiveMatchId",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_Visibility_Status",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Draws",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Elo",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GoogleEmail",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsGuest",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Losses",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RankedGames",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Wins",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ActiveMatchId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "IsReady",
                table: "RoomParticipants");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "RoomParticipants");
        }
    }
}
