using _23DTHD6_DemoBanCo.Models;
using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;

namespace _23DTHD6_DemoBanCo.Hubs
{
    public class ChessHub : Hub
    {
        // =========================================
        // Người chơi tham gia room
        // =========================================
        public async Task JoinRoom(string roomId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                roomId
            );

            Console.WriteLine(
                $"{Context.ConnectionId} joined room {roomId}"
            );
        }


        // =========================================
        // Người chơi rời room
        // =========================================
        public async Task LeaveRoom(string roomId)
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                roomId
            );

            Console.WriteLine(
                $"{Context.ConnectionId} left room {roomId}"
            );
        }


        // =========================================
        // Nhận nước đi
        // =========================================
        public async Task MovePiece(
            string roomId,
            string pieceId,
            int fromRow,
            int fromCol,
            int toRow,
            int toCol)
        {
            var move = new ChessMove
            {
                PieceId = pieceId,

                FromRow = fromRow,
                FromCol = fromCol,

                ToRow = toRow,
                ToCol = toCol
            };


            Console.WriteLine(
                $"Room: {roomId} | " +
                $"{pieceId}: " +
                $"({fromRow},{fromCol}) -> " +
                $"({toRow},{toCol})"
            );


            // Gửi nước đi cho tất cả người trong room
            await Clients
                //.Group(roomId)
                .All
                .SendAsync(
                    "ReceiveMove",
                    move
                );
        }

        public async Task SendMessage(
           string roomId,
           string userName,
           string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            message = message.Trim();


            await Clients.All
                //.Group(roomId)
                .SendAsync(
                    "ReceiveMessage",
                    userName,
                    message
                );
        }

        public async Task SendIceCandidate(
                string roomId,
                string candidate)
        {
            await Clients
                .All
                .SendAsync(
                    "ReceiveIceCandidate",
                    candidate
                );
        }
    }
}
