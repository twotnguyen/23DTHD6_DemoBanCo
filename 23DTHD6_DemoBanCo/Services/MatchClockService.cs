using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Hubs;
using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Đồng hồ ván đấu chạy nền, quét mỗi giây một lần.
    ///
    /// Hai việc:
    ///   1. Ván có đồng hồ: trừ thời gian của bên đang đi theo khoảng đã trôi, về 0 thì xử thua.
    ///   2. Ván không giới hạn: quá 180s không có nước đi thì bắt đầu cảnh báo, quá 210s thì xử thua.
    ///
    /// Mỗi vòng tạo scope mới thay vì giữ scope lâu dài, vì DbContext không dùng được
    /// song song và scope giữ lâu sẽ giữ cả đối tượng trong ChangeTracker suốt vòng đời app.
    /// </summary>
    public sealed class MatchClockService : BackgroundService
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<ChessHub> _hubContext;
        private readonly ILogger<MatchClockService> _logger;

        public MatchClockService(
            IServiceScopeFactory scopeFactory,
            IHubContext<ChessHub> hubContext,
            ILogger<MatchClockService> logger)
        {
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TickInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Ứng dụng đang tắt, dừng vòng quét.
                    return;
                }
                catch (Exception ex)
                {
                    // Một ván lỗi không được làm chết cả service, các ván khác vẫn phải chạy.
                    _logger.LogError(ex, "Lỗi khi quét đồng hồ ván đấu.");
                }
            }
        }

        private async Task TickAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var matchService = scope.ServiceProvider.GetRequiredService<MatchService>();

            DateTime now = DateTime.UtcNow;

            var playing = await db.Matches
                .Where(m => m.Status == MatchService.StatusPlaying)
                .ToListAsync(stoppingToken);

            foreach (var match in playing)
            {
                stoppingToken.ThrowIfCancellationRequested();

                if (match.DisconnectedUserId != null)
                {
                    // Đang mất kết nối thì để MatchService tự tính 60s, không trừ đồng hồ.
                    await matchService.HandleDisconnectTimeoutAsync(match.Id);
                    continue;
                }

                // Ván còn đếm ngược 3-2-1: chưa tính giờ, chưa tính treo ván, và chưa
                // gửi nước hợp lệ cho ai — nếu không bên đỏ mất 3 giây và luật R17 có
                // thể bắn ngay khi ván mở.
                //
                // Vòng đếm cuối cùng (mốc đã qua) PHẢI phát MatchUpdated: nếu không,
                // sự kiện MatchStarted duy nhất đã gửi lúc tạo ván vẫn mang
                // countdownSecondsLeft > 0 và danh sách nước hợp lệ rỗng, nên cả hai
                // bên sẽ bị khoá bàn vĩnh viễn dù ván đã mở.
                if (MatchService.IsCountingDown(match))
                {
                    await NotifyGroupAsync(match.Id, "MatchTick", new
                    {
                        matchId = match.Id,
                        countingDown = true,
                        countdownSecondsLeft = (int)Math.Max(
                            0, (match.CountdownEndsAt!.Value - now).TotalSeconds)
                    }, stoppingToken);

                    continue;
                }

                // Chỉ ván vừa kết thúc đếm mới cần phát lại toàn bộ trạng thái; các
                // vòng sau không phát lại để khỏi dội dữ liệu mỗi giây.
                if (match.CountdownEndsAt.HasValue
                    && (now - match.CountdownEndsAt.Value).TotalSeconds < 5)
                {
                    await _hubContext.Clients
                        .Group(ChessHub.GetMatchGroupName(match.Id))
                        .SendAsync("MatchCountdownOver", new { matchId = match.Id }, stoppingToken);
                }

                // Đề nghị hoà và đề nghị đi lại đều có hạn 30 giây. Client có thể đóng tab
                // nên không gửi được câu trả lời; đồng hồ nền là nơi duy nhất dọn được.
                if (await matchService.ExpireDrawOfferAsync(match.Id))
                {
                    await NotifyGroupAsync(match.Id, "DrawOfferResolved", new
                    {
                        matchId = match.Id,
                        accepted = false,
                        reason = "Hết thời hạn 30 giây, đề nghị hoà đã bị huỷ."
                    }, stoppingToken);

                    continue;
                }

                if (await matchService.ExpireUndoRequestAsync(match.Id))
                {
                    await NotifyGroupAsync(match.Id, "UndoResolved", new
                    {
                        matchId = match.Id,
                        accepted = false,
                        expired = true
                    }, stoppingToken);

                    continue;
                }

                if (match.TimeLimitSeconds > 0)
                {
                    await TickClockAsync(db, matchService, match, now, stoppingToken);

                    // Phát đồng hồ mỗi giây để client không phải tự đoán: ván đang trừ
                    // giờ thì server là nguồn chân lý, client chỉ hiển thị con số nhận về.
                    // Còn bên mất kết nối thì báo số giây chờ còn lại của đồng hồ ân hạn 60s.
                    await NotifyGroupAsync(match.Id, "MatchTick", new
                    {
                        matchId = match.Id,
                        redTimeLeft = match.RedTimeLeftSeconds,
                        blackTimeLeft = match.BlackTimeLeftSeconds,
                        turnSide = match.TurnSide,
                        disconnectedUserId = match.DisconnectedUserId,
                        disconnectSecondsLeft = match.DisconnectedAt == null
                            ? 0
                            : (int)Math.Max(
                                0,
                                MatchService.DisconnectGraceSeconds
                                - (now - match.DisconnectedAt.Value).TotalSeconds),
                        serverTimeUtc = now.ToString("O")
                    }, stoppingToken);
                }
                else
                {
                    await TickInactivityAsync(db, matchService, match, now, stoppingToken);
                }
            }
        }

        /// <summary>
        /// Phát một sự kiện cho đúng group của ván. Bọc lại vì nhiều chỗ gọi đều cần
        /// cùng một cách viết tên group, tránh lệch chuỗi "match:{id}".
        /// </summary>
        private Task NotifyGroupAsync(int matchId, string eventName, object payload, CancellationToken ct)
            => _hubContext.Clients
                .Group(ChessHub.GetMatchGroupName(matchId))
                .SendAsync(eventName, payload, ct);

        /// <summary>
        /// Trừ thời gian của bên đang đi theo số giây đã trôi kể từ mốc thời gian cuối.
        /// Trừ xong đẩy mốc về hiện tại, nếu không ván sau sẽ trừ lại phần đã trôi.
        /// </summary>
        private static async Task TickClockAsync(
            AppDbContext db,
            MatchService matchService,
            Match match,
            DateTime now,
            CancellationToken stoppingToken)
        {
            int elapsed = (int)Math.Floor((now - match.LastMoveAt).TotalSeconds);

            if (elapsed <= 0)
            {
                return;
            }

            if (match.TurnSide == 0)
            {
                match.RedTimeLeftSeconds = Math.Max(0, match.RedTimeLeftSeconds - elapsed);
            }
            else
            {
                match.BlackTimeLeftSeconds = Math.Max(0, match.BlackTimeLeftSeconds - elapsed);
            }

            match.LastMoveAt = now;

            await db.SaveChangesAsync(stoppingToken);

            if (match.RedTimeLeftSeconds <= 0 || match.BlackTimeLeftSeconds <= 0)
            {
                await matchService.HandleTimeoutAsync(match.Id);
            }
        }

        /// <summary>
        /// Ván không giới hạn thời gian.
        ///   Từ InactivityPromptSeconds trở đi: bắn cảnh báo một lần cho cả ván.
        ///   Từ InactivityPromptSeconds + InactivityLoseSeconds trở đi: bên đến lượt thua.
        /// </summary>
        private async Task TickInactivityAsync(
            AppDbContext db,
            MatchService matchService,
            Match match,
            DateTime now,
            CancellationToken stoppingToken)
        {
            double idleSeconds = (now - match.LastMoveAt).TotalSeconds;

            if (idleSeconds < MatchService.InactivityPromptSeconds)
            {
                return;
            }

            // Chỉ bắn một lần cho mỗi lần treo, nếu không sẽ spam mỗi giây.
            if (!match.InactivityWarned)
            {
                match.InactivityWarned = true;
                await db.SaveChangesAsync(stoppingToken);

                await _hubContext.Clients
                    .Group(ChessHub.GetMatchGroupName(match.Id))
                    .SendAsync("InactivityWarning", new
                    {
                        matchId = match.Id,
                        secondsLeft = (int)Math.Max(
                            0,
                            MatchService.InactivityPromptSeconds
                            + MatchService.InactivityLoseSeconds
                            - idleSeconds),
                        turnSide = match.TurnSide
                    },
                    stoppingToken);
            }

            if (idleSeconds < MatchService.InactivityPromptSeconds
                + MatchService.InactivityLoseSeconds)
            {
                return;
            }

            await matchService.HandleInactivityAsync(match.Id);
        }
    }
}
