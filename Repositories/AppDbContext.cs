using FinancialAdvisorAI.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinancialAdvisorAI.API.Repositories
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // Database tables - we'll add more as we progress
        public DbSet<User> Users { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<OngoingInstruction> OngoingInstructions { get; set; }
        public DbSet<AgentTask> AgentTasks { get; set; }
        public DbSet<EmailCache> EmailCaches { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure User entity
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Email).IsRequired();
                entity.HasIndex(e => e.Email).IsUnique();
            });

            // Configure ChatMessage entity
            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Content).IsRequired();
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure OngoingInstruction entity
            modelBuilder.Entity<OngoingInstruction>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Instruction).IsRequired();
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure AgentTask entity
            modelBuilder.Entity<AgentTask>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Description).IsRequired();
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<EmailCache>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.MessageId).IsRequired();
                entity.HasIndex(e => new { e.UserId, e.MessageId }).IsUnique();
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
