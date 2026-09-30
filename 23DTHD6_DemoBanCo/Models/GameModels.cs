namespace _23DTHD6_DemoBanCo.Models
{
    /// <summary>
    /// Một ván đấu. Server là nguồn chân lý duy nhất của trạng thái này.
    /// </summary>
    public class Match
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public Room? Room { get; set; }

        public MatchType Type { get; set; }

        /// <summary>Thời gian mỗi bên tính bằng giây. 0 = không giới hạn.</summary>
        public int TimeLimitSeconds { get; set; }

        /// <summary>Bên quân đỏ đi trước.</summary>
        public int RedUserId { get; set; }

        public int BlackUserId { get; set; }

        /// <summary>Bên đang được đi. 0 = đỏ, 1 = đen.</summary>
        public int TurnSide { get; set; }

        public string Status { get; set; } = "PLAYING";

        public MatchEndReason EndReason { get; set; } = MatchEndReason.None;

        public int RedTimeLeftSeconds { get; set; }

        public int BlackTimeLeftSeconds { get; set; }

        /// <summary>
        /// Bên thắng: 0 = đỏ, 1 = đen, -1 = hoà. Chỉ có giá trị khi Status == "FINISHED".
        /// Riêng ván chơi với máy thì bên thắng là BlackUserId (0 khi chưa gán người chơi).
        /// </summary>
        public int WinnerSide { get; set; } = -1;

        /// <summary>Điểm Elo tính từ khi tạo tài khoản, dùng để tính K-factor.</summary>
        public int RedRankedGames { get; set; }

        public int BlackRankedGames { get; set; }

        public int RedUndoCount { get; set; }

        public int BlackUndoCount { get; set; }

        /// <summary>Mốc thời gian lần cuối có nước đi, dùng cho quy tắc R17 chống treo ván.</summary>
        public DateTime LastMoveAt { get; set; }

        /// <summary>
        /// Đã gửi cảnh báo "sắp hết giờ vì treo" cho cả ván chưa.
        /// Đồng hồ nền chỉ bắn một lần, có cờ này mới không bắn lại mỗi giây.
        /// </summary>
        public bool InactivityWarned { get; set; }

        /// <summary>Bên đang bị mất kết nối, dùng cho xử lý rage quit 60s.</summary>
        public int? DisconnectedUserId { get; set; }

        public DateTime? DisconnectedAt { get; set; }

        public int AiDifficulty { get; set; } = -1;

        public DateTime StartedAt { get; set; }

        public DateTime? EndedAt { get; set; }

        /// <summary>Thế cờ hiện tại theo chuẩn FEN Xiangqi, server là nguồn chân lý.</summary>
        public string Fen { get; set; } = "";

        public ICollection<MatchMove> Moves { get; set; } = new List<MatchMove>();
    }

    /// <summary>
    /// Một nước đi trong cây nước đi. Con trỏ CurrentMoveId cho phép lùi (undo).
    /// </summary>
    public class MatchMove
    {
        public int Id { get; set; }

        public int MatchId { get; set; }

        public Match? Match { get; set; }

        /// <summary>Id của nước đi cha, null = nước đi gốc.</summary>
        public int? ParentMoveId { get; set; }

        public int MoveNumber { get; set; }

        /// <summary>0 = đỏ, 1 = đen.</summary>
        public int Side { get; set; }

        public string PieceId { get; set; } = "";

        public int FromRow { get; set; }

        public int FromCol { get; set; }

        public int ToRow { get; set; }

        public int ToCol { get; set; }

        /// <summary>Id quân bị ăn, null nếu không ăn.</summary>
        public string? CapturedPieceId { get; set; }

        public string SanMove { get; set; } = "";

        /// <summary>Thế cờ sau khi đi, phục vụ phát hiện lặp thế cờ và xem lại.</summary>
        public string FenAfter { get; set; } = "";

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Kênh chat trong phòng. PLAYERS_PRIVATE chỉ 2 đấu thủ, ROOM_PUBLIC gồm cả khán giả.
    /// </summary>
    public class ChatMessage
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public int UserId { get; set; }

        public User? User { get; set; }

        /// <summary>0 = PLAYERS_PRIVATE, 1 = ROOM_PUBLIC.</summary>
        public int Channel { get; set; }

        public string Body { get; set; } = "";

        /// <summary>Loại nội dung: 0 = text, 1 = sticker.</summary>
        public int Kind { get; set; }

        /// <summary>Mã ngắn của sticker, ví dụ ":tea:". Null nếu là text.</summary>
        public string? Sticker { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public enum FriendshipStatus
    {
        Pending = 0,
        Accepted = 1,
        Rejected = 2
    }

    /// <summary>
    /// Bạn bè. Lưu đúng 1 chiều với UserId &lt; FriendId.
    /// Chỉ khi status = Accepted mới được nhắn tin riêng 1-1.
    /// </summary>
    public class Friendship
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int FriendId { get; set; }

        public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Người bị đuổi khỏi phòng. Chặn tới khi phòng đóng, kể cả khi có link/mã mới.
    /// </summary>
    public class RoomBlock
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public int BlockedUserId { get; set; }

        public int BlockedByUserId { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Cuộc hội thoại riêng 1-1. Chỉ tạo được giữa hai người đã là bạn bè.
    /// </summary>
    public class DirectConversation
    {
        public int Id { get; set; }

        public int UserALowId { get; set; }

        public int UserBHighId { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class DirectMessage
    {
        public int Id { get; set; }

        public int ConversationId { get; set; }

        public int SenderId { get; set; }

        public User? Sender { get; set; }

        public string Body { get; set; } = "";

        public int Kind { get; set; }

        public string? Sticker { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? ReadAt { get; set; }
    }

    /// <summary>
    /// Mã OTP đang hiệu lực. Hạn 3 phút, sai quá 5 lần thì hủy.
    /// Ở bản demo không gửi email thật, mã được hiển thị cho người dùng xem trực tiếp.
    /// </summary>
    public class OtpCode
    {
        public int Id { get; set; }

        /// <summary>Mục đích: REGISTER, CHANGE_USERNAME.</summary>
        public string Purpose { get; set; } = "";

        /// <summary>Email (với REGISTER) hoặc Username hiện tại (với CHANGE_USERNAME).</summary>
        public string Target { get; set; } = "";

        /// <summary>Hash SHA-256 của mã OTP, KHÔNG phải mã gốc.</summary>
        public string Code { get; set; } = "";

        /// <summary>Salt ngẫu nhiên đi kèm hash, mỗi mã một salt riêng.</summary>
        public string CodeSalt { get; set; } = "";

        public DateTime ExpiresAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public int FailedAttempts { get; set; }

        /// <summary>Mã đã bị hủy do sai quá số lần cho phép.</summary>
        public bool IsCancelled { get; set; }

        public bool IsUsed { get; set; }
    }
}