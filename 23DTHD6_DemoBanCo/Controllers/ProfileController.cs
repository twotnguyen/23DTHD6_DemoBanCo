using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Services;

namespace _23DTHD6_DemoBanCo.Controllers
{
    /// <summary>
    /// Hai màn hình "xem lại": bảng xếp hạng Elo và lịch sử ván đấu.
    ///
    /// Cả hai đều chỉ đọc nên không cần chống giả mạo CSRF; token chỉ cần khi POST.
    ///
    /// VÌ SAO CÒN TIẾM AppDbContext: LeaderboardService và MatchHistoryService đã tự
    /// lo phần đọc Users / Matches của họ. Ở đây chỉ cần đọc tên hiển thị của chính
    /// người đang xem để dựng tiêu đề lịch sử, một lần truy vấn rẻ hơn là đổi cả
    /// chữ ký service (service là nơi quy tắc nghiệp vụ tập trung, không nên lấp lỗ).
    /// </summary>
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly AppDbContext _db;
        private readonly LeaderboardService _leaderboard;
        private readonly MatchHistoryService _history;

        public ProfileController(
            AppDbContext db,
            LeaderboardService leaderboard,
            MatchHistoryService history)
        {
            _db = db;
            _leaderboard = leaderboard;
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
        /// Bảng xếp hạng Top 50, kèm dòng cá nhân ghim ở chân bảng.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Leaderboard()
        {
            int userId = CurrentUserId;

            var top = await _leaderboard.GetTopAsync(LeaderboardService.TopCount);
            var mine = await _leaderboard.GetUserRankAsync(userId);

            ViewBag.Top = top;
            ViewBag.Mine = mine;
            ViewBag.CurrentUserId = userId;

            // Dòng cá nhân vẫn ghim ở chân bảng kể cả khi đã nằm trong Top 50: đó là
            // điều người chơi cần nhìn thấy nhất. Cờ này chỉ để tầng trên KHÔNG tô
            // viền "bạn" lần nữa cho đúng một dòng trong bảng chính, tránh rối mắt.
            bool mineInTop = mine is not null && top.Count > 0
                             && mine.Rank <= top.Count
                             && top.Any(r => r.UserId == mine.UserId);
            ViewBag.MineInTop = mineInTop;

            return View();
        }

        /// <summary>
        /// Lịch sử các ván đã đấu (hiện service trả về các ván với máy, mới nhất trước).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> History()
        {
            int userId = CurrentUserId;

            var items = await _history.GetByUserAsync(userId, MatchHistoryService.DefaultTake);

            ViewBag.Items = items;
            ViewBag.CurrentUserId = userId;

            // Tên hiển thị dùng cho tiêu đề lịch sử. Lấy riêng một truy vấn thay vì
            // trải record trong MatchHistoryService vì service đó chỉ phục vụ bảng
            // ván đấu, không cần biết tên chủ tài khoản.
            ViewBag.DisplayName = userId == 0
                ? ""
                : await _db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefaultAsync() ?? "";
            // Tên view là MatchHistory.cshtml theo quy ước màn hình SCR-MATCH-HISTORY,
            // còn action tên History để URL ngắn. Không khai báo rõ thì MVC tìm
            // theo tên action và báo không thấy view.
            return View("MatchHistory");
        }
    }
}