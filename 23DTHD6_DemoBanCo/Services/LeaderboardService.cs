using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Bảng xếp hạng Elo (đặc tả 7.1).
    ///
    /// QUY TẮC LỌC:
    ///  - Bỏ tài khoản Khách: IsGuest = true. Đặc tả 1.3 nói rõ Khách không tính Elo,
    ///    nên đưa vào bảng xếp hạng là sai.
    ///  - Bỏ tài khoản chưa từng đánh ván xếp hạng (Elo &lt;= 0). Tài khoản mới tạo có Elo = 1200
    ///    nên nếu không lọc, cả kho bảng mới đăng ký sẽ chen vào giữa bảng và làm bảng mất ý nghĩa.
    ///    Đây là quyết định của tôi, ghi rõ ở đây để người đọc biết.
    ///  - Nếu sau này muốn hiện cả tài khoản chưa đánh, chỉ cần bỏ điều kiện Elo &gt; 0 trong
    ///    BuildQuery(), các thứ hạng vẫn đánh số liên tục đúng.
    ///
    /// THỨ HẠNG:
    ///  - Sắp xếp Elo giảm dần. Hòa Elo thì bên nào thắng nhiều hơn đứng trước,
    ///    vẫn hòa thì Username tăng dần (A-Z) để thứ hạng luôn ổn định và không nhảy vị trí
    ///    giữa hai lần tải trang khi hai người có cùng Elo và cùng số trận thắng.
    /// </summary>
    public sealed class LeaderboardService
    {
        /// <summary>Số người mặc định trong bảng xếp hạng, theo đặc tả là Top 50.</summary>
        public const int TopCount = 50;

        private readonly AppDbContext _db;

        public LeaderboardService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>Một dòng của bảng xếp hạng, đã tính sẵn tên bậc và tỉ lệ thắng.</summary>
        public sealed record LeaderboardRow(
            int Rank,
            int UserId,
            string DisplayName,
            int Elo,
            string TierName,
            int Wins,
            int Losses,
            int Draws,
            double WinRate);

        /// <summary>Bảng xếp hạng Top N, thứ hạng đánh số liên tục từ 1.</summary>
        public async Task<List<LeaderboardRow>> GetTopAsync(int take = TopCount)
        {
            if (take <= 0)
                return new List<LeaderboardRow>();

            var users = await BuildQuery()
                .OrderByDescending(u => u.Elo)
                .ThenByDescending(u => u.Wins)
                .ThenBy(u => u.Username)
                .Take(take)
                .Select(u => new { u.Id, u.DisplayName, u.Elo, u.Wins, u.Losses, u.Draws })
                .ToListAsync();

            var rows = new List<LeaderboardRow>(users.Count);

            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];
                rows.Add(new LeaderboardRow(
                    i + 1,
                    u.Id,
                    u.DisplayName,
                    u.Elo,
                    EloCalculator.GetTierName(EloCalculator.GetTier(u.Elo)),
                    u.Wins,
                    u.Losses,
                    u.Draws,
                    CalculateWinRate(u.Wins, u.Losses, u.Draws)));
            }

            return rows;
        }

        /// <summary>
        /// Thứ hạng của đúng một người, để ghim dòng cá nhân ở cuối bảng (đặc tả 7.1.4).
        /// Người chưa từng đánh xếp hạng thì trả null, tầng trên tự hiện "chưa có thứ hạng".
        /// </summary>
        public async Task<LeaderboardRow?> GetUserRankAsync(int userId)
        {
            // Phải lấy cả Username: thứ tự phá hoà đổi khi cùng Elo và cùng số trận thắng
            // thì so Username, không dùng DisplayName (tên hiển thị có thể trùng nhau).
            var target = await BuildQuery()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.Username, u.DisplayName, u.Elo, u.Wins, u.Losses, u.Draws })
                .FirstOrDefaultAsync();

            if (target is null)
                return null;

            // Đếm số người đứng trước: Elo cao hơn, hoặc bằng Elo mà thắng nhiều hơn,
            // hoặc bằng cả hai mà Username nhỏ hơn. Cùng thứ tự với GetTopAsync.
            int better = await BuildQuery()
                .CountAsync(u =>
                    u.Elo > target.Elo
                    || (u.Elo == target.Elo && u.Wins > target.Wins)
                    || (u.Elo == target.Elo && u.Wins == target.Wins && u.Username.CompareTo(target.Username) < 0));

            return new LeaderboardRow(
                better + 1,
                target.Id,
                target.DisplayName,
                target.Elo,
                EloCalculator.GetTierName(EloCalculator.GetTier(target.Elo)),
                target.Wins,
                target.Losses,
                target.Draws,
                CalculateWinRate(target.Wins, target.Losses, target.Draws));
        }

        /// <summary>
        /// Lấy danh sách Id người chơi đứng đầu, dùng cho hệ thống "mời bạn bè" đang online.
        /// Không trả về cả bảng để khỏi tạo bản ghi thừa khi chỉ cần Id.
        /// </summary>
        public async Task<List<int>> GetTopUserIdsAsync(int take = 10)
        {
            if (take <= 0)
                return new List<int>();

            return await BuildQuery()
                .OrderByDescending(u => u.Elo)
                .ThenByDescending(u => u.Wins)
                .ThenBy(u => u.Username)
                .Select(u => u.Id)
                .Take(take)
                .ToListAsync();
        }

        /// <summary>Truy vấn nền: bỏ Khách và tài khoản chưa từng đánh xếp hạng.</summary>
        private IQueryable<User> BuildQuery()
        {
            return _db.Users
                .AsNoTracking()
                .Where(u => !u.IsGuest && u.Elo > 0);
        }

        /// <summary>
        /// Tỉ lệ thắng theo trận, làm tròn 1 chữ số thập phân. Chưa đánh trận nào thì trả 0
        /// thay vì chia cho 0.
        /// </summary>
        private static double CalculateWinRate(int wins, int losses, int draws)
        {
            int total = wins + losses + draws;

            if (total <= 0)
                return 0;

            return Math.Round(wins * 100.0 / total, 1);
        }
    }
}
