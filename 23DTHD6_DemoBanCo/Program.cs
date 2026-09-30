using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Hubs;
using _23DTHD6_DemoBanCo.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddSignalR();

// Cơ sở dữ liệu SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Nghiệp vụ phòng chơi
builder.Services.AddScoped<RoomService>();

// Ván đấu server-authoritative và chat
builder.Services.AddScoped<MatchService>();
builder.Services.AddScoped<ChatService>();

// Đồng hồ ván đấu chạy nền: trừ thời gian, xử thua khi hết giờ hoặc treo ván
builder.Services.AddHostedService<MatchClockService>();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHub<ChessHub>("/chessHub");
app.Run();
