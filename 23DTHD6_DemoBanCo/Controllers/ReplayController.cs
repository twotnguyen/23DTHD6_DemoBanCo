using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;
using _23DTHD6_DemoBanCo.Services;

namespace _23DTHD6_DemoBanCo.Controllers
{
    /// <summary>
    /// Màn hình xem lại ván đấu: tua tới/lui từng nước trên bàn chỉ xem, tải PGN.
    ///
    /// QUY TẮC BẢO MẬT (kế thừa từ MatchHistoryService): chỉ hai đấu thủ của ván
    /// (RedUserId / BlackUserId) mới được xem nước đi và PGN. Mọi cổng đọc ở đây
    /// đều đi qua MatchHistoryService.ExistsAsync trước khi chạm dữ liệu; bản thân
    /// service còn chặn thêm một lớp bằng cách trả danh sách rỗng/PGN rỗng cho
    /// người ngoài ván, vì ván xếp hạng cấm khán giả xem lại và nước đi của ván
    /// đang chơi là thông tin riêng của đấu thủ.
    ///
    /// VÌ SAO PGN DO SERVER SINH: nội dung PGN tóm tắt toàn bộ nước đi — chính là
    /// dữ liệu bị hạn chế ở trên — nên nó phải được sinh sau cổng kiểm quyền,
    /// theo đúng một chuẩn Xiangqi PGN duy nhất nằm trong MatchHistoryService.
    /// Client chỉ nhận chuỗi text để hiển thị/tải về, không bao giờ tự dựng PGN
    /// từ lịch sử nước đi thô nên không thể rò rỉ dữ liệu qua tầng trình bày.
    /// </summary>
    [Authorize]
    [Route("Replay")]
    public class ReplayController : Controller
    {
        private readonly AppDbContext _db;
        private readonly MatchHistoryService _history;

        public ReplayController(AppDbContext db, MatchHistoryService history)
        {
            _db = db;
            _history = history;
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
        /// Màn hình xem lại: đặt toàn bộ dữ liệu vào ViewBag cho
        /// Views/Replay/Index.cshtml tua từng nước (xem hợp đồng ở đầu view).
        /// </summary>
        [HttpGet("{id:int}")] // /Replay/123 — liên kết từ màn hình lịch sử ván đấu.
        [HttpGet("Index/{id:int}")] // /Replay/Index/123 — dạng địa chỉ mặc định.
        public async Task<IActionResult> Index(int id)
        {
            int userId = CurrentUserId;

            if (id <= 0)
                return NotFound("Không tìm thấy ván này.");

            // Cổng kiểm quyền duy nhất: chỉ hai đấu thủ mới được xem lại.
            if (!await _history.ExistsAsync(id, userId))
                return NotFound("Bạn không có quyền xem ván này.");

            var match = await _db.Matches.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);
            if (match is null)
                return NotFound("Không tìm thấy ván này.");

            // Nước đi do service cung cấp (đã chặn người ngoài ván ở trong).
            // Sắp theo thứ tự ván đấu để fenList[i] luôn là thế cờ sau moves[i-1].
            var moves = (await _history.GetMovesAsync(id, userId))
                .OrderBy(m => m.MoveNumber)
                .ThenBy(m => m.Id)
                .ToList();

            // Phần tử 0 là thế cờ khởi đầu để bước tua đầu tiên có bàn để vẽ,
            // các phần tử sau là thế cờ sau từng nước đi, đúng thứ tự.
            var fenList = new List<string>(moves.Count + 1) { GameEvaluator.InitialFen };
            foreach (var mv in moves)
                fenList.Add(mv.FenAfter);

            // Tên hiển thị hai phe, gom một truy vấn. Phe máy không phải tài khoản
            // (UserId == 0) nên hiển thị "Máy" thay vì tra bảng Users.
            var wanted = new[] { match.RedUserId, match.BlackUserId }
                .Where(u => u > 0).Distinct().ToList();
            var names = await _db.Users.AsNoTracking()
                .Where(u => wanted.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            ViewBag.matchId = id;
            ViewBag.initialFen = GameEvaluator.InitialFen;
            ViewBag.fenList = fenList;
            ViewBag.moves = moves;
            ViewBag.redName = match.RedUserId == 0
                ? "Máy"
                : names.GetValueOrDefault(match.RedUserId, "Đỏ");
            ViewBag.blackName = match.BlackUserId == 0
                ? "Máy"
                : names.GetValueOrDefault(match.BlackUserId, "Đen");
            ViewBag.resultText = BuildResultText(match);
            ViewBag.pgn = await _history.ToPgnAsync(id, userId);
            ViewBag.title = $"Xem lại ván #{id}";

            return View();
        }

        /// <summary>
        /// Tải PGN về máy. Dự phòng phía server cho nút "Tải PGN" khi
        /// trình duyệt chặn tải blob phía client.
        /// </summary>
        [HttpGet("DownloadPgn/{id:int}")]
        public async Task<IActionResult> DownloadPgn(int id)
        {
            int userId = CurrentUserId;

            if (id <= 0)
                return NotFound("Không tìm thấy ván này.");

            if (!await _history.ExistsAsync(id, userId))
                return NotFound("Bạn không có quyền xem ván này.");

            string pgn = await _history.ToPgnAsync(id, userId);
            return File(Encoding.UTF8.GetBytes(pgn), "application/x-chess-pgn", $"van-{id}.pgn");
        }

        /// <summary>
        /// Câu kết quả tiếng Việt: tên bên thắng + lý do kết thúc.
        /// Ván chưa xong (EndReason == None) thì báo đang diễn ra.
        /// </summary>
        private static string BuildResultText(Match match)
        {
            if (match.EndReason == MatchEndReason.None)
                return "Ván đang diễn ra";

            string reason = match.EndReason switch
            {
                MatchEndReason.Checkmate => "Chiếu hết",
                MatchEndReason.Stalemate => "Bị vây khốn (thua)",
                MatchEndReason.Repetition => "Lặp thế cờ (hòa)",
                MatchEndReason.Resign => "Đầu hàng",
                MatchEndReason.Timeout => "Hết giờ",
                MatchEndReason.Disconnect => "Mất kết nối",
                MatchEndReason.Inactivity => "Treo ván",
                MatchEndReason.AgreedDraw => "Hoà (đồng ý)",
                MatchEndReason.Interrupted => "Bị gián đoạn",
                _ => "Kết thúc"
            };

            return match.WinnerSide switch
            {
                0 => $"Đỏ thắng - {reason}",
                1 => $"Đen thắng - {reason}",
                _ => $"Hòa - {reason}"
            };
        }
    }
}
