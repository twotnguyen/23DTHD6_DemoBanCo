using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;
using _23DTHD6_DemoBanCo.Services;

namespace _23DTHD6_DemoBanCo.Controllers
{
    /// <summary>
    /// Luồng tài khoản: đăng nhập, đăng ký 3 bước có OTP, đăng nhập khách, hồ sơ, đổi tên đăng nhập qua OTP.
    ///
    /// CÁCH LƯU TIẾN TRÌNH: dùng HttpContext.Session, lưu object dạng JSON.
    /// KHÔNG dùng TempData cho tiến trình vì CookieTempDataProvider mặc định chỉ serialize được
    /// string và primitive, ghi object vào đó sẽ ném InvalidOperationException lúc runtime.
    /// TempData vẫn dùng cho thông báo lỗi dạng string một lần, không ảnh hưởng gì.
    ///
    /// Session KHÔNG lưu PasswordHash: hash mật khẩu đã nằm ở bảng Users, tiến trình chỉ giữ
    /// Id của tài khoản tạm. Cookie phiên không nên chứa dữ liệu bí mật.
    ///
    /// Tiến trình hết hạn sau 10 phút. Quá hạn thì coi như chưa bắt đầu và xoá tài khoản tạm
    /// chưa xác minh email, để tên đăng nhập không bị chiếm vĩnh viễn.
    /// </summary>
    public class AccountController : Controller
    {
        /// <summary>Tiến trình đăng ký / đổi tên chỉ sống 10 phút.</summary>
        private static readonly TimeSpan ProgressLifetime = TimeSpan.FromMinutes(10);

        private const string RegisterProgressKey = "RegisterProgress";
        private const string ChangeUsernameProgressKey = "ChangeUsernameProgress";
        private const string UnlockedKey = "ChangeUsernameUnlocked";
        private const string DevCodeKey = "OtpDevCode";

        private readonly AppDbContext _db;
        private readonly AccountService _accounts;
        private readonly OtpService _otp;

        public AccountController(AppDbContext db, AccountService accounts, OtpService otp)
        {
            _db = db;
            _accounts = accounts;
            _otp = otp;
        }

        // ==================== Đăng nhập / đăng xuất ====================

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");

            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(model);

            string username = model.Username.Trim();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);

            // Thông báo giống nhau cho cả hai trường hợp để không lộ tài khoản có tồn tại hay không.
            // Tài khoản khách không có mật khẩu nên cũng không đăng nhập được bằng form này.
            if (user == null || user.IsGuest || !PasswordHasher.Verify(model.Password, user.PasswordHash))
            {
                model.ErrorMessage = "Tên đăng nhập hoặc mật khẩu không đúng.";
                return View(model);
            }

