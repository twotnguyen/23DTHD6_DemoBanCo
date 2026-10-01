using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;
using _23DTHD6_DemoBanCo.Services;

namespace _23DTHD6_DemoBanCo.Controllers
{
    /// <summary>
    /// Bạn bè và tin nhắn riêng 1-1 ngoài phòng thi đấu.
    ///
    /// VÌ SAO GÕI TAY QUA HUB MÀ KHÔNG DÙNG SIGNALR:
    ///   SocialHub có đủ phần realtime, nhưng luồng nhắn tin vẫn phải chạy được khi
    ///   kết nối WebSocket bị tụt (phòng hội đồng Wi-Fi yếu). Nút "Gửi" ở đây là một
    ///   form POST thật, nên người dùng không bao giờ mất tin vì rớt mạng; hub chỉ lo
    ///   đẩy tin tới các cửa sổ khác đang mở, và thay số badge chưa đọc trên thanh
    ///   điều hướng. Hai tầng cùng dùng một service nên không có đường ghi trùng.
    ///
    /// LUẬT NGHIỆP VỤ ÉP BUỘC Ở ĐÂY:
    ///   - Nhắn tin riêng CHỈ với bạn bè đã chấp nhận (đặc tả EC-02). Service
    ///     DirectMessageService tự chặn; ở đây chỉ chuyển lời từ chối về cho người gửi.
    ///   - Mọi thao tác đều lấy danh tính từ cookie, không tin userId do client gửi lên.
    /// </summary>
    [Authorize]
    public class SocialController : Controller
    {
        private readonly AppDbContext _db;
        private readonly FriendshipService _friendship;
        private readonly DirectMessageService _directMessages;

        public SocialController(
            AppDbContext db,
            FriendshipService friendship,
            DirectMessageService directMessages)
        {
            _db = db;
            _friendship = friendship;
            _directMessages = directMessages;
        }

        private int CurrentUserId
        {
            get
            {
                string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
                return int.TryParse(value, out int id) ? id : 0;
            }
        }

        /// <summary>
        /// Trang bạn bè: danh sách bạn đã kết, trạng thái online, và các lời mời đang chờ.
        ///
        /// "Đang online" suy ra từ RoomParticipant.ConnectionId khác rỗng: mỗi kết nối
        /// SignalR đang sống giữ một dòng tham gia phòng có ConnectionId. Người chưa
        /// vào phòng nào thì không có dòng nào, nên bị coi là ngoại tuyến — đây là
        /// cách đơn giản và đúng với mô hình "chơi qua phòng" của dự án.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Friends()
        {
            int userId = CurrentUserId;

            var friends = await _friendship.GetFriendsAsync(userId);

            var friendIds = friends.Select(f => f.UserId).ToList();

            // EF Core 8 chưa có ToHashSetAsync cho IQueryable, nên lấy List trước rồi
            // đổi sang HashSet. Số bạn tối đa 50 nên chuyển đổi hai lần là rẻ.
            var onlineIds = friendIds.Count == 0
                ? new List<int>()
                : await _db.RoomParticipants.AsNoTracking()
                    .Where(p => friendIds.Contains(p.UserId) && p.ConnectionId != "")
                    .Select(p => p.UserId)
                    .Distinct()
                    .ToListAsync();

            var online = onlineIds.ToHashSet();


            // Lời mời đến: những dòng mà mình là phía nhận (FriendId). Join sang
            // bảng Users để lấy tên hiển thị, vì Friendship chỉ giữ hai con số id.
            var incoming = await _db.Friendships.AsNoTracking()
                .Where(f => f.FriendId == userId && f.Status == FriendshipStatus.Pending)
                .Join(
                    _db.Users.AsNoTracking(),
                    f => f.UserId,
                    u => u.Id,
                    (f, u) => new { RequestId = f.Id, UserId = u.Id, Username = u.Username, DisplayName = u.DisplayName })
                .ToListAsync();

            ViewBag.Friends = friends;
            ViewBag.Online = online;
            ViewBag.Incoming = incoming;
            ViewBag.CurrentUserId = userId;

            return View();
        }

