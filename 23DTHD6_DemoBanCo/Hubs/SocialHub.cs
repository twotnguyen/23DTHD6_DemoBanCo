using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;
using _23DTHD6_DemoBanCo.Services;

namespace _23DTHD6_DemoBanCo.Hubs
{
    /// <summary>
    /// Kênh realtime cho phần xã hội: kết bạn, nhắn tin riêng 1-1, và mời bạn vào phòng cờ.
    ///
    /// LUẬT BẢO MẬT ÉP BUỘC Ở ĐÂY (không được nới lỏng ở tầng client):
    ///   1. Danh tính người gửi LUÔN lấy từ cookie (Context.User), không bao giờ tin
    ///      client tự khai userId.
    ///   2. Nhắn tin riêng CHỈ được với người đã là bạn bè (status = Accepted).
    ///      Đây là đặc tả EC-02 chặn spam/quấy rối từ tài khoản lạ.
    ///   3. Mời vào phòng CHỈ gửi cho bạn bè đang Online, và lời mời tự hết hạn sau
    ///      30 giây (đặc tả 2.5) nên không lưu vào CSDL — lời mời là thứ tạm thời.
    ///   4. Mọi broadcast đi qua Clients.User / Clients.Caller, TUYỆT ĐỐI không
    ///      dùng Clients.All vì sẽ lộ tin riêng của người này cho người khác.
    /// </summary>
    [Authorize]
    public class SocialHub : Hub
    {
        /// <summary>
        /// Thời hạn một lời mời vào phòng (đặc tả 2.5). Hết hạn thì token bị gỡ khỏi
        /// bộ nhớ và người nhận nhận được sự kiện tự tắt pop-up.
        /// </summary>
        public const int InviteLifetimeSeconds = 30;

        private static readonly ConcurrentDictionary<string, PendingInvite> _invites = new();

        private readonly FriendshipService _friendship;
        private readonly DirectMessageService _directMessages;
        private readonly RoomService _rooms;
        private readonly AppDbContext _db;

        public SocialHub(
            FriendshipService friendship,
            DirectMessageService directMessages,
            RoomService rooms,
            AppDbContext db)
        {
            _friendship = friendship;
            _directMessages = directMessages;
            _rooms = rooms;
            _db = db;
        }

        /// <summary>
        /// Một lời mời đang chờ trả lời. Chỉ tồn tại trong RAM: nếu server sập thì lời mời
        /// mất, và điều đó đúng — người nhận phải vào lại phòng từ link/mã nếu cần.
        /// </summary>
        private sealed record PendingInvite(
            string Token,
            int RoomId,
            string RoomName,
            int FromUserId,
            string FromDisplayName,
            int ToUserId,
            DateTime ExpiresAt);

        private int CurrentUserId
        {
            get
            {
                string? value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                return int.TryParse(value, out int id) ? id : 0;
            }
        }

        private int CurrentUserIdOrThrow
        {
            get
            {
                int id = CurrentUserId;
                if (id == 0)
                {
                    throw new HubException("Chưa đăng nhập.");
                }

                return id;
            }
        }

        /// <summary>
        /// Clients.User nhận khoá string. Hub đăng ký connection theo claim
        /// NameIdentifier (cũng là string), nên mọi chỗ gửi tin riêng đều phải ép sang
        /// string — truyền số nguyên sẽ không khớp kết nối nào và tin bị rơi im lặng.
        /// </summary>
        private static string Key(int userId) => userId.ToString();

        // =========================================
        // Bạn bè
        // =========================================

        /// <summary>
        /// Gửi lời mời kết bạn theo tên đăng nhập (người dùng nhớ tên chứ không nhớ id).
        /// Trả về Id của lời mời vừa tạo, 0 nếu thất bại.
        /// </summary>
        public async Task<int> SendFriendRequest(string username)
        {
            int userId = CurrentUserIdOrThrow;

            string wanted = (username ?? "").Trim();
            if (wanted.Length == 0)
            {
                await RejectAsync("Vui lòng nhập tên đăng nhập.");
                return 0;
            }

            var target = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == wanted);

