using System.ComponentModel.DataAnnotations;

namespace FinancialAdvisorAI.API.Models
{
    public class AgentTask
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        [Required]
        public string Description { get; set; } = string.Empty;

        public string Status { get; set; } = "pending"; // "pending", "in_progress", "waiting_response", "completed", "failed"

        public string? Context { get; set; } // JSON data for task context

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
