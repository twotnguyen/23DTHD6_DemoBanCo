using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace _23DTHD6_DemoBanCo.Migrations
{
    /// <inheritdoc />
    public partial class MatchFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Rooms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MatchType",
                table: "Rooms",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SideSwapExpiresAt",
                table: "Rooms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SideSwapRequestedBy",
                table: "Rooms",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeLimitSeconds",
                table: "Rooms",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AiDepthReached",
                table: "MatchMoves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AiElapsedMs",
                table: "MatchMoves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AiNodesEvaluated",
                table: "MatchMoves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AiPrincipalVariation",
                table: "MatchMoves",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsAiMove",
                table: "MatchMoves",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BlackRematchBy",
                table: "Matches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CountdownEndsAt",
                table: "Matches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DrawOfferExpiresAt",
                table: "Matches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DrawOfferState",
                table: "Matches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DrawOfferedBy",
                table: "Matches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RedRematchBy",
                table: "Matches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RematchState",
                table: "Matches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UndoRequestExpiresAt",
                table: "Matches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UndoRequestedBy",
                table: "Matches",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "MatchType",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "SideSwapExpiresAt",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "SideSwapRequestedBy",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "TimeLimitSeconds",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "AiDepthReached",
                table: "MatchMoves");

            migrationBuilder.DropColumn(
                name: "AiElapsedMs",
                table: "MatchMoves");

            migrationBuilder.DropColumn(
                name: "AiNodesEvaluated",
                table: "MatchMoves");

            migrationBuilder.DropColumn(
                name: "AiPrincipalVariation",
                table: "MatchMoves");

            migrationBuilder.DropColumn(
                name: "IsAiMove",
                table: "MatchMoves");

            migrationBuilder.DropColumn(
                name: "BlackRematchBy",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "CountdownEndsAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "DrawOfferExpiresAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "DrawOfferState",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "DrawOfferedBy",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "RedRematchBy",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "RematchState",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "UndoRequestExpiresAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "UndoRequestedBy",
                table: "Matches");
        }
    }
}