            if (target == null)
            {
                await RejectAsync("Không tìm thấy người dùng này.");
                return 0;
            }

            var (ok, error) = await _friendship.SendRequestAsync(userId, target.Id);

            if (!ok)
            {
                await RejectAsync(error ?? "Không gửi được lời mời kết bạn.");
                return 0;
            }

            // Báo cho người nhận để họ thấy thông báo ngay, không phải tải lại trang.
            await Clients.User(Key(target.Id)).SendAsync("FriendRequestReceived", new
            {
                fromUserId = userId,
                fromDisplayName = await DisplayNameAsync(userId),
                message = "Bạn có một lời mời kết bạn mới."
            });

            var pending = await _db.Friendships
                .AsNoTracking()
                .FirstOrDefaultAsync(f =>
                    (f.UserId == Math.Min(userId, target.Id) && f.FriendId == Math.Max(userId, target.Id))
                    && f.Status == FriendshipStatus.Pending);

            return pending?.Id ?? 0;
        }

        /// <summary>
        /// Trả lời lời mời kết bạn. Cả hai bên đều nhận thông báo để danh sách bạn
        /// của họ cập nhật ngay mà không phải F5.
        /// </summary>
        public async Task RespondFriendRequest(int requestId, bool accept)
        {
            int userId = CurrentUserIdOrThrow;

            // Đọc trước để biết phía bên kia là ai, vì service chỉ trả về ok/error.
            var friendship = await _db.Friendships
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == requestId);

            if (friendship == null)
            {
                await RejectAsync("Không tìm thấy lời mời.");
                return;
            }

            int otherUserId = friendship.UserId == userId ? friendship.FriendId : friendship.UserId;

            var (ok, error) = await _friendship.RespondAsync(requestId, userId, accept);

            if (!ok)
            {
                await RejectAsync(error ?? "Không trả lời được lời mời.");
                return;
            }

            await Clients.User(Key(userId)).SendAsync("FriendshipChanged", new
            {
                otherUserId,
                status = accept ? "Accepted" : "Rejected"
            });

