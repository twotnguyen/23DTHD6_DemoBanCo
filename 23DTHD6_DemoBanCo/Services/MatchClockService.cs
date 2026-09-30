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

                if (match.TimeLimitSeconds > 0)
                {
                    await TickClockAsync(db, matchService, match, now, stoppingToken);
                }
                else
                {
                    await TickInactivityAsync(db, matchService, match, now, stoppingToken);
                }
            }
        }

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
