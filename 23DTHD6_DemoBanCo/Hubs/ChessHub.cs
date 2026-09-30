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

        public async Task JoinRoom(int roomId, string? side)
        {
            int userId = CurrentUserId;
            if (userId == 0)
                throw new HubException("Chưa đăng nhập.");

            var room = await _roomService.GetByIdAsync(roomId);
            if (room == null)
                throw new HubException("Phòng không tồn tại.");

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

            // Mất kết nối chỉ ghi nhận cờ đếm ngược 60s, không kết thúc ván tức thời.
            await _roomService.RemoveParticipantByConnectionAsync(Context.ConnectionId);

            foreach (int roomId in affected)
            {
                await NotifyParticipantsChangedAsync(roomId);
                await LeaveMatchGroupsAsync(roomId);
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
        /// Bắt đầu ván trong phòng. Chỉ gọi được bởi lobby sau khi xác nhận cả hai đấu thủ
        /// đã bấm Ready; hàm này không tự kiểm tra Ready để tránh hai nơi cùng ra quyết định.
        ///
        /// timeMode: "blitz" = 300s, "rapid" = 600s, "standard" = 900s, "unlimited" = 0.
        /// </summary>
        public async Task StartMatch(int roomId, int redUserId, int blackUserId, string timeMode)
        {
            if (CurrentUserId == 0)
            {
                throw new HubException("Chưa đăng nhập.");
            }

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

            var match = await _matchService.StartMatchAsync(
                roomId, redUserId, blackUserId, MatchType.Casual, seconds, AiDifficulty.Easy);

            // Hai đấu thủ vào group ván để nhận MatchUpdated, còn khán giả chỉ xem qua
            // group phòng nên không thêm vào đây.
            foreach (int userId in new[] { redUserId, blackUserId })
            {
                var connectionIds = await _db.RoomParticipants
                    .AsNoTracking()
                    .Where(p => p.RoomId == roomId && p.UserId == userId)
                    .Select(p => p.ConnectionId)
                    .ToListAsync();

                foreach (string connectionId in connectionIds)
                {
                    await Groups.AddToGroupAsync(connectionId, GetMatchGroupName(match.Id));
                }
            }

            // Ghi ván đang chạy vào phòng để phòng chờ biết có ván để vào xem.
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);

            if (room != null)
            {
                room.ActiveMatchId = match.Id;
                room.Status = RoomStatus.Playing;
                await _db.SaveChangesAsync();
            }

            await Clients.Group(GetRoomGroupName(roomId)).SendAsync("MatchStarted", new
            {
                matchId = match.Id,
                fen = match.Fen,
                turnSide = match.TurnSide
            });
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

            await Clients.Group(GetMatchGroupName(matchId)).SendAsync("MatchUpdated", new
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
                undoLeft = yourSide == 0
                    ? MatchService.MaxUndoPerSide - match.RedUndoCount
                    : yourSide == 1
                        ? MatchService.MaxUndoPerSide - match.BlackUndoCount
                        : 0,
                youWon = match.Status == MatchService.StatusFinished
                    ? yourSide >= 0 ? (int)match.WinnerSide == yourSide : (bool?)null
                    : (bool?)null
            });

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

        /// <summary>Sinh link mời có token để người nhận vào đúng phòng này.</summary>
        public async Task<string> RequestJoinInvite(string roomCode)
        {
            int userId = CurrentUserIdOrThrow;

            var room = await _roomService.GetByCodeAsync(roomCode);

            if (room == null)
            {
                return "";
            }

            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            string token = Guid.NewGuid().ToString("N");
            await Clients.Group(GetRoomGroupName(room.Id)).SendAsync("JoinInviteReceived", new
            {
                roomId = room.Id,
                roomName = room.Name,
                fromUserId = userId,
                displayName = user?.DisplayName ?? "Người chơi",
                token
            });

            return $"{room.Code}:{token}";
        }

        public async Task<bool> ResponseJoinInvite(string fromUserId, bool accept)
        {
            int userId = CurrentUserIdOrThrow;

            if (!accept)
            {
                await Clients.User(fromUserId).SendAsync("JoinInviteResolved", new
                {
                    fromUserId = userId,
                    accepted = false
                });

                return false;
            }

            await Clients.User(fromUserId).SendAsync("JoinInviteResolved", new
            {
                fromUserId = userId,
                accepted = true
            });

            return true;
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
