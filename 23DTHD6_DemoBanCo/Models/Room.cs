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

        public DateTime JoinedAt { get; set; }
    }
}