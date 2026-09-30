using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Bạn bè: gửi lời mời, trả lời, xoá.
    ///
    /// Quy ước lưu trữ: mỗi cặp người dùng chỉ có MỘT dòng Friendship, lưu đúng
    /// 1 chiều với UserId &lt; FriendId. Nhờ vậy không có hai dòng (A,B) và (B,A)
    /// cùng tồn tại, và chỉ mục duy nhất trên cặp này không bao giờ đụng nhau.
    ///
    /// Lưu ý về ngữ nghĩa: vì chỉ có một dòng cho cả cặp, trạng thái là của CẢ HAI
    /// người chứ không riêng của người gửi. "Cả hai chiều Accepted" ở đây có nghĩa
    /// là dòng duy nhất đó đang ở trạng thái Accepted và hai người cùng tham gia nó.
    /// </summary>
    public sealed class FriendshipService
    {
        /// <summary>Số bạn tối đa của một tài khoản.</summary>
        public const int MaxFriends = 50;

        private readonly AppDbContext _db;

        public FriendshipService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Gửi lời mời kết bạn. Trả về (thành công, lỗi tiếng Việt hoặc null).
        ///
        /// Nếu đã có lời mời ngược chiều đang chờ, nâng luôn dòng đó thành Accepted
        /// thay vì tạo dòng mới, nếu không sẽ phải có hai dòng cho một cặp.
        /// </summary>
        public async Task<(bool ok, string? error)> SendRequestAsync(int fromUserId, int toUserId)
        {
            if (fromUserId == toUserId)
            {
                return (false, "Không thể tự kết bạn với chính mình.");
            }

            bool targetExists = await _db.Users.AnyAsync(u => u.Id == toUserId);

            if (!targetExists)
            {
                return (false, "Không tìm thấy người dùng này.");
            }

            int low = Math.Min(fromUserId, toUserId);
            int high = Math.Max(fromUserId, toUserId);

            // Dùng chung một cách sắp xếp cho cả hai chiều, nên tra cứu được cả dòng
            // đang lưu theo chiều ngược lẫn chiều thuận.
            var existing = await _db.Friendships
                .FirstOrDefaultAsync(f => f.UserId == low && f.FriendId == high);

            if (existing != null)
            {
                switch (existing.Status)
                {
                    case FriendshipStatus.Accepted:
                        return (false, "Hai người đã là bạn bè.");

                    case FriendshipStatus.Pending:
                        return (false, "Bạn đã gửi lời mời rồi, hãy chờ trả lời.");

                    case FriendshipStatus.Rejected:
                        // Bị từ chối rồi thì cho gửi lại, đặt về Pending.
                        existing.Status = FriendshipStatus.Pending;
                        existing.CreatedAt = DateTime.UtcNow;
                        await _db.SaveChangesAsync();
                        return (true, null);
                }
            }

            int friendCount = await _db.Friendships.CountAsync(
                f => (f.UserId == low && f.FriendId == high && f.Status == FriendshipStatus.Accepted)
                  || (f.UserId == high && f.FriendId == low && f.Status == FriendshipStatus.Accepted));

            if (friendCount >= MaxFriends)
            {
                return (false, $"Đã đạt giới hạn {MaxFriends} bạn bè.");
            }

            _db.Friendships.Add(new Friendship
            {
                UserId = low,
                FriendId = high,
                Status = FriendshipStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return (true, null);
        }

        /// <summary>
        /// Trả lời lời mời. Chỉ người NHẬN mới được trả lời, nên phải kiểm đúng người nhận
        /// thay vì tin userId do client gửi lên.
        /// </summary>
        public async Task<(bool ok, string? error)> RespondAsync(int requestId, int userId, bool accept)
        {
            var friendship = await _db.Friendships
                .FirstOrDefaultAsync(f => f.Id == requestId);

            if (friendship == null)
            {
                return (false, "Không tìm thấy lời mời.");
            }

            // Người nhận là phía không phải người gửi, tức là phía có id lớn hơn
            // vì dòng luôn lưu UserId < FriendId.
            if (friendship.FriendId != userId)
            {
                return (false, "Bạn không thể trả lời lời mời của người khác.");
            }

            if (friendship.Status != FriendshipStatus.Pending)
            {
                return (false, "Lời mời này đã được xử lý rồi.");
            }

            friendship.Status = accept ? FriendshipStatus.Accepted : FriendshipStatus.Rejected;

            await _db.SaveChangesAsync();

            return (true, null);
        }

        /// <summary>Xoá bạn bè. Giữ nguyên kiểu trả về bool cho gọn, lỗi đã bao gồm trong false.</summary>
        public async Task<bool> RemoveAsync(int userId, int friendId)
        {
            if (userId == friendId)
            {
                return false;
            }

            int low = Math.Min(userId, friendId);
            int high = Math.Max(userId, friendId);

            var friendship = await _db.Friendships
                .FirstOrDefaultAsync(f => f.UserId == low && f.FriendId == high);

            if (friendship == null)
            {
                return false;
            }

            _db.Friendships.Remove(friendship);
            await _db.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// Danh sách bạn bè và lời mời đang chờ, kèm trạng thái để giao diện tự hiển thị
        /// nút Đồng ý / Từ chối hay nút Nhắn tin.
        /// </summary>
        public async Task<List<(int UserId, string DisplayName, FriendshipStatus Status)>>
            GetFriendsAsync(int userId)
        {
            var rows = await _db.Friendships
                .AsNoTracking()
                .Where(f => f.UserId == userId || f.FriendId == userId)
                .ToListAsync();

            if (rows.Count == 0)
            {
                return new List<(int, string, FriendshipStatus)>();
            }

            // Lấy id của phía đối diện: dòng lưu UserId < FriendId nên phía còn lại
            // chính là phía đối diện với userId đang xem.
            var otherIds = rows
                .Select(f => f.UserId == userId ? f.FriendId : f.UserId)
                .Distinct()
                .ToList();

            var names = await _db.Users
                .AsNoTracking()
                .Where(u => otherIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            var result = new List<(int, string, FriendshipStatus)>();

            foreach (var row in rows)
            {
                int otherId = row.UserId == userId ? row.FriendId : row.UserId;

                result.Add((
                    otherId,
                    names.TryGetValue(otherId, out var name) ? name : "Người chơi",
                    row.Status));
            }

            return result;
        }

        /// <summary>
        /// Hai người chỉ là bạn bè khi dòng duy nhất của cặp đang Accepted.
        /// Chỉ một phía Accepted (Pending) thì vẫn chưa được nhắn tin riêng.
        /// </summary>
        public async Task<bool> AreFriendsAsync(int a, int b)
        {
            if (a == b)
            {
                return false;
            }

            int low = Math.Min(a, b);
            int high = Math.Max(a, b);

            return await _db.Friendships.AnyAsync(
                f => f.UserId == low
                  && f.FriendId == high
                  && f.Status == FriendshipStatus.Accepted);
        }

        /// <summary>
        /// Bỏ chặn một người trong phòng. RoomBlock của lobby dùng để chặn vào phòng,
        /// hàm này chỉ gỡ chặn, không tự tạo bản ghi chặn.
        /// </summary>
        public async Task<(bool ok, string? error)> RemoveBlockAsync(int roomId, int blockedByUserId, int blockedUserId)
        {
            if (blockedByUserId == blockedUserId)
            {
                return (false, "Không thể bỏ chặn chính mình.");
            }

            var block = await _db.RoomBlocks
                .FirstOrDefaultAsync(b => b.RoomId == roomId
                    && b.BlockedByUserId == blockedByUserId
                    && b.BlockedUserId == blockedUserId);

            if (block == null)
            {
                return (false, "Không có lượt chặn nào cần bỏ.");
            }

            _db.RoomBlocks.Remove(block);
            await _db.SaveChangesAsync();

            return (true, null);
        }
    }
}
