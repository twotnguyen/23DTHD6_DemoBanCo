using _23DTHD6_DemoBanCo.Models;
using Microsoft.EntityFrameworkCore;

namespace _23DTHD6_DemoBanCo.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();

        public DbSet<Room> Rooms => Set<Room>();

        public DbSet<RoomParticipant> RoomParticipants => Set<RoomParticipant>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<Room>()
                .HasIndex(r => r.Code)
                .IsUnique();

            modelBuilder.Entity<Room>()
                .HasOne(r => r.Owner)
                .WithMany()
                .HasForeignKey(r => r.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RoomParticipant>()
                .HasOne(p => p.Room)
                .WithMany(r => r.Participants)
                .HasForeignKey(p => p.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RoomParticipant>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Một người chỉ xuất hiện một lần trong mỗi phòng
            modelBuilder.Entity<RoomParticipant>()
                .HasIndex(p => new { p.RoomId, p.UserId })
                .IsUnique();
        }
    }
}