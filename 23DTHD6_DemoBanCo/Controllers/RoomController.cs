using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Hubs;
using _23DTHD6_DemoBanCo.Services;
using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Controllers
{
    [Authorize]
    public class RoomController : Controller
    {
        private readonly AppDbContext _db;
        private readonly RoomService _roomService;
        private readonly IHubContext<ChessHub> _hubContext;

        public RoomController(AppDbContext db, RoomService roomService, IHubContext<ChessHub> hubContext)
        {
            _db = db;
            _roomService = roomService;
            _hubContext = hubContext;
        }

        /// <summary>Lấy Id người đang đăng nhập từ cookie.</summary>
        private int CurrentUserId
        {
            get
            {
                string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
                return int.TryParse(value, out int id) ? id : 0;
            }
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            int userId = CurrentUserId;

            var allRooms = await _roomService.GetRoomsWithDetailsAsync();
            var myRooms = await _roomService.GetRoomsByOwnerAsync(userId);

            ViewBag.MyRooms = myRooms;
            ViewBag.AllRooms = allRooms;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string? name)
        {
            int userId = CurrentUserId;

            if (userId == 0)
                return Challenge();

            var room = await _roomService.CreateRoomAsync(userId, name);

            // Phòng mới tạo thì vào thẳng màn hình phòng chờ, có bàn cờ, mã QR
            // và các nút điều khiển. Details là view cũ, chỉ giữ để tương thích.
            return RedirectToAction("Waiting", new { id = room.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            int userId = CurrentUserId;

            var room = await _roomService.GetByIdAsync(id);
            if (room == null)
                return NotFound("Không tìm thấy phòng này.");

            ViewBag.IsOwner = room.OwnerId == userId;
            ViewBag.CurrentUserId = userId;
            return View(room);
        }

        /// <summary>Mở phòng theo mã, dùng khi người khác nhập mã phòng.</summary>
        [HttpGet]
        public async Task<IActionResult> Join(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return RedirectToAction("Index");

            var room = await _roomService.GetByCodeAsync(code);
            if (room == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy phòng với mã này.";
                return RedirectToAction("Index");
            }

            // Giữ tương thích với link cũ, nhưng đi tới phòng chờ cho đầy đủ tính năng
            return RedirectToAction("Waiting", new { id = room.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            int userId = CurrentUserId;

            bool deleted = await _roomService.DeleteRoomAsync(id, userId);
            if (!deleted)
            {
                TempData["ErrorMessage"] = "Chỉ chủ phòng mới có thể xoá phòng này.";
                return RedirectToAction("Index");
            }

            return RedirectToAction("Index");
        }
        /// <summary>Sảnh: danh sách phòng công khai, người chơi chọn chế độ rồi tạo phòng.</summary>
        [HttpGet]
        public async Task<IActionResult> Lobby()
        {
            int userId = CurrentUserId;
            if (userId == 0)
                return Challenge();

            ViewBag.PublicRooms = await _roomService.GetPublicRoomsAsync();
            ViewBag.MyRooms = await _roomService.GetRoomsByOwnerAsync(userId);
            ViewBag.CurrentUserId = userId;
            ViewBag.Capacity = RoomService.MaxRoomCapacity;

            // Điểm và bậc của chính người đang đăng nhập, hiện ngay trên sảnh
            var me = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            ViewBag.MyElo = me?.Elo ?? EloCalculator.StartingElo;
            ViewBag.MyWins = me?.Wins ?? 0;
            ViewBag.MyLosses = me?.Losses ?? 0;
            ViewBag.MyTierName = EloCalculator.GetTierName(
                EloCalculator.GetTier(me?.Elo ?? EloCalculator.StartingElo));

            return View();
        }

        /// <summary>
        /// Màn hình phòng chờ. Mọi thông tin cho giao diện đặt vào ViewBag theo
        /// đúng convention sẵn có của dự án, không dùng ViewModel mới.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Waiting(int id)
        {
            int userId = CurrentUserId;
            if (userId == 0)
                return Challenge();

            var room = await _roomService.GetByIdAsync(id);
            if (room == null)
                return NotFound("Không tìm thấy phòng này.");

            if (await _roomService.IsBlockedAsync(id, userId))
            {
                TempData["ErrorMessage"] = "Bạn đã bị chủ phòng đuổi khỏi phòng này.";
                return RedirectToAction("Lobby");
            }
            // Vào thẳng bằng link, QR hay F5 lại trang thì vẫn phải được ghi vào
            // phòng, nếu không sẽ không thấy ghế và không ready được.
            var (joinedRoom, joinError) = await JoinRoomAsParticipantAsync(room, userId);
            if (joinError != null)
            {
                TempData["ErrorMessage"] = joinError;
                return RedirectToAction("Lobby");
            }

            room = joinedRoom;
            // Lấy lại từ phòng vừa nạp lại, nếu không biến mine sẽ trỏ tới
            // danh sách cũ và không có người vừa vào.
            var mine = room.Participants.FirstOrDefault(p => p.UserId == userId);


            ViewBag.CurrentUserId = userId;
            ViewBag.IsOwner = room.OwnerId == userId;
            ViewBag.MyRole = mine?.Role ?? ParticipantRole.Spectator;
            ViewBag.MySide = mine?.Side;
            ViewBag.IsMyTurnReady = mine?.IsReady ?? false;
            ViewBag.PlayerCount = room.Participants.Count(p => p.Role == ParticipantRole.Player);
            ViewBag.SpectatorCount = room.Participants.Count(p => p.Role == ParticipantRole.Spectator);
            ViewBag.Capacity = RoomService.MaxRoomCapacity;
            ViewBag.SideTaken = room.Participants.FirstOrDefault(p => p.Side != null)?.Side ?? "";
            ViewBag.Visibility = room.Visibility;
            ViewBag.RoomCode = room.Code;
            ViewBag.ErrorMessage = TempData["ErrorMessage"];

            return View(room);
        }

        /// <summary>
        /// Đăng ký người dùng vào phòng: đã có thì chỉ cập nhật, chưa có thì xếp ghế
        /// và vai trò. Dùng chung cho cả vào bằng mã lẫn mở thẳng /Room/Waiting/{id}
        /// để không xảy ra tình trạng vào được bằng mã nhưng F5 thì mất ghế.
        /// Trả về (phòng đã nạp lại, thông điệp lỗi). Thông điệp null nghĩa là vào được.
        /// </summary>
        private async Task<(Room Room, string? Error)> JoinRoomAsParticipantAsync(Room room, int userId)
        {
            // Đã trong phòng rồi thì không ghi lại, chỉ chờ cập nhật từ hub
            var existing = room.Participants.FirstOrDefault(p => p.UserId == userId);
            if (existing != null)
                return (room, null);

            if (room.Participants.Count >= RoomService.MaxRoomCapacity)
                return (room, "Phòng đã đủ người, bạn không vào được phòng này nữa.");

            // Ghế còn trống thì làm đấu thủ, hết ghế thì làm khán giả
            var role = await _roomService.DecideRoleAsync(room, userId);
            string? side = role == ParticipantRole.Player ? AssignFreeSide(room, userId) : null;

            await _roomService.AddParticipantAsync(room.Id, userId, "", side);

            // AddParticipantAsync chưa gán Role nên set riêng cho người vừa vào
            var participant = await _roomService.GetParticipantAsync(room.Id, userId);
            if (participant != null)
            {
                participant.Role = role;
                await _db.SaveChangesAsync();
            }

            // Nạp lại để phần hiển thị bên dưới thấy đủ người vừa vào
            return (await _roomService.GetByIdAsync(room.Id) ?? room, null);
        }

        /// <summary>Vào phòng bằng mã, chấp nhận cả mã 6 ký tự cũ lẫn mã 8 ký tự mới.</summary>
        [HttpGet]
        public async Task<IActionResult> JoinByCode(string code)
        {
            int userId = CurrentUserId;
            if (userId == 0)
                return Challenge();

            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["ErrorMessage"] = "Bạn chưa nhập mã phòng.";
                return RedirectToAction("Lobby");
            }

            var room = await _roomService.GetByCodeAsync(code);
            if (room == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy phòng với mã này.";
                return RedirectToAction("Lobby");
            }

            // Đã bị chủ phòng chặn thì không cho vào lại dù có đúng mã
            if (await _roomService.IsBlockedAsync(room.Id, userId))
            {
                TempData["ErrorMessage"] = "Bạn đã bị chủ phòng đuổi khỏi phòng này.";
                return RedirectToAction("Lobby");
            }

            // Phòng khoá chỉ mở khi có mã, nên người nhập mã vẫn vào được
            var (_, joinError) = await JoinRoomAsParticipantAsync(room, userId);
            if (joinError != null)
            {
                TempData["ErrorMessage"] = joinError;
                return RedirectToAction("Lobby");
            }

            return RedirectToAction("Waiting", new { id = room.Id });
        }

        /// <summary>Đổi ghế Đỏ / Đen khi phòng chưa đủ hai đấu thủ.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchSide(int roomId, string side)
        {
            int userId = CurrentUserId;

            bool ok = await _roomService.SwitchSideAsync(roomId, userId, side);
            if (!ok)
            {
                TempData["ErrorMessage"] = "Không đổi được ghế. Ghế đã có người ngồi, hoặc ván đã bắt đầu.";
                return RedirectToAction("Waiting", new { id = roomId });
            }

            // Phần đổi phe là của lobby nên không có ai khác phát lại danh sách
            // người chơi. Payload giữ đúng shape ChessHub đang dùng.
            await BroadcastRoomStateAsync(roomId);

            return RedirectToAction("Waiting", new { id = roomId });
        }

        /// <summary>
        /// Xin đổi ghế: nếu còn ghế trống thì chuyển thẳng sang ghế đó,
        /// còn lại báo lại để người chơi biết phải dùng nút đổi ghế.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestSwap(int roomId)
        {
            int userId = CurrentUserId;

            var participant = await _roomService.GetParticipantAsync(roomId, userId);
            if (participant == null)
            {
                TempData["ErrorMessage"] = "Bạn không ở trong phòng này.";
                return RedirectToAction("Lobby");
            }

            string? other = participant.Side == RoomService.SideRed
                ? RoomService.SideBlack
                : RoomService.SideRed;

            bool ok = await _roomService.SwitchSideAsync(roomId, userId, other ?? "");

            if (!ok)
            {
                TempData["ErrorMessage"] = "Ghế bên kia đã có người, bạn không đổi được lúc này.";
                return RedirectToAction("Waiting", new { id = roomId });
            }

            await BroadcastRoomStateAsync(roomId);

            TempData["ErrorMessage"] = "Đã đổi ghế của bạn.";
            return RedirectToAction("Waiting", new { id = roomId });
        }

        // Không còn action SetReady: nút "Sẵn sàng" gọi hub ToggleReady, và
        // ToggleReady tự gọi TryStartMatchAsync khi cả hai đã sẵn sàng. Giữ lại
        // action POST ở đây sẽ tạo thêm một nơi ghi cờ IsReady cạnh tranh với
        // hub, nên đã bỏ hẳn.

        /// <summary>Chủ phòng đổi chế độ công khai / theo mã / khoá.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeVisibility(int roomId, string visibility)
        {
            int userId = CurrentUserId;

            if (!Enum.TryParse(visibility, ignoreCase: true, out RoomVisibility parsed))
            {
                TempData["ErrorMessage"] = "Chế độ không hợp lệ.";
                return RedirectToAction("Waiting", new { id = roomId });
            }

            bool ok = await _roomService.SetVisibilityAsync(roomId, userId, parsed);
            if (!ok)
            {
                TempData["ErrorMessage"] = "Chỉ chủ phòng mới đổi được chế độ phòng.";
                return RedirectToAction("Waiting", new { id = roomId });
            }

            // Đồng bộ realtime: phát lại trạng thái phòng cho đúng group của phòng này
            await BroadcastRoomStateAsync(roomId);

            return RedirectToAction("Waiting", new { id = roomId });
        }

        /// <summary>
        /// Đuổi một người khỏi phòng. Quyền được kiểm ở tầng dữ liệu, không tin hidden field.
        /// Người bị đuổi bị ghi vào danh sách chặn nên không vào lại được nữa.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Kick(int roomId, int targetUserId)
        {
            int userId = CurrentUserId;

            var room = await _roomService.GetByIdAsync(roomId);
            if (room == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy phòng này.";
                return RedirectToAction("Lobby");
            }

            if (room.OwnerId != userId)
            {
                TempData["ErrorMessage"] = "Chỉ chủ phòng mới đuổi được người khác.";
                return RedirectToAction("Waiting", new { id = roomId });
            }

            var target = room.Participants.FirstOrDefault(p => p.UserId == targetUserId);
            if (target == null)
            {
                TempData["ErrorMessage"] = "Người đó không ở trong phòng.";
                return RedirectToAction("Waiting", new { id = roomId });
            }

            await _roomService.BlockUserAsync(roomId, targetUserId, userId);
            await _roomService.RemoveParticipantAsync(roomId, targetUserId);

            // Danh sách người trong phòng đã đổi, phát lại để không ai thấy
            // vị trí của người bị đuổi nữa.
            await BroadcastRoomStateAsync(roomId);

            TempData["ErrorMessage"] = "Đã đuổi người đó khỏi phòng.";
            return RedirectToAction("Waiting", new { id = roomId });
        }

        /// <summary>Trả về ảnh SVG của mã QR, dùng thẳng làm thuộc tính src.</summary>
        [HttpGet]
        public async Task<IActionResult> QrCode(int roomId, string scope)
        {
            int userId = CurrentUserId;

            var room = await _roomService.GetByIdAsync(roomId);
            if (room == null)
                return NotFound("Không tìm thấy phòng này.");

            if (room.OwnerId != userId)
            {
                TempData["ErrorMessage"] = "Chỉ chủ phòng mới xem được mã QR mời.";
                return RedirectToAction("Waiting", new { id = roomId });
            }

            // "join" mãi để vào phòng, "share" mãi mời chung
            string content = string.Equals(scope, "join", StringComparison.OrdinalIgnoreCase)
                ? Url.Action("JoinByCode", "Room", new { code = room.Code }, Request.Scheme)
                : Url.Action("Waiting", "Room", new { id = room.Id }, Request.Scheme);

            string svg = QrCodeService.GenerateSvg(content);
            return Content(svg, "image/svg+xml");
        }

        /// <summary>Chọn ghế còn trống cho người mới vào: Đỏ trước, còn thiếu thì Đen.</summary>
        private static string? AssignFreeSide(Room room, int userId)
        {
            var taken = room.Participants
                .Where(p => p.UserId != userId && p.Side != null)
                .Select(p => p.Side!)
                .ToHashSet();

            if (!taken.Contains(RoomService.SideRed))
                return RoomService.SideRed;

            if (!taken.Contains(RoomService.SideBlack))
                return RoomService.SideBlack;

            return null;
        }

        /// <summary>
        /// Phát lại trạng thái phòng cho mọi client đang ở trong group.
        /// Cùng shape với ChessHub để client chỉ cần xử lý một dạng dữ liệu.
        /// </summary>
        private async Task BroadcastRoomStateAsync(int roomId)
        {
            var room = await _roomService.GetByIdAsync(roomId);
            if (room == null)
                return;

            var people = room.Participants.OrderBy(p => p.JoinedAt).ToList();
            var group = _hubContext.Clients.Group($"room:{roomId}");

            var list = people
                .Select(p => new
                {
                    userId = p.UserId,
                    displayName = p.User?.DisplayName ?? "Người chơi",
                    side = p.Side,
                    sideLabel = p.Side switch
                    {
                        RoomService.SideRed => "Đỏ",
                        RoomService.SideBlack => "Đen",
                        _ => "Chưa chọn"
                    },
                    role = p.Role.ToString(),
                    isReady = p.IsReady,
                    joinedAt = p.JoinedAt
                })
                .ToList();

            await group.SendAsync("RoomParticipantsChanged", new
            {
                count = people.Count,
                capacity = RoomService.MaxRoomCapacity,
                participants = list
            });

            await group.SendAsync("RoomStateChanged", new
            {
                roomId,
                visibility = room.Visibility.ToString(),
                status = room.Status.ToString(),
                activeMatchId = room.ActiveMatchId,
                readyCount = people.Count(p => p.IsReady),
                playerCount = people.Count(p => p.Role == ParticipantRole.Player),
                spectatorCount = people.Count(p => p.Role == ParticipantRole.Spectator),
                capacity = RoomService.MaxRoomCapacity
            });
        }
    }
}