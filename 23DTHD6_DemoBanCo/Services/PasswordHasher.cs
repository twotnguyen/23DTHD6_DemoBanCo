using System.Security.Cryptography;
using System.Text;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Băm và kiểm tra mật khẩu bằng PBKDF2-SHA256.
    /// Dùng thuật toán có sẵn của .NET nên không cần thêm package.
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSize = 16;   // 128 bit
        private const int KeySize = 32;    // 256 bit
        private const int Iterations = 100_000;

        public static string Hash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize);

            // Lưu cả salt lẫn key để kiểm tra được lần sau
            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
        }

        public static bool Verify(string password, string storedHash)
        {
            string[] parts = storedHash.Split('.');
            if (parts.Length != 3)
                return false;

            if (!int.TryParse(parts[0], out int iterations))
                return false;

            byte[] salt;
            byte[] expectedKey;
            try
            {
                salt = Convert.FromBase64String(parts[1]);
                expectedKey = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] actualKey = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedKey.Length);

            // So sánh theo thời gian cố định để không lộ thông tin qua thời gian thực hiện
            return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
        }
    }
}