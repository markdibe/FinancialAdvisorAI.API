using System.ComponentModel.DataAnnotations;

namespace FinancialAdvisorAI.API.Models
{
    /// <summary>
    /// Tracks actions the AI agent takes automatically
    /// </summary>
    public class AgentActivity
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// User whose account the action was taken on
        /// </summary>
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        /// <summary>
        /// Instruction that triggered this activity (if applicable)
        /// </summary>
        public int? OngoingInstructionId { get; set; }
        public OngoingInstruction? OngoingInstruction { get; set; }

        /// <summary>
        /// Type of activity: EmailSent, CalendarEventCreated, HubSpotContactCreated, etc.
        /// </summary>
        [Required]
        public string ActivityType { get; set; } = string.Empty;

        /// <summary>
        /// Description of what the AI did
        /// </summary>
        [Required]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Details in JSON format
        /// Example: {"email_to": "john@example.com", "subject": "Re: Meeting Request"}
        /// </summary>
        public string? Details { get; set; }

        /// <summary>
        /// What triggered this activity (email ID, calendar event ID, etc.)
        /// </summary>
        public string? TriggeredBy { get; set; }

        /// <summary>
        /// Status: Success, Failed, Pending
        /// </summary>
        [Required]
        public string Status { get; set; } = "Success";

        /// <summary>
        /// Error message if failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// When this activity occurred
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether user has seen this activity
        /// </summary>
        public bool IsRead { get; set; } = false;
    }
}