namespace _23DTHD6_DemoBanCo.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Username { get; set; } = "";

        public string DisplayName { get; set; } = "";

        /// <summary>Hash PBKDF2 của mật khẩu, không bao giờ lưu mật khẩu gốc.</summary>
        public string PasswordHash { get; set; } = "";

        /// <summary>Email đăng nhập. Khách (guest) thì để null. Không cho phép đổi sau khi tạo.</summary>
        public string? Email { get; set; }

        /// <summary>GoogleEmail do Google OAuth xác nhận, dùng để ghép tài khoản.</summary>
        public string? GoogleEmail { get; set; }

        /// <summary>True nếu tài khoản tạo bằng chế độ khách, không có mật khẩu thật.</summary>
        public bool IsGuest { get; set; }

        /// <summary>True sau khi đã xác thực OTP gửi về <see cref="Email"/>.</summary>
        public bool EmailVerified { get; set; }

        /// <summary>Điểm Elo hiện tại. Người mới chơi bắt đầu ở 1200.</summary>
        public int Elo { get; set; } = 1200;

        /// <summary>Số ván đã đấu xếp hạng. Dùng để tính hệ số K của EloCalculator.</summary>
        public int RankedGames { get; set; } = 0;

        public int Wins { get; set; } = 0;

        public int Losses { get; set; } = 0;

        public int Draws { get; set; } = 0;
        public DateTime CreatedAt { get; set; }
    }
}