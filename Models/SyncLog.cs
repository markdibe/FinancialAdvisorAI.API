using System.ComponentModel.DataAnnotations;

namespace FinancialAdvisorAI.API.Models
{
    public class SyncLog
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }

        [Required]
        public string SyncType { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        [Required]
        public string Status { get; set; } = "Running";

        public int ItemsProcessed { get; set; }
        public int ItemsAdded { get; set; }
        public int ItemsUpdated { get; set; }

        public string? ErrorMessage { get; set; }
        public string? Details { get; set; }
    }
}