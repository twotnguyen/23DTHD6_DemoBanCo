using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Models;

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

        public DbSet<Match> Matches => Set<Match>();

        public DbSet<MatchMove> MatchMoves => Set<MatchMove>();

        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

        public DbSet<Friendship> Friendships => Set<Friendship>();

        public DbSet<RoomBlock> RoomBlocks => Set<RoomBlock>();

        public DbSet<DirectConversation> DirectConversations => Set<DirectConversation>();

        public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();

        public DbSet<OtpCode> OtpCodes => Set<OtpCode>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<Room>()
                .HasIndex(r => r.Code)
                .IsUnique();

            // Sảnh lấy phòng theo trạng thái hiển thị và vòng đời
            modelBuilder.Entity<Room>()
                .HasIndex(r => new { r.Visibility, r.Status });

            // Tìm ván đang diễn ra của một phòng
            modelBuilder.Entity<Room>()
                .HasIndex(r => r.ActiveMatchId);

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

            modelBuilder.Entity<Match>()
                .HasOne(m => m.Room)
                .WithMany()
                .HasForeignKey(m => m.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Match>()
                .HasIndex(m => m.Status);

            modelBuilder.Entity<MatchMove>()
                .HasOne(mv => mv.Match)
                .WithMany(m => m.Moves)
                .HasForeignKey(mv => mv.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatMessage>()
                .HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatMessage>()
                .HasIndex(c => new { c.RoomId, c.CreatedAt });

            // Bạn bè: cặp (A,B) và (B,A) là một, lưu đúng 1 chiều với UserId < FriendId
            modelBuilder.Entity<Friendship>()
                .HasIndex(f => new { f.UserId, f.FriendId })
                .IsUnique();

            modelBuilder.Entity<RoomBlock>()
                .HasIndex(b => new { b.RoomId, b.BlockedUserId })
                .IsUnique();

            modelBuilder.Entity<DirectConversation>()
                .HasIndex(c => new { c.UserALowId, c.UserBHighId })
                .IsUnique();

            modelBuilder.Entity<DirectMessage>()
                .HasOne(d => d.Sender)
                .WithMany()
                .HasForeignKey(d => d.SenderId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OtpCode>()
                .HasIndex(o => new { o.Purpose, o.Target });

            modelBuilder.Entity<OtpCode>()
                .HasIndex(o => new { o.Purpose, o.Target, o.CreatedAt });
        }
    }
}