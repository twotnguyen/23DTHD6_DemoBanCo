namespace _23DTHD6_DemoBanCo.Models
{
    /// <summary>Vai trò của một người trong phòng.</summary>
    public enum ParticipantRole
    {
        Player = 0,      // Đấu thủ (có ghế cờ)
        Spectator = 1    // Khán giả (chỉ xem)
    }

    /// <summary>Chế độ hiển thị / truy cập của phòng.</summary>
    public enum RoomVisibility
    {
        Public = 0,      // Ai cũng thấy trong sảnh, vào tự do
        CodeOnly = 1,    // Chỉ vào được khi có mã
        Locked = 2       // Khoá, không ai vào được từ bên ngoài
    }

    /// <summary>Vòng đời của phòng chơi, dùng để sảnh lọc và chặn vào phòng.</summary>
    public enum RoomStatus
    {
        Waiting = 0,     // Đang chờ người chơi, ai cũng vào được (nếu không khoá)
        Playing = 1,     // Đang có ván, chỉ khán giả vào được
        Finished = 2     // Ván vừa kết thúc, chờ chủ phòng chơi lại
    }

    /// <summary>Loại trận đấu quyết định luật áp dụng.</summary>
    public enum MatchType
    {
        Casual = 0,     // Tự do: có thể có khán giả, undo, xem lại
        Ranked = 1,     // Xếp hạng: cấm xem, cấm undo, có Elo
        Ai = 2          // Đấu với máy
    }

    /// <summary>Lý do kết thúc ván.</summary>
    public enum MatchEndReason
    {
        None = 0,
        Checkmate = 1,      // Chiếu hết
        Stalemate = 2,      // Bị vây khốn, hết nước đi -> xử thua
        Repetition = 3,     // Lặp thế cờ 3 lần -> hoà
        Resign = 4,         // Đầu hàng
        Timeout = 5,        // Hết giờ
        Disconnect = 6,     // Mất kết nối quá 60s
        Inactivity = 7,     // Treo ván
        AgreedDraw = 8,     // Xin hoà được chấp nhận
        Interrupted = 9     // Server sập -> giữ nguyên Elo
    }

    /// <summary>Cấp độ máy cờ.</summary>
    public enum AiDifficulty
    {
        Easy = 0,       // Depth 2, <=300ms
        Medium = 1,     // Depth 4, <=1000ms
        Hard = 2        // Depth 6, <=3000ms
    }
}