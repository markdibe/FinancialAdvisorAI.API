using System.ComponentModel.DataAnnotations;

namespace FinancialAdvisorAI.API.Models
{
    public class EmailCache
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        [Required]
        public string MessageId { get; set; } = string.Empty; // Gmail message ID

        public string? ThreadId { get; set; }

        [Required]
        public string Subject { get; set; } = string.Empty;

        public string? FromEmail { get; set; }
        public string? FromName { get; set; }

        public string? ToEmail { get; set; }

        public string? Body { get; set; }
        public string? Snippet { get; set; }

        public DateTime? EmailDate { get; set; }

        public bool IsRead { get; set; }
        public bool IsSent { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
