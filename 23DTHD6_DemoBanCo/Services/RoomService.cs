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

        private readonly AppDbContext _db;

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