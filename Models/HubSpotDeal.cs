using System.ComponentModel.DataAnnotations;

namespace FinancialAdvisorAI.API.Models
{
    public class HubSpotDeal
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        [Required]
        public string HubSpotId { get; set; } = string.Empty;

        public string? DealName { get; set; }
        public string? DealStage { get; set; }
        public string? Pipeline { get; set; }
        public decimal? Amount { get; set; }
        public DateTime? CloseDate { get; set; }
        public string? Priority { get; set; }

        public DateTime? LastModifiedDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