            await Clients.User(Key(otherUserId)).SendAsync("FriendshipChanged", new
            {
                otherUserId = userId,
                status = accept ? "Accepted" : "Rejected"
            });
        }

        /// <summary>Xoá bạn bè. Cả hai bên đều được báo để gỡ khỏi danh sách.</summary>
        public async Task RemoveFriend(int friendUserId)
        {
            int userId = CurrentUserIdOrThrow;

            bool removed = await _friendship.RemoveAsync(userId, friendUserId);

            if (!removed)
            {
                await RejectAsync("Không xoá được bạn này.");
                return;
            }

            await Clients.User(Key(userId)).SendAsync("FriendshipChanged",
                new { otherUserId = friendUserId, status = "Removed" });

            await Clients.User(Key(friendUserId)).SendAsync("FriendshipChanged",
                new { otherUserId = userId, status = "Removed" });
        }

        // =========================================
        // Tin nhắn riêng 1-1
        // =========================================

        /// <summary>
        /// Gửi tin nhắn riêng. Service chặn sẵn trường hợp chưa là bạn bè và lọc từ
        /// cấm, nên ở đây chỉ cần chuyển lỗi về cho người gửi — không phát ra cho
        /// phía bên kia, vì lời gửi không hợp lệ thì bên kia không cần biết.
        /// </summary>
        public async Task SendDirectMessage(int toUserId, string body, string? sticker)
        {
            int userId = CurrentUserIdOrThrow;

            var (ok, error) = await _directMessages.SendAsync(userId, toUserId, body, sticker);

            if (!ok)
            {
                await Clients.Caller.SendAsync("ChatRejected", new { error });
                return;
            }

            var history = await _directMessages.GetHistoryAsync(userId, toUserId, 1);
            var latest = history.LastOrDefault();

            var payload = new
            {
                id = latest?.Id ?? 0,
                userId,
                displayName = await DisplayNameAsync(userId),
                conversationId = latest?.ConversationId ?? 0,
                body = latest?.Body ?? "",
                kind = latest?.Kind ?? 0,
                sticker = latest?.Sticker,
                createdAt = latest?.CreatedAt.ToString("O") ?? ""
            };

            // Gửi cho cả hai: người gửi cần thấy tin ngay, người nhận cần chấm đỏ chưa đọc.
            await Clients.User(Key(userId)).SendAsync("DirectMessageReceived", payload);
            await Clients.User(Key(toUserId)).SendAsync("DirectMessageReceived", payload);

            int unread = await _directMessages.GetUnreadCountAsync(toUserId);
            await Clients.User(Key(toUserId)).SendAsync("UnreadCountChanged", new { count = unread });
        }

        /// <summary>Đánh dấu đã đọc rồi báo lại số chưa đọc cho thanh điều hướng.</summary>
        public async Task MarkDirectMessagesRead(int otherUserId)
        {
            int userId = CurrentUserIdOrThrow;

            await _directMessages.MarkAsReadAsync(userId, otherUserId);

            int unread = await _directMessages.GetUnreadCountAsync(userId);
            await Clients.Caller.SendAsync("UnreadCountChanged", new { count = unread });
        }

        // =========================================
        // Mời bạn vào phòng (đặc tả 2.5)
        // =========================================

        /// <summary>
        /// Mời một người bạn vào phòng cờ. Năm điều kiện phải thoả mới gửi được, và tất cả
        /// đều kiểm ở server: gọi thiếu thì người lạ có thể dùng để spam hoặc để xem
        /// phòng xếp hạng.
        /// </summary>
        public async Task<string> InviteFriendToRoom(int roomId, int friendUserId)
        {
            int userId = CurrentUserIdOrThrow;

            if (userId == friendUserId)
            {
                await RejectAsync("Bạn không thể tự mời chính mình.");
                return "";
            }

            // Chỉ đấu thủ trong phòng được mời; khán giả không có quyền rải lời mời.
            bool isPlayer = await _db.RoomParticipants.AsNoTracking().AnyAsync(p =>
                p.RoomId == roomId && p.UserId == userId && p.Role == ParticipantRole.Player);

            if (!isPlayer)
            {
                await RejectAsync("Chỉ đấu thủ trong phòng mới được mời người khác.");
                return "";
            }

            // Chỉ mời được bạn bè đã chấp nhận, không mời người lạ.
            if (!await _friendship.AreFriendsAsync(userId, friendUserId))
            {
                await RejectAsync("Chỉ có thể mời người đã là bạn bè.");
                return "";
            }

            // Bạn offline thì không gửi được: pop-up sẽ nằm vô nghĩa 30 giây rồi tự tắt.
            bool online = await _db.RoomParticipants.AsNoTracking()
                .AnyAsync(p => p.UserId == friendUserId && p.ConnectionId != "");

            if (!online)
            {
                await RejectAsync("Ngoại tuyến");
                return "";
            }

            var room = await _rooms.GetByIdAsync(roomId);

            if (room == null || room.Status == RoomStatus.Closed)
            {
                await RejectAsync("Phòng không còn hoạt động.");
                return "";
            }

            if (room.Participants.Count >= RoomService.MaxRoomCapacity)
            {
                await RejectAsync("Phòng thi đấu đã đầy người, vui lòng chọn phòng khác!");
                return "";
            }

            string token = Guid.NewGuid().ToString("N");

            _invites[token] = new PendingInvite(
                token,
                room.Id,
                room.Name,
                userId,
                await DisplayNameAsync(userId),
                friendUserId,
                DateTime.UtcNow.AddSeconds(InviteLifetimeSeconds));

            await Clients.User(Key(friendUserId)).SendAsync("RoomInviteReceived", new
            {
                token,
                roomId = room.Id,
                roomName = room.Name,
                fromUserId = userId,
                fromDisplayName = await DisplayNameAsync(userId),
                expiresInSeconds = InviteLifetimeSeconds
            });

            // Tự dọn sau 30 giây. Client có thể đóng tab nên không được tin client
            // báo "đã hết hạn" — hẹn giờ ở server là cách duy nhất chắc chắn.
            _ = ExpireInviteLaterAsync(token, room.Id, friendUserId);

            return token;
        }

        /// <summary>
        /// Trả lời lời mời. Token hết hạn thì trả về lỗi thay vì vào phòng: vào được bằng
        /// link cũ sau 30 giây sẽ mở đường cho người bị chặn lọt vào phòng.
        /// </summary>
        public async Task<bool> RespondRoomInvite(string token, bool accept)
        {
            int userId = CurrentUserIdOrThrow;

            if (!_invites.TryGetValue(token ?? "", out var invite)
                || invite.ExpiresAt <= DateTime.UtcNow)
            {
                await RejectAsync("Lời mời đã hết hạn hoặc không còn hiệu lực.");
                return false;
            }

            if (invite.ToUserId != userId)
            {
                await RejectAsync("Lời mời này không dành cho bạn.");
                return false;
            }

            _invites.TryRemove(token!, out _);

            if (!accept)
            {
                await Clients.User(Key(invite.FromUserId)).SendAsync("RoomInviteDeclined", new
                {
                    roomId = invite.RoomId,
                    byUserId = userId
                });

                return false;
            }

            // Cả hai bên đều nhận: người mời biết đã vào được, người nhận cần
            // đường dẫn để tự chuyển tới phòng chờ.
            await Clients.User(Key(invite.FromUserId)).SendAsync("RoomInviteAccepted", new
            {
                roomId = invite.RoomId,
                byUserId = userId,
                displayName = await DisplayNameAsync(userId)
            });

            await Clients.Caller.SendAsync("RoomInviteAccepted", new
            {
                roomId = invite.RoomId,
                fromUserId = invite.FromUserId,
                roomName = invite.RoomName,
                displayName = invite.FromDisplayName
            });

            return true;
        }

        /// <summary>
        /// Gỡ lời mời khỏi bộ nhớ sau khi hết hạn và báo người nhận để pop-up tự tắt.
        /// Chạy nền, không chặn hub: nếu không có ai trả lời thì hàm kết thúc im lặng.
        /// </summary>
        private async Task ExpireInviteLaterAsync(string token, int roomId, int toUserId)
        {
            await Task.Delay(TimeSpan.FromSeconds(InviteLifetimeSeconds));

            if (!_invites.TryRemove(token, out _))
            {
                // Đã được trả lời trước khi hết hạn: không cần báo gì thêm.
                return;
            }

            try
            {
                await Clients.User(Key(toUserId)).SendAsync("RoomInviteExpired", new
                {
                    token,
                    roomId,
                    reason = "Lời mời đã hết hạn sau 30 giây."
                });
            }
            catch
            {
                // Người nhận đã ngắt kết nối: bỏ qua, dọn token là việc chính.
            }
        }

        // =========================================
        // Nội bộ
        // =========================================

        /// <summary>
        /// Lỗi nghiệp vụ đi về CHÍNH người gửi. Không ném exception vì sẽ làm client
        /// mất hết các handler đã đăng ký trong lúc xử lý lỗi.
        /// </summary>
        private Task RejectAsync(string error)
            => Clients.Caller.SendAsync("SocialRejected", new { error });

        private async Task<string> DisplayNameAsync(int userId)
        {
            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            return user?.DisplayName ?? "Người chơi";
        }
    }
}