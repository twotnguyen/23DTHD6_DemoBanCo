using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Hubs;
// System.IO (ImplicitUsings) cũng có MatchType, nên phải chỉ rõ enum của dự án.
using MatchType = _23DTHD6_DemoBanCo.Models.MatchType;
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
        private readonly MatchmakingService _matchmaking;

        public RoomController(
            AppDbContext db,
            RoomService roomService,
            IHubContext<ChessHub> hubContext,
            MatchmakingService matchmaking)
        {
            _db = db;
            _roomService = roomService;
            _hubContext = hubContext;
            _matchmaking = matchmaking;
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

        /// <summary>
        /// Mở phòng theo mã, dùng khi người khác nhập mã phòng hoặc quét mã QR.
        /// Trả thẳng về phòng chờ vì đó là màn hình duy nhất còn dùng sau khi bỏ
        /// màn hình "chi tiết phòng" cũ.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Join(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return RedirectToAction("Lobby");

            var room = await _roomService.GetByCodeAsync(code);

            if (room == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy phòng với mã này.";
                return RedirectToAction("Lobby");
            }

            return RedirectToAction("Waiting", new { id = room.Id });
        }

        /// <summary>
        /// Tạo phòng mới. Chủ phòng mặc định ngồi ghế Đỏ (đặc tả 2.3), và được
        /// đưa thẳng vào màn hình phòng chờ để chọn ghế, sẵn sàng và mời bạn.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string? name)
        {
            int userId = CurrentUserId;

            if (userId == 0)
                return Challenge();

            var room = await _roomService.CreateRoomAsync(userId, name);

            return RedirectToAction("Waiting", new { id = room.Id });
        }

        /// <summary>
        /// Chủ phòng xoá phòng. Phòng còn khán giả cũng bị dọn sạch vì bản ghi phòng
        /// không còn tái dùng được nữa.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            int userId = CurrentUserId;

            bool deleted = await _roomService.DeleteRoomAsync(id, userId);

            if (!deleted)
            {
                TempData["ErrorMessage"] = "Chỉ chủ phòng mới có thể xoá phòng này.";
            }

            return RedirectToAction("Lobby");
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

            // Khách không được xếp hạng: UI cần biết để bỏ nút thay vì bấm rồi mới
            // bị từ chối. Số người đang chờ cũng hiện sẵn cho thấy hàng đợi có ai.
            ViewBag.IsGuest = me?.IsGuest ?? false;
            ViewBag.CasualQueueSize = _matchmaking.QueueSize(MatchmakingQueue.QueueKind.Casual);
            ViewBag.RankedQueueSize = _matchmaking.QueueSize(MatchmakingQueue.QueueKind.Ranked);
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
            // Đã trong phòng rồi thì không ghi lại, chỉ chờ cập nhật từ hub.
            // Không chặn ở đây vì chủ phòng có thể khoá giữa lúc người chơi đang
            // ở trong phòng và F5 lại trang (đặc tả 4.3: người đang ở trong giữ lại).
            var existing = room.Participants.FirstOrDefault(p => p.UserId == userId);
            bool alreadyInRoom = existing != null;

            var blocked = CheckJoinAllowed(room, alreadyInRoom);
            if (blocked != null)
                return (room, blocked);

            if (alreadyInRoom)
                return (room, null);

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


        /// <summary>
        /// Chặn người vào phòng theo luật của phòng. Trả về thông điệp lỗi tiếng Việt,
        /// null nghĩa là vào được.
        ///
        /// Ba luật ở đây (đặc tả 2.6 / 4.3 / EC-01):
        ///   - Phòng Closed là vĩnh viễn, không ai vào được nữa kể cả bằng link/mã/QR.
        ///   - Phòng Locked chặn mọi lượt vào mới từ bên ngoài, nhưng người ĐANG ở trong
        ///     vẫn được giữ lại xem tiếp.
        ///   - Phòng Ranked cấm khán giả tuyệt đối, nên người chưa ngồi ghế thì từ chối
        ///     dù có đúng mã.
        /// </summary>
        private static string? CheckJoinAllowed(Room room, bool alreadyInRoom)
        {
            if (room.Status == RoomStatus.Closed)
                return "Phòng thi đấu này đã đóng.";

            if (room.Visibility == RoomVisibility.Locked && !alreadyInRoom)
                return "Phòng đang khóa, không ai vào được từ bên ngoài.";

            if (room.MatchType == MatchType.Ranked && !alreadyInRoom)
                return "Ván xếp hạng không cho phép người xem.";

            if (!alreadyInRoom && room.Participants.Count >= RoomService.MaxRoomCapacity)
                return "Phòng thi đấu đã đầy người, vui lòng chọn phòng khác!";

            return null;
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
        /// Xin đổi bên khi đã đủ hai đấu thủ (đặc tả 2.3). Chỉ ghi nhận đề nghị và mở
        /// hạn 30 giây; hoán ghế thật sự diễn ra khi đối thủ bấm đồng ý qua hub.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestSwap(int roomId)
        {
            int userId = CurrentUserId;

            var (ok, error) = await _roomService.RequestSideSwapAsync(roomId, userId);

            if (!ok)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToAction("Waiting", new { id = roomId });
            }

            var participant = await _roomService.GetParticipantAsync(roomId, userId);

            await _hubContext.Clients
                .Group(ChessHub.GetRoomGroupName(roomId))
                .SendAsync("SideSwapRequested", new
                {
                    roomId,
                    fromUserId = userId,
                    displayName = participant?.User?.DisplayName ?? "Người chơi",
                    secondsLeft = RoomService.SideSwapResponseSeconds
                });

            TempData["ErrorMessage"] = "Đã gửi yêu cầu đổi bên, chờ đối thủ trả lời.";
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
        /// Đuổi khán giả. Quyền thuộc về CẢ HAI đấu thủ (đặc tả 4.2 - 8B), không chỉ
        /// chủ phòng. Người bị đuổi bị ghi vào danh sách chặn nên không vào lại được
        /// phòng này cho tới khi phòng đóng hoàn toàn.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Kick(int roomId, int targetUserId)
        {
            int userId = CurrentUserId;

            var (ok, error) = await _roomService.KickSpectatorAsync(roomId, userId, targetUserId);

            if (!ok)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToAction("Waiting", new { id = roomId });
            }

            // Danh sách người trong phòng đã đổi, phát lại để không ai thấy
            // vị trí của người bị đuổi nữa.
            await BroadcastRoomStateAsync(roomId);

            // Đẩy thẳng người bị đuổi về sảnh kèm thông báo từ chính hub, vì họ
            // có thể đang mở phòng ở tab khác chứ không phải trang này.
            // Clients.User nhận khoá kiểu string. Hub ghi connection theo claim
            // NameIdentifier, cũng là kiểu string, nên phải ép sang string ở đây —
            // truyền số nguyên sẽ không khớp với bất kỳ kết nối nào và tin bị rơi.
            await _hubContext.Clients.User(targetUserId.ToString()).SendAsync("KickedFromRoom", new
            {
                roomId,
                message = "Bạn đã bị đuổi khỏi phòng thi đấu."
            });

            TempData["ErrorMessage"] = "Đã đuổi người đó khỏi phòng.";
            return RedirectToAction("Waiting", new { id = roomId });
        }


        /// <summary>
        /// Màn hình đấu với máy cho một ván đã tạo. Chỉ đấu thủ trong ván được xem,
        /// và phải là ván AI: nếu mở nhầm ván PvP bằng đường dẫn gõ tay thì vẫn bị chặn,
        /// vì ở đây không có nhóm realtime nào để rò tin ván người.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> AiGame(int id)
        {
            int userId = CurrentUserId;
            if (userId == 0)
                return Challenge();

            var match = await _db.Matches
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (match == null || match.Type != MatchType.Ai)
                return NotFound("Không tìm thấy ván đấu với máy.");

            if (match.RedUserId != userId && match.BlackUserId != userId)
                return Forbid();

            // Phe người chơi để view dựng bàn đúng hướng: 0 = Đỏ, 1 = Đen.
            ViewBag.MatchId = match.Id;
            ViewBag.Fen = match.Fen;
            ViewBag.TurnSide = match.TurnSide;
            ViewBag.PlayerSide = match.RedUserId == userId ? 0 : 1;
            ViewBag.AiDifficulty = (AiDifficulty)match.AiDifficulty;
            ViewBag.Status = match.Status;
            ViewBag.UndoLeft = AiMatchService.MaxUndoPerAiMatch
                - (match.RedUserId == userId ? match.RedUndoCount : match.BlackUndoCount);

            return View();
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

            // "join" mãi để vào phòng, "share" mãi mời chung.
            // Url.Action trả về string? khi không sinh được URL, nên phải chặn null
            // tại đây — nếu để null lọt xuống GenerateSvg thì QR sẽ rỗng.
            string? content = string.Equals(scope, "join", StringComparison.OrdinalIgnoreCase)
                ? Url.Action("JoinByCode", "Room", new { code = room.Code }, Request.Scheme)
                : Url.Action("Waiting", "Room", new { id = room.Id }, Request.Scheme);

            if (string.IsNullOrEmpty(content))
                return NotFound("Không sinh được nội dung mã QR.");

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