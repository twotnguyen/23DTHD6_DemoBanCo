using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Phần nghiệp vụ ghép trận cần đọc CSDL: lấy Elo và cờ khách, rồi tạo phòng cho
    /// cặp đã ghép.
    ///
    /// Trạng thái hàng đợi nằm ở <see cref="MatchmakingQueue"/> (singleton) chứ không
    /// nằm ở đây: service này là scoped nên mỗi hub có một instance riêng, nếu để
    /// hàng đợi ở đây thì hai kết nối sẽ không bao giờ thấy nhau.
    /// </summary>
    public sealed class MatchmakingService
    {
        private readonly AppDbContext _db;
        private readonly RoomService _roomService;
        private readonly MatchmakingQueue _queue;

        public MatchmakingService(AppDbContext db, RoomService roomService, MatchmakingQueue queue)
        {
            _db = db;
            _roomService = roomService;
            _queue = queue;
        }

        /// <summary>
        /// Đưa người chơi vào hàng đợi. Trả về (thành công, thông điệp lỗi).
        /// Khách không được vào hàng đời xếp hạng (đặc tả 7.1).
        /// </summary>
        public (bool ok, string? error) Enqueue(
            int userId,
            string connectionId,
            MatchmakingQueue.QueueKind kind,
            int elo,
            bool isGuest)
        {
            if (isGuest && kind == MatchmakingQueue.QueueKind.Ranked)
                return (false, "Chế độ Khách không được tham gia xếp hạng.");

            _queue.Enqueue(userId, connectionId, kind, elo);
            return (true, null);
        }

        /// <summary>Rút người chơi khỏi hàng đợi. Trả về true nếo vốn đang chờ.</summary>
        public bool TryDequeue(int userId, out MatchmakingQueue.QueueEntry? entry)
            => _queue.TryDequeue(userId, out entry);

        /// <summary>Rút theo ConnectionId, dùng khi kết nối SignalR bị đóng.</summary>
        public bool TryDequeueByConnection(string connectionId, out MatchmakingQueue.QueueEntry? entry)
            => _queue.TryDequeueByConnection(connectionId, out entry);

        /// <summary>Số người đang chờ trong một hàng đợi.</summary>
        public int QueueSize(MatchmakingQueue.QueueKind kind)
            => _queue.QueueSize(kind);

        /// <summary>Thử ghép cặp trong hàng đời tự do, không quan tâm Elo.</summary>
        public (MatchmakingQueue.QueueEntry a, MatchmakingQueue.QueueEntry b)? TryMatchCasual()
            => _queue.TryMatchCasual();

        /// <summary>Thử ghép cặp trong hàng đời xếp hạng theo biên chênh lệch Elo.</summary>
        public (MatchmakingQueue.QueueEntry a, MatchmakingQueue.QueueEntry b)? TryMatchRanked()
            => _queue.TryMatchRanked();

        /// <summary>
        /// Tạo phòng cho cặp đã ghép: thêm cả hai vào phòng, người vào hàng đợi
        /// trước ngồi ghế đỏ, người vào sau ngồi ghế đen. Phòng tự do để công khai
        /// và cho phép khán giả; phòng xếp hạng khoá lại để không ai vào xem.
        /// </summary>
        public async Task<Room> CreateRoomForMatchAsync(
            MatchmakingQueue.QueueEntry a,
            MatchmakingQueue.QueueEntry b,
            MatchmakingQueue.QueueKind kind)
        {
            // Người vào hàng đợi trước đi trước.
            var first = a.JoinedAt <= b.JoinedAt ? a : b;
            var second = ReferenceEquals(first, a) ? b : a;

            var room = new Room
            {
                Code = await _roomService.GenerateUniqueCode8Async(),
                Name = kind == MatchmakingQueue.QueueKind.Ranked
                    ? "Trận xếp hạng"
                    : "Trận tự do",
                OwnerId = first.UserId,
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                Status = RoomStatus.Waiting,

                // Phòng xếp hạng không hiện trong sảnh và không cho vào xem.
                Visibility = kind == MatchmakingQueue.QueueKind.Ranked
                    ? RoomVisibility.Locked
                    : RoomVisibility.Public
            };

            _db.Rooms.Add(room);
            await _db.SaveChangesAsync();

            await AddSeatAsync(room.Id, first, RoomService.SideRed);
            await AddSeatAsync(room.Id, second, RoomService.SideBlack);

            return room;
        }

        private async Task AddSeatAsync(int roomId, MatchmakingQueue.QueueEntry entry, string side)
        {
            _db.RoomParticipants.Add(new RoomParticipant
            {
                RoomId = roomId,
                UserId = entry.UserId,
                ConnectionId = entry.ConnectionId,
                Side = side,
                Role = ParticipantRole.Player,
                IsReady = false,
                JoinedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
    }
}
