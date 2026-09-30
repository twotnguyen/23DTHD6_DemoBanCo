using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using _23DTHD6_DemoBanCo.Models;
using _23DTHD6_DemoBanCo.Services;

namespace _23DTHD6_DemoBanCo.Hubs
{
    [Authorize]
    public class ChessHub : Hub
    {
        private readonly RoomService _roomService;

        public ChessHub(RoomService roomService)
        {
            _roomService = roomService;
        }

        /// <summary>Lấy Id người đang đăng nhập từ cookie đăng nhập.</summary>
        private int CurrentUserId
        {
            get
            {
                string? value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                return int.TryParse(value, out int id) ? id : 0;
            }
        }

        // =========================================
        // Người chơi tham gia room
        // =========================================
        public async Task JoinRoom(int roomId, string? side)
        {
            int userId = CurrentUserId;
            if (userId == 0)
                throw new HubException("Chưa đăng nhập.");

            var room = await _roomService.GetByIdAsync(roomId);
            if (room == null)
                throw new HubException("Phòng không tồn tại.");

            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(roomId));

            await _roomService.AddParticipantAsync(
                roomId,
                userId,
                Context.ConnectionId,
                NormalizeSide(side));

            await NotifyParticipantsChangedAsync(roomId);
        }

        // =========================================
        // Người chơi rời room
        // =========================================
        public async Task LeaveRoom(int roomId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(roomId));
            await _roomService.RemoveParticipantByConnectionAsync(Context.ConnectionId);

            await NotifyParticipantsChangedAsync(roomId);
        }


        /// <summary>
        /// Gửi danh sách người chơi hiện tại cho mọi người trong phòng.
        /// Client cập nhật trực tiếp, không tải lại trang.
        /// </summary>
        private async Task NotifyParticipantsChangedAsync(int roomId)
        {
            var room = await _roomService.GetByIdAsync(roomId);

            var list = room?.Participants
                .OrderBy(p => p.JoinedAt)
                .Select(p => new
                {
                    userId = p.UserId,
                    displayName = p.User?.DisplayName ?? "Người chơi",
                    side = p.Side,
                    joinedAt = p.JoinedAt
                })
                .ToList();

            int count = list?.Count ?? 0;

            await Clients.Group(GetGroupName(roomId))
                .SendAsync("RoomParticipantsChanged", new
                {
                    count = count,
                    capacity = 2,
                    participants = list
                });
        }

        // Kết nối bị đóng đột ngột: dọn người chơi ra khỏi phòng và báo cho phòng biết
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var affected = await _roomService.GetRoomIdsByConnectionAsync(Context.ConnectionId);

            await _roomService.RemoveParticipantByConnectionAsync(Context.ConnectionId);

            foreach (int roomId in affected)
                await NotifyParticipantsChangedAsync(roomId);

            await base.OnDisconnectedAsync(exception);
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

            // Gửi nước đi cho tất cả người trong room
            await Clients
                .Group(roomId)
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

            await Clients.Group(roomId)
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
                .Group(roomId)
                .SendAsync(
                    "ReceiveIceCandidate",
                    candidate
                );
        }

        /// <summary>Tên group của phòng, dùng chung giữa các lời gọi SignalR.</summary>
        private static string GetGroupName(int roomId) => $"room:{roomId}";

        private static string? NormalizeSide(string? side)
        {
            if (string.IsNullOrWhiteSpace(side))
                return null;

            string value = side.Trim().ToLower();
            return value == "den" || value == "do" ? value : null;
        }
    }
}