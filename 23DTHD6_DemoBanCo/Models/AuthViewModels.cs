using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace _23DTHD6_DemoBanCo.Models
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
        [StringLength(30, MinimumLength = 3, ErrorMessage = "Tên đăng nhập phải từ 3 đến 30 ký tự.")]
        [RegularExpression("^[a-zA-Z0-9_]+$", ErrorMessage = "Tên đăng nhập chỉ gồm chữ, số và dấu gạch dưới.")]
        [Display(Name = "Tên đăng nhập")]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập tên hiển thị.")]
        [StringLength(40, ErrorMessage = "Tên hiển thị không được vượt quá 40 ký tự.")]
        [Display(Name = "Tên hiển thị")]
        public string DisplayName { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
        [Display(Name = "Xác nhận mật khẩu")]
        public string ConfirmPassword { get; set; } = "";

        public string? ErrorMessage { get; set; }
    }

    public class LoginViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
        [Display(Name = "Tên đăng nhập")]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu")]
        public string Password { get; set; } = "";

        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Bước 1 của đăng ký: chọn tên đăng nhập và mật khẩu.
    /// Ở bước này chưa có email nên chưa thể gửi mã OTP.
    /// </summary>
    public class RegisterStep1ViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
        [RegularExpression("^[a-zA-Z0-9_]{3,20}$", ErrorMessage = "Tên đăng nhập phải từ 3 đến 20 ký tự, chỉ gồm chữ, số và dấu gạch dưới.")]
        [Display(Name = "Tên đăng nhập")]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
        [Display(Name = "Xác nhận mật khẩu")]
        public string ConfirmPassword { get; set; } = "";

        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Bước 2 của đăng ký: nhập email. Bấm "Xác nhận Email" mới chuyển sang bước 3.
    /// Ở bước này vẫn chưa tạo tài khoản, email chỉ được lưu vào tiến trình đăng ký.
    /// </summary>
    public class RegisterStep2ViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập email.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [StringLength(254, ErrorMessage = "Email không được vượt quá 254 ký tự.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = "";

        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Bước 3 của đăng ký: nhập mã OTP 6 chữ số vừa được sinh.
    /// Demo không có SMTP nên DevCodeHint hiển thị thẳng mã cho người dùng.
    /// </summary>
    public class OtpVerifyViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập mã xác thực.")]
        [RegularExpression("^[0-9]{6}$", ErrorMessage = "Mã xác thực gồm đúng 6 chữ số.")]
        [Display(Name = "Mã xác thực")]
        public string Code { get; set; } = "";

        /// <summary>Mã demo hiển thị thẳng cho người dùng. Rỗng nếu không ở chế độ demo.</summary>
        [Display(Name = "Mã demo")]
        public string? DevCodeHint { get; set; }

        public string? ErrorMessage { get; set; }

        /// <summary>Thời điểm sớm nhất được gửi lại mã. Chỉ hiện nút gửi lại sau mốc này.</summary>
        public DateTimeOffset ResendAvailableAt { get; set; }
    }

    /// <summary>Đăng nhập bằng chế độ Khách: chỉ cần một tên hiển thị.</summary>
    public class GuestLoginViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên hiển thị.")]
        [StringLength(20, MinimumLength = 2, ErrorMessage = "Tên hiển thị phải từ 2 đến 20 ký tự.")]
        [Display(Name = "Tên hiển thị")]
        public string DisplayName { get; set; } = "";

        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Hồ sơ: sửa tên hiển thị tự do, không cần OTP. Email chỉ hiển thị, không sửa được.
    /// </summary>
    public class ProfileViewModel
    {
        /// <summary>Tên đăng nhập đã xác minh, không sửa ở đây.</summary>
        [Display(Name = "Tên đăng nhập")]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập tên hiển thị.")]
        [StringLength(30, MinimumLength = 2, ErrorMessage = "Tên hiển thị phải từ 2 đến 30 ký tự.")]
        [Display(Name = "Tên hiển thị")]
        public string DisplayName { get; set; } = "";

        /// <summary>Email chỉ đọc, không có ô nhập trong form.</summary>
        [Display(Name = "Email")]
        public string? Email { get; set; }

        public bool IsGuest { get; set; }

        public bool EmailVerified { get; set; }

        public int Elo { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public int Draws { get; set; }

        public string? ErrorMessage { get; set; }

        public string? SuccessMessage { get; set; }
    }

    /// <summary>
    /// Đổi tên đăng nhập qua OTP 4 bước.
    /// Unlocked = true chỉ khi OTP đã đúng, lúc đó ô nhập tên mới mới cho sửa.
    /// </summary>
    public class ChangeUsernameViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập mới.")]
        [RegularExpression("^[a-zA-Z0-9_]{3,20}$", ErrorMessage = "Tên đăng nhập phải từ 3 đến 20 ký tự, chỉ gồm chữ, số và dấu gạch dưới.")]
        [Display(Name = "Tên đăng nhập mới")]
        public string NewUsername { get; set; } = "";

        public string? ErrorMessage { get; set; }

        /// <summary>True sau khi OTP đúng: đã mở khoá được ô nhập tên mới.</summary>
        public bool Unlocked { get; set; }
    }

    /// <summary>Thông tin lấy từ Google OAuth khi người dùng đăng nhập bằng Google.</summary>
    public class GoogleAccountViewModel
    {
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        public string Email { get; set; } = "";

        [Display(Name = "Tên hiển thị")]
        public string DisplayName { get; set; } = "";
    }

    /// <summary>
    /// Tiến trình đăng ký 3 bước, lưu trong Session dưới dạng JSON.
    ///
    /// KHÔNG lưu PasswordHash ở đây: Session đi qua cookie phiên, để hash mật khẩu vào đó
    /// là rủi ro không cần thiết. Hash đã nằm ở bảng Users, tiến trình chỉ cần Id tài khoản tạm.
    /// </summary>
    public class RegisterProgress
    {
        /// <summary>Id tài khoản tạm đã tạo ở bước 1, dùng cho bước 3.</summary>
        public int PendingUserId { get; set; }

        public string Username { get; set; } = "";

        public string? Email { get; set; }

        /// <summary>
        /// Mã OTP demo sinh ở bước 3, hiển thị thẳng cho người dùng vì dự án không có SMTP.
        /// Chỉ nằm trong Session của chính người đang đăng ký, không ghi log, không trả API khác.
        /// </summary>
        public string? DevCodeHint { get; set; }

        /// <summary>Mô tả bước hiện tại: 1, 2 hoặc 3.</summary>
        public int Step { get; set; } = 1;

        /// <summary>Mốc hết hạn của tiến trình, 10 phút kể từ lần lưu gần nhất.</summary>
        public DateTimeOffset ExpiresAt { get; set; }

        [JsonIgnore]
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    }

    /// <summary>
    /// Tiến trình đổi tên đăng nhập, lưu trong Session dưới dạng JSON. Cùng kiểu hạn 10 phút với đăng ký.
    /// </summary>
    public class ChangeUsernameProgress
    {
        public string Email { get; set; } = "";

        public DateTimeOffset ExpiresAt { get; set; }

        [JsonIgnore]
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    }
}