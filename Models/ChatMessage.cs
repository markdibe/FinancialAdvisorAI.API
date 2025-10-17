
using System.ComponentModel.DataAnnotations;
namespace FinancialAdvisorAI.API.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        [Required]
        public string Content { get; set; } = string.Empty;

        public string Role { get; set; } = "user"; // "user" or "assistant"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // For storing context/metadata
        public string? Metadata { get; set; }
    }
}
