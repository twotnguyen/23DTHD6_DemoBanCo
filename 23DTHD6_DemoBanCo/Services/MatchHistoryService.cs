using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;

// ImplicitUsings kéo cả System.IO vào nên MatchType trùng với System.IO.MatchType.
// Alias chỉ định rõ kiểu enum của dự án.
using MatchType = _23DTHD6_DemoBanCo.Models.MatchType;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Lịch sử ván đấu với máy (đặc tả 11B) và xuất ván để xem lại.
    ///
    /// QUY TẮC BẢO MẬT XEM LẠI:
    ///  Chỉ hai người chơi trong ván (RedUserId, BlackUserId) mới xem được nước đi và PGN.
    ///  Khán giả và người ngoài ván bị chặn ở mọi cổng đọc: GetMovesAsync trả danh sách rỗng,
    ///  ToPgnAsync trả chuỗi rỗng. Lý do: ván xếp hạng cấm xem lại, và nước đi của ván đang
    ///  chơi là thông tin mà chỉ đấu thủ mới được biết.
    /// </summary>
    public sealed class MatchHistoryService
    {
        /// <summary>Số ván mặc định trả về cho danh sách lịch sử.</summary>
        public const int DefaultTake = 50;

        private readonly AppDbContext _db;

        public MatchHistoryService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Một dòng lịch sử ván đấu với máy, đã quy đổi sẵn nhãn hiển thị cho tiếng Việt.
        /// </summary>
        /// <param name="Result">1 = thắng, 0 = thua. Hoà với máy không xảy ra nên không cần giá trị 0.5.</param>
        public sealed record MatchHistoryItem(
            int MatchId,
            string OpponentName,
            int Result,
            string ResultLabel,
            AiDifficulty Difficulty,
            string DifficultyLabel,
            DateTime StartedAt,
            int MoveCount);

        /// <summary>
        /// Lịch sử các ván đấu với máy của một người, mới nhất trước.
        /// Chỉ lấy ván Type == MatchType.Ai.
        /// </summary>
        public async Task<List<MatchHistoryItem>> GetByUserAsync(int userId, int take = DefaultTake)
        {
            if (take <= 0)
                return new List<MatchHistoryItem>();

            var matches = await _db.Matches
                .AsNoTracking()
                .Where(m => m.Type == MatchType.Ai
                            && (m.RedUserId == userId || m.BlackUserId == userId))
                .OrderByDescending(m => m.StartedAt)
                .ThenByDescending(m => m.Id)
                .Take(take)
                .Select(m => new
                {
                    m.Id,
                    m.RedUserId,
                    m.BlackUserId,
                    m.WinnerSide,
                    m.Status,
                    m.AiDifficulty,
                    m.StartedAt,
                    MoveCount = m.Moves.Count
                })
                .ToListAsync();

            var items = new List<MatchHistoryItem>(matches.Count);

            foreach (var m in matches)
            {
                // Ván chưa xong thì chưa có kết quả, đánh dấu 0 (thua) cho tới khi ván kết thúc.
                bool finished = string.Equals(m.Status, "FINISHED", StringComparison.Ordinal);
                bool won = finished && m.WinnerSide >= 0
                           && IsPlayerSide(m.RedUserId, m.BlackUserId, m.WinnerSide, userId);

                items.Add(new MatchHistoryItem(
                    m.Id,
                    "Máy",
                    won ? 1 : 0,
                    won ? "Thắng" : "Thua",
                    ToDifficulty(m.AiDifficulty),
                    DifficultyLabel(ToDifficulty(m.AiDifficulty)),
                    m.StartedAt,
                    m.MoveCount));
            }

            return items;
        }

        /// <summary>
        /// Nước đi của một ván, theo đúng thứ tự đã đi. Người xem không phải đấu thủ thì trả rỗng.
        /// </summary>
        public async Task<List<MatchMove>> GetMovesAsync(int matchId, int viewerUserId)
        {
            if (!await ExistsAsync(matchId, viewerUserId))
                return new List<MatchMove>();

            return await _db.MatchMoves
                .AsNoTracking()
                .Where(mv => mv.MatchId == matchId)
                .OrderBy(mv => mv.Id)
                .ToListAsync();
        }

        /// <summary>
        /// Xuất ván theo định dạng PGN của cờ tướng (Xiangqi PGN).
        ///
        /// LƯU Ý VỀ CHUẨN: bộ chuẩn PGN cờ tướng (do Xiangqi Federation phổ biến) chưa
        /// thống nhất hoàn toàn giữa các phần mềm. Bản xuất ở đây bám theo dạng phổ biến nhất:
        ///  - Thẻ [Event]/[Site]/[Date]/[Round]/[White]/[Black]/[Result] ở phần đầu.
        ///  - Nước đi dùng ký hiệu ICCS kiểu Xiangqi (ví dụ "H2e2", "R1a3") đã được
        ///    GameEvaluator.ToSan tính sẵn và lưu ở MatchMove.SanMove.
        ///  - Mỗi nước đi một dòng, đánh số nước theo cặp (nước đỏ là nước lẻ).
        ///  - Kết quả ghi bằng ký hiệu chuẩn: 1-0 (đỏ thắng), 0-1 (đen thắng), 1/2-1/2 (hòa),
        ///    và * nếu ván chưa kết thúc.
        /// Nếu cần tương thích tuyệt đối với một phần mềm cụ thể thì phải chỉnh riêng chỗ này.
        /// </summary>
        public async Task<string> ToPgnAsync(int matchId, int viewerUserId)
        {
            if (!await ExistsAsync(matchId, viewerUserId))
                return "";

            var match = await _db.Matches
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (match is null)
                return "";

            var moves = await _db.MatchMoves
                .AsNoTracking()
                .Where(mv => mv.MatchId == matchId)
                .OrderBy(mv => mv.Id)
                .Select(mv => new { mv.SanMove, mv.Side })
                .ToListAsync();

            var red = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == match.RedUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync() ?? "Đỏ";

            var black = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == match.BlackUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync() ?? "Máy";

            bool finished = string.Equals(match.Status, "FINISHED", StringComparison.Ordinal);
            string result = !finished ? "*"
                : match.WinnerSide switch
                {
                    0 => "1-0",
                    1 => "0-1",
                    _ => "1/2-1/2"
                };

            var sb = new StringBuilder();

            sb.Append("[Event \"Cờ Tướng Online\"]\n");
            sb.Append($"[Site \"Cờ Tướng Online\"]\n");
            sb.Append($"[Date \"{match.StartedAt.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)}\"]\n");
            sb.Append($"[Round \"-\"]\n");
            sb.Append($"[White \"{Escape(red)}\"]\n");
            sb.Append($"[Black \"{Escape(black)}\"]\n");
            sb.Append($"[Result \"{result}\"]\n");
            sb.Append('\n');

            // Mỗi nước đi một dòng. Nước đi thứ 1, 3, 5... là của phe Đỏ nên có số thứ tự nước.
            int ply = 0;
            foreach (var mv in moves)
            {
                if (mv.Side == 0)
                {
                    sb.Append((ply / 2 + 1).ToString(CultureInfo.InvariantCulture));
                    sb.Append(". ");
                }

                sb.Append(string.IsNullOrEmpty(mv.SanMove) ? "*" : mv.SanMove);
                sb.Append(' ');
                ply++;
            }

            sb.Append(result);
            sb.Append('\n');

            return sb.ToString();
        }

        /// <summary>
        /// Người xem có quyền xem lại ván này không: phải là một trong hai đấu thủ.
        /// Dùng chung cho GetMovesAsync và ToPgnAsync để không có chỗ nào lọc quyền khác đi.
        /// </summary>
        public async Task<bool> ExistsAsync(int matchId, int userId)
        {
            if (userId <= 0)
                return false;

            return await _db.Matches
                .AsNoTracking()
                .AnyAsync(m => m.Id == matchId
                               && (m.RedUserId == userId || m.BlackUserId == userId));
        }

        /// <summary>
        /// Bên thắng có phải là người đang xem lại không.
        ///
        /// Nhận các thuộc tính rời thay vì nhận cả Match, vì nơi gọi duyệt trên kết quả
        /// Select của LINQ là kiểu anonymous, truyền vào tham số kiểu Match sẽ không biên dịch được.
        /// Trong ván với máy, phe không có người chơi có UserId = 0 vì máy không phải tài khoản.
        /// </summary>
        private static bool IsPlayerSide(int redUserId, int blackUserId, int winnerSide, int userId)
        {
            return winnerSide == 0 ? redUserId == userId : blackUserId == userId;
        }

        private static AiDifficulty ToDifficulty(int value)
        {
            return Enum.IsDefined(typeof(AiDifficulty), value)
                ? (AiDifficulty)value
                : AiDifficulty.Easy;
        }

        private static string DifficultyLabel(AiDifficulty difficulty)
        {
            return difficulty switch
            {
                AiDifficulty.Easy => "Dễ",
                AiDifficulty.Medium => "Trung bình",
                AiDifficulty.Hard => "Khó",
                _ => "Dễ"
            };
        }

        /// <summary>PGN yêu cầu dấu nháy kép và gạch chéo ngược phải thoát.</summary>
        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
