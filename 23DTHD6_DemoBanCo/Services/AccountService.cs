using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Nghiệp vụ tài khoản: kiểm tra trùng lặp, tạo tài khoản, sửa tên hiển thị, đổi username, xác nhận OTP.
    ///
    /// QUY ƯỚC BẢO MẬT:
    ///  - Username luôn Trim() trước khi so khớp trùng lặp, và so khớp PHÂN BIỆT HOA THƯỜNG
    ///    (SQLite so chuỗi theo kiểu nhị phân, khớp với index UNIQUE đã cấu hình trên bảng Users).
    ///  - Email KHÔNG BAO GIỜ cho sửa. Không có hàm nào trong service này gán lại Email của
    ///    một tài khoản đã tồn tại ngoài CompleteRegistrationAsync lúc đăng ký.
    ///  - Email trùng thì trả về thông báo CHUNG, không tiết lộ email đó đã có tài khoản hay chưa.
    /// </summary>
    public sealed class AccountService
    {
        /// <summary>Mục đích OTP khi đăng ký tài khoản mới.</summary>
        public const string RegisterPurpose = "REGISTER";

        /// <summary>Mục đích OTP khi đổi tên đăng nhập.</summary>
        public const string ChangeUsernamePurpose = "CHANGE_USERNAME";

        /// <summary>Thông báo chung cho mọi trường hợp email không dùng được. Cố tình không nói lý do.</summary>
        private const string EmailGenericError = "Email không hợp lệ hoặc đã được dùng. Vui lòng thử email khác.";

        private static readonly Regex UsernamePattern =
            new("^[a-zA-Z0-9_]{3,20}$", RegexOptions.Compiled);

        private readonly AppDbContext _db;
        private readonly OtpService _otp;

        /// <summary>
        /// Cần cả OtpService vì bước hoàn tất đăng ký phải xác thực mã bằng đúng logic
        /// hạn 3 phút / sai 5 lần hủy của OtpService, không viết lại ở đây.
        /// </summary>
        public AccountService(AppDbContext db, OtpService otp)
        {
            _db = db;
            _otp = otp;
        }

        /// <summary>
        /// Tên đăng nhập đã có người dùng chưa. So khớp phân biệt hoa thường.
        /// excludeUserId dùng khi đổi username: bỏ qua chính tài khoản đang đổi.
        /// </summary>
        public async Task<bool> IsUsernameTakenAsync(string username, int? excludeUserId = null)
        {
            string value = (username ?? "").Trim();
            if (value.Length == 0)
                return false;

            var query = _db.Users.Where(u => u.Username == value);

            if (excludeUserId.HasValue)
                query = query.Where(u => u.Id != excludeUserId.Value);

            return await query.AnyAsync();
        }

        /// <summary>Email đã có tài khoản chưa. So khớp không phân biệt hoa thường (chuẩn email).</summary>
        public async Task<bool> IsEmailTakenAsync(string email)
        {
            string value = NormalizeEmail(email);
            if (value.Length == 0)
                return false;

            return await _db.Users.AnyAsync(u => u.Email == value);
        }

        /// <summary>
        /// Tạo tài khoản mới. Chưa xác minh email (EmailVerified = false);
        /// việc bật cờ đó do CompleteRegistrationAsync đảm nhiệm sau khi OTP đúng.
        /// Tài khoản khách (isGuest = true) không lưu hash mật khẩu, vì không có mật khẩu nào để đăng nhập.
        /// </summary>
        public async Task<(User user, string? error)> CreateUserAsync(
            string username,
            string displayName,
            string? email,
            string? password,
            bool isGuest)
        {
            string name = (username ?? "").Trim();

            if (!UsernamePattern.IsMatch(name))
                return (null!, "Tên đăng nhập phải từ 3 đến 20 ký tự, chỉ gồm chữ, số và dấu gạch dưới.");

            if (await IsUsernameTakenAsync(name))
                return (null!, "Tên đăng nhập đã tồn tại. Vui lòng chọn tên khác.");

            string? mail = string.IsNullOrWhiteSpace(email) ? null : NormalizeEmail(email);

            if (mail != null && await IsEmailTakenAsync(mail))
                return (null!, EmailGenericError);

            var user = new User
            {
                Username = name,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim(),
                Email = mail,
                EmailVerified = false,
                IsGuest = isGuest,
                Elo = EloCalculator.StartingElo,
                CreatedAt = DateTime.UtcNow
            };

            // Khách không có mật khẩu nên để PasswordHash rỗng: không thể đăng nhập bằng tài khoản này
            if (!isGuest && !string.IsNullOrEmpty(password))
                user.PasswordHash = PasswordHasher.Hash(password);

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return (user, null);
        }

        /// <summary>
        /// Sửa tên hiển thị, tự do, không cần OTP. 2-30 ký tự, chấp nhận tiếng Việt có dấu.
        /// Trả về user đã cập nhật, hoặc null nếu dữ liệu không hợp lệ / không tìm thấy tài khoản.
        /// </summary>
        public async Task<User?> UpdateDisplayNameAsync(int userId, string displayName)
        {
            string value = (displayName ?? "").Trim();

            if (value.Length < 2 || value.Length > 30)
                return null;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return null;

            user.DisplayName = value;
            await _db.SaveChangesAsync();

            return user;
        }

        /// <summary>
        /// Đổi tên đăng nhập sau khi đã xác minh OTP. Giữ nguyên user.Id nên dữ liệu cũ không đổi.
        /// Email không đụng tới ở đây.
        /// </summary>
        public async Task<(bool ok, string? error)> ChangeUsernameAsync(int userId, string newUsername)
        {
            string value = (newUsername ?? "").Trim();

            if (!UsernamePattern.IsMatch(value))
                return (false, "Tên đăng nhập phải từ 3 đến 20 ký tự, chỉ gồm chữ, số và dấu gạch dưới.");

            var current = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (current is null)
                return (false, "Không tìm thấy tài khoản của bạn.");

            if (string.Equals(current.Username, value, StringComparison.Ordinal))
                return (false, "Tên mới phải khác tên đăng nhập hiện tại.");

            if (await IsUsernameTakenAsync(value, userId))
                return (false, "Tên đăng nhập đã tồn tại. Vui lòng chọn tên khác.");

            current.Username = value;
            await _db.SaveChangesAsync();

            return (true, null);
        }

        /// <summary>Bước cuối của luồng đăng ký: xác thực mã OTP rồi mới lưu Email và bật EmailVerified.</summary>
        /// <param name="codeRejected">
        /// True nghĩa là mã đã bị HỦY vĩnh viễn (hết hạn, hoặc sai quá số lần cho phép).
        /// Khi đó tài khoản đang chờ cũng bị xoá và tiến trình phải quay lại bước 1, vì mã cũ không dùng được nữa.
        /// False nghĩa là chỉ gõ sai mã: GIỮ tài khoản đang chờ và giữ tiến trình để người dùng thử tiếp.
        /// </param>
        public async Task<(bool ok, string? error, bool codeRejected)> CompleteRegistrationAsync(int userId, string email, string code)
        {
            string mail = NormalizeEmail(email);

            var otp = await _otp.VerifyAsync(RegisterPurpose, mail, code);

            if (!otp.Success)
            {
                // Chỉ xoá tài khoản tạm khi mã đã hỏng vĩnh viễn; gõ sai thì giữ lại cho lượt thử sau
                if (IsCodeRejected(otp.Error))
                {
                    await RemovePendingUserAsync(userId);
                    return (false, otp.Error, true);
                }

                return (false, otp.Error, false);
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return (false, "Không tìm thấy tài khoản đang đăng ký.", false);

            // Chỉ tới đây Email mới được gán lần đầu và không bao giờ gán lại được nữa
            user.Email = mail;
            user.EmailVerified = true;
            await _db.SaveChangesAsync();

            return (true, null, false);
        }

        /// <summary>Sinh tên đăng nhập ngẫu nhiên cho tài khoản khách, chỉ gồm ký tự hợp lệ.</summary>
        public static string GenerateGuestUsername()
        {
            const string alphabet = "abcdefghijkmnopqrstuvwxyz23456789";

            var suffix = new char[10];
            for (int i = 0; i < suffix.Length; i++)
                suffix[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

            return "khach_" + new string(suffix);
        }

        /// <summary>Chuẩn hoá email: trim + hạ chữ thường, để so trùng lặp cho chắc.</summary>
        private static string NormalizeEmail(string email)
        {
            return (email ?? "").Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Mã OTP đã bị hủy vĩnh viễn hay chưa: hết hạn, hoặc sai quá MaxFailedAttempts lần.
        /// Hai trường hợp này mã không dùng lại được nên tiến trình phải bắt đầu lại.
        /// </summary>
        private static bool IsCodeRejected(string? error)
        {
            if (string.IsNullOrEmpty(error))
                return false;

            return error.Contains("hết hạn", StringComparison.Ordinal)
                || error.Contains($"sai quá {OtpService.MaxFailedAttempts} lần", StringComparison.Ordinal);
        }

        /// <summary>
        /// Xoá tài khoản đang chờ xác minh khi mã OTP đã hỏng vĩnh viễn. Chỉ xoá tài khoản chưa xác minh email.
        /// </summary>
        private async Task RemovePendingUserAsync(int userId)
        {
            var pending = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (pending is null || pending.EmailVerified)
                return;

            _db.Users.Remove(pending);
            await _db.SaveChangesAsync();
        }
    }
}
