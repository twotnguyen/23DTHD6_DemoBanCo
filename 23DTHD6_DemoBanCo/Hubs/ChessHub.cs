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
    /// Kênh realtime của phòng chơi.
    ///
    /// Nguyên tắc bắt buộc ở đây:
    ///   1. Danh tính người gửi LUÔN lấy từ cookie (Context.User), không bao giờ tin client tự khai.
    ///   2. Mọi broadcast đi qua SignalR group, TUYỆT ĐỐI không dùng Clients.All.
    ///      Bản demo cũ từng dùng Clients.All khiến tin của phòng này lọt sang phòng khác.
    ///   3. Tên group thống nhất: phòng là "room:{roomId}", ván là "match:{matchId}".
    ///   4. Lỗi nghiệp vụ trả về cho CHÍNH người gửi, không ném exception làm sập hub.
    /// </summary>
    [Authorize]
    public class ChessHub : Hub
    {
        private readonly RoomService _roomService;
        private readonly MatchService _matchService;
        private readonly ChatService _chatService;
        private readonly AppDbContext _db;

        public ChessHub(RoomService roomService, MatchService matchService, ChatService chatService, AppDbContext db)
        {
            _roomService = roomService;
            _matchService = matchService;
            _chatService = chatService;
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

        // =========================================
        // Người chơi tham gia / rời phòng
        // =========================================

        /// <summary>
        /// Vào phòng và vào group realtime. Ván xếp hạng chặn khán giả tuyệt đối
        /// (đặc tả EC-01): phòng đó bị khoá và không ai được xem, kể cả khi có link/mã.
        /// </summary>
        public async Task JoinRoom(int roomId, string? side)
        {
            int userId = CurrentUserId;
            if (userId == 0)
                throw new HubException("Chưa đăng nhập.");

            var room = await _roomService.GetByIdAsync(roomId);
            if (room == null)
                throw new HubException("Phòng không tồn tại.");

            if (room.Status == RoomStatus.Closed)
                throw new HubException("Phòng này đã đóng.");

            // Chỉ chặn khi người vào KHÔNG phải đấu thủ. Hai đấu thủ được ghép từ hàng
            // đợi đã có sẵn dòng RoomParticipant nên vẫn vào được bình thường.
            bool isRanked = room.MatchType == MatchType.Ranked;
            var alreadyIn = await _db.RoomParticipants
                .AsNoTracking()
                .AnyAsync(p => p.RoomId == roomId && p.UserId == userId);

            if (isRanked && !alreadyIn)
                throw new HubException("Ván xếp hạng không cho phép người xem.");

            await Groups.AddToGroupAsync(Context.ConnectionId, GetRoomGroupName(roomId));

            await _roomService.AddParticipantAsync(
                roomId,
                userId,
                Context.ConnectionId,
                NormalizeSide(side));

            await NotifyParticipantsChangedAsync(roomId);
        }

        public async Task LeaveRoom(int roomId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetRoomGroupName(roomId));
            await _roomService.RemoveParticipantByConnectionAsync(Context.ConnectionId);

            await NotifyParticipantsChangedAsync(roomId);
        }

        /// <summary>
        /// Gửi danh sách người chơi hiện tại cho mọi người trong phòng.
        /// Client cập nhật trực tiếp, không tải lại trang.
        /// </summary>
        private async Task NotifyParticipantsChangedAsync(int roomId)
        {
            var room = await _roomService.GetByIdAsync(roomId);

            // Side và Role gửi ra đúng dạng chuỗi mà phòng chờ dùng, để client không phải
            // tự dịch lại. Phe dùng ký hiệu lưu trong DB: "do" / "den".
            var list = room?.Participants
                .OrderBy(p => p.JoinedAt)
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

            int count = list?.Count ?? 0;

            await Clients.Group(GetRoomGroupName(roomId))
                .SendAsync("RoomParticipantsChanged", new
                {
                    count = count,
                    // capacity là tổng số người trong phòng (2 đấu thủ + 5 khán giả),
                    // dùng chung hằng số với RoomService để không lệch nhau.
                    capacity = RoomService.MaxRoomCapacity,
                    participants = list
                });
        }

        // Kết nối bị đóng đột ngột: dọn người chơi ra khỏi phòng và báo cho phòng biết
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var affected = await _roomService.GetRoomIdsByConnectionAsync(Context.ConnectionId);
            int userId = CurrentUserId;

            // Mất kết nối chỉ ghi nhận cờ đếm ngược 60s, không kết thúc ván tức thời.
            await _roomService.RemoveParticipantByConnectionAsync(Context.ConnectionId);

            foreach (int roomId in affected)
            {
                // Chủ phòng rời đi thì nhượng quyền cho người còn lại (đặc tả 2.3).
                // Không có ai nhận được thì phòng sẽ đóng ngay ở nhánh dưới.
                if (userId != 0 && await _roomService.TransferHostIfOwnerLeftAsync(roomId, userId))
                {
                    await Clients.Group(GetRoomGroupName(roomId)).SendAsync("HostTransferred", new
                    {
                        roomId,
                        previousOwnerId = userId
                    });
                }

                await NotifyParticipantsChangedAsync(roomId);
                await LeaveMatchGroupsAsync(roomId);

                // Không còn đấu thủ nào thì phòng đóng vĩnh viễn. Báo cả phòng trước khi
                // đóng để các client còn treo (khán giả) biết phải tự về sảnh.
                bool closed = await _roomService.CloseIfNoPlayersLeftAsync(roomId);

                if (closed)
                {
                    await Clients.Group(GetRoomGroupName(roomId)).SendAsync("RoomClosed", new
                    {
                        roomId,
                        reason = "Phòng đã đóng vì không còn đấu thủ nào."
                    });

                    await BroadcastRoomStateAsync(roomId);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Rời group ván của những ván đang chạy mà người này tham gia.
        /// Mất kết nối không được phép kết thúc ván ngay, MatchService tự tính 60s.
        /// </summary>
        private async Task LeaveMatchGroupsAsync(int roomId)
        {
            int userId = CurrentUserId;
            if (userId == 0)
            {
                return;
            }

            var matchIds = await _db.Matches
                .Where(m => m.RoomId == roomId && m.Status == MatchService.StatusPlaying)
                .Where(m => m.RedUserId == userId || m.BlackUserId == userId)
                .Select(m => m.Id)
                .ToListAsync();

            foreach (int matchId in matchIds)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetMatchGroupName(matchId));
                await _matchService.MarkDisconnectedAsync(matchId, userId);
            }
        }

        /// <summary>
        /// Khởi tạo ván theo yêu cầu của client, dùng cho chế độ có chọn giờ.
        /// Toàn bộ điều kiện ghép và chống trùng nằm trong TryStartMatchAsync,
        /// hàm này chỉ kiểm tham số rồi gọi lại. Không tự tạo ván để tránh hai nơi
        /// cùng quyết định.
        ///
        /// timeMode: "blitz" = 300s, "rapid" = 600s, "standard" = 900s, "unlimited" = 0.
        /// </summary>
        /// <summary>
        /// Khởi tạo ván theo yêu cầu của client, dùng cho chế độ có chọn giờ.
        /// Toàn bộ điều kiện và chống ghép trùng nằm trong
        /// <see cref="TryStartMatchAsync"/>, hàm này chỉ chuyển thời lượng rồi gọi lại.
        ///
        /// timeMode: "blitz" = 300s, "rapid" = 600s, "standard" = 900s, "unlimited" = 0.
        /// </summary>
        public async Task StartMatch(int roomId, int redUserId, int blackUserId, string timeMode)
        {
            int seconds = TimeModeToSeconds(timeMode);

            if (seconds < 0)
            {
                await Clients.Caller.SendAsync("RoomActionRejected", new
                {
                    roomId,
                    error = "Chế độ thời gian không hợp lệ."
                });
                return;
            }

            // Client có thể tự truyền id, nên kiểm lại trước khi dùng.
            if (redUserId == blackUserId
                || !await AreBothSeatedInRoomAsync(roomId, redUserId, blackUserId))
            {
                await Clients.Caller.SendAsync("RoomActionRejected", new
                {
                    roomId,
                    error = "Hai bên phải là hai đấu thủ khác nhau đang ngồi trong phòng này."
                });
                return;
            }

            await TryStartMatchAsync(roomId, seconds);
        }

        /// <summary>Đổi tên chế độ thời gian ra số giây, trả về -1 nếu không hợp lệ.</summary>
        private static int TimeModeToSeconds(string? timeMode) => timeMode switch
        {
            "blitz" => 300,
            "rapid" => 600,
            "standard" => 900,
            "unlimited" => 0,
            _ => -1
        };

        /// <summary>Trả lại khoá ghép ván khi lệnh bị từ chối, để phòng không bị kẹt vĩnh viễn.</summary>
        private async Task ReleaseStartClaimAsync(int roomId)
        {
            await _db.Rooms
                .Where(r => r.Id == roomId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.IsStartingMatch, false));
        }


        /// <summary>
        /// Khởi tạo ván khi phòng đủ hai đấu thủ và cả hai đã bấm sẵn sàng.
        ///
        /// Gọi được từ nhiều nơi (ToggleReady, controller qua IHubContext) nên phải tự
        /// kiểm tra hết điều kiện, và chặn trùng bằng cập nhật nguyên tử trên bảng Rooms:
        /// nếu chỉ đọc rồi mới ghi thì hai lệnh cùng lúc đều thấy phòng trống và cùng tạo ván.
        /// </summary>
        public async Task TryStartMatchAsync(int roomId, int timeLimitSeconds = 0)
        {
            // Đánh dấu trước, tạo ván sau. ExecuteUpdate là nguyên tử nên chỉ một lệnh thắng.
            int claimed = await _db.Rooms
                .Where(r => r.Id == roomId
                    && r.ActiveMatchId == null
                    && r.Status != RoomStatus.Playing
                    && !r.IsStartingMatch)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.IsStartingMatch, true));

            if (claimed == 0)
            {
                return;
            }

            try
            {
                var players = await _db.RoomParticipants
                    .Where(p => p.RoomId == roomId && p.Role == ParticipantRole.Player)
                    .OrderBy(p => p.JoinedAt)
                    .ToListAsync();

                // Thiếu người hoặc chưa ai sẵn sàng: trả khoá rồi thoát, đợi lần bấm sau.
                if (players.Count != 2 || players.Any(p => !p.IsReady))
                {
                    await ReleaseStartClaimAsync(roomId);
                    return;
                }

                // Ưu tiên ghế đã chọn; còn lại thì vào trước làm đỏ, vào sau làm đen.
                var red = players.FirstOrDefault(p => p.Side == RoomService.SideRed);
                var black = players.FirstOrDefault(p => p.Side == RoomService.SideBlack);

                if (red == null || black == null)
                {
                    red = players[0];
                    black = players[1];

                    red.Side = RoomService.SideRed;
                    black.Side = RoomService.SideBlack;
                }

                // Chế độ trận và ngân sách giờ lấy từ phòng, không lấy từ tham số client:
                // phòng xếp hạng phải luôn là 10 phút Rapid và không bao giờ có khán giả.
                var roomConfig = await _db.Rooms.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == roomId);

                var match = await _matchService.StartMatchAsync(
                    roomId, red.UserId, black.UserId,
                    roomConfig?.MatchType ?? MatchType.Casual,
                    roomConfig?.TimeLimitSeconds ?? 0,
                    AiDifficulty.Easy);

                foreach (var player in new[] { red, black })
                {
                    if (!string.IsNullOrEmpty(player.ConnectionId))
                    {
                        await Groups.AddToGroupAsync(
                            player.ConnectionId, GetMatchGroupName(match.Id));
                    }
                }

                var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);

                if (room != null)
                {
                    room.ActiveMatchId = match.Id;
                    room.Status = RoomStatus.Playing;
                }

                // Xoá cờ sẵn sàng, nếu không ván sau mở ra vẫn thấy "2/2 sẵn sàng".
                await _db.RoomParticipants
                    .Where(p => p.RoomId == roomId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.IsReady, false));

                await _db.SaveChangesAsync();

                await BroadcastRoomStateAsync(roomId);

                await Clients.Group(GetRoomGroupName(roomId)).SendAsync("MatchStarted", new
                {
                    matchId = match.Id,
                    fen = match.Fen,
                    turnSide = match.TurnSide,
                    redUserId = red.UserId,
                    blackUserId = black.UserId
                });
            }
            catch
            {
                // Lỗi thì trả khoá, nếu không phòng kẹt ở IsStartingMatch mãi mãi.
                await ReleaseStartClaimAsync(roomId);
                throw;
            }
        }

        /// <summary>
        /// Cả hai phải đang ngồi trong phòng và là đấu thủ, không phải khán giả.
        /// Client tự truyền userId lên nên phải kiểm lại thay vì tin.
        /// </summary>
        private async Task<bool> AreBothSeatedInRoomAsync(int roomId, int redUserId, int blackUserId)
        {
            int seatedPlayers = await _db.RoomParticipants
                .CountAsync(p => p.RoomId == roomId
                    && p.Role == ParticipantRole.Player
                    && (p.UserId == redUserId || p.UserId == blackUserId));

            return seatedPlayers == 2;
        }

        // =========================================
        // Ván đấu: nước đi, đi lại, kết thúc
        // =========================================

        /// <summary>
        /// Nhận nước đi từ client. Server kiểm luật rồi mới ghi và phát cho cả ván.
        /// Trả về kết quả cho CHÍNH người gửi để UI hiện lỗi cụ thể.
        /// </summary>
        public async Task<MatchService.MoveResult> SubmitMove(int matchId, ChessMove move)
        {
            int userId = CurrentUserIdOrThrow;

            if (move == null)
            {
                return MatchService.MoveResult.Fail("Nước đi không hợp lệ.");
            }

            var result = await _matchService.SubmitMoveAsync(matchId, userId, move);

            if (result.Success)
            {
                await BroadcastMatchUpdatedAsync(matchId);
            }

            return result;
        }

        public async Task RequestUndo(int matchId)
        {
            int userId = CurrentUserIdOrThrow;

            var result = await _matchService.RequestUndoAsync(matchId, userId);

            if (!result.Success)
            {
                await Clients.Caller.SendAsync("UndoRejected", new { matchId, error = result.Error });
                return;
            }

            int roomId = await _db.Matches
                .Where(m => m.Id == matchId)
                .Select(m => m.RoomId)
                .FirstOrDefaultAsync();

            var participant = await _roomService.GetParticipantAsync(roomId, userId);

            await Clients.Group(GetMatchGroupName(matchId)).SendAsync("UndoRequested", new
            {
                matchId,
                fromUserId = userId,
                displayName = participant?.User?.DisplayName ?? "Người chơi"
            });
        }

        public async Task RespondUndo(int matchId, bool accept)
        {
            int userId = CurrentUserIdOrThrow;

            if (!accept)
            {
                // Từ chối không trừ lượt (đặc tả 3.2): việc huỷ đề nghị nằm ở service,
                // không sửa bộ đếm ở tầng hub.
                if (!await _matchService.RejectUndoAsync(matchId, userId))
                {
                    await Clients.Caller.SendAsync("UndoRejected",
                        new { matchId, error = "Không có yêu cầu đi lại nào đang chờ trả lời." });
                    return;
                }

                await Clients.Group(GetMatchGroupName(matchId))
                    .SendAsync("UndoResolved", new { matchId, accepted = false, byUserId = userId });

                return;
            }

            var result = await _matchService.AcceptUndoAsync(matchId, userId);

            if (!result.Success)
            {
                await Clients.Caller.SendAsync("UndoRejected", new { matchId, error = result.Error });
                return;
            }

            await Clients.Group(GetMatchGroupName(matchId))
                .SendAsync("UndoResolved", new { matchId, accepted = true, byUserId = userId });

            await BroadcastMatchUpdatedAsync(matchId);
        }

        public async Task Resign(int matchId)
        {
            int userId = CurrentUserIdOrThrow;

            var result = await _matchService.ResignAsync(matchId, userId);

            if (result.Success)
            {
                await BroadcastMatchUpdatedAsync(matchId);
            }
            else
            {
                await Clients.Caller.SendAsync("MoveRejected", new { matchId, error = result.Error });
            }
        }

        /// <summary>Bên mất kết nối quay lại ván: xoá cờ đếm ngược 60s.</summary>
        public async Task DisconnectFromMatch(int matchId)
        {
            int userId = CurrentUserIdOrThrow;

            await _matchService.ReconnectAsync(matchId, userId);
        }

        /// <summary>
        /// Phát trạng thái ván cho group "match:{id}".
        /// Payload là nguồn chân lý: client vẽ bàn theo đây, không tự suy luật luật cờ.
        ///
        /// VÌ SAO KHÔNG PHÁT MỘT PAYLOAD CHO CẢ GROUP:
        ///   yourSide và canMove phụ thuộc người NHẬN, không phụ thuộc người phát.
        ///   Nếu gộp vào một Clients.Group(...).SendAsync thì cả hai đấu thủ nhận cùng
        ///   một yourSide — tức là cả hai cùng tưởng mình cầm một phe, và người cầm
        ///   phe không đến lượt sẽ bị khoá bàn vĩnh viễn. Vì vậy phần dùng chung được
        ///   dựng một lần (FEN, nước hợp lệ, chiếu tướng), còn phần riêng theo người
        ///   nhận thì gửi riêng cho từng connection.
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

            // Ván còn đếm ngược thì không gửi nước hợp lệ: client tô ô sẵn sẽ khiến
            // người chơi tưởng được đi trong lúc đồng hồ 3-2-1 còn chạy.
            bool countingDown = MatchService.IsCountingDown(match);

            int? checkSide = !isPlaying
                ? null
                : checkedKingRed != null ? 0
                : checkedKingBlack != null ? 1
                : (int?)null;

            if (isPlaying && !countingDown)
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

            // Phần DÙNG CHUNG: dựng một lần cho mọi người nhận.
            var shared = new
            {
                matchId = match.Id,
                fen = match.Fen,
                status = match.Status,
                endReason = (int)match.EndReason,
                turnSide = match.TurnSide,
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

                // Trạng thái đề nghị để client hiện nút trả lời ngay khi vào lại ván
                // (F5), không bỏ sót đề nghị đang chờ.
                drawOfferedBy = match.DrawOfferState == MatchService.OfferStatePending
                    ? match.DrawOfferedBy
                    : null,
                drawSecondsLeft = match.DrawOfferState == MatchService.OfferStatePending
                    && match.DrawOfferExpiresAt.HasValue
                        ? (int)SecondsUntil(match.DrawOfferExpiresAt.Value, DateTime.UtcNow)
                        : 0,
                undoRequestedBy = match.UndoRequestedBy,
                undoSecondsLeft = match.UndoRequestExpiresAt.HasValue
                    ? (int)SecondsUntil(match.UndoRequestExpiresAt.Value, DateTime.UtcNow)
                    : 0,

                // Đồng hồ ân hạn mất kết nối 60s (đặc tả EC-03) và cửa sổ tái đấu (R14).
                disconnectedUserId = match.DisconnectedUserId,
                disconnectSecondsLeft = match.DisconnectedAt.HasValue
                    ? (int)SecondsUntil(match.DisconnectedAt.Value.AddSeconds(
                        MatchService.DisconnectGraceSeconds), DateTime.UtcNow)
                    : 0,
                canRematch = MatchService.CanRematch(match),
                rematchPendingBy = match.RematchState == MatchService.OfferStatePending
                    ? new[] { match.RedRematchBy, match.BlackRematchBy }
                        .Where(id => id.HasValue).Select(id => id!.Value).ToArray()
                    : Array.Empty<int>()
            };

            // Danh sách connection đang ở trong group: mỗi người nhận MỘT payload
            // riêng với yourSide đúng của mình. Gửi theo danh sách thay vì
            // Clients.Group để tránh lỗi "cả hai cùng tưởng mình cùng phe".
            var connections = await _db.RoomParticipants.AsNoTracking()
                .Where(p => p.RoomId == match.RoomId && p.ConnectionId != "")
                .Select(p => new { p.ConnectionId, p.UserId, p.Role })
                .ToListAsync();

            foreach (var seat in connections)
            {
                // Khán giả nhận yourSide = -1: không được đi, chỉ xem (đặc tả 4.1).
                int side = seat.Role == ParticipantRole.Player
                    ? (match.RedUserId == seat.UserId ? 0
                        : match.BlackUserId == seat.UserId ? 1 : -1)
                    : -1;

                await Clients.Client(seat.ConnectionId).SendAsync("MatchUpdated", new
                {
                    shared.matchId,
                    shared.fen,
                    shared.status,
                    shared.endReason,
                    shared.turnSide,
                    yourSide = side,
                    canMove = match.Status == MatchService.StatusPlaying
                        && side >= 0
                        && side == match.TurnSide,
                    shared.checkSide,
                    shared.checkRow,
                    shared.checkCol,
                    shared.legalMoves,
                    shared.lastMove,
                    shared.capturedPieceId,
                    shared.redTimeLeft,
                    shared.blackTimeLeft,
                    shared.serverTimeUtc,
                    undoLeft = side == 0
                        ? MatchService.MaxUndoPerSide - match.RedUndoCount
                        : side == 1
                            ? MatchService.MaxUndoPerSide - match.BlackUndoCount
                            : 0,
                    youWon = match.Status == MatchService.StatusFinished
                        ? (side >= 0 ? (int)match.WinnerSide == side : (bool?)null)
                        : (bool?)null,

                    // Đồng hồ đếm ngược: client hiện lớp 3-2-1 và khoá bàn cho tới khi bằng 0.
                    countdownSecondsLeft = countingDown
                        ? (int)SecondsUntil(match.CountdownEndsAt!.Value, DateTime.UtcNow)
                        : 0,
                    shared.drawOfferedBy,
                    shared.drawSecondsLeft,
                    shared.undoRequestedBy,
                    shared.undoSecondsLeft,
                    shared.disconnectedUserId,
                    shared.disconnectSecondsLeft,
                    shared.canRematch,
                    shared.rematchPendingBy
                });
            }

            // Khi ván kết thúc, phòng phải trở lại trạng thái chờ và bỏ ván đang chạy,
            // nếu không phòng sẽ kẹt ở Playing vĩnh viễn và không ai vào lại được.
            if (match.Status == MatchService.StatusFinished)
            {
                await ReleaseRoomAfterMatchAsync(match);
            }
        }

        /// <summary>
        /// Ván đã xong: gỡ ván khỏi phòng, đặt phòng về chờ, xoá cờ sẵn sàng và báo cả phòng.
        /// </summary>
        private async Task ReleaseRoomAfterMatchAsync(Match match)
        {
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == match.RoomId);

            if (room == null || room.ActiveMatchId != match.Id)
            {
                return;
            }

            var people = await _db.RoomParticipants
                .AsNoTracking()
                .Where(p => p.RoomId == room.Id)
                .ToListAsync();

            room.ActiveMatchId = null;
            room.Status = RoomStatus.Finished;
            room.IsStartingMatch = false;

            // Bỏ sạch cờ sẵn sàng để ván sau bắt đầu từ đầu.
            await _db.RoomParticipants
                .Where(p => p.RoomId == room.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.IsReady, false));

            await _db.SaveChangesAsync();

            await Clients.Group(GetRoomGroupName(match.RoomId)).SendAsync("RoomStateChanged", new
            {
                roomId = room.Id,
                visibility = room.Visibility.ToString(),
                status = room.Status.ToString(),
                activeMatchId = room.ActiveMatchId,
                readyCount = 0,
                playerCount = people.Count(p => p.Role == ParticipantRole.Player),
                spectatorCount = people.Count(p => p.Role == ParticipantRole.Spectator),
                capacity = RoomService.MaxRoomCapacity
            });
        }

        /// <summary>
        /// Trả trạng thái phòng cho người vừa mở trang (hoặc vừa F5).
        ///
        /// VÌ SAO CẦN HÀM NÀY: trước khi vào phòng chờ, trang chỉ nhận thế cờ khởi
        /// tạo từ API tĩnh. Nếu người chơi F5 giữa lúc ván đang diễn ra, không có
        /// sự kiện nào phát lại (ván vẫn chạy, không ai vừa đi nước nào), bàn cờ sẽ
        /// quay về thế khởi tạo và người chơi tưởng mất hết nước đi. Hàm này trả về
        /// id ván đang chạy để client vào thẳng chế độ thi đấu rồi đồng bộ tiếp.
        /// </summary>
        public async Task<object?> GetRoomState(int roomId)
        {
            int userId = CurrentUserIdOrThrow;

            var room = await _db.Rooms.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roomId);

            if (room == null)
            {
                return null;
            }

            if (room.Status == RoomStatus.Closed)
            {
                return new { roomId, closed = true, activeMatchId = (int?)null, undoLeft = 0 };
            }

            int? matchId = room.Status == RoomStatus.Playing
                ? room.ActiveMatchId
                : null;

            // Lượt đi lại còn lại tính theo phe của người gọi, đúng như nút hiển thị.
            int undoLeft = 0;

            if (matchId.HasValue)
            {
                var match = await _db.Matches.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == matchId.Value);

                if (match != null)
                {
                    if (match.RedUserId == userId)
                        undoLeft = MatchService.MaxUndoPerSide - match.RedUndoCount;
                    else if (match.BlackUserId == userId)
                        undoLeft = MatchService.MaxUndoPerSide - match.BlackUndoCount;
                }
            }

            return new
            {
                roomId,
                closed = false,
                activeMatchId = matchId,
                status = room.Status.ToString(),
                isRanked = room.MatchType == MatchType.Ranked,
                undoLeft
            };
        }

        /// <summary>
        /// Phát lại toàn bộ trạng thái ván cho người gọi.
        ///
        /// Dùng khi bàn đang bị khoá dù không nên: hết đồng hồ đếm 3-2-1, vừa F5,
        /// hay kết nối lại. Những lúc đó không có nước đi nào xảy ra nên không có sự
        /// kiện nào phát tự nhiên, mà sự kiện MatchStarted lúc tạo ván lại mang danh
        /// sách nước hợp lệ rỗng vì đang đếm — gọi hàm này là cách duy nhất để lấy
        /// đúng nước hợp lệ mà không phải tự suy luật luật cờ ở client.
        /// </summary>
        public async Task GetMatchState(int matchId)
        {
            int userId = CurrentUserIdOrThrow;

            var match = await _db.Matches.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null
                || (match.RedUserId != userId && match.BlackUserId != userId))
            {
                return;
            }

            await BroadcastMatchUpdatedAsync(matchId);
        }

        // =========================================
        // Chat
        // =========================================

        /// <summary>
        /// Gửi tin nhắn vào phòng. Lọc từ cấm và kiểm tra quyền kênh trước khi phát,
        /// tin lỗi trả về cho người gửi chứ không phát ra cả phòng.
        /// </summary>
        public async Task SendChat(int roomId, int channel, string body, string? sticker)
        {
            int userId = CurrentUserIdOrThrow;

            ChatMessage message;
            try
            {
                message = await _chatService.SendRoomMessageAsync(roomId, userId, channel, body, sticker);
            }
            catch (InvalidOperationException ex)
            {
                await Clients.Caller.SendAsync("ChatRejected", new { roomId, error = ex.Message });
                return;
            }

            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            await Clients.Group(GetRoomGroupName(roomId)).SendAsync("ReceiveChat", new
            {
                id = message.Id,
                userId = message.UserId,
                displayName = user?.DisplayName ?? "Người chơi",
                channel = message.Channel,
                body = message.Body,
                kind = message.Kind,
                sticker = message.Sticker,
                createdAt = message.CreatedAt.ToString("O")
            });
        }

        /// <summary>Lấy lịch sử tin nhắn của một kênh, mới nhất nằm cuối.</summary>
        public async Task<List<object>> GetChatHistory(int roomId, int channel, int take)
        {
            int userId = CurrentUserIdOrThrow;

            if (!await _chatService.CanAccessChannelAsync(roomId, userId, channel))
            {
                return new List<object>();
            }

            var messages = await _chatService.GetHistoryAsync(roomId, channel, take);

            var userIds = messages.Select(m => m.UserId).Distinct().ToList();
            var names = await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            return messages.Select(m => (object)new
            {
                id = m.Id,
                userId = m.UserId,
                displayName = names.TryGetValue(m.UserId, out var name) ? name : "Người chơi",
                channel = m.Channel,
                body = m.Body,
                kind = m.Kind,
                sticker = m.Sticker,
                createdAt = m.CreatedAt.ToString("O")
            }).ToList();
        }

        // =========================================
        // Phòng chờ
        // =========================================

        public async Task ToggleReady(int roomId, bool ready)
        {
            int userId = CurrentUserIdOrThrow;

            var participant = await _db.RoomParticipants
                .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId);

            if (participant == null)
            {
                await Clients.Caller.SendAsync("RoomActionRejected", new { roomId, error = "Bạn không ở trong phòng này." });
                return;
            }

            participant.IsReady = ready;
            await _db.SaveChangesAsync();

            await NotifyParticipantsChangedAsync(roomId);
            await BroadcastRoomStateAsync(roomId);

            // Bấm sẵn sàng là sự kiện duy nhất phát ra khi cả hai đã sẵn sàng, nên ván
            // phải được khởi tạo ngay tại đây. Nếu chờ client gọi StartMatch thì hai
            // người bấm xong sẽ đứng im vĩnh viễn, không ai tạo ván.
            await TryStartMatchAsync(roomId);
        }

        /// <summary>
        /// Xin đổi bên khi đã đủ hai đấu thủ. Chỉ ghi nhận và mở hạn 30 giây (đặc tả 2.3);
        /// việc thực sự hoán ghế nằm ở RespondSideSwap khi đối thủ đồng ý.
        /// </summary>
        public async Task RequestSideSwap(int roomId)
        {
            int userId = CurrentUserIdOrThrow;

            var (ok, error) = await _roomService.RequestSideSwapAsync(roomId, userId);

            if (!ok)
            {
                await Clients.Caller.SendAsync("RoomActionRejected", new { roomId, error });
                return;
            }

            var requester = await _roomService.GetParticipantAsync(roomId, userId);

            await Clients.Group(GetRoomGroupName(roomId)).SendAsync("SideSwapRequested", new
            {
                roomId,
                fromUserId = userId,
                displayName = requester?.User?.DisplayName ?? "Người chơi",
                secondsLeft = RoomService.SideSwapResponseSeconds
            });
        }

        /// <summary>
        /// Trả lời yêu cầu đổi bên. Đồng ý thì hoán ghế và xoá cờ sẵn sàng của cả hai;
        /// từ chối thì giữ nguyên. Cả hai nhánh đều phát lại danh sách người để cả phòng
        /// thấy ghế mới ngay lập tức.
        /// </summary>
        public async Task RespondSideSwap(int roomId, bool accept)
        {
            int userId = CurrentUserIdOrThrow;

            if (accept)
            {
                var (ok, error) = await _roomService.AcceptSideSwapAsync(roomId, userId);

                if (!ok)
                {
                    await Clients.Caller.SendAsync("RoomActionRejected", new { roomId, error });
                    return;
                }
            }
            else if (!await _roomService.RejectSideSwapAsync(roomId, userId))
            {
                await Clients.Caller.SendAsync("RoomActionRejected",
                    new { roomId, error = "Không có yêu cầu đổi bên nào đang chờ trả lời." });
                return;
            }

            await Clients.Group(GetRoomGroupName(roomId)).SendAsync("SideSwapResolved", new
            {
                roomId,
                accepted = accept,
                byUserId = userId
            });

            await NotifyParticipantsChangedAsync(roomId);
            await BroadcastRoomStateAsync(roomId);
        }

        // =========================================
        // Xin hoà
        // =========================================

        /// <summary>
        /// Xin hoà: ghi nhận và mở hạn 30 giây cho đối thủ (đặc tả 3.3). Đồng hồ nền tự
        /// huỷ nếu đối thủ không trả lời, nên client không cần báo hết hạn.
        /// </summary>
        public async Task OfferDraw(int matchId)
        {
            int userId = CurrentUserIdOrThrow;

            var result = await _matchService.OfferDrawAsync(matchId, userId);

            if (!result.Success)
            {
                await Clients.Caller.SendAsync("MoveRejected", new { matchId, error = result.Error });
                return;
            }

            var me = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            await Clients.Group(GetMatchGroupName(matchId)).SendAsync("DrawOffered", new
            {
                matchId,
                fromUserId = userId,
                displayName = me?.DisplayName ?? "Người chơi",
                secondsLeft = MatchService.DrawResponseSeconds
            });
        }

        /// <summary>
        /// Trả lời đề nghị hoà. Đồng ý thì ván kết thúc ngay với lý do AgreedDraw và cả
        /// ván nhận MatchUpdated; từ chối thì chỉ huỷ đề nghị, ván đi tiếp.
        /// </summary>
        public async Task RespondDraw(int matchId, bool accept)
        {
            int userId = CurrentUserIdOrThrow;

            if (!accept)
            {
                if (!await _matchService.RejectDrawAsync(matchId, userId))
                {
                    await Clients.Caller.SendAsync("MoveRejected",
                        new { matchId, error = "Không có đề nghị hoà nào đang chờ trả lời." });
                    return;
                }

                await Clients.Group(GetMatchGroupName(matchId))
                    .SendAsync("DrawOfferResolved", new { matchId, accepted = false, byUserId = userId });
                return;
            }

            var reason = await _matchService.AcceptDrawAsync(matchId, userId);

            if (reason == MatchEndReason.None)
            {
                await Clients.Caller.SendAsync("MoveRejected",
                    new { matchId, error = "Không có đề nghị hoà nào đang chờ trả lời." });
                return;
            }

            await Clients.Group(GetMatchGroupName(matchId))
                .SendAsync("DrawOfferResolved", new { matchId, accepted = true, byUserId = userId });

            await BroadcastMatchUpdatedAsync(matchId);
        }

        // =========================================
        // Tái đấu
        // =========================================

        /// <summary>
        /// Bấm "Tái đấu" sau khi ván xong (đặc tả R14). Chỉ khi CẢ HAI cùng bấm mới tạo
        /// ván mới, và ván mới tự động đổi bên Đỏ <-> Đen.
        /// </summary>
        public async Task RequestRematch(int matchId)
        {
            int userId = CurrentUserIdOrThrow;

            var previous = await _db.Matches.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (previous == null)
            {
                await Clients.Caller.SendAsync("MoveRejected",
                    new { matchId, error = "Không tìm thấy ván đấu." });
                return;
            }

            if (!MatchService.CanRematch(previous))
            {
                await Clients.Caller.SendAsync("MoveRejected",
                    new { matchId, error = "Đã quá thời gian cho phép tái đấu." });
                return;
            }

            var newMatch = await _matchService.RequestRematchAsync(matchId, userId);

            if (newMatch == null)
            {
                await Clients.Group(GetMatchGroupName(matchId))
                    .SendAsync("RematchPending", new { matchId, byUserId = userId });
                return;
            }

            await AnnounceRematchAsync(previous, newMatch);
        }

        /// <summary>
        /// Ván tái đấu đã tạo: đưa cả hai đấu thủ vào group ván mới và báo phòng chuyển
        /// sang phòng thi đấu. Ghế đã đảo nên không báo lại danh sách ghế phòng chờ.
        /// </summary>
        private async Task AnnounceRematchAsync(Match previous, Match next)
        {
            foreach (int userId in new[] { next.RedUserId, next.BlackUserId })
            {
                if (userId == 0)
                {
                    continue;
                }

                foreach (string connectionId in await _db.RoomParticipants
                    .Where(p => p.RoomId == next.RoomId && p.UserId == userId)
                    .Select(p => p.ConnectionId)
                    .ToListAsync())
                {
                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        await Groups.AddToGroupAsync(connectionId, GetMatchGroupName(next.Id));
                    }
                }
            }

            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == next.RoomId);

            if (room != null)
            {
                room.ActiveMatchId = next.Id;
                room.Status = RoomStatus.Playing;
                await _db.SaveChangesAsync();
            }

            await Clients.Group(GetRoomGroupName(next.RoomId)).SendAsync("MatchStarted", new
            {
                matchId = next.Id,
                previousMatchId = previous.Id,
                fen = next.Fen,
                turnSide = next.TurnSide,
                redUserId = next.RedUserId,
                blackUserId = next.BlackUserId,
                isRematch = true
            });
        }

        /// <summary>
        /// Gọi từ server (controller qua IHubContext) khi cả hai vừa bấm sẵn sàng.
        /// Không kiểm tra lại điều kiện vì điều kiện đã do SetReady đảm bảo.
        /// </summary>
        public Task StartMatchWhenBothReady(int roomId)
        {
            return TryStartMatchAsync(roomId);
        }

        /// <summary>
        /// Phát trạng thái phòng chờ cho cả phòng.
        /// Để internal để client không invoke trực tiếp được; controller gọi qua
        /// IHubContext&lt;ChessHub&gt; rồi tới hàm này.
        /// </summary>
        internal async Task BroadcastRoomStateAsync(int roomId)
        {
            var room = await _db.Rooms
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roomId);

            if (room == null)
            {
                return;
            }

            var people = await _db.RoomParticipants
                .AsNoTracking()
                .Where(p => p.RoomId == roomId)
                .ToListAsync();

            await Clients.Group(GetRoomGroupName(roomId)).SendAsync("RoomStateChanged", new
            {
                roomId = room.Id,
                visibility = room.Visibility.ToString(),
                status = room.Status.ToString(),
                activeMatchId = room.ActiveMatchId,
                readyCount = people.Count(p => p.IsReady && p.Role == ParticipantRole.Player),
                playerCount = people.Count(p => p.Role == ParticipantRole.Player),
                spectatorCount = people.Count(p => p.Role == ParticipantRole.Spectator),
                // Dùng chung hằng số với RoomService.MaxRoomCapacity (2 đấu thủ + 5 khán giả).
                capacity = RoomService.MaxRoomCapacity
            });
        }

        // =========================================
        // Mời bạn
        // =========================================

        /// <summary>
        /// Trả về thông tin chia sẻ phòng để giao diện dựng link mời, mã phòng và mã QR
        /// (đặc tả 2.2). Không gửi tín hiệu cho ai cả — chỉ trả về cho người gọi.
        ///
        /// Hàm cũ từng sinh token ngẫu nhiên rồi phát cho CẢ PHÒNG, khiến mọi người
        /// trong phòng đều nhận lời mời. Đặc tả 2.5 đã chuyển việc mời bạn bè sang
        /// SocialHub với đúng 2 người liên quan, nên phần này chỉ còn phát sinh dữ liệu
        /// để dựng mã QR.
        /// </summary>
        public async Task<object?> GetShareInfo(string roomCode)
        {
            int userId = CurrentUserIdOrThrow;

            var room = await _roomService.GetByCodeAsync(roomCode);

            if (room == null)
            {
                return null;
            }

            return new
            {
                roomId = room.Id,
                roomCode = room.Code,
                roomName = room.Name,
                isOwner = room.OwnerId == userId,
                qrUrl = $"/Room/QrCode?roomId={room.Id}&scope=join"
            };
        }

        // =========================================
        // WebRTC signaling
        // =========================================

        /// <summary>
        /// Chuyển tiếp SDP offer/answer trong phòng.
        /// Bản demo cũ thiếu hai hàm này nên bấm "Gọi video" luôn báo Method does not exist.
        /// Chỉ gửi cho thành viên khác trong phòng, không phát toàn cục.
        /// </summary>
        public async Task SendOffer(int roomId, string offerJson)
        {
            await SendWebRtcSignalAsync(roomId, "ReceiveOffer", "offerJson", offerJson);
        }

        public async Task SendAnswer(int roomId, string answerJson)
        {
            await SendWebRtcSignalAsync(roomId, "ReceiveAnswer", "answerJson", answerJson);
        }

        public async Task SendIceCandidate(int roomId, string candidate)
        {
            await SendWebRtcSignalAsync(roomId, "ReceiveIceCandidate", "candidate", candidate);
        }

        private async Task SendWebRtcSignalAsync(int roomId, string eventName, string payloadField, string payload)
        {
            int userId = CurrentUserId;

            if (userId == 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                await Clients.Caller.SendAsync("WebRtcRejected", new { roomId, error = "Tín hiệu rỗng." });
                return;
            }

            var room = await _roomService.GetByIdAsync(roomId);

            if (room == null)
            {
                await Clients.Caller.SendAsync("WebRtcRejected", new { roomId, error = "Phòng không tồn tại." });
                return;
            }

            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            var envelope = new Dictionary<string, object?>
            {
                ["fromUserId"] = userId,
                ["fromDisplayName"] = user?.DisplayName ?? "Người chơi"
            };
            envelope[payloadField] = payload;

            // Bỏ chính người gửi ra khỏi danh sách nhận, không gửi lại cho bản thân.
            await Clients
                .GroupExcept(GetRoomGroupName(roomId), Context.ConnectionId)
                .SendAsync(eventName, envelope);
        }

        // =========================================
        // Tên group
        // =========================================

        /// <summary>Tên group của phòng: "room:{roomId}".</summary>
        public static string GetRoomGroupName(int roomId) => $"room:{roomId}";

        /// <summary>Tên group của ván: "match:{matchId}".</summary>
        public static string GetMatchGroupName(int matchId) => $"match:{matchId}";


        /// <summary>
        /// Số giây còn lại tới một mốc thời gian, không bao giờ âm.
        /// Client đếm ngược theo số này nên server là nguồn chân lý; làm tròn lên để
        /// không bao giờ hiện 0 giây khi thực tế còn dư chút ít.
        /// </summary>
        private static double SecondsUntil(DateTime targetUtc, DateTime nowUtc)
        {
            double seconds = (targetUtc - nowUtc).TotalSeconds;
            return seconds <= 0 ? 0 : seconds;
        }
        // Tên group: phòng "room:{id}", ván "match:{id}".

        private static string? NormalizeSide(string? side)
        {
            if (string.IsNullOrWhiteSpace(side))
                return null;

            string value = side.Trim().ToLower();
            return value == "den" || value == "do" ? value : null;
        }
    }
}
