using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;

// ImplicitUsings kéo cả System.IO vào nên MatchType bị trùng với System.IO.MatchType.
// Alias chỉ định rõ kiểu nào của dự án, tránh phải viết Models.MatchType ở mọi chỗ.
using MatchType = _23DTHD6_DemoBanCo.Models.MatchType;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Ván đấu với máy (đặc tả 6.1, 6.2, 6.3).
    ///
    /// NGUYÊN TẮC: file này KHÔNG chứa thuật toán cờ. Mọi luật đi, chiếu, kết thúc ván
    /// và tìm nước đi đều gọi sang ChessRules / CheckDetector / GameEvaluator / AiEngine.
    /// Lớp này chỉ lo phần "ván đấu": tạo phòng, gọi máy đi, giữ trần đi lại, và báo cáo.
    ///
    /// QUY ƯỚC BÊN QUÂN:
    ///  - Match lưu RedUserId / BlackUserId. Máy KHÔNG có UserId, nên ván với máy đặt
    ///    BlackUserId = 0 khi người chơi cầm Đỏ, và RedUserId = người chơi khi họ cầm Đen.
    ///    Phe còn lại là 0 = phe máy.
    ///  - Người chơi cầm Đen thì MÁY đi trước vì luật cờ tướng luôn cho Đỏ đi trước.
    ///
    /// ĐỒNG HỒ: TimeLimitSeconds = 0 (không giới hạn) theo đặc tả 6.3, nên
    /// RedTimeLeftSeconds và BlackTimeLeftSeconds đều giữ 0 và MatchClockService bỏ qua ván này.
    /// </summary>
    public sealed class AiMatchService
    {
        /// <summary>Trần số lần đi lại trong một ván đấu với máy.</summary>
        public const int MaxUndoPerAiMatch = 3;

        /// <summary>UserId quy ước cho phe máy: máy không phải tài khoản nên không có Id thật.</summary>
        private const int AiUserId = 0;

        private readonly AppDbContext _db;
        private readonly RoomService _rooms;
        private readonly MatchService _matches;

        public AiMatchService(AppDbContext db, RoomService rooms, MatchService matches)
        {
            _db = db;
            _rooms = rooms;
            _matches = matches;
        }

        /// <summary>
        /// Bắt đầu ván đấu với máy.
        ///
        /// roomId = null thì tạo phòng riêng cho ván đấu máy, tên tự động kiểu "Đấu với máy - Dễ".
        /// choosingRed = true cho người chơi cầm Đỏ, ngược lại cầm Đen và máy đi trước.
        /// </summary>
        public async Task<Match> StartAsync(int userId, int? roomId, AiDifficulty difficulty, bool choosingRed)
        {
            Room room;

            if (roomId.HasValue)
            {
                var existing = await _rooms.GetByIdAsync(roomId.Value);
                if (existing is null)
                    throw new InvalidOperationException("Không tìm thấy phòng để đấu với máy.");

                room = existing;
            }
            else
            {
                // Ván đấu máy không cần khán giả, nhưng vẫn tạo phòng thật để mọi màn hình
                // đều lấy thông tin ván từ cùng một chỗ, không có nhánh riêng rẽ ngữ cảnh.
                room = await _rooms.CreateRoomAsync(userId, BuildRoomName(difficulty));
            }

            room.Status = RoomStatus.Playing;
            room.ActiveMatchId = null;
            room.IsStartingMatch = false;

            // Máy không phải tài khoản nên bên không có người chơi sẽ để 0.
            int redUserId = choosingRed ? userId : AiUserId;
            int blackUserId = choosingRed ? AiUserId : userId;

            var match = await _matches.StartMatchAsync(
                room.Id,
                redUserId,
                blackUserId,
                MatchType.Ai,
                timeLimitSeconds: 0,
                aiDifficulty: difficulty);

            room.ActiveMatchId = match.Id;
            await _db.SaveChangesAsync();

            // Người chơi cầm Đen thì đến lượt máy ngay: máy phải đi nước đầu tiên.
            if (!choosingRed)
            {
                await PlayAiMoveAsync(match.Id);

                // Nạp lại để trả về thế cờ và lượt đã cập nhật sau nước đi của máy.
                // Không nạp lại thì người gọi nhận FEN khởi tạo và tưởng chưa có ai đi.
                var updated = await _db.Matches.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == match.Id);

                if (updated is not null)
                    return updated;
            }

            return match;
        }

        /// <summary>
        /// Người chơi gửi một nước đi. Luật và tính kết quả do MatchService lo, ở đây chỉ
        /// kiểm tra bốn điều trước khi gọi: đúng ván, đúng người, đến lượt, còn là ván AI.
        ///
        /// Sau khi người chơi đi hợp lệ và ván chưa xong, máy đi ngay nước trả lời.
        /// </summary>
        public async Task<MatchService.MoveResult> ApplyPlayerMoveAsync(int matchId, int userId, ChessMove move)
        {
            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);

            if (match is null)
                return MatchService.MoveResult.Fail("Ván đấu không tồn tại.");

            if (match.Type != MatchType.Ai)
                return MatchService.MoveResult.Fail("Ván này không phải ván đấu với máy.");

            // Máy đi trước cầm UserId = 0; nếu không kiểm tra, lệnh của máy có thể lọt vào đây.
            if (match.RedUserId != userId && match.BlackUserId != userId)
                return MatchService.MoveResult.Fail("Bạn không tham gia ván này.");

            var result = await _matches.SubmitMoveAsync(matchId, userId, move);

            // Ván đã kết thúc (người chơi vừa chiếu hết máy) thì không có nước đi lượt máy nữa.
            if (!result.Success || result.EndReason != MatchEndReason.None)
                return result;

            await PlayAiMoveAsync(matchId);

            return result;
        }

        /// <summary>
        /// Đi lại 1 CẶP nước đi, tức 2 ply: 1 nước của máy + 1 nước của người chơi.
        ///
        /// QUY TẮC LÙI 1 CẶP NƯỚC ĐI (2 ply):
        ///  Ván người - máy đi luôn xen kẽ, mỗi vòng là đúng 2 ply. Nếu chỉ xoá nước
        ///  cuối cùng thì lượt lại thuộc về máy, người chơi phải đợi máy đi nữa mới được đi,
        ///  và người chơi mất quyền đi nước mình vừa đi mà không hiểu vì sao. Nên phải lùi
        ///  TRỌN một vòng: bỏ nước của máy (ply chẵn về cuối) rồi bỏ nước của người
        ///  chơi (ply lẻ về trước), đưa ván về đúng lúc người chơi vừa đi xong.
        ///
        ///  Sau khi lùi, lượt trả về cho người chơi, và người chơi có thể đi nước khác
        ///  thay vì bị buộc phải đi lại đúng nước cũ.
        /// </summary>
        public async Task<MatchService.MoveResult?> UndoAsync(int matchId, int userId)
        {
            var match = await _db.Matches
                .Include(m => m.Moves)
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (match is null)
                return MatchService.MoveResult.Fail("Ván đấu không tồn tại.");

            if (match.Type != MatchType.Ai)
                return MatchService.MoveResult.Fail("Ván này không phải ván đấu với máy.");

            if (match.RedUserId != userId && match.BlackUserId != userId)
                return MatchService.MoveResult.Fail("Bạn không tham gia ván này.");

            if (match.Status != "PLAYING")
                return MatchService.MoveResult.Fail("Ván đấu không còn hoạt động.");

            int? side = ResolveSide(match, userId);

            if (side is null)
                return MatchService.MoveResult.Fail("Bạn không tham gia ván này.");

            int used = side.Value == 0 ? match.RedUndoCount : match.BlackUndoCount;

            if (used >= MaxUndoPerAiMatch)
            {
                return MatchService.MoveResult.Fail(
                    $"Bạn đã dùng hết {MaxUndoPerAiMatch} lượt đi lại trong ván này.");
            }

            var ordered = match.Moves.OrderBy(m => m.Id).ToList();

            // Cần tối thiểu một vòng đầy đủ (nước người chơi + nước máy) mới lùi được.
            if (ordered.Count < 2)
                return MatchService.MoveResult.Fail("Chưa có đủ nước đi để đi lại.");

            // Lùi 2 ply: bỏ 2 nước cuối. Nước chẵn (người chơi) và nước lẻ (máy) nằm
            // ở 2 vị trí cuối của ván người-máy, không phụ thuộc ai đi trước.
            var lastTwo = ordered.TakeLast(2).ToList();
            var toRemove = ordered.Where(m => m.Id >= lastTwo[0].Id).ToList();

            _db.MatchMoves.RemoveRange(toRemove);
            await _db.SaveChangesAsync();

            var remaining = await _db.MatchMoves
                .Where(mv => mv.MatchId == matchId)
                .OrderBy(mv => mv.Id)
                .ToListAsync();

            // Khôi phục thế cờ và lượt về đúng vị trí trước khi lùi: người chơi được đi tiếp.
            match.Fen = remaining.Count == 0 ? GameEvaluator.InitialFen : remaining[^1].FenAfter;
            match.TurnSide = RemainingPlayerTurn(remaining, match);
            match.LastMoveAt = DateTime.UtcNow;
            match.InactivityWarned = false;

            if (side.Value == 0) match.RedUndoCount++;
            else match.BlackUndoCount++;

            await _db.SaveChangesAsync();

            return MatchService.MoveResult.Ok();
        }

        /// <summary>
        /// Chuỗi thống kê cho widget AI (đặc tả 9.2): số node đã duyệt, độ sâu đạt được,
        /// thời gian suy nghĩ, và nước đi dự đoán.
        ///
        /// GIẢ ĐỊNH ĐÃ GHI RÕ: bảng MatchMove KHÔNG có cột lưu nodes/depth/thời gian của máy
        /// (đã kiểm tra Models/GameModels.cs). Nên không có dữ liệu lưu để đọc lại, và
        /// thống kê chỉ có ý nghĩa tại thời điểm máy vừa suy nghĩ. Vì vậy hàm này chạy
        /// AiEngine một lần trên thế cờ hiện tại để lấy số liệu, thay vì đọc lịch sử.
        ///
        /// Hệ quả cần nói rõ: gọi hàm này tốn thêm một lượt suy nghĩ của máy, và thống kê
        /// là ước lượng ở thế cờ hiện tại chứ không phải số liệu của nước đi đã đi.
        /// Nếu sau này muốn thống kê chính xác theo từng nước đi, cần thêm cột
        /// AiNodes / AiDepth / AiElapsedMs / AiPrincipalVariation vào MatchMove.
        /// </summary>
        public async Task<string> GetStatsAsync(int matchId)
        {
            var match = await _db.Matches.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (match is null)
                return "Không tìm thấy ván đấu.";

            if (match.Type != MatchType.Ai)
                return "Ván này không phải ván đấu với máy.";

            if (string.IsNullOrEmpty(match.Fen))
                return "Ván đấu chưa có thế cờ để thống kê.";

            ChessBoard board;

            try
            {
                board = GameEvaluator.FromFen(match.Fen);
            }
            catch (FormatException)
            {
                return "Thế cờ hiện tại không đọc được, không có thống kê.";
            }

            var sideToMove = match.TurnSide == 0 ? Side.Red : Side.Black;
            var difficulty = (AiDifficulty)match.AiDifficulty;

            var result = AiEngine.FindBestMove(board, sideToMove, difficulty);

            string predicted = result.BestMove is null
                ? "Máy không còn nước đi nào"
                : GameEvaluator.ToSan(board, result.BestMove);

            string variation = string.IsNullOrEmpty(result.PrincipalVariation)
                ? "Không có"
                : result.PrincipalVariation;

            return $"Nước dự đoán: {predicted} | Độ sâu: {result.DepthReached} | "
                 + $"Số node: {result.NodesEvaluated} | Thời gian: {result.ElapsedMs} ms | "
                 + $"Biến chính: {variation}";
        }

        // =========================================
        // Nội bộ
        // =========================================

        /// <summary>
        /// Máy đi một nước bằng AiEngine rồi ghi vào cây nước đi.
        ///
        /// Phe máy được nhận diện bằng UserId = 0 trong Match (máy không phải tài khoản),
        /// nên nước đi của máy gửi qua cùng cổng SubmitMoveAsync với lượt đi của người chơi:
        /// luật, kết thúc ván, cây nước đi và đánh số MoveNumber đều nằm đúng một chỗ,
        /// không có đường ghi nước đi nào thứ hai.
        ///
        /// Ván đã kết thúc, không phải ván AI, hoặc lượt không thuộc phe máy thì không làm gì.
        /// </summary>
        private async Task PlayAiMoveAsync(int matchId)
        {
            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);

            if (match is null || match.Type != MatchType.Ai)
                return;

            if (!string.Equals(match.Status, MatchService.StatusPlaying, StringComparison.Ordinal))
                return;

            // Lượt hiện tại phải thuộc phe máy mới được đi. Phe máy là phe có UserId = 0.
            if (!IsAiTurn(match))
                return;

            var board = GameEvaluator.FromFen(match.Fen);
            var sideToMove = match.TurnSide == 0 ? Side.Red : Side.Black;
            var difficulty = (AiDifficulty)match.AiDifficulty;

            // Ngân sách thời gian theo cấp độ do AiEngine tự giữ, ở đây không lặp lại.
            var aiResult = AiEngine.FindBestMove(board, sideToMove, difficulty);

            if (aiResult.BestMove is null)
                return;

            var engineMove = aiResult.BestMove;

            // Chuyển Move của engine sang ChessMove để đi qua đúng cổng của MatchService.
            var submit = new ChessMove
            {
                PieceId = engineMove.PieceId,
                FromRow = engineMove.FromRow,
                FromCol = engineMove.FromCol,
                ToRow = engineMove.ToRow,
                ToCol = engineMove.ToCol
            };

            // UserId đại diện cho phe đang đi: 0 nghĩa là chính phe máy.
            int sideUserId = match.TurnSide == 0 ? match.RedUserId : match.BlackUserId;

            var result = await _matches.SubmitMoveAsync(matchId, sideUserId, submit);

            if (!result.Success)
            {
                // Không ghi được nước đi thì đóng ván chứ không để người chơi bấm đi mãi không được.
                match.Status = MatchService.StatusFinished;
                match.EndReason = MatchEndReason.Interrupted;
                match.EndedAt = DateTime.UtcNow;
                match.WinnerSide = -1;
                await _db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Lượt hiện tại có thuộc phe máy không: phe đó phải có UserId = 0.
        /// Ván với người thật thì cả hai phe đều có UserId khác 0 nên luôn false,
        /// đây là chốt chặn để PlayAiMoveAsync không bao giờ chen vào ván người với người.
        /// </summary>
        private static bool IsAiTurn(Match match)
        {
            return match.TurnSide == 0 ? match.RedUserId == AiUserId : match.BlackUserId == AiUserId;
        }

        /// <summary>
        /// Sau khi lùi, lượt thuộc về ai. Nếu còn nước nào trên bàn thì lượt là phe
        /// vừa đó (người chơi), nếu không còn nước nào thì về phe Đỏ (0).
        /// Trong ván người-máy, nước cuối còn lại luôn là của người chơi.
        /// </summary>
        private static int RemainingPlayerTurn(List<MatchMove> remaining, Match match)
        {
            if (remaining.Count == 0)
                return 0;

            int lastSide = remaining[^1].Side;

            // Nước cuối là của máy thì lượt về người chơi, ngược lại về máy.
            // Phe máy là phe không có UserId thật.
            bool lastWasAi = lastSide == 0 ? match.BlackUserId == 0 : match.RedUserId == 0;
            return lastWasAi ? Opposite(lastSide) : lastSide;
        }

        private static int Opposite(int side) => side == 0 ? 1 : 0;

        /// <summary>Người gửi cầm bên nào: 0 = đỏ, 1 = đen, null = không tham gia.</summary>
        private static int? ResolveSide(Match match, int userId)
        {
            if (match.RedUserId == userId) return 0;
            if (match.BlackUserId == userId) return 1;
            return null;
        }

        private static string BuildRoomName(AiDifficulty difficulty)
        {
            string level = difficulty switch
            {
                AiDifficulty.Easy => "Dễ",
                AiDifficulty.Medium => "Trung bình",
                AiDifficulty.Hard => "Khó",
                _ => "Dễ"
            };

            return $"Đấu với máy - {level}";
        }
    }
}
