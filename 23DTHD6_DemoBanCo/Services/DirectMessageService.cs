using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Tin nhắn riêng 1-1.
    ///
    /// QUY TẮC QUAN TRỌNG: chỉ nhắn tin được với người ĐÃ LÀ BẠN BÈ, tức là dòng
    /// Friendship của cặp đang ở trạng thái Accepted. Lời mời đang chờ (Pending)
    /// hoặc đã bị từ chối (Rejected) đều không được nhắn. Luật này do
    /// FriendshipService.AreFriendsAsync kiểm, không tin vào client.
    ///
    /// Cuộc hội thoại lưu cặp id theo thứ tự tăng dần (UserALowId, UserBHighId) nên
    /// mỗi cặp người chỉ có đúng một cuộc hội thoại, không tạo trùng.
    /// </summary>
    public sealed class DirectMessageService
    {
        public const int MaxMessageLength = 500;

        private readonly AppDbContext _db;
        private readonly FriendshipService _friendshipService;

        public DirectMessageService(AppDbContext db, FriendshipService friendshipService)
        {
            _db = db;
            _friendshipService = friendshipService;
        }

        /// <summary>
        /// Gửi một tin nhắn riêng. Trả về (thành công, lỗi tiếng Việt hoặc null).
        /// Không ném exception để hub chỉ cần trả lỗi cho người gửi.
        /// </summary>
        public async Task<(bool ok, string? error)> SendAsync(
            int fromUserId,
            int toUserId,
            string body,
            string? sticker)
        {
            if (fromUserId == toUserId)
            {
                return (false, "Không thể tự nhắn tin cho chính mình.");
            }

            // Luật cốt lõi: chưa là bạn bè thì không nhắn được, kể cả đã từng gửi lời mời.
            if (!await _friendshipService.AreFriendsAsync(fromUserId, toUserId))
            {
                return (false, "Chỉ có thể nhắn tin với người đã là bạn bè.");
            }

            int kind;
            string text;
            string? stickerCode = null;

            if (!string.IsNullOrWhiteSpace(sticker))
            {
                string requested = sticker.Trim();

                if (!ChatService.IsKnownSticker(requested))
                {
                    return (false, "Sticker không có trong danh sách cho phép.");
                }

                kind = 1;
                text = "";
                stickerCode = requested;
            }
            else
            {
                string trimmed = (body ?? "").Trim();

                if (trimmed.Length == 0)
                {
                    return (false, "Nội dung tin nhắn không được để trống.");
                }

                if (trimmed.Length > MaxMessageLength)
                {
                    return (false, $"Nội dung tin nhắn tối đa {MaxMessageLength} ký tự.");
                }

                kind = 0;

                // Lọc từ cấm trước khi ghi, nên dữ liệu lưu xuống đã sạch.
                text = BadWordFilter.Filter(trimmed);
            }

            var conversation = await GetOrCreateConversationAsync(fromUserId, toUserId);

            if (conversation == null)
            {
                return (false, "Không tạo được cuộc hội thoại.");
            }

            _db.DirectMessages.Add(new DirectMessage
            {
                ConversationId = conversation.Id,
                SenderId = fromUserId,
                Body = text,
                Kind = kind,
                Sticker = stickerCode,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return (true, null);
        }

        /// <summary>Lấy tin nhắn gần nhất của một cuộc hội thoại, mới nhất nằm cuối.</summary>
        public async Task<List<DirectMessage>> GetHistoryAsync(int userId, int otherUserId, int take = 50)
        {
            if (take <= 0 || userId == otherUserId)
            {
                return new List<DirectMessage>();
            }

            var conversation = await FindConversationAsync(userId, otherUserId);

            if (conversation == null)
            {
                return new List<DirectMessage>();
            }

            return await _db.DirectMessages
                .AsNoTracking()
                .Where(m => m.ConversationId == conversation.Id)
                .OrderByDescending(m => m.Id)
                .Take(take)
                .OrderBy(m => m.Id)
                .ToListAsync();
        }

        /// <summary>
        /// Số tin chưa đọc của một người: chỉ tính tin gửi TỚI người đó.
        /// Tin mình gửi ra không tính, nên không bao giờ tự thấy badge của chính mình.
        /// </summary>
        public async Task<int> GetUnreadCountAsync(int userId)
        {
            return await _db.DirectMessages
                .Where(m => m.ReadAt == null)
                .Join(
                    _db.DirectConversations,
                    m => m.ConversationId,
                    c => c.Id,
                    (m, c) => new { m, c })
                .Where(x => x.m.SenderId != userId
                    && (x.c.UserALowId == userId || x.c.UserBHighId == userId))
                .CountAsync();
        }

        /// <summary>Đánh dấu đã đọc mọi tin của phía đối diện gửi tới. Tin mình gửi không đụng tới.</summary>
        public async Task MarkAsReadAsync(int userId, int otherUserId)
        {
            if (userId == otherUserId)
            {
                return;
            }

            var conversation = await FindConversationAsync(userId, otherUserId);

            if (conversation == null)
            {
                return;
            }

            await _db.DirectMessages
                .Where(m => m.ConversationId == conversation.Id
                    && m.SenderId == otherUserId
                    && m.ReadAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.ReadAt, (DateTime?)DateTime.UtcNow));
        }

        /// <summary>
        /// Danh sách khung chat bên trái: mỗi cuộc hội thoại một dòng, kèm tin cuối
        /// cùng số tin chưa đọc để giao diện vẽ badge.
        /// </summary>
        public async Task<List<(int OtherUserId, string DisplayName, string LastMessage, DateTime LastAt, int Unread)>>
            GetConversationListAsync(int userId)
        {
            var conversations = await _db.DirectConversations
                .AsNoTracking()
                .Where(c => c.UserALowId == userId || c.UserBHighId == userId)
                .ToListAsync();

            if (conversations.Count == 0)
            {
                return new List<(int OtherUserId, string DisplayName, string LastMessage, DateTime LastAt, int Unread)>();
            }

            var conversationIds = conversations.Select(c => c.Id).ToList();

            var messages = await _db.DirectMessages
                .AsNoTracking()
                .Where(m => conversationIds.Contains(m.ConversationId))
                .ToListAsync();

            // Id của phía đối diện, đổi công thức tuỳ vào mình đang là A hay B.
            var otherIds = conversations
                .Select(c => c.UserALowId == userId ? c.UserBHighId : c.UserALowId)
                .Distinct()
                .ToList();

            var names = await _db.Users
                .AsNoTracking()
                .Where(u => otherIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            // Phải khai báo có TÊN phần tử giống kiểu trả về, nếu khai báo vô danh thì
            // các thành phần như LastAt sẽ không tồn tại và dòng sắp xếp bên dưới không biết đọc gì.
            var result = new List<(int OtherUserId, string DisplayName, string LastMessage, DateTime LastAt, int Unread)>();

            foreach (var conversation in conversations)
            {
                int otherId = conversation.UserALowId == userId
                    ? conversation.UserBHighId
                    : conversation.UserALowId;

                var thread = messages
                    .Where(m => m.ConversationId == conversation.Id)
                    .OrderBy(m => m.Id)
                    .ToList();

                var last = thread.LastOrDefault();

                if (last == null)
                {
                    // Cuộc hội thoại chưa có tin nào: vẫn hiện để người dùng mở và nhắn.
                    result.Add((
                        otherId,
                        names.TryGetValue(otherId, out var emptyName) ? emptyName : "Người chơi",
                        "",
                        conversation.CreatedAt,
                        0));
                    continue;
                }

                int unread = thread.Count(m => m.SenderId != userId && m.ReadAt == null);

                // Tin sticker không có Body, hiện mã sticker để giao diện render emoji.
                string preview = last.Kind == 1 && last.Sticker != null ? last.Sticker : last.Body;

                result.Add((
                    otherId,
                    names.TryGetValue(otherId, out var name) ? name : "Người chơi",
                    preview,
                    last.CreatedAt,
                    unread));
            }

            // Tin mới nhất lên đầu, như khung chat thường thấy.
            result.Sort((a, b) => b.LastAt.CompareTo(a.LastAt));

            return result;
        }

        // =========================================
        // Nội bộ
        // =========================================

        /// <summary>Tìm cuộc hội thoại của một cặp, không tạo mới.</summary>
        private Task<DirectConversation?> FindConversationAsync(int userId, int otherUserId)
        {
            int low = Math.Min(userId, otherUserId);
            int high = Math.Max(userId, otherUserId);

            return _db.DirectConversations
                .FirstOrDefaultAsync(c => c.UserALowId == low && c.UserBHighId == high);
        }

        /// <summary>
        /// Lấy cuộc hội thoại, chưa có thì tạo. Ghi ngay sau khi tạo để lấy được Id
        /// mà vẫn an toàn với chỉ mục duy nhất: cặp id đã được sắp thứ tự từ trước,
        /// nên hai lệnh cùng lúc vẫn không tạo ra hai cuộc hội thoại trùng nhau.
        /// </summary>
        private async Task<DirectConversation?> GetOrCreateConversationAsync(int userId, int otherUserId)
        {
            var existing = await FindConversationAsync(userId, otherUserId);

            if (existing != null)
            {
                return existing;
            }

            var conversation = new DirectConversation
            {
                UserALowId = Math.Min(userId, otherUserId),
                UserBHighId = Math.Max(userId, otherUserId),
                CreatedAt = DateTime.UtcNow
            };

            _db.DirectConversations.Add(conversation);
            await _db.SaveChangesAsync();

            return conversation;
        }
    }
}
