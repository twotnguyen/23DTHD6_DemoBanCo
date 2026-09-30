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
}