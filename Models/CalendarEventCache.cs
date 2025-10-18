using System.ComponentModel.DataAnnotations;

namespace FinancialAdvisorAI.API.Models
{
    public class CalendarEventCache
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        [Required]
        public string EventId { get; set; } = string.Empty; // Google Calendar event ID

        [Required]
        public string Summary { get; set; } = string.Empty;

        public string? Description { get; set; }
        public string? Location { get; set; }

        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        public bool IsAllDay { get; set; }

        public string? Attendees { get; set; } // JSON array of email addresses

        public string? Status { get; set; } // confirmed, tentative, cancelled

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
