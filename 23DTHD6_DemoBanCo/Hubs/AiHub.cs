using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;
using _23DTHD6_DemoBanCo.Services;
// System.IO (ImplicitUsings) cũng có MatchType, nên phải chỉ rõ enum của dự án.
using MatchType = _23DTHD6_DemoBanCo.Models.MatchType;

namespace _23DTHD6_DemoBanCo.Hubs
{
    /// <summary>
    /// Kênh realtime của ván đấu với máy.
    ///
    /// Nguyên tắc bắt buộc ở đây:
    ///   1. Danh tính người gửi LUÔN lấy từ cookie (Context.User), không bao giờ tin
    ///      client tự khai. Client chỉ gửi cấp độ và bên muốn cầm.
    ///   2. Mọi broadcast đi qua SignalR group, TUYỆT ĐỐI không dùng Clients.All.
    ///   3. Tên group là "ai:{matchId}" — CỐ Ý khác "match:{matchId}" của ChessHub.
    ///      Hai hub khác nhau nhưng phải nằm ở group khác nhau: nếu dùng chung tên,
    ///      ván đấu máy (chỉ một người chơi) sẽ trộn tin vào group phòng PvP có
    ///      cùng Id, và người chơi PvP sẽ nhận trạng thái của ván máy.
    ///   4. Lỗi nghiệp vụ trả về cho CHÍNH người gửi, không ném exception làm sập hub.
    ///
    /// Payload "MatchUpdated" / "MatchStarted" CỐ Ý giống hệt ChessHub (cùng tên
    /// trường, cùng kiểu) để một module phía client vẽ được cả ván người-máy và ván
    /// người-với-người mà không cần biết đang nói chuyện với hub nào.
    /// </summary>
    [Authorize]
    public class AiHub : Hub
    {
        private readonly AiMatchService _ai;
        private readonly MatchService _matchService;
        private readonly AppDbContext _db;

        public AiHub(AiMatchService ai, MatchService matchService, AppDbContext db)
        {
            _ai = ai;
            _matchService = matchService;
            _db = db;
        }

        /// <summary>Lấy Id người đang đăng nhập từ cookie đăng nhập.</summary>
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
        /// Tên group của ván đấu máy: "ai:{matchId}".
        ///
        /// Không dùng lại "match:{matchId}" của ChessHub: xem ghi chú đầu file.
        /// </summary>
        public static string GetAiMatchGroupName(int matchId) => $"ai:{matchId}";

        // =========================================
        // Bắt đầu ván
        // =========================================

        /// <summary>
        /// Mở ván đấu với máy và trả về matchId cho client dùng cho mọi lệnh sau.
        ///
        /// difficulty: "easy" | "medium" | "hard"
        /// side      : "red" | "black" | "random"
        ///
        /// "random" được QUYẾT ĐỊNH ở server, không phải ở client: nếu client tự
        /// bốc thì người xem mạng có thể can thiệp, và mỗi lần bấm lại sẽ ra bên
        /// khác nhau. Ở đây bốc một lần rồi đóng băng kết quả vào ván.
        /// </summary>
        public async Task<int> StartMatch(string difficulty, string side)
        {
            int userId = CurrentUserIdOrThrow;

            AiDifficulty level = ParseDifficulty(difficulty);
            bool choosingRed = ParseSide(side);

            var match = await _ai.StartAsync(userId, null, level, choosingRed);

            // Vào group TRƯỚC khi phát MatchStarted, nếu không người gọi sẽ không
            // nhận được chính sự kiện mở ván.
            await Groups.AddToGroupAsync(Context.ConnectionId, GetAiMatchGroupName(match.Id));

            int playerSide = match.RedUserId == userId ? 0 : 1;

            await Clients.Group(GetAiMatchGroupName(match.Id)).SendAsync("MatchStarted", new
            {
                matchId = match.Id,
                fen = match.Fen,
                turnSide = match.TurnSide,
                redUserId = match.RedUserId,
                blackUserId = match.BlackUserId,
                aiDifficulty = match.AiDifficulty,
                playerSide,
                undoLeft = AiMatchService.MaxUndoPerAiMatch
            });

            // Khi người chơi cầm Đen thì máy đã đi nước mở đầu ngay trong StartAsync,
            // tức là TRƯỚC khi kết nối này vào group. Không phát bổ sung thì widget
            // AI sẽ trống trơn cho tới nước đi thứ hai dù máy đã đi một nước.
            await BroadcastAiStatsAsync(match.Id, onlyIfMachineHasMoved: true);

            return match.Id;
        }

        // =========================================
        // Nước đi, đi lại, kết thúc
        // =========================================

