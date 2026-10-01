namespace _23DTHD6_DemoBanCo.Models
{
    public class Room
    {
        public int Id { get; set; }

        /// <summary>Mã ngắn để người khác nhập tay hoặc dán vào link (ví dụ: K7F2M9).</summary>
        public string Code { get; set; } = "";

        public string Name { get; set; } = "";

        /// <summary>Người tạo phòng, cũng là chủ phòng.</summary>
        public int OwnerId { get; set; }

        public User? Owner { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? LastActivityAt { get; set; }

        /// <summary>Chế độ hiển thị / truy cập của phòng. Mặc định Public (0).</summary>
        public RoomVisibility Visibility { get; set; } = RoomVisibility.Public;

        /// <summary>Phòng đang chờ người chơi hay đang có ván diễn ra. Mặc định Waiting (0).</summary>
        public RoomStatus Status { get; set; } = RoomStatus.Waiting;

        /// <summary>
        /// Ván đấu đang diễn ra của phòng, null khi phòng đang chờ.
        /// Cột tham chiếu thuần (không có navigation) để tránh vòng FK hai chiều với Match.RoomId.
        /// </summary>
        public int? ActiveMatchId { get; set; }

        /// <summary>
        /// Khoá ván của phòng: đặt khi ván bắt đầu, xoá khi ván kết thúc.
        /// Chặn hai lệnh StartMatch chạy song song — chỉ kiểm ActiveMatchId rồi mới
        /// ghi vẫn hở, hai lệnh cùng lúc đều đọc được phòng đang trống.
        /// </summary>
        public bool IsStartingMatch { get; set; }

        /// <summary>
        /// Chế độ trận của phòng. Phòng ghép từ hàng đợi xếp hạng là Ranked, và theo
        /// đặc tả EC-01 thì loại phòng này không hiện trong sảnh và không có khán giả.
        /// Phòng tự tạo mặc định Casual.
        /// </summary>
        public MatchType MatchType { get; set; } = MatchType.Casual;

        /// <summary>
        /// Ngân sách giờ mỗi bên (giây) mà phòng này dùng khi bắt đầu ván.
        /// 0 = không giới hạn. Phòng xếp hạng luôn là 600 (10 phút Rapid).
        /// </summary>
        public int TimeLimitSeconds { get; set; }

        /// <summary>
        /// Mốc hết hạn của lượt chờ đối thủ đồng ý đổi bên, null nếu không có đề nghị nào.
        /// Hết hạn thì đề nghị bị bỏ qua (đặc tả 2.3 cho 30 giây).
        /// </summary>
        public DateTime? SideSwapExpiresAt { get; set; }

        /// <summary>UserId người vừa đề nghị đổi bên, null nếu không có đề nghị nào đang chờ.</summary>
        public int? SideSwapRequestedBy { get; set; }

        /// <summary>Mốc thời gian phòng bị đóng, null khi phòng còn hoạt động.</summary>
        public DateTime? ClosedAt { get; set; }

        public ICollection<RoomParticipant> Participants { get; set; } = new List<RoomParticipant>();
    }

    public class RoomParticipant
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public Room? Room { get; set; }

        public int UserId { get; set; }

        public User? User { get; set; }

        /// <summary>ConnectionId của SignalR để gửi thông báo riêng cho đúng người này.</summary>
        public string ConnectionId { get; set; } = "";

        /// <summary>Phe đánh: "den" hoặc "do", null nghĩa là chưa chọn phe.</summary>
        public string? Side { get; set; }

        /// <summary>Đấu thủ (có ghế cờ) hay khán giả (chỉ xem). Mặc định Player (0).</summary>
        public ParticipantRole Role { get; set; } = ParticipantRole.Player;

        /// <summary>Người chơi đã bấm sẵn sàng, chờ đủ hai bên thì bắt đầu ván.</summary>
        public bool IsReady { get; set; }

        public DateTime JoinedAt { get; set; }
    }
}
