using System.Security.Cryptography;
using System.Text;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;
using Microsoft.EntityFrameworkCore;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Đồ án demo không có SMTP nên mã được trả về cho tầng trên hiển thị thẳng cho người dùng.
    ///
    /// BẢO MẬT: cột Code lưu HASH SHA-256 của mã cộng với salt riêng cho từng mã, không lưu mã gốc.
    /// Đọc được file SQLite cũng không dựng lại được mã còn hiệu lực để chiếm tài khoản.
    /// Dùng SHA-256 một lượt chứ không PBKDF2 100.000 vòng như mật khẩu, vì mã chỉ sống 3 phút
    /// và không tái sử dụng được (mỗi lần sinh là một mã khác nhau).
    ///
    /// QUY TẮC THỜI HẠN (áp dụng cho mọi purpose):
    ///  - Mã sống 180 giây (3 phút) kể từ lúc sinh. Hết hạn thì tự động vô hiệu hoá khi kiểm tra.
    ///  - Sai quá 5 lần thì mã bị huỷ, phải xin mã mới.
    ///  - Hai lần yêu cầu liên tiếp cách nhau dưới 60 giây thì lần sau bị từ chối (chống spam bấm nút).
    ///  - Khi sinh mã mới, mọi mã cũ còn hiệu lực của cùng (purpose, target) bị huỷ: chỉ mã mới nhất dùng được.
    /// </summary>
    public sealed class OtpService
    {
        public const int CodeLength = 6;

        /// <summary>Hạn sử dụng của một mã: 3 phút.</summary>
        public const int ExpirySeconds = 180;

        /// <summary>Sai quá số lần này thì mã bị huỷ.</summary>
        public const int MaxFailedAttempts = 5;

        /// <summary>Khoảng lặng giữa hai lần gửi liên tiếp: 60 giây.</summary>
        public const int ResendCooldownSeconds = 60;

        private readonly AppDbContext _db;

        public OtpService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>Kết quả xử lý OTP. Với GenerateAsync, Code có giá trị để tầng trên hiển thị (demo không gửi mail).</summary>
        public sealed record OtpResult(bool Success, string? Code, string? Error);

        /// <summary>
        /// Sinh mã mới cho (purpose, target). Hủy mã cũ còn hiệu lực, sau đó trả về mã để hiển thị.
        /// </summary>
        public async Task<OtpResult> GenerateAsync(string purpose, string target)
        {
            if (string.IsNullOrWhiteSpace(purpose))
                return new OtpResult(false, null, "Thiếu mục đích mã xác thực.");

            if (string.IsNullOrWhiteSpace(target))
                return new OtpResult(false, null, "Thiếu địa chỉ nhận mã xác thực.");

            string p = purpose.Trim();
            string t = NormalizeTarget(target);
            DateTime now = DateTime.UtcNow;

            // Chống spam: chặn nếu vừa yêu cầu mã trong 60 giây gần nhất
            var latest = await _db.OtpCodes
                .Where(o => o.Purpose == p && o.Target == t)
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync();

            if (latest is not null)
            {
                DateTime nextAllowedAt = latest.CreatedAt.AddSeconds(ResendCooldownSeconds);
                if (now < nextAllowedAt)
                {
                    int waitSeconds = (int)Math.Ceiling((nextAllowedAt - now).TotalSeconds);
                    return new OtpResult(false, null, $"Vui lòng chờ {waitSeconds} giây để yêu cầu mã mới.");
                }
            }

            // Chỉ mã mới nhất được dùng: huỷ toàn bộ mã cũ chưa dùng và chưa huỷ
            var activeCodes = await _db.OtpCodes
                .Where(o => o.Purpose == p && o.Target == t && !o.IsUsed && !o.IsCancelled)
                .ToListAsync();

            foreach (var old in activeCodes)
                old.IsCancelled = true;

            string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D" + CodeLength);

            // Chỉ lưu hash + salt, không bao giờ lưu mã gốc xuống cơ sở dữ liệu
            string salt = GenerateCodeSalt();

            _db.OtpCodes.Add(new OtpCode
            {
                Purpose = p,
                Target = t,
                Code = HashCode(code, salt),
                CodeSalt = salt,
                CreatedAt = now,
                ExpiresAt = now.AddSeconds(ExpirySeconds),
                FailedAttempts = 0,
                IsCancelled = false,
                IsUsed = false
            });

            await _db.SaveChangesAsync();

            return new OtpResult(true, code, null);
        }

        /// <summary>
        /// Kiểm tra mã người dùng nhập. Các bước theo đúng thứ tự:
        /// 1. Tìm mã gần nhất chưa dùng, chưa huỷ. Không có thì báo "Mã không hợp lệ."
        /// 2. Quá hạn 180 giây thì huỷ mã và báo "Mã đã hết hạn."
        /// 3. Sai mã thì tăng bộ đếm sai; đủ 5 lần thì huỷ mã và báo yêu cầu mã mới.
        /// 4. Đúng mã thì đánh dấu đã dùng (mã chỉ dùng được một lần).
        /// </summary>
        public async Task<OtpResult> VerifyAsync(string purpose, string target, string code)
        {
            if (string.IsNullOrWhiteSpace(purpose) || string.IsNullOrWhiteSpace(target))
                return new OtpResult(false, null, "Mã không hợp lệ.");

            if (string.IsNullOrWhiteSpace(code))
                return new OtpResult(false, null, "Vui lòng nhập mã xác thực.");

            string p = purpose.Trim();
            string t = NormalizeTarget(target);
            string input = code.Trim();
            DateTime now = DateTime.UtcNow;

            // Bước 1: lấy mã mới nhất còn hiệu lực
            var record = await _db.OtpCodes
                .Where(o => o.Purpose == p && o.Target == t && !o.IsUsed && !o.IsCancelled)
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync();

            if (record is null)
                return new OtpResult(false, null, "Mã không hợp lệ.");

            // Bước 2: hết hạn
            if (now > record.ExpiresAt)
            {
                record.IsCancelled = true;
                await _db.SaveChangesAsync();
                return new OtpResult(false, null, "Mã đã hết hạn. Vui lòng yêu cầu mã mới.");
            }

            // Bước 3: sai mã
            if (!CodesMatch(input, record.Code, record.CodeSalt))
            {
                record.FailedAttempts++;
                int remaining = MaxFailedAttempts - record.FailedAttempts;

                if (record.FailedAttempts >= MaxFailedAttempts)
                {
                    record.IsCancelled = true;
                    await _db.SaveChangesAsync();
                    return new OtpResult(false, null,
                        $"Bạn đã nhập sai quá {MaxFailedAttempts} lần. Vui lòng yêu cầu mã mới.");
                }

                await _db.SaveChangesAsync();
                return new OtpResult(false, null,
                    $"Mã không đúng. Bạn còn {remaining} lần thử.");
            }

            // Bước 4: đúng mã
            record.IsUsed = true;
            await _db.SaveChangesAsync();

            return new OtpResult(true, null, null);
        }

        /// <summary>Che bớt email để hiển thị an toàn: a**b@gmail.com.</summary>
        public static string MaskEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return "";

            string value = email.Trim();
            int at = value.IndexOf('@');

            string local = at > 0 ? value[..at] : value;
            string domain = at > 0 ? value[at..] : "";

            if (local.Length == 0)
                return domain;

            string maskedLocal = local.Length switch
            {
                1 => "*",
                2 => local[..1] + "*",
                _ => local[..1] + "**" + local[^1..]
            };

            return maskedLocal + domain;
        }

        /// <summary>
        /// Email so khớp không phân biệt hoa thường; Username so khớp phân biệt vì đăng ký đã chặn chữ hoa trùng nhau.
        /// Cả hai đều trim khoảng trắng thừa trước khi so.
        /// </summary>
        private static string NormalizeTarget(string target)
        {
            string value = target.Trim();
            return value.Contains('@') ? value.ToLowerInvariant() : value;
        }

        /// <summary>
        /// So sánh mã người nhập với mã đã lưu theo thời gian cố định.
        /// Mã nhập được băm lại bằng salt đã lưu rồi mới so, nên không cần đệm độ dài:
        /// cả hai vế đều là hash cố định 32 byte.
        /// </summary>
        private static bool CodesMatch(string input, string storedHash, string salt)
        {
            byte[] actual = Convert.FromBase64String(HashCode(input, salt));
            byte[] expected = Convert.FromBase64String(storedHash);

            if (actual.Length != expected.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        /// <summary>Sinh salt ngẫu nhiên 16 byte cho mỗi mã OTP.</summary>
        private static string GenerateCodeSalt()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        }

        /// <summary>Hash mã OTP bằng SHA-256 kèm salt, trả về Base64.</summary>
        private static string HashCode(string code, string salt)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(salt + "|" + code));
            return Convert.ToBase64String(bytes);
        }
    }
}