            await SignInAsync(user);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("Cookie");
            return RedirectToAction(nameof(Login));
        }

        // ==================== Đăng ký 3 bước + OTP ====================

        /// <summary>
        /// BƯỚC 1 - GET. Điền tên đăng nhập và mật khẩu.
        /// Chưa có email nên chưa thể gửi mã OTP ở bước này.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Register()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");

            // Dọn tài khoản tạm của lần đăng ký bỏ dở quá 10 phút,
            // tránh tên đăng nhập bị chiếm vĩnh viễn bởi tài khoản chưa xác minh email
            await DiscardExpiredPendingUserAsync();

            ViewData["ErrorMessage"] = TempData["ErrorMessage"] as string;

            return View(new RegisterStep1ViewModel());
        }

        /// <summary>
        /// BƯỚC 1 - POST. Kiểm tra tên đăng nhập UNIQUE (sau khi Trim), băm mật khẩu rồi lưu
        /// tiến trình sang bước 2. Tài khoản tạm CHƯA có email và CHƯA xác minh.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterStep1ViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Bỏ tài khoản tạm cũ đang treo trong Session trước khi tạo tài khoản mới
            await DiscardStalePendingUserAsync();

            var (user, error) = await _accounts.CreateUserAsync(
                model.Username, model.Username, null, model.Password, isGuest: false);

            if (error != null)
            {
                model.ErrorMessage = error;
                return View(model);
            }

            // KHÔNG lưu PasswordHash trong tiến trình: hash đã nằm ở bảng Users,
            // Session đi qua cookie phiên nên không nên chứa dữ liệu bí mật
            SaveRegisterProgress(new RegisterProgress
            {
                PendingUserId = user.Id,
                Username = user.Username,
                Step = 2
            });

            return RedirectToAction(nameof(RegisterStep2));
        }

        /// <summary>
        /// BƯỚC 2 - GET. Nhập email. Ở bước này vẫn chưa tạo tài khoản thật, chỉ ghi nhớ email
        /// để bước 3 sinh mã OTP gắn với email này.
        /// </summary>
        [HttpGet]
        public IActionResult RegisterStep2()
        {
            if (!TryGetRegisterProgress(out _))
                return RedirectToAction(nameof(Register));

            return View(new RegisterStep2ViewModel());
        }

        /// <summary>
        /// BƯỚC 2 - POST. Bấm "Xác nhận Email": chỉ kiểm tra định dạng và trùng lặp rồi chuyển
        /// sang bước 3. Email trùng báo thông báo CHUNG, không tiết lộ email đó đã có tài khoản.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterStep2(RegisterStep2ViewModel model)
        {
            if (!TryGetRegisterProgress(out var progress))
                return RedirectToAction(nameof(Register));

            if (!ModelState.IsValid)
                return View(model);

            string email = model.Email.Trim();

            if (await _accounts.IsEmailTakenAsync(email))
            {
                model.ErrorMessage = "Email không hợp lệ hoặc đã được dùng. Vui lòng thử email khác.";
                return View(model);
            }

            progress.Email = email;
            progress.Step = 3;

            // SaveRegisterProgress tự đặt lại mốc hết hạn 10 phút
            SaveRegisterProgress(progress);

            return RedirectToAction(nameof(VerifyOtp));
        }

        /// <summary>
        /// BƯỚC 3 - GET. Màn hình nhập mã OTP.
        /// Nếu chưa có mã nào vừa gửi thì sinh mới bằng OtpService.GenerateAsync("REGISTER", email).
        /// Demo không có SMTP nên mã được trả về và hiển thị thẳng trong khung "Mã demo".
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> VerifyOtp()
        {
            if (!TryGetRegisterProgress(out var progress))
                return RedirectToAction(nameof(Register));

            if (string.IsNullOrEmpty(progress.Email))
                return RedirectToAction(nameof(RegisterStep2));

            // Trong 60 giây cooldown thì GenerateAsync bị chặn, dùng lại mã đang có
            if (string.IsNullOrEmpty(progress.DevCodeHint))
            {
                var generated = await _otp.GenerateAsync(AccountService.RegisterPurpose, progress.Email);

                if (generated.Success)
                    progress.DevCodeHint = generated.Code;

                SaveRegisterProgress(progress);
            }

            return View(new OtpVerifyViewModel
            {
                DevCodeHint = progress.DevCodeHint,
                ResendAvailableAt = DateTimeOffset.UtcNow.AddSeconds(OtpService.ResendCooldownSeconds)
            });
        }

        /// <summary>
        /// BƯỚC 3 - POST. Nhập mã OTP 6 chữ số.
        ///
        /// Đúng: lưu Email, bật EmailVerified, đăng nhập bằng cookie rồi sang sảnh.
        /// Gõ sai: Ở LẠI trang này, hiện lỗi và giữ nguyên tiến trình, chỉ mất 1 lượt thử.
        /// Mã hỏng vĩnh viễn (hết hạn hoặc sai quá 5 lần): mã không dùng lại được nên xoá tiến trình
        /// và tài khoản tạm, đưa người dùng về bước 1 để đăng ký lại.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOtp(OtpVerifyViewModel model)
        {
            if (!TryGetRegisterProgress(out var progress))
                return RedirectToAction(nameof(Register));

            if (string.IsNullOrEmpty(progress.Email))
                return RedirectToAction(nameof(RegisterStep2));

            // Sai định dạng mã thì chỉ báo lỗi, KHÔNG tiêu tốn lượt thử của OtpService
            if (!ModelState.IsValid)
            {
                model.DevCodeHint = progress.DevCodeHint;
                return View(model);
            }

            var (ok, error, codeRejected) = await _accounts.CompleteRegistrationAsync(
                progress.PendingUserId, progress.Email, model.Code);

            if (!ok)
            {
                // Mã đã hỏng vĩnh viễn: không dùng lại được, phải bắt đầu lại từ bước 1
                if (codeRejected)
                {
                    HttpContext.Session.Remove(RegisterProgressKey);
                    TempData["ErrorMessage"] = error;
                    return RedirectToAction(nameof(Register));
                }

                // Chỉ gõ sai mã: giữ tiến trình và trả lại đúng view hiện tại để thử tiếp ngay.
                // Ô mã để trống nhưng vẫn hiện mã demo, không bắt người dùng bấm gửi lại mã.
                return View(new OtpVerifyViewModel
                {
                    Code = "",
                    ErrorMessage = error,
                    DevCodeHint = progress.DevCodeHint,
                    ResendAvailableAt = DateTimeOffset.UtcNow.AddSeconds(OtpService.ResendCooldownSeconds)
                });
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == progress.PendingUserId);
            if (user is null)
            {
                HttpContext.Session.Remove(RegisterProgressKey);
                return RedirectToAction(nameof(Register));
            }

            await SignInAsync(user);

            HttpContext.Session.Remove(RegisterProgressKey);
            return RedirectToAction("Lobby", "Room");
        }

        // ==================== Chế độ Khách ====================

        /// <summary>Đăng nhập khách: chỉ cần một tên hiển thị, không cần mật khẩu.</summary>
        [HttpGet]
        public IActionResult Guest()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Lobby", "Room");

            return View(new GuestLoginViewModel());
        }

        /// <summary>
        /// Tạo tài khoản khách rồi đăng nhập bằng cookie.
        /// Không lưu hash mật khẩu vì tài khoản khách không có mật khẩu, Elo khởi đầu 1200.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Guest(GuestLoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var (user, error) = await _accounts.CreateUserAsync(
                AccountService.GenerateGuestUsername(), model.DisplayName, null, null, isGuest: true);

            if (error != null)
            {
                model.ErrorMessage = error;
                return View(model);
            }

            await SignInAsync(user);
            return RedirectToAction("Lobby", "Room");
        }

        // ==================== Hồ sơ ====================

        /// <summary>
        /// Hồ sơ: xem tên đăng nhập, email và sửa tên hiển thị.
        /// Sửa tên hiển thị tự do, KHÔNG cần OTP.
        /// </summary>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            int userId = CurrentUserId;
            if (userId == 0)
                return RedirectToAction(nameof(Login));

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return RedirectToAction(nameof(Login));

            var view = ToProfileViewModel(user);
            view.SuccessMessage = TempData["SuccessMessage"] as string;
            view.ErrorMessage = TempData["ErrorMessage"] as string;

            return View(view);
        }

        /// <summary>
        /// Lưu tên hiển thị mới. Tên đăng nhập và email không có ô nhập trong form nên không thể sửa từ đây.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Profile(ProfileViewModel model)
        {
            int userId = CurrentUserId;
            if (userId == 0)
                return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid)
            {
                var current = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (current is null)
                    return RedirectToAction(nameof(Login));

                // Nạp lại phần chỉ đọc để form hiện đúng sau khi validation fail
                var view = ToProfileViewModel(current);
                view.DisplayName = model.DisplayName;
                // ModelStateEntry không có ErrorMessage, phải lấy từ danh sách Errors
                view.ErrorMessage = ModelState[nameof(ProfileViewModel.DisplayName)]?.Errors
                    .FirstOrDefault()?.ErrorMessage;
                return View(view);
            }

            var updated = await _accounts.UpdateDisplayNameAsync(userId, model.DisplayName);

            if (updated is null)
            {
                TempData["ErrorMessage"] = "Tên hiển thị phải từ 2 đến 30 ký tự.";
                return RedirectToAction(nameof(Profile));
            }

            // Đồng bộ lại cookie để navbar hiện tên mới ngay, không phải đăng xuất rồi đăng nhập lại
            await SignInAsync(updated);

            TempData["SuccessMessage"] = "Đã cập nhật tên hiển thị.";
            return RedirectToAction(nameof(Profile));
        }

        // ==================== Đổi tên đăng nhập qua OTP (4 bước) ====================

        /// <summary>
        /// BƯỚC 1 - GET. Màn hình đổi tên đăng nhập.
        /// Tên hiện tại và email hiển thị readonly: KHÔNG cho đổi email.
        /// Khi đã xác minh OTP (TempData cờ Unlocked) thì mới hiện ô nhập tên mới.
        /// </summary>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> ChangeUsername()
        {
            int userId = CurrentUserId;
            if (userId == 0)
                return RedirectToAction(nameof(Login));

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return RedirectToAction(nameof(Login));

            // Cờ trạng thái lưu trong Session dưới dạng chuỗi "1"/"0"
            bool unlocked = HttpContext.Session.GetString(UnlockedKey) == "1";
            string? devCode = HttpContext.Session.GetString(DevCodeKey);

            ViewData["CurrentUsername"] = user.Username;
            ViewData["ReadOnlyEmail"] = user.Email;
            ViewData["DevCodeHint"] = devCode;
            ViewData["ErrorMessage"] = TempData["ErrorMessage"] as string;

            return View(new ChangeUsernameViewModel { Unlocked = unlocked });
        }

        /// <summary>
        /// BƯỚC 1 - POST. Bấm "Gửi mã": sinh OTP với purpose CHANGE_USERNAME gắn vào email HIỆN TẠI.
        /// Tài khoản khách không có email nên không đổi tên đăng nhập được.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> ChangeUsernameSendCode()
        {
            int userId = CurrentUserId;
            if (userId == 0)
                return RedirectToAction(nameof(Login));

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return RedirectToAction(nameof(Login));

            if (string.IsNullOrEmpty(user.Email))
            {
                TempData["ErrorMessage"] = "Tài khoản Khách không có email nên không đổi tên đăng nhập được.";
                return RedirectToAction(nameof(Profile));
            }

            var result = await _otp.GenerateAsync(AccountService.ChangeUsernamePurpose, user.Email);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction(nameof(ChangeUsername));
            }

            // Tiến trình lưu trong Session dạng JSON, không phải TempData
            HttpContext.Session.SetString(ChangeUsernameProgressKey, JsonSerializer.Serialize(
                new ChangeUsernameProgress
                {
                    Email = user.Email,
                    ExpiresAt = DateTimeOffset.UtcNow.Add(ProgressLifetime)
                }));

            // Đã mở khoá trước thì phải khoá lại: mã mới cần xác minh từ đầu
            HttpContext.Session.SetString(UnlockedKey, "0");

            // Demo không có SMTP nên mã hiển thị thẳng cho người dùng
            HttpContext.Session.SetString(DevCodeKey, result.Code ?? "");

            return RedirectToAction(nameof(ChangeUsername));
        }

        /// <summary>
        /// BƯỚC 2 và 3 - POST. Nhập mã OTP.
        /// Đúng thì mở khoá ô nhập tên mới (đặt cờ Unlocked trong TempData).
        /// Sai quá 5 lần thì OtpService tự huỷ mã và báo phải gửi lại.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> ChangeUsernameVerifyOtp(OtpVerifyViewModel model)
        {
            int userId = CurrentUserId;
            if (userId == 0)
                return RedirectToAction(nameof(Login));

            if (!TryGetChangeUsernameProgress(out var progress))
            {
                TempData["ErrorMessage"] = "Phiên đổi tên đã hết hạn. Vui lòng gửi mã lại.";
                return RedirectToAction(nameof(ChangeUsername));
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Mã xác thực gồm đúng 6 chữ số.";
                return RedirectToAction(nameof(ChangeUsername));
            }

            var result = await _otp.VerifyAsync(AccountService.ChangeUsernamePurpose, progress.Email, model.Code);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction(nameof(ChangeUsername));
            }

            // OTP đúng: mở khoá bước 4. Mã demo đã dùng xong nên xoá đi.
            HttpContext.Session.Remove(DevCodeKey);
            HttpContext.Session.SetString(UnlockedKey, "1");

            return RedirectToAction(nameof(ChangeUsername));
        }

        /// <summary>
        /// BƯỚC 4 - POST. Lưu tên đăng nhập mới.
        /// Chỉ chạy được khi cờ Unlocked còn trong TempData, tức là OTP ở bước 2-3 đã đúng.
        /// user.Id giữ nguyên nên phòng, ván đấu, lịch sử không bị ảnh hưởng.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> ChangeUsernameSave(ChangeUsernameViewModel model)
        {
            int userId = CurrentUserId;
            if (userId == 0)
                return RedirectToAction(nameof(Login));

            bool unlocked = HttpContext.Session.GetString(UnlockedKey) == "1";

            if (!unlocked)
            {
                TempData["ErrorMessage"] = "Bạn phải xác minh mã trước khi đổi tên đăng nhập.";
                return RedirectToAction(nameof(ChangeUsername));
            }

            if (!ModelState.IsValid)
            {
                return await ChangeUsernameErrorView(model.NewUsername,
                    "Tên đăng nhập phải từ 3 đến 20 ký tự, chỉ gồm chữ, số và dấu gạch dưới.");
            }

            var (ok, error) = await _accounts.ChangeUsernameAsync(userId, model.NewUsername);

            if (!ok)
                return await ChangeUsernameErrorView(model.NewUsername, error);

            // Đồng bộ cookie với tên đăng nhập mới
            var updated = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (updated is not null)
                await SignInAsync(updated);

            HttpContext.Session.Remove(ChangeUsernameProgressKey);
            HttpContext.Session.Remove(UnlockedKey);
            HttpContext.Session.Remove(DevCodeKey);
            TempData["SuccessMessage"] = "Đã đổi tên đăng nhập thành công.";

            return RedirectToAction(nameof(Profile));
        }

        // ==================== Hàm dùng chung ====================

        private int CurrentUserId
        {
            get
            {
                string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
                return int.TryParse(value, out int id) ? id : 0;
            }
        }

        /// <summary>
        /// Đăng nhập bằng cookie. Giữ 3 claim như cũ: NameIdentifier, Name, và "display_name"
        /// mà _Layout.cshtml đọc để hiện tên trên navbar và _23DTHD6_DemoBanCo dùng để đồng bộ tên.
        /// </summary>
        private async Task SignInAsync(User user)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("display_name", user.DisplayName)
            };

            await HttpContext.SignInAsync(
                "Cookie",
                new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookie")),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                });
        }

        /// <summary>
        /// Lưu tiến trình đăng ký vào Session dưới dạng JSON.
        /// Session không tự xoá sau khi đọc như TempData, nên không cần Keep.
        /// </summary>
        private void SaveRegisterProgress(RegisterProgress progress)
        {
            progress.ExpiresAt = DateTimeOffset.UtcNow.Add(ProgressLifetime);
            HttpContext.Session.SetString(RegisterProgressKey, JsonSerializer.Serialize(progress));
        }

        /// <summary>
        /// Lấy tiến trình đăng ký từ Session. Hết hạn 10 phút thì coi như chưa bắt đầu.
        ///
        /// Hàm này KHÔNG async: C# không cho phép async kèm tham số out/ref/in (CS1988).
        /// Dữ liệu JSON hỏng (phiên cũ, cookie bị sửa) cũng được coi như không có tiến trình.
        /// </summary>
        private bool TryGetRegisterProgress([NotNullWhen(true)] out RegisterProgress? progress)
        {
            progress = ReadJson<RegisterProgress>(RegisterProgressKey);

            if (progress is null || progress.IsExpired)
            {
                HttpContext.Session.Remove(RegisterProgressKey);
                progress = null;
                return false;
            }

            return true;
        }

        /// <summary>Đọc object từ Session, trả về null nếu thiếu hoặc JSON không hợp lệ.</summary>
        private T? ReadJson<T>(string key) where T : class
        {
            string? raw = HttpContext.Session.GetString(key);

            if (string.IsNullOrEmpty(raw))
                return null;

            try
            {
                return JsonSerializer.Deserialize<T>(raw);
            }
            catch (JsonException)
            {
                HttpContext.Session.Remove(key);
                return null;
            }
        }

        /// <summary>
        /// Dọn tài khoản tạm đăng ký đã hết hạn 10 phút. Chỉ xoá tài khoản CHƯA xác minh email.
        /// </summary>
        private async Task DiscardExpiredPendingUserAsync()
        {
            var stale = ReadJson<RegisterProgress>(RegisterProgressKey);

            if (stale is null)
                return;

            HttpContext.Session.Remove(RegisterProgressKey);

            if (stale.IsExpired)
                await DiscardPendingUserAsync(stale.PendingUserId);
        }

        /// <summary>
        /// Người dùng bắt đầu đăng ký mới trong khi tiến trình cũ còn treo: xoá tài khoản tạm cũ
        /// để tên đăng nhập của nó không bị chiếm oan. Chỉ xoá nếu tiến trình cũ đã quá 10 phút.
        /// </summary>
        private async Task DiscardStalePendingUserAsync()
        {
            var stale = ReadJson<RegisterProgress>(RegisterProgressKey);

            if (stale is null || !stale.IsExpired)
                return;

            HttpContext.Session.Remove(RegisterProgressKey);
            await DiscardPendingUserAsync(stale.PendingUserId);
        }

        /// <summary>Xoá tài khoản đang chờ xác minh email (chỉ xoá tài khoản chưa xác minh).</summary>
        private async Task DiscardPendingUserAsync(int userId)
        {
            var pending = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.EmailVerified);

            if (pending is null)
                return;

            _db.Users.Remove(pending);
            await _db.SaveChangesAsync();
        }

        /// <summary>Lấy tiến trình đổi tên đăng nhập từ Session, cùng quy tắc hạn 10 phút.</summary>
        private bool TryGetChangeUsernameProgress([NotNullWhen(true)] out ChangeUsernameProgress? progress)
        {
            progress = ReadJson<ChangeUsernameProgress>(ChangeUsernameProgressKey);

            if (progress is null || progress.IsExpired)
            {
                HttpContext.Session.Remove(ChangeUsernameProgressKey);
                HttpContext.Session.Remove(UnlockedKey);
                progress = null;
                return false;
            }

            return true;
        }

        /// <summary>Hiển thị lại trang đổi tên với lỗi, giữ nguyên trạng thái đã mở khoá.</summary>
        private async Task<IActionResult> ChangeUsernameErrorView(string newUsername, string? error)
        {
            int userId = CurrentUserId;
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (user is not null)
            {
                ViewData["CurrentUsername"] = user.Username;
                ViewData["ReadOnlyEmail"] = user.Email;
            }

            ViewData["DevCodeHint"] = HttpContext.Session.GetString(DevCodeKey);
            ViewData["ErrorMessage"] = error;

            // Cờ đã mở khoá nằm trong Session nên vẫn còn khi người dùng sửa lại tên mới,
            // không phải xác minh OTP lần nữa

            return View(nameof(ChangeUsername), new ChangeUsernameViewModel
            {
                NewUsername = newUsername,
                ErrorMessage = error,
                Unlocked = true
            });
        }

        private static ProfileViewModel ToProfileViewModel(User user)
        {
            return new ProfileViewModel
            {
                Username = user.Username,
                DisplayName = user.DisplayName,
                Email = user.Email,
                IsGuest = user.IsGuest,
                EmailVerified = user.EmailVerified,
                Elo = user.Elo,
                Wins = user.Wins,
                Losses = user.Losses,
                Draws = user.Draws
            };
        }
    }
}