        /// <summary>
        /// Nhận nước đi của người chơi. AiMatchService ghi nước đi rồi cho máy đi
        /// trả lời ngay, nên chỉ cần phát trạng thái sau cả hai nước.
        /// </summary>
        public async Task SubmitMove(int matchId, ChessMove move)
        {
            int userId = CurrentUserIdOrThrow;

            if (move == null)
            {
                await Clients.Caller.SendAsync("MoveRejected", new { matchId, error = "Nước đi không hợp lệ." });
                return;
            }

            var result = await _ai.ApplyPlayerMoveAsync(matchId, userId, move);

            if (!result.Success)
            {
                await Clients.Caller.SendAsync("MoveRejected", new { matchId, error = result.Error });
                return;
            }

            await BroadcastMatchUpdatedAsync(matchId);

            // Số liệu chỉ có nghĩa sau khi máy đã đi, nên phát ngay sau MatchUpdated
            // để bảng AI và bàn cờ không lệch thời điểm.
            await BroadcastAiStatsAsync(matchId, onlyIfMachineHasMoved: true);
        }

        /// <summary>
        /// Đồng bộ lại trạng thái ván cho người vừa mở trang (hoặc vừa F5).
        ///
        /// VÌ SAO CẦN HÀM NÀY: ván với máy được tạo ở sảnh, xong trang chuyển sang
        /// /Room/AiGame/{id}. Lúc đó kết nối SignalR mới vừa được mở nên không nhận
        /// được sự kiện MatchStarted đã phát trước đó, và nếu máy đã đi nước mở đầu
        /// (người chơi cầm Đen) thì bàn cờ sẽ đứng ở thế cờ khởi tạo với đúng số quân
        /// cũ. Gọi hàm này ngay khi vào trang là cách duy nhất để lấy đúng thế cờ
        /// kèm tập nước hợp lệ, thay vì dựa vào việc "chắc là có sự kiện đến".
        /// </summary>
        public async Task SyncMatch(int matchId)
        {
            int userId = CurrentUserIdOrThrow;

            var match = await _db.Matches.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null
                || match.Type != MatchType.Ai
                || (match.RedUserId != userId && match.BlackUserId != userId))
            {
                return;
            }

            // Vào group trước: nếu không, các sự kiện phát sau lần này sẽ không tới
            // người vừa mở trang và bàn cờ lại đứng yên.
            await Groups.AddToGroupAsync(Context.ConnectionId, GetAiMatchGroupName(matchId));

            await BroadcastMatchUpdatedAsync(matchId);
            await BroadcastAiStatsAsync(matchId);
        }

        /// <summary>
        /// Đi lại 1 cặp nước đi (2 ply). Trả về chuỗi lỗi tiếng Việt, rỗng là thành công.
        /// Client dùng chính chuỗi trả về để hiện thông báo nên không cần chờ thêm
        /// một sự kiện "UndoRejected" riêng.
        /// </summary>
        public async Task<string> Undo(int matchId)
        {
            int userId = CurrentUserIdOrThrow;

            var result = await _ai.UndoAsync(matchId, userId);

            if (result is null || !result.Success)
            {
                return result?.Error ?? "Không thực hiện được đi lại.";
            }

            await BroadcastMatchUpdatedAsync(matchId);

            // Đã lùi thì số liệu của nước bị xoá không còn đúng với thế cờ hiện tại.
            await BroadcastAiStatsAsync(matchId);

            return string.Empty;
        }


        /// <summary>Đầu hàng: bên bấm thua, ván kết thúc với lý do Resign.</summary>
        public async Task Resign(int matchId)
        {
            int userId = CurrentUserIdOrThrow;

            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);

            if (match is null || match.Type != MatchType.Ai)
            {
                await Clients.Caller.SendAsync("MoveRejected", new { matchId, error = "Ván đấu không tồn tại." });
                return;
            }

            // Máy không có UserId nên không thể đầu hàng; chặn sớm ở đây để thông báo
            // lỗi nói đúng nguyên nhân thay vì trả về lỗi chung chung.
            if (match.RedUserId != userId && match.BlackUserId != userId)
            {
                await Clients.Caller.SendAsync("MoveRejected", new { matchId, error = "Bạn không tham gia ván này." });
                return;
            }

            var result = await _matchService.ResignAsync(matchId, userId);

            if (!result.Success)
            {
                await Clients.Caller.SendAsync("MoveRejected", new { matchId, error = result.Error });
                return;
            }

            await BroadcastMatchUpdatedAsync(matchId);
        }

        /// <summary>
        /// Mất kết nối: chỉ rời group, KHÔNG kết thúc ván.
        ///
        /// Ở phòng PvP có luật xử thua khi mất kết nối 60 giây. Ván đấu máy thì không
        /// có đối thủ người để xử thua, và máy không biết người chơi đã đi hay chưa:
        /// đóng tab rồi mở lại là một việc rất bình thường. Kết thúc ván ở đây sẽ
        /// biến thao tác F5 thành thua ván.
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            int userId = CurrentUserId;

            if (userId != 0)
            {
                // Hub là transient (mỗi lệnh một instance) nên không giữ được danh sách
                // group trong field. Tra ván đang chạy của chính người này trong DB:
                // ván đã kết thúc thì không cần rời group nữa, và cách này đúng kể cả
                // khi kết nối đã mở lại (connection id mới, group cũ đã tự rơi).
                var liveMatchIds = await _db.Matches
                    .AsNoTracking()
                    .Where(m => m.Type == MatchType.Ai)
                    .Where(m => m.Status == MatchService.StatusPlaying)
                    .Where(m => m.RedUserId == userId || m.BlackUserId == userId)
                    .Select(m => m.Id)
                    .ToListAsync();

                foreach (int matchId in liveMatchIds)
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetAiMatchGroupName(matchId));
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        // =========================================
        // Phát trạng thái
        // =========================================

        /// <summary>
        /// Phát trạng thái ván cho group "ai:{id}".
        /// Payload là nguồn chân lý: client vẽ bàn theo đây, không tự suy luận luật cờ.
        ///
        /// Cấu trúc field CỐ Ý giống hệt BroadcastMatchUpdatedAsync của ChessHub để
        /// một module client phục vụ được cả hai hub.
        /// </summary>
        private async Task BroadcastMatchUpdatedAsync(int matchId)
        {
            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null)
            {
                return;
            }

            var legalMoves = new List<object>();
            var lastMove = await _db.MatchMoves
                .Where(m => m.MatchId == matchId)
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync();

            // Bàn cờ và vị trí tướng bị chiếu đều cần cho phần payload bên dưới,
            // nên dựng một lần rồi dùng chung, không dựng lại trong từng nhánh.
            bool isPlaying = match.Status == MatchService.StatusPlaying;
            var board = GameEvaluator.FromFen(match.Fen);

            // Chỉ hỏi chiếu tướng khi ván còn chạy: ván đã xong thì không viền đỏ nữa.
            ChessPiece? checkedKingRed = isPlaying ? CheckDetector.GetCheckedKing(board, Side.Red) : null;
            ChessPiece? checkedKingBlack = isPlaying ? CheckDetector.GetCheckedKing(board, Side.Black) : null;
            ChessPiece? checkedKing = checkedKingRed ?? checkedKingBlack;

            int? checkSide = !isPlaying
                ? null
                : checkedKingRed != null ? 0
                : checkedKingBlack != null ? 1
                : (int?)null;

            if (isPlaying)
            {
                var sideToMove = match.TurnSide == 0 ? Side.Red : Side.Black;
                var moves = ChessRules.GetLegalMoves(board, sideToMove);

                foreach (var move in moves)
                {
                    legalMoves.Add(new
                    {
                        move.FromRow,
                        move.FromCol,
                        move.ToRow,
                        move.ToCol
                    });
                }
            }

            int myUserId = CurrentUserId;
            int yourSide = match.RedUserId == myUserId ? 0 : (match.BlackUserId == myUserId ? 1 : -1);

            await Clients.Group(GetAiMatchGroupName(matchId)).SendAsync("MatchUpdated", new
            {
                matchId = match.Id,
                fen = match.Fen,
                status = match.Status,
                endReason = (int)match.EndReason,
                turnSide = match.TurnSide,
                yourSide,
                canMove = match.Status == MatchService.StatusPlaying
                    && yourSide == match.TurnSide
                    && yourSide >= 0,
                // Vị trí tướng đang bị chiếu: client vẽ viền đỏ và rung ô này.
                // Cả ba trường đều null khi ván đã xong, để client bỏ viền đỏ.
                checkSide,
                checkRow = checkedKing?.Row,
                checkCol = checkedKing?.Col,
                legalMoves,
                lastMove = lastMove == null
                    ? null
                    : new
                    {
                        lastMove.FromRow,
                        lastMove.FromCol,
                        lastMove.ToRow,
                        lastMove.ToCol,
                        san = lastMove.SanMove
                    },
                capturedPieceId = lastMove?.CapturedPieceId,
                redTimeLeft = match.RedTimeLeftSeconds,
                blackTimeLeft = match.BlackTimeLeftSeconds,
                serverTimeUtc = DateTime.UtcNow.ToString("O"),
                // Ván đấu máy có trần đi lại riêng (3 lượt cho cả ván, không chia
                // theo bên như PvP), nên lấy từ hằng của AiMatchService.
                undoLeft = yourSide == 0
                    ? AiMatchService.MaxUndoPerAiMatch - match.RedUndoCount
                    : yourSide == 1
                        ? AiMatchService.MaxUndoPerAiMatch - match.BlackUndoCount
                        : 0,
                youWon = match.Status == MatchService.StatusFinished
                    ? yourSide >= 0 ? (int)match.WinnerSide == yourSide : (bool?)null
                    : (bool?)null
            });
        }

        /// <summary>
        /// Phát số liệu suy nghĩ của máy (đặc tả 9.2).
        ///
        /// onlyIfMachineHasMoved = true dùng sau khi người chơi vừa đi: nếu ván vừa
        /// kết thúc bằng nước của chính người chơi thì máy chưa hề đi, gửi số liệu
        /// rỗng lên widget chỉ làm nó nhảy về trạng thái "chưa có".
        /// </summary>
        private async Task BroadcastAiStatsAsync(int matchId, bool onlyIfMachineHasMoved = false)
        {
            var match = await _db.Matches.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (match is null || match.Type != MatchType.Ai)
                return;

            var machineMoves = await _ai.GetMachineMovesAsync(matchId);

            if (machineMoves.Count == 0)
            {
                if (onlyIfMachineHasMoved)
                    return;

                await Clients.Group(GetAiMatchGroupName(matchId)).SendAsync("AiStats", new
                {
                    matchId,
                    difficulty = match.AiDifficulty,
                    difficultyLabel = DifficultyLabel((AiDifficulty)match.AiDifficulty),
                    maxDepth = AiEngine.DepthFor((AiDifficulty)match.AiDifficulty),
                    hasStats = false,
                    summary = "Máy chưa đi nước nào trong ván này nên chưa có số liệu.",
                    nodesEvaluated = 0,
                    depthReached = 0,
                    elapsedMs = 0,
                    bestMove = "",
                    principalVariation = "",
                    moves = Array.Empty<object>()
                });

                return;
            }

            MatchMove latest = machineMoves[^1];
            AiDifficulty level = (AiDifficulty)match.AiDifficulty;

            var rows = machineMoves
                .Select(m => new
                {
                    m.MoveNumber,
                    m.Side,
                    san = m.SanMove,
                    nodesEvaluated = m.AiNodesEvaluated,
                    depthReached = m.AiDepthReached,
                    elapsedMs = m.AiElapsedMs,
                    principalVariation = m.AiPrincipalVariation
                })
                .ToArray();

            await Clients.Group(GetAiMatchGroupName(matchId)).SendAsync("AiStats", new
            {
                matchId,
                difficulty = match.AiDifficulty,
                difficultyLabel = DifficultyLabel(level),
                // maxDepth đi kèm để client không phải tự nhân bảng độ sâu ở phía mình.
                maxDepth = AiEngine.DepthFor(level),
                hasStats = true,
                summary = AiMatchService.FormatMachineMoveStats(latest),
                nodesEvaluated = latest.AiNodesEvaluated,
                depthReached = latest.AiDepthReached,
                elapsedMs = latest.AiElapsedMs,
                bestMove = latest.SanMove,
                principalVariation = latest.AiPrincipalVariation,
                moves = rows
            });
        }

        // =========================================
        // Nội bộ
        // =========================================

        /// <summary>
        /// Đổi chuỗi của client ra enum. Sai giá trị thì báo lỗi ngay thay vì âm thầm
        /// rơi về mặc định, vì "typo" ở đây đồng nghĩa với chơi nhầm cấp độ mà
        /// người chơi không hề biết.
        /// </summary>
        private static AiDifficulty ParseDifficulty(string? difficulty) => difficulty switch
        {
            "easy" => AiDifficulty.Easy,
            "medium" => AiDifficulty.Medium,
            "hard" => AiDifficulty.Hard,
            _ => throw new HubException("Cấp độ không hợp lệ. Chọn Dễ, Trung bình hoặc Khó.")
        };

        /// <summary>Đổi chuỗi bên của client ra quyết định cầm Đỏ hay cầm Đen.</summary>
        private static bool ParseSide(string? side) => side switch
        {
            "red" => true,
            "black" => false,
            // Bốc ở server: client bấm lại nhiều lần cũng không đổi được kết quả,
            // và không thể can thiệp từ phía người xem mạng.
            "random" => Random.Shared.Next(2) == 0,
            _ => throw new HubException("Bên cầm không hợp lệ. Chọn Đỏ, Đen hoặc Ngẫu nhiên.")
        };

        private static string DifficultyLabel(AiDifficulty difficulty) => difficulty switch
        {
            AiDifficulty.Easy => "Dễ",
            AiDifficulty.Medium => "Trung bình",
            AiDifficulty.Hard => "Khó",
            _ => "Dễ"
        };
    }
}