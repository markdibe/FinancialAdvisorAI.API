using System.ComponentModel.DataAnnotations;

namespace FinancialAdvisorAI.API.Models
{
    public class OngoingInstruction
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        [Required]
        public string Instruction { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
