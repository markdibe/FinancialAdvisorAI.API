using System.ComponentModel.DataAnnotations;

namespace FinancialAdvisorAI.API.Models
{
    /// <summary>
    /// Represents an ongoing instruction that the AI should follow automatically
    /// </summary>
    public class OngoingInstruction
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// User who created this instruction
        /// </summary>
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        /// <summary>
        /// The instruction text (e.g., "When someone emails about meetings, check my calendar and respond")
        /// </summary>
        [Required]
        public string InstructionText { get; set; } = string.Empty;

        /// <summary>
        /// Trigger type: Email, Calendar, HubSpot, or All
        /// </summary>
        [Required]
        public string TriggerType { get; set; } = "Email"; // Email, Calendar, HubSpot, All

        /// <summary>
        /// AI-generated trigger conditions (JSON format)
        /// Example: {"keywords": ["meeting", "schedule"], "sender_type": "new_contact"}
        /// </summary>
        public string? TriggerConditions { get; set; }

        /// <summary>
        /// Actions the AI should take (JSON format)
        /// Example: {"action": "send_email", "search_calendar": true, "create_hubspot_contact": true}
        /// </summary>
        public string? Actions { get; set; }

        /// <summary>
        /// Whether this instruction is currently active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Priority level (higher = executed first)
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// How many times this instruction has been triggered
        /// </summary>
        public int ExecutionCount { get; set; } = 0;

        /// <summary>
        /// When this instruction was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When this instruction was last updated
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// When this instruction was last executed
        /// </summary>
        public DateTime? LastExecutedAt { get; set; }
    }
}