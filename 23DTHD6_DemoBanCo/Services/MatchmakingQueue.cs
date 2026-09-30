using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Hàng đợi ghép trận, toàn bộ nằm trong bộ nhớ (đặc tả 2.0 và 7.1).
    ///
    /// Lớp này cố tình KHÔNG dùng DbContext. Singleton không thể phụ thuộc
    /// service scoped (sẽ ném lỗi khi app khởi động), mà hàng đợi thì bắt buộc phải
    /// là singleton, nếu không mỗi lần tạo hub sẽ có một hàng đợi riêng và không
    /// bao giờ ghép được ai. Vì vậy tách trạng thái (hàng đợi) ra khỏi phần đọc
    /// CSDL (MatchmakingService).
    ///
    /// Mất hàng đợi khi restart server: chấp nhận được với bản demo.
    /// </summary>
    public sealed class MatchmakingQueue
    {
        /// <summary>Loại hàng đợi. Hai loại tách biệt, không ghép chéo.</summary>
        public enum QueueKind
        {
            Casual = 0,
            Ranked = 1
        }

        /// <summary>Một người đang chờ trong hàng đợi.</summary>
        public sealed record QueueEntry(
            int UserId,
            string ConnectionId,
            QueueKind Kind,
            int Elo,
            DateTime JoinedAt,
            int SearchRange,
            int WindowStartSec);

        /// <summary>Biên chênh lệch Elo ban đầu của hàng đợi xếp hạng (đặc tả 7.1).</summary>
        public const int RankedInitialRange = 100;

        /// <summary>Mỗi 10 giây chờ, biên chênh lệch mở rộng thêm 50 điểm.</summary>
        public const int RangeStep = 50;

        /// <summary>Cứ mỗi 10 giây thì mở rộng một lần.</summary>
        public const int RangeStepSeconds = 10;

        /// <summary>Biên chênh lệch tối đa, không mở rộng vô hạn.</summary>
        public const int MaxSearchRange = 300;

        private readonly Dictionary<QueueKind, Dictionary<int, QueueEntry>> _queues = new()
        {
            [QueueKind.Casual] = new Dictionary<int, QueueEntry>(),
            [QueueKind.Ranked] = new Dictionary<int, QueueEntry>()
        };

        /// <summary>
        /// Chặn ghép trùng khi hai người bấm gần như đồng thời. Mọi thao tác đọc và
        /// ghi hàng đợi đều nằm trong khoá này.
        /// </summary>
        private readonly object _sync = new();

        /// <summary>
        /// Đưa người chơi vào hàng đợi. Bấm lần hai cùng loại sẽ cập nhật bản ghi cũ,
        /// bấm khác loại thì chuyển sang hàng đợi mới chứ không nhân bản.
        /// Việc kiểm khách / xếp hạng do MatchmakingService lo trước khi gọi hàm này.
        /// </summary>
        public void Enqueue(int userId, string connectionId, QueueKind kind, int elo)
        {
            lock (_sync)
            {
                // Rút khỏi hàng đợi cũ trước, để một người không nằm ở hai chỗ.
                foreach (var queue in _queues.Values)
                    queue.Remove(userId);

                _queues[kind][userId] = new QueueEntry(
                    userId, connectionId, kind, elo, DateTime.UtcNow, RankedInitialRange, 0);
            }
        }

        /// <summary>Rút người chơi khỏi hàng đợi. Trả về true nếu vốn đang chờ.</summary>
        public bool TryDequeue(int userId, out QueueEntry? entry)
        {
            lock (_sync)
            {
                entry = null;

                foreach (var queue in _queues.Values)
                {
                    if (!queue.TryGetValue(userId, out QueueEntry? found))
                        continue;

                    queue.Remove(userId);
                    entry = found;
                    return true;
                }

                return false;
            }
        }

        /// <summary>Rút theo ConnectionId, dùng khi kết nối SignalR bị đóng.</summary>
        public bool TryDequeueByConnection(string connectionId, out QueueEntry? entry)
        {
            lock (_sync)
            {
                entry = null;

                foreach (var queue in _queues.Values)
                {
                    var found = queue.Values.FirstOrDefault(e => e.ConnectionId == connectionId);
                    if (found == null)
                        continue;

                    queue.Remove(found.UserId);
                    entry = found;
                    return true;
                }

                return false;
            }
        }

        /// <summary>Số người đang chờ trong một hàng đợi.</summary>
        public int QueueSize(QueueKind kind)
        {
            lock (_sync)
                return _queues[kind].Count;
        }

        /// <summary>Lấy bản ghi của một người nếu họ đang chờ trong đúng hàng đợi đó.</summary>
        public bool TryGetEntry(int userId, QueueKind kind, out QueueEntry? entry)
        {
            lock (_sync)
                return _queues[kind].TryGetValue(userId, out entry);
        }

        /// <summary>
        /// Ghép 2 người đang chờ hàng đời tự do, không quan tâm Elo. Người vào trước
        /// đứng đầu danh sách chờ và sẽ ngồi ghế đỏ.
        /// </summary>
        public (QueueEntry a, QueueEntry b)? TryMatchCasual()
        {
            lock (_sync)
            {
                var queue = _queues[QueueKind.Casual];
                if (queue.Count < 2)
                    return null;

                var ordered = queue.Values
                    .OrderBy(e => e.JoinedAt)
                    .ThenBy(e => e.UserId)
                    .Take(2)
                    .ToList();

                queue.Remove(ordered[0].UserId);
                queue.Remove(ordered[1].UserId);

                return (ordered[0], ordered[1]);
            }
        }

        /// <summary>
        /// Ghép 2 người chờ hàng đời xếp hạng khi chênh lệch Elo nằm trong biên của
        /// cả hai. Lấy biên nhỏ hơn: người mới vào có biên hẹp hơn thì không được
        /// ghép chỉ vì một bên chấp nhận.
        /// </summary>
        public (QueueEntry a, QueueEntry b)? TryMatchRanked()
        {
            lock (_sync)
            {
                var queue = _queues[QueueKind.Ranked];
                if (queue.Count < 2)
                    return null;

                // Cập nhật biên theo số giây đã chờ trước khi so từng cặp.
                var entries = queue.Values.ToList();
                foreach (var entry in entries)
                {
                    int seconds = (int)(DateTime.UtcNow - entry.JoinedAt).TotalSeconds;
                    int range = ExpandRange(entry, seconds);
                    queue[entry.UserId] = entry with
                    {
                        SearchRange = range,
                        WindowStartSec = seconds / RangeStepSeconds
                    };
                }

                var ordered = entries
                    .OrderBy(e => e.JoinedAt)
                    .ToList();

                for (int i = 0; i < ordered.Count; i++)
                {
                    for (int j = i + 1; j < ordered.Count; j++)
                    {
                        int allowed = Math.Min(ordered[i].SearchRange, ordered[j].SearchRange);
                        int diff = Math.Abs(ordered[i].Elo - ordered[j].Elo);

                        if (diff > allowed)
                            continue;

                        queue.Remove(ordered[i].UserId);
                        queue.Remove(ordered[j].UserId);
                        return (ordered[i], ordered[j]);
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Mở rộng biên chênh lệch Elo theo số giây đã chờ (đặc tả 7.1):
        /// cứ mỗi 10 giây thì biên rộng thêm 50 điểm, tức 100 -> 150 -> 200 -> ...
        /// và dừng ở 300. Nếu không chặn trên thì người chờ lâu sẽ bị ghép với
        /// người lệch Elo quá xa, nên trần là cần thiết chứ không phải tùy ý.
        /// </summary>
        public int ExpandRange(QueueEntry entry, int secondsSinceJoin)
        {
            int steps = secondsSinceJoin / RangeStepSeconds;
            int range = RankedInitialRange + steps * RangeStep;
            return Math.Min(range, MaxSearchRange);
        }
    }
}
