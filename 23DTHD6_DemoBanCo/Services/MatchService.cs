using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;

// ImplicitUsings của dự án kéo vào System.IO, nơi có MatchType trùng tên với enum của
// dự án. Khai báo alias để không phải viết Models.MatchType ở mọi chỗ.
using MatchType = _23DTHD6_DemoBanCo.Models.MatchType;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Ván đấu server-authoritative: server giữ thế cờ, lượt đi, đồng hồ và kết quả.
    /// Client chỉ gửi ý định di chuyển, mọi thứ còn lại do hàm này kiểm và phát lại cho cả ván.
    ///
    /// Cây nước đi lưu trong MatchMove, mỗi nước trỏ về nước cha (ParentMoveId).
    /// Nhờ vậy đi lại (undo) chỉ là trỏ con trỏ về nước cha rồi khôi phục FEN.
    /// </summary>
    public sealed class MatchService
    {
        public const int MaxUndoPerSide = 3;

        /// <summary>Mất kết nối quá số giây này thì kết thúc ván, xử thua bên mất kết nối.</summary>
        public const int DisconnectGraceSeconds = 60;

        /// <summary>Không có nước đi nào trong bấy giây thì bắt đầu đếm lùi.</summary>
        public const int InactivityPromptSeconds = 180;

        /// <summary>Đếm lùi tới số giây này thì xử thua bên đến lượt mà không đi.</summary>
        public const int InactivityLoseSeconds = 30;

        public const string StatusPlaying = "PLAYING";
        public const string StatusFinished = "FINISHED";

        // Số lần lặp thế cờ do GameEvaluator.RepetitionLimit quyết định, không khai lại ở đây
        // để hai hằng số không trôi khỏi nhau.

        /// <summary>
        /// Kết quả một lệnh nước đi. Error dạng chuỗi tiếng Việt để client hiện thẳng lên UI.
        /// </summary>
        public sealed record MoveResult(bool Success, string? Error, MatchEndReason EndReason)
        {
            public static MoveResult Ok(MatchEndReason endReason = MatchEndReason.None)
                => new(true, null, endReason);

            public static MoveResult Fail(string error)
                => new(false, error, MatchEndReason.None);
        }

        private readonly AppDbContext _db;

        public MatchService(AppDbContext db)
        {
            _db = db;
        }

        // =========================================
        // Bắt đầu ván
        // =========================================

        /// <summary>
        /// Tạo ván mới cho phòng. Bên đỏ đi trước (TurnSide = 0).
        /// timeLimitSeconds = 0 nghĩa là không giới hạn thời gian, đồng hồ giữ nguyên 0.
        /// </summary>
        public async Task<Match> StartMatchAsync(
            int roomId,
            int redUserId,
            int blackUserId,
            MatchType type,
            int timeLimitSeconds,
            AiDifficulty aiDifficulty)
        {
            var match = new Match
            {
                RoomId = roomId,
                Type = type,
                TimeLimitSeconds = timeLimitSeconds,
                RedUserId = redUserId,
                BlackUserId = blackUserId,
                TurnSide = 0,
                Status = StatusPlaying,
                EndReason = MatchEndReason.None,
                RedTimeLeftSeconds = timeLimitSeconds,
                BlackTimeLeftSeconds = timeLimitSeconds,
                Fen = GameEvaluator.InitialFen,
                AiDifficulty = type == MatchType.Ai ? (int)aiDifficulty : -1,
                StartedAt = DateTime.UtcNow,
                LastMoveAt = DateTime.UtcNow
            };

            _db.Matches.Add(match);
            await _db.SaveChangesAsync();

            return match;
        }

        // =========================================
        // Gửi nước đi
        // =========================================

        /// <summary>
        /// Kiểm tra và áp dụng một nước đi. Thứ tự kiểm tra cố định:
        /// ván còn sống -> đúng người, đúng lượt -> nước đi hợp lệ -> ghi -> cập nhật -> xét thắng.
        /// </summary>
        public async Task<MoveResult> SubmitMoveAsync(int matchId, int userId, ChessMove move)
        {
            var match = await _db.Matches
                .Include(m => m.Moves)
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null)
            {
                return MoveResult.Fail("Ván đấu không tồn tại.");
            }

            if (match.Status != StatusPlaying)
            {
                return MoveResult.Fail("Ván đấu không còn hoạt động.");
            }

            // Quyền lượt đi lấy từ UserId trong cookie, không tin client tự khai.
            int? side = ResolveSide(match, userId);

            if (side == null)
            {
                return MoveResult.Fail("Bạn không tham gia ván này.");
            }

            if (side.Value != match.TurnSide)
            {
                return MoveResult.Fail("Chưa đến lượt của bạn.");
            }

            var board = GameEvaluator.FromFen(match.Fen);

            // Nước lặp: client gửi lại cùng một lệnh (mạng lỗi, bấm 2 lần) thì trả kết quả cũ,
            // không áp dụng hai lần và không vi phạm luật.
            var duplicate = FindDuplicateMove(match, move);

            if (duplicate != null)
            {
                return MoveResult.Ok(match.EndReason);
            }

            var legal = ChessRules.GetLegalMoves(board, ToEngineSide(side.Value));

            if (!IsLegal(legal, move))
            {
                return MoveResult.Fail("Nước đi không hợp lệ.");
            }

            // Nước đi của client đã hợp lệ, giờ mới tra quân thật trên bàn để áp dụng.
            var engineMove = ToEngineMove(board, move);

            if (engineMove == null)
            {
                return MoveResult.Fail("Không tìm thấy quân ở ô xuất phát.");
            }

            // SAN ghi trước khi bàn thay đổi, FEN ghi sau. Lý do: SAN mô tả nước đi trên
            // thế cờ cũ, còn FEN là thế cờ kết quả.
            string san = GameEvaluator.ToSan(board, engineMove);

            // Bên còn lại sẽ là bên được đi sau nước này.
            int opponentSide = side.Value == 0 ? 1 : 0;

            // Phải áp dụng nước đi vào bàn trước khi đọc FEN: nếu không, fenAfter sẽ trùng
            // fen trước đó và điều kiện kết thúc ván sẽ tính trên thế cờ cũ.
            ChessRules.ApplyMove(board, engineMove);
            string fenAfter = GameEvaluator.ToFen(board, ToEngineSide(opponentSide));

            var record = new MatchMove
            {
                MatchId = matchId,
                ParentMoveId = match.Moves.Count == 0 ? null : match.Moves.Max(m => m.Id),
                MoveNumber = match.Moves.Count + 1,
                Side = side.Value,
                PieceId = move.PieceId,
                FromRow = move.FromRow,
                FromCol = move.FromCol,
                ToRow = move.ToRow,
                ToCol = move.ToCol,
                SanMove = san,
                FenAfter = fenAfter,
                CreatedAt = DateTime.UtcNow
            };

            // ---- Kết thúc ván: chiếu hết / vây khốn / lặp thế ----
            // Tất cả đi qua GameEvaluator để không có chỗ nào tự tính khác đi.
            // EvaluateGameEnd nhận bên sắp đi, tức là bên vừa bị đối phương vừa đánh.
            MatchEndReason endReason = GameEvaluator.EvaluateGameEnd(
                board, ToEngineSide(opponentSide));

            if (endReason == MatchEndReason.None)
            {
                var fenHistory = new List<string>(match.Moves.Select(m => m.FenAfter)) { fenAfter };

                if (GameEvaluator.IsRepetitionDraw(fenHistory))
                {
                    endReason = MatchEndReason.Repetition;
                }
            }

            match.Fen = fenAfter;
            match.LastMoveAt = DateTime.UtcNow;
            // Đã có nước đi thì bỏ cảnh báo treo ván của lượt trước.
            match.InactivityWarned = false;

            if (endReason == MatchEndReason.None)
            {
                match.TurnSide = opponentSide;
            }
            else
            {
                match.Status = StatusFinished;
                match.EndReason = endReason;
                match.EndedAt = DateTime.UtcNow;

                // Chiếu hết, vây khốn: bên vừa đi là bên thắng. Ba lý do còn lại là hoà.
                match.WinnerSide = endReason is MatchEndReason.Checkmate or MatchEndReason.Stalemate
                    ? side.Value
                    : -1;

                // Số ván đã đấu chỉ cần khi tính Elo, không đọc mỗi nước đi.
                match.RedRankedGames = await _db.Users
                    .Where(u => u.Id == match.RedUserId)
                    .Select(u => u.RankedGames)
                    .FirstOrDefaultAsync();
                match.BlackRankedGames = await _db.Users
                    .Where(u => u.Id == match.BlackUserId)
                    .Select(u => u.RankedGames)
                    .FirstOrDefaultAsync();
            }

            await _db.SaveChangesAsync();

            if (endReason != MatchEndReason.None)
            {
                await ApplyEloIfRankedAsync(match, endReason);
            }

            return MoveResult.Ok(endReason);
        }

        // =========================================
        // Đi lại
        // =========================================

        /// <summary>
        /// Xin đi lại 1 cặp nước (2 ply). Ván xếp hạng không cho đi lại.
        /// Lượt đã dùng chỉ trừ khi đề nghị được chấp nhận.
        /// </summary>
        public async Task<MoveResult> RequestUndoAsync(int matchId, int userId)
        {
            var match = await _db.Matches
                .Include(m => m.Moves)
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null)
            {
                return MoveResult.Fail("Ván đấu không tồn tại.");
            }
            if (match.Type == MatchType.Ranked)
            {
                return MoveResult.Fail("Ván xếp hạng không cho phép đi lại.");
            }

            int? side = ResolveSide(match, userId);

            if (side == null)
            {
                return MoveResult.Fail("Bạn không tham gia ván này.");
            }

            int used = side.Value == 0 ? match.RedUndoCount : match.BlackUndoCount;

            if (used >= MaxUndoPerSide)
            {
                return MoveResult.Fail(
                    $"Bạn đã sử dụng hết {MaxUndoPerSide} lượt xin đi lại trong ván này.");
            }

            var lastMove = match.Moves
                .OrderBy(m => m.Id)
                .LastOrDefault();

            // Cần có ít nhất 2 nước thì mới lùi được một cặp: nước của bên xin và nước trả lời.
            if (lastMove == null || match.Moves.Count < 2)
            {
                return MoveResult.Fail("Chưa có đủ nước đi để đi lại.");
            }

            return MoveResult.Ok();
        }

        /// <summary>
        /// Chấp nhận đề nghị đi lại: lùi 2 ply trên cây và khôi phục thế cờ.
        /// Không hoàn lại thời gian đã trôi.
        /// </summary>
        public async Task<MoveResult> AcceptUndoAsync(int matchId, int userId)
        {
            var match = await _db.Matches
                .Include(m => m.Moves)
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null || match.Status != StatusPlaying)
            {
                return MoveResult.Fail("Ván đấu không còn hoạt động.");
            }

            int? side = ResolveSide(match, userId);

            if (side == null)
            {
                return MoveResult.Fail("Bạn không tham gia ván này.");
            }

            var ordered = match.Moves.OrderBy(m => m.Id).ToList();

            if (ordered.Count < 2)
            {
                return MoveResult.Fail("Chưa có đủ nước đi để đi lại.");
            }

            // Lùi tới nước cha của nước cuối: bỏ nước cuối và nước ngay trước đó.
            var target = ordered[^2].ParentMoveId;

            var toRemove = target == null
                ? ordered
                : ordered.Where(m => m.Id > target.Value).ToList();

            _db.MatchMoves.RemoveRange(toRemove);
            await _db.SaveChangesAsync();

            var remaining = await _db.MatchMoves
                .Where(m => m.MatchId == matchId)
                .OrderBy(m => m.Id)
                .ToListAsync();

            match.Fen = remaining.Count == 0 ? GameEvaluator.InitialFen : remaining[^1].FenAfter;
            match.TurnSide = remaining.Count == 0 ? 0 : remaining[^1].Side;
            match.LastMoveAt = DateTime.UtcNow;

            if (side.Value == 0) match.RedUndoCount++;
            else match.BlackUndoCount++;

            await _db.SaveChangesAsync();

            return MoveResult.Ok();
        }

        // =========================================
        // Kết nối, hết giờ, treo ván, đầu hàng
        // =========================================

        /// <summary>Ghi nhận một bên rớt kết nối. Chưa kết thúc ván, chờ đủ 60s.</summary>
        public async Task MarkDisconnectedAsync(int matchId, int userId)
        {
            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null || match.Status != StatusPlaying)
            {
                return;
            }

            if (match.RedUserId != userId && match.BlackUserId != userId)
            {
                return;
            }

            match.DisconnectedUserId = userId;
            match.DisconnectedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        /// <summary>Bên đã mất kết nối quá thời gian cho phép thì xử thua.</summary>
        public async Task<Match?> HandleDisconnectTimeoutAsync(int matchId)
        {
            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null || match.Status != StatusPlaying)
            {
                return null;
            }

            if (match.DisconnectedUserId == null || match.DisconnectedAt == null)
            {
                return null;
            }

            if ((DateTime.UtcNow - match.DisconnectedAt.Value).TotalSeconds < DisconnectGraceSeconds)
            {
                return null;
            }

            // Bên mất kết nối quá hạn là bên thua, không suy từ lượt đi hiện tại.
            int loser = match.DisconnectedUserId.Value == match.RedUserId ? 0 : 1;

            await FinishMatchAsync(match, MatchEndReason.Disconnect, loser);
            return match;
        }

        /// <summary>Bên đã quay lại ván trong thời hạn thì xoá cờ mất kết nối.</summary>
        public async Task ReconnectAsync(int matchId, int userId)
        {
            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null)
            {
                return;
            }

            if (match.DisconnectedUserId == userId)
            {
                match.DisconnectedUserId = null;
                match.DisconnectedAt = null;
                await _db.SaveChangesAsync();
            }
        }

        /// <summary>Bên hết giờ thì xử thua, bên kia thắng với lý do Timeout.</summary>
        public async Task<Match?> HandleTimeoutAsync(int matchId)
        {
            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null || match.Status != StatusPlaying)
            {
                return null;
            }

            // timeLimitSeconds == 0 nghĩa là không giới hạn, không bao giờ hết giờ.
            if (match.TimeLimitSeconds == 0 || match.DisconnectedUserId != null)
            {
                return null;
            }

            // Bên hết giờ là bên đang đến lượt mà đồng hồ đã về 0.
            // timeLimitSeconds == 0 thì đã bị chặn ở trên, nên mốc 0 ở đây là hết giờ thật.
            int loser = match.RedTimeLeftSeconds <= 0 ? 0 : 1;

            await FinishMatchAsync(match, MatchEndReason.Timeout, loser);
            return match;
        }

        /// <summary>
        /// Không có nước đi quá InactivityPromptSeconds thì xử thua bên đến lượt,
        /// lý do Inactivity.
        /// </summary>
        public async Task<Match?> HandleInactivityAsync(int matchId)
        {
            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null || match.Status != StatusPlaying)
            {
                return null;
            }

            double idleSeconds = (DateTime.UtcNow - match.LastMoveAt).TotalSeconds;

            if (idleSeconds < InactivityPromptSeconds + InactivityLoseSeconds)
            {
                return null;
            }

            await FinishMatchAsync(match, MatchEndReason.Inactivity, match.TurnSide);
            return match;
        }

        /// <summary>Đầu hàng: bên nhấn đầu hàng thua, lý do Resign.</summary>
        public async Task<MoveResult> ResignAsync(int matchId, int userId)
        {
            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null || match.Status != StatusPlaying)
            {
                return MoveResult.Fail("Ván đấu không còn hoạt động.");
            }

            int? side = ResolveSide(match, userId);

            if (side == null)
            {
                return MoveResult.Fail("Bạn không tham gia ván này.");
            }

            await FinishMatchAsync(match, MatchEndReason.Resign, side.Value);
            return MoveResult.Ok(MatchEndReason.Resign);
        }

        /// <summary>Xin hoà và được đối phương đồng ý thì kết thúc ván hoà.</summary>
        public async Task<MoveResult> AgreeDrawAsync(int matchId, int userId)
        {
            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null || match.Status != StatusPlaying)
            {
                return MoveResult.Fail("Ván đấu không còn hoạt động.");
            }

            int? side = ResolveSide(match, userId);

            if (side == null)
            {
                return MoveResult.Fail("Bạn không tham gia ván này.");
            }

            await FinishMatchAsync(match, MatchEndReason.AgreedDraw);
            return MoveResult.Ok(MatchEndReason.AgreedDraw);
        }

        // =========================================
        // Elo
        // =========================================

        /// <summary>
        /// Cộng Elo sau ván xếp hạng. Interrupted (server sập) thì giữ nguyên, không tính.
        /// Đầu hàng vẫn trừ Elo bình thường.
        /// </summary>
        public async Task ApplyEloIfRankedAsync(Match match, MatchEndReason reason)
        {
            if (match.Type != MatchType.Ranked || reason == MatchEndReason.Interrupted)
            {
                return;
            }

            var red = await _db.Users.FirstOrDefaultAsync(u => u.Id == match.RedUserId);
            var black = await _db.Users.FirstOrDefaultAsync(u => u.Id == match.BlackUserId);

            if (red == null || black == null)
            {
                return;
            }

            bool redWon = IsRedWinner(match, reason);
            bool draw = reason == MatchEndReason.Repetition
                || reason == MatchEndReason.AgreedDraw
                || reason == MatchEndReason.Stalemate;

            double scoreRed = draw ? 0.5 : (redWon ? 1.0 : 0.0);

            var (newRed, newBlack) = EloCalculator.ApplyResult(
                red.Elo, black.Elo, scoreRed, red.RankedGames, black.RankedGames);

            red.Elo = newRed;
            black.Elo = newBlack;

            if (draw)
            {
                red.Draws++;
                black.Draws++;
            }
            else if (redWon)
            {
                red.Wins++;
                black.Losses++;
            }
            else
            {
                red.Losses++;
                black.Wins++;
            }

            red.RankedGames++;
            black.RankedGames++;

            await _db.SaveChangesAsync();
        }

        // =========================================
        // Nội bộ
        // =========================================

        /// <summary>
        /// Bên đỏ có thắng không. WinnerSide = -1 nghĩa là hoà, không bên nào thắng.
        /// </summary>
        private static bool IsRedWinner(Match match, MatchEndReason reason)
        {
            return match.WinnerSide == 0;
        }

        /// <summary>
        /// Đóng ván. loserSide là bên thua (-1 khi hoà), truyền vào để không phải suy ngược
        /// từ lượt đi: đầu hàng, hết giờ, mất kết nối và treo ván đều có bên thua riêng.
        /// </summary>
        private async Task FinishMatchAsync(Match match, MatchEndReason reason, int loserSide = -1)
        {
            if (match.Status != StatusPlaying)
            {
                return;
            }

            match.Status = StatusFinished;
            match.EndReason = reason;
            match.EndedAt = DateTime.UtcNow;
            match.WinnerSide = loserSide switch
            {
                0 => 1,
                1 => 0,
                _ => -1
            };

            match.RedRankedGames = await _db.Users
                .Where(u => u.Id == match.RedUserId)
                .Select(u => u.RankedGames)
                .FirstOrDefaultAsync();
            match.BlackRankedGames = await _db.Users
                .Where(u => u.Id == match.BlackUserId)
                .Select(u => u.RankedGames)
                .FirstOrDefaultAsync();

            await _db.SaveChangesAsync();

            await ApplyEloIfRankedAsync(match, reason);
        }

        private static int OppositeTurn(int side) => side == 0 ? 1 : 0;

        /// <summary>
        /// Người gửi cầm bên nào: 0 = đỏ, 1 = đen, null = không tham gia ván.
        /// Chỉ dựa trên UserId, không có đường nào để client tự khai bên.
        /// </summary>
        private static int? ResolveSide(Match match, int userId)
        {
            if (match.RedUserId == userId) return 0;
            if (match.BlackUserId == userId) return 1;
            return null;
        }

        private static Side ToEngineSide(int side) => side == 0 ? Side.Red : Side.Black;

        /// <summary>
        /// Nước đi client gửi lên có nằm trong tập nước hợp lệ của engine không.
        /// So bằng 4 toạ độ, không so đối tượng Move, vì mỗi lần gọi GetLegalMoves
        /// là một danh sách Move mới.
        /// </summary>
        private static bool IsLegal(List<Move> legal, ChessMove candidate)
        {
            foreach (var move in legal)
            {
                if (move.FromRow == candidate.FromRow
                    && move.FromCol == candidate.FromCol
                    && move.ToRow == candidate.ToRow
                    && move.ToCol == candidate.ToCol)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Chuyển nước đi của client (Models.ChessMove, là model đi qua SignalR) sang
        /// kiểu Move của engine. Engine dùng ChessBoard lưu quân theo vị trí nên phải
        /// tra quân thật trên bàn, không tự tạo quân mới.
        /// Trả về null khi ô xuất phát trống: nghĩa là client gửi ô không có quân.
        /// </summary>
        private static Move? ToEngineMove(ChessBoard board, ChessMove candidate)
        {
            int rows = board.Rows;
            int cols = board.Cols;

            if (candidate.FromRow < 0 || candidate.FromRow >= rows
                || candidate.FromCol < 0 || candidate.FromCol >= cols
                || candidate.ToRow < 0 || candidate.ToRow >= rows
                || candidate.ToCol < 0 || candidate.ToCol >= cols)
            {
                return null;
            }

            var piece = board[candidate.FromRow, candidate.FromCol];

            if (piece == null)
            {
                return null;
            }

            return new Move(
                piece,
                candidate.FromRow,
                candidate.FromCol,
                candidate.ToRow,
                candidate.ToCol);
        }

        /// <summary>Nước đã tồn tại y hệt trong cây thì coi như lệnh lặp, không áp dụng lại.</summary>
        private static MatchMove? FindDuplicateMove(Match match, ChessMove candidate)
        {
            foreach (var move in match.Moves)
            {
                if (move.FromRow == candidate.FromRow
                    && move.FromCol == candidate.FromCol
                    && move.ToRow == candidate.ToRow
                    && move.ToCol == candidate.ToCol
                    && move.Side == match.TurnSide)
                {
                    return move;
                }
            }

            return null;
        }
    }
}
