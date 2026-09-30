using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Services
{
    public class RoomService
    {
        // Không dùng chữ I, O, 0, 1 để tránh nhầm khi nhập tay
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int CodeLength = 6;
        private const int CodeLength8 = 8;

        private readonly AppDbContext _db;

        /// <summary>Số khán giả tối đa của một phòng.</summary>
        public const int MaxSpectators = 5;

        /// <summary>Tổng số người tối đa trong phòng: 2 đấu thủ + 5 khán giả.</summary>
        public const int MaxRoomCapacity = 7;

        /// <summary>Hai phe đánh, dùng đúng ký hiệu đã lưu ở RoomParticipant.Side.</summary>
        public const string SideRed = "do";
        public const string SideBlack = "den";

        private static readonly string[] Sides = { SideRed, SideBlack };

        public RoomService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>Tạo mã phòng ngẫu nhiên chưa tồn tại trong database.</summary>
        private async Task<string> GenerateUniqueCodeAsync()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var code = new string(
                    Enumerable.Range(0, CodeLength)
                        .Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)])
                        .ToArray());

                bool exists = await _db.Rooms.AnyAsync(r => r.Code == code);
                if (!exists)
                    return code;
            }

            // Rất hiếm gặp: sinh mã dài hơn thay vì báo lỗi
            return Guid.NewGuid().ToString("N")[..CodeLength].ToUpper();
        }

        /// <summary>
        /// Tạo mã phòng 8 ký tự theo đặc tả. Loại chữ I, O, 0, 1 để không nhầm khi nhập tay.
        /// Mã cũ 6 ký tự vẫn dùng được, hai loại cùng tồn tại trong database.
        /// </summary>
        public async Task<string> GenerateUniqueCode8Async()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var code = new string(
                    Enumerable.Range(0, CodeLength8)
                        .Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)])
                        .ToArray());

                bool exists = await _db.Rooms.AnyAsync(r => r.Code == code);
                if (!exists)
                    return code;
            }

            // Rất hiếm gặp: sinh mã dài hơn thay vì báo lỗi
            return Guid.NewGuid().ToString("N")[..CodeLength8].ToUpper();
        }

        public async Task<Room> CreateRoomAsync(int ownerId, string? name)
        {
            var room = new Room
            {
                Code = await GenerateUniqueCodeAsync(),
                Name = string.IsNullOrWhiteSpace(name) ? "Phòng của tôi" : name.Trim(),
                OwnerId = ownerId,
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };

            _db.Rooms.Add(room);
            await _db.SaveChangesAsync();

            return room;
        }

        public Task<Room?> GetByIdAsync(int roomId)
        {
            return _db.Rooms
                .Include(r => r.Owner)
                .Include(r => r.Participants).ThenInclude(p => p.User)
                .FirstOrDefaultAsync(r => r.Id == roomId);
        }

        public Task<Room?> GetByCodeAsync(string code)
        {
            string normalized = code.Trim().ToUpper();

            return _db.Rooms
                .Include(r => r.Owner)
                .Include(r => r.Participants).ThenInclude(p => p.User)
                .FirstOrDefaultAsync(r => r.Code == normalized);
        }

        public async Task<List<Room>> GetRoomsWithDetailsAsync()
        {
            return await _db.Rooms
                .AsNoTracking()
                .Include(r => r.Owner)
                .Include(r => r.Participants).ThenInclude(p => p.User)
                .OrderByDescending(r => r.LastActivityAt ?? r.CreatedAt)
                .ToListAsync();
        }

        /// <summary>Chỉ trả phòng công khai, phòng hoạt động gần nhất lên đầu.</summary>
        public async Task<List<Room>> GetPublicRoomsAsync()
        {
            return await _db.Rooms
                .AsNoTracking()
                .Include(r => r.Owner)
                .Include(r => r.Participants).ThenInclude(p => p.User)
                .Where(r => r.Visibility == RoomVisibility.Public)
                .OrderByDescending(r => r.LastActivityAt ?? r.CreatedAt)
                .ToListAsync();
        }

        /// <summary>Người này đã bị chủ phòng đuổi và chưa được vào lại phòng đó chưa.</summary>
        public Task<bool> IsBlockedAsync(int roomId, int userId)
        {
            return _db.RoomBlocks
                .AsNoTracking()
                .AnyAsync(b => b.RoomId == roomId && b.BlockedUserId == userId);
        }

        /// <summary>
        /// Ghi người bị đuổi vào danh sách chặn của phòng. Chặn tới khi phòng đóng,
        /// kể cả khi người đó có mã phòng. Chỉ chủ phòng mới gọi được.
        /// </summary>
        public async Task BlockUserAsync(int roomId, int blockedUserId, int blockedByUserId)
        {
            if (blockedUserId == blockedByUserId)
                return;

            var room = await _db.Rooms
                .FirstOrDefaultAsync(r => r.Id == roomId && r.OwnerId == blockedByUserId);

            // Chỉ chủ phòng mới được chặn, và không chặn chính mình
            if (room == null || blockedUserId == blockedByUserId)
                return;

            var already = await _db.RoomBlocks
                .FirstOrDefaultAsync(b => b.RoomId == roomId && b.BlockedUserId == blockedUserId);

            if (already != null)
                return; // đã chặn rồi thì giữ nguyên người chặn cũ

            _db.RoomBlocks.Add(new RoomBlock
            {
                RoomId = roomId,
                BlockedUserId = blockedUserId,
                BlockedByUserId = blockedByUserId,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Quyết định vai trò khi vào phòng: ghế còn trống thì làm đấu thủ,
        /// hết ghế thì làm khán giả, đủ cả 7 người thì không còn chỗ.
        /// </summary>
        public Task<ParticipantRole> DecideRoleAsync(Room room, int userId)
        {
            var taken = room.Participants
                .Where(p => p.UserId != userId)
                .Select(p => p.Side)
                .Where(s => s != null)
                .ToHashSet();

            // Còn ghế trống thì xếp vào ghế đó
            foreach (string side in Sides)
            {
                if (!taken.Contains(side))
                    return Task.FromResult(ParticipantRole.Player);
            }

            int total = room.Participants.Count(p => p.UserId != userId);
            if (total < MaxRoomCapacity)
                return Task.FromResult(ParticipantRole.Spectator);

            throw new InvalidOperationException("Phòng đã đủ người, không còn chỗ cho bạn nữa.");
        }

        /// <summary>Chỉ chủ phòng mới đổi được chế độ hiển thị.</summary>
        public async Task<bool> SetVisibilityAsync(int roomId, int requesterId, RoomVisibility visibility)
        {
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);

            // Không phải chủ phòng thì không được đổi
            if (room == null || room.OwnerId != requesterId)
                return false;

            room.Visibility = visibility;
            room.LastActivityAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// Chủ phòng rời đi thì chuyển quyền cho người vào phòng sớm nhất còn lại.
        /// Không còn ai thì trả null.
        /// </summary>
        public async Task<Room?> TransferHostAsync(int roomId)
        {
            var room = await _db.Rooms
                .Include(r => r.Owner)
                .Include(r => r.Participants).ThenInclude(p => p.User)
                .FirstOrDefaultAsync(r => r.Id == roomId);

            if (room == null)
                return null;

            // Người vào sớm nhất sẽ kế nhiệm
            var heir = room.Participants
                .OrderBy(p => p.JoinedAt)
                .FirstOrDefault();

            if (heir == null)
                return null;

            room.OwnerId = heir.UserId;
            room.LastActivityAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return room;
        }

        /// <summary>Chuyển người chơi sang ghế còn trống, chỉ khi phòng chưa đủ hai đấu thủ.</summary>
        public async Task<bool> SwitchSideAsync(int roomId, int userId, string side)
        {
            string normalized = NormalizeSide(side);
            if (normalized == null)
                return false;

            var participant = await _db.RoomParticipants
                .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId);

            if (participant == null)
                return false;

            // Ghế đã có người khác ngồi thì không đổi được
            bool occupied = await _db.RoomParticipants
                .AnyAsync(p => p.RoomId == roomId && p.Side == normalized && p.UserId != userId);

            if (occupied)
                return false;

            // Ván đã bắt đầu thì phe nằm ở Match.RedUserId/BlackUserId của realtime.
            // Đổi RoomParticipant.Side lúc này sẽ lệch với ván đang chơi nên không cho đổi.
            bool playing = await _db.Rooms
                .AnyAsync(r => r.Id == roomId && r.Status == RoomStatus.Playing);

            if (playing)
                return false;

            participant.Side = normalized;
            await _db.SaveChangesAsync();

            return true;
        }

        /// <summary>Bật hoặc tắt cờ sẵn sàng của người chơi trong phòng.</summary>
        public async Task<bool> SetReadyAsync(int roomId, int userId, bool ready)
        {
            var participant = await _db.RoomParticipants
                .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId);

            if (participant == null)
                return false;

            participant.IsReady = ready;
            await _db.SaveChangesAsync();

            return true;
        }

        /// <summary>Chuẩn hoá phe về "do" hoặc "den", trả null nếu giá trị không hợp lệ.</summary>
        public static string? NormalizeSide(string? side)
        {
            if (string.IsNullOrWhiteSpace(side))
                return null;

            string value = side.Trim().ToLower();
            return Sides.Contains(value) ? value : null;
        }

        public Task<List<Room>> GetRoomsByOwnerAsync(int ownerId)
        {
            return _db.Rooms
                .AsNoTracking()
                .Include(r => r.Owner)
                .Include(r => r.Participants).ThenInclude(p => p.User)
                .Where(r => r.OwnerId == ownerId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        /// <summary>Thêm người chơi vào phòng. Trả về false nếu người này đã có mặt trong phòng.</summary>
        public async Task<RoomParticipant?> AddParticipantAsync(int roomId, int userId, string connectionId, string? side)
        {
            var existing = await _db.RoomParticipants
                .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId);

            if (existing != null)
            {
                // Người chơi mở lại phòng ở tab mới: cập nhật connectionId cho theo
                existing.ConnectionId = connectionId;
                existing.Side ??= side;
                await _db.SaveChangesAsync();
                return existing;
            }

            var participant = new RoomParticipant
            {
                RoomId = roomId,
                UserId = userId,
                ConnectionId = connectionId,
                Side = side,
                JoinedAt = DateTime.UtcNow
            };

            _db.RoomParticipants.Add(participant);

            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
            if (room != null)
                room.LastActivityAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return participant;
        }

        public Task<RoomParticipant?> GetParticipantAsync(int roomId, int userId)
        {
            return _db.RoomParticipants
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId);
        }

        /// <summary>Tìm các phòng mà kết nối này đang tham gia, dùng khi kết nối bị đóng.</summary>
        public async Task<List<int>> GetRoomIdsByConnectionAsync(string connectionId)
        {
            return await _db.RoomParticipants
                .Where(p => p.ConnectionId == connectionId)
                .Select(p => p.RoomId)
                .ToListAsync();
        }

        public async Task RemoveParticipantByConnectionAsync(string connectionId)
        {
            var participants = await _db.RoomParticipants
                .Where(p => p.ConnectionId == connectionId)
                .ToListAsync();

            if (participants.Count == 0)
                return;

            _db.RoomParticipants.RemoveRange(participants);
            await _db.SaveChangesAsync();
        }

        public async Task RemoveParticipantAsync(int roomId, int userId)
        {
            var participant = await _db.RoomParticipants
                .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId);

            if (participant == null)
                return;

            _db.RoomParticipants.Remove(participant);
            await _db.SaveChangesAsync();
        }

        /// <summary>Xoá phòng và toàn bộ người chơi trong phòng. Chỉ chủ phòng được gọi.</summary>
        public async Task<bool> DeleteRoomAsync(int roomId, int requesterId)
        {
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);

            // Chỉ chủ phòng mới được xoá
            if (room == null || room.OwnerId != requesterId)
                return false;

            var participants = await _db.RoomParticipants
                .Where(p => p.RoomId == roomId)
                .ToListAsync();

            _db.RoomParticipants.RemoveRange(participants);
            _db.Rooms.Remove(room);
            await _db.SaveChangesAsync();

            return true;
        }

        public async Task TouchRoomAsync(int roomId)
        {
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
            if (room == null)
                return;

            room.LastActivityAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}