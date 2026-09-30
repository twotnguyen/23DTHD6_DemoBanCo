namespace _23DTHD6_DemoBanCo.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Username { get; set; } = "";

        public string DisplayName { get; set; } = "";

        /// <summary>Hash PBKDF2 của mật khẩu, không bao giờ lưu mật khẩu gốc.</summary>
        public string PasswordHash { get; set; } = "";

        public DateTime CreatedAt { get; set; }
    }
}