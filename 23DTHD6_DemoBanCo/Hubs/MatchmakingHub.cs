using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Services;

namespace _23DTHD6_DemoBanCo.Hubs
{
    /// <summary>
    /// Hub ghép trận: nhận người chơi vào hàng đợi tự do hoặc xếp hạng, tự ghép cặp
    /// khi đủ điều kiện rồi tạo phòng và báo cả hai về phòng chờ.
    ///
    /// Hub này KHÔNG tạo ván. Ván chỉ khởi động khi cả hai bấm "Sẵn sàng" trong
    /// phòng chờ, lúc đó ChessHub lo phần ghép nước đi. Nhờ vậy mọi trận, kể cả
    /// trận tự tạo phòng, đều đi qua đúng một mắt xích khởi tạo ván.
    /// </summary>
    [Authorize]
    public class MatchmakingHub : Hub
    {
        private readonly MatchmakingService _matchmaking;
        private readonly AppDbContext _db;

        public MatchmakingHub(MatchmakingService matchmaking, AppDbContext db)
        {
            _matchmaking = matchmaking;
            _db = db;
        }

        private int CurrentUserId
        {
            get
            {
                string? value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                return int.TryParse(value, out int id) ? id : 0;
            }
        }

        /// <summary>
        /// Vào hàng đợi và thử ghép ngay. kind nhận "casual" hoặc "ranked".
        /// Tham số connectionId do client gửi lên vì client có thể mở nhiều tab.
        /// </summary>
        public async Task JoinQueue(string kind, string connectionId)
        {
            int userId = CurrentUserId;
            if (userId == 0)
            {
                await Clients.Caller.SendAsync("QueueRejected", new { error = "Chưa đăng nhập." });
                return;
            }

            if (!Enum.TryParse(kind, ignoreCase: true, out MatchmakingQueue.QueueKind queueKind))
            {
                await Clients.Caller.SendAsync("QueueRejected", new { error = "Loại hàng đợi không hợp lệ." });
                return;
            }

            // Elo và cờ khách lấy từ DB, không tin giá trị client gửi lên.
            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                await Clients.Caller.SendAsync("QueueRejected", new { error = "Không tìm thấy tài khoản." });
                return;
            }

            string effectiveConnectionId = string.IsNullOrWhiteSpace(connectionId)
                ? Context.ConnectionId
                : connectionId;

            var (ok, error) = _matchmaking.Enqueue(
                userId, effectiveConnectionId, queueKind, user.Elo, user.IsGuest);

            if (!ok)
            {
                await Clients.Caller.SendAsync("QueueRejected", new { error });
                return;
            }

            // Thử ghép ngay: có thể đã có người chờ sẵn nên vào là có đối thủ luôn.
            var matched = queueKind == MatchmakingQueue.QueueKind.Ranked
                ? _matchmaking.TryMatchRanked()
                : _matchmaking.TryMatchCasual();

            if (matched == null)
            {
                int position = _matchmaking.QueueSize(queueKind);
                string label = queueKind == MatchmakingQueue.QueueKind.Ranked ? "xếp hạng" : "tự do";

                await Clients.Caller.SendAsync("Queued", new
                {
                    kind = label,
                    position,
                    message = $"Đang chờ đối thủ chế độ {label}. Bạn đang ở vị trí {position}."
                });
                return;
            }

            var room = await _matchmaking.CreateRoomForMatchAsync(
                matched.Value.a, matched.Value.b, queueKind);

            string typeLabel = queueKind == MatchmakingQueue.QueueKind.Ranked ? "Ranked" : "Casual";

            // Báo riêng cho từng người để họ biết mình ngồi ghế nào.
            await Clients.Client(matched.Value.a.ConnectionId).SendAsync("Matched", new
            {
                roomId = room.Id,
                code = room.Code,
                role = "player",
                side = "do",
                type = typeLabel
            });

            await Clients.Client(matched.Value.b.ConnectionId).SendAsync("Matched", new
            {
                roomId = room.Id,
                code = room.Code,
                role = "player",
                side = "den",
                type = typeLabel
            });
        }

        /// <summary>Hủy ghép trận. Hủy lúc nào cũng được, không bị phạt.</summary>
        public async Task LeaveQueue()
        {
            int userId = CurrentUserId;
            if (userId == 0)
                return;

            if (_matchmaking.TryDequeue(userId, out MatchmakingQueue.QueueEntry? entry))
            {
                string label = entry!.Kind == MatchmakingQueue.QueueKind.Ranked ? "xếp hạng" : "tự do";
                await Clients.Caller.SendAsync("QueueCancelled", new
                {
                    kind = label,
                    message = $"Đã hủy tìm trận {label}."
                });
            }
        }

        /// <summary>
        /// Kết nối đóng giữa lúc đang chờ thì phải rút khỏi hàng đợi, nếu không
        /// hàng đợi sẽ giữ một người không còn ở đây và chặn người mới vô phòng.
        /// </summary>
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            if (_matchmaking.TryDequeueByConnection(Context.ConnectionId, out MatchmakingQueue.QueueEntry? entry))
            {
                // Không gửi được cho người đã ngắt, chỉ ghi lại để tra cứu.
                Console.WriteLine(
                    $"Rút khỏi hàng đợi do mất kết nối: user={entry!.UserId}, loại={entry.Kind}");
            }

            return base.OnDisconnectedAsync(exception);
        }
    }
}
