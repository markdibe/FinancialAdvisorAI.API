using Microsoft.EntityFrameworkCore;
using FinancialAdvisorAI.API.Models;

namespace FinancialAdvisorAI.API.Repositories
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // Database tables
        public DbSet<User> Users { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<OngoingInstruction> OngoingInstructions { get; set; }
        public DbSet<AgentTask> AgentTasks { get; set; }
        public DbSet<EmailCache> EmailCaches { get; set; }
        public DbSet<CalendarEventCache> CalendarEventCaches { get; set; }

        public DbSet<HubSpotContact> HubSpotContacts { get; set; }
        public DbSet<HubSpotCompany> HubSpotCompanies { get; set; }
        public DbSet<HubSpotDeal> HubSpotDeals { get; set; }

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

            // Configure EmailCache entity
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

            // Configure CalendarEventCache entity
            modelBuilder.Entity<CalendarEventCache>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EventId).IsRequired();
                entity.Property(e => e.Summary).IsRequired();
                entity.HasIndex(e => new { e.UserId, e.EventId }).IsUnique();
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<HubSpotContact>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.HubSpotId).IsRequired();
                entity.HasIndex(e => new { e.UserId, e.HubSpotId }).IsUnique();
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // NEW: Configure HubSpotCompany entity
            modelBuilder.Entity<HubSpotCompany>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.HubSpotId).IsRequired();
                entity.HasIndex(e => new { e.UserId, e.HubSpotId }).IsUnique();
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // NEW: Configure HubSpotDeal entity
            modelBuilder.Entity<HubSpotDeal>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.HubSpotId).IsRequired();
                entity.HasIndex(e => new { e.UserId, e.HubSpotId }).IsUnique();
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}