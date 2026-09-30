using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Hubs;
using _23DTHD6_DemoBanCo.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddSignalR();

// Session lưu tiến trình đăng ký 3 bước và tiến trình đổi tên đăng nhập dưới dạng JSON.
// TempData không dùng được cho object vì CookieTempDataProvider chỉ serialize string và primitive.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(10);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Cơ sở dữ liệu SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Nghiệp vụ phòng chơi
builder.Services.AddScoped<RoomService>();

// Ghép trận: hàng đợi phải là singleton để mọi kết nối dùng chung, nhưng singleton
// không được phụ thuộc DbContext scoped (sẽ chặn app khởi động). Nên tách hai phần:
// MatchmakingQueue giữ trạng thái trong bộ nhớ, MatchmakingService lo phần CSDL.
builder.Services.AddSingleton<MatchmakingQueue>();
builder.Services.AddScoped<MatchmakingService>();

// Tài khoản: OTP, đăng ký, đổi tên đăng nhập
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<AccountService>();

// Ván đấu server-authoritative và chat
builder.Services.AddScoped<MatchService>();
builder.Services.AddScoped<ChatService>();

// Bảng xếp hạng Elo, lịch sử ván đấu với máy, và luồng đấu với máy.
// AiMatchService phụ thuộc RoomService + MatchService (đều đã đăng ký ở trên),
// nên thứ tự dòng không quan trọng, container DI tự giải phụ thuộc theo đồ thị.
builder.Services.AddScoped<LeaderboardService>();
builder.Services.AddScoped<MatchHistoryService>();
builder.Services.AddScoped<AiMatchService>();

// Bạn bè và tin nhắn riêng. DirectMessageService phụ thuộc FriendshipService nên
// cả hai cùng nằm trong nhóm này.
builder.Services.AddScoped<FriendshipService>();
builder.Services.AddScoped<DirectMessageService>();

// Đồng hồ ván đấu chạy nền: trừ thời gian, xử thua khi hết giờ hoặc treo ván
builder.Services.AddHostedService<MatchClockService>();

// Tiến trình đăng ký 3 bước lưu trong Session vì CookieTempDataProvider mặc định
// chỉ serialize được string, không lưu được object.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(10);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Đăng nhập bằng cookie
builder.Services.AddAuthentication("Cookie")
    .AddCookie("Cookie", options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

var app = builder.Build();

// Áp dụng migration để tạo/cập nhật bảng
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Phải đặt sau UseRouting và trước UseAuthentication.
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHub<ChessHub>("/chessHub");
app.MapHub<MatchmakingHub>("/matchmakingHub");
app.Run();