        /// <summary>
        /// Gửi lời mời kết bạn theo tên đăng nhập, vì người dùng nhớ tên chứ không nhớ id.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendFriendRequest(string username)
        {
            int userId = CurrentUserId;

            string wanted = (username ?? "").Trim();

            var target = wanted.Length == 0
                ? null
                : await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == wanted);

            if (target == null)
            {
                TempData["Notice"] = "Không tìm thấy người dùng này.";
                return RedirectToAction(nameof(Friends));
            }

            var (ok, error) = await _friendship.SendRequestAsync(userId, target.Id);

            TempData["Notice"] = ok ? "Đã gửi lời mời kết bạn." : error;

            return RedirectToAction(nameof(Friends));
        }

        /// <summary>Chấp nhận hoặc từ chối một lời mời kết bạn đang chờ.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RespondFriendRequest(int requestId, bool accept)
        {
            int userId = CurrentUserId;

            var (ok, error) = await _friendship.RespondAsync(requestId, userId, accept);

            TempData["Notice"] = ok
                ? (accept ? "Đã chấp nhận lời mời kết bạn." : "Đã từ chối lời mời.")
                : error;

            return RedirectToAction(nameof(Friends));
        }

        /// <summary>Xoá bạn bè.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFriend(int friendUserId)
        {
            int userId = CurrentUserId;

            bool removed = await _friendship.RemoveAsync(userId, friendUserId);

            TempData["Notice"] = removed ? "Đã xoá bạn." : "Không xoá được bạn này.";

            return RedirectToAction(nameof(Friends));
        }

        /// <summary>
        /// Khung tin nhắn: cột trái là danh sách cuộc hội thoại kèm badge chưa đọc,
        /// cột phải là lịch sử với người được chọn. Không chọn ai thì cột phải để trống.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Messages(int? with = null)
        {
            int userId = CurrentUserId;

            var conversations = await _directMessages.GetConversationListAsync(userId);
            ViewBag.Conversations = conversations;

            // View dùng UserId này để canh phải bong bóng tin của chính mình, nên phải
            // có ở MỌI nhánh trả về — thiếu ở nhánh nào thì nhánh đó ném lỗi.
            ViewBag.CurrentUserId = userId;

            if (with is null or 0)
            {
                ViewBag.PeerId = 0;
                ViewBag.PeerName = "";
                ViewBag.History = new List<DirectMessage>();
                ViewBag.Stickers = ChatService.Stickers;
                return View();
            }

            int peerId = with.Value;

            // Chỉ mở khung chat với người đã là bạn bè. Nếu không kiểm ở đây thì URL
            // gõ tay vẫn xem được lịch sử chat của người lạ — lỗi rò rỉ riêng tư.
            if (!await _friendship.AreFriendsAsync(userId, peerId))
            {
                TempData["Notice"] = "Chỉ có thể nhắn tin với người đã là bạn bè.";
                return RedirectToAction(nameof(Messages));
            }

            var history = await _directMessages.GetHistoryAsync(userId, peerId);

            // Đánh dấu đã đọc SAU khi lấy lịch sử: nếu làm trước thì badge vẫn còn
            // một tin vừa mới, người dùng phải F5 mới thấy sạch.
            await _directMessages.MarkAsReadAsync(userId, peerId);

            ViewBag.PeerId = peerId;
            ViewBag.PeerName = await _db.Users.AsNoTracking()
                .Where(u => u.Id == peerId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync() ?? "Người chơi";
            ViewBag.History = history;
            ViewBag.Stickers = ChatService.Stickers;

            return View();
        }

        /// <summary>
        /// Gửi tin nhắn riêng bằng form POST. Không dùng hub để gửi: nếu WebSocket
        /// rớt giữa lúc đang gõ thì tin vẫn phải gửi được, không được nuốt mất.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(int toUserId, string body, string? sticker)
        {
            int userId = CurrentUserId;

            var (ok, error) = await _directMessages.SendAsync(userId, toUserId, body, sticker);

            if (!ok)
            {
                TempData["Notice"] = error;
            }

            return RedirectToAction(nameof(Messages), new { with = toUserId });
        }
    }
}