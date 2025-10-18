using System.ComponentModel.DataAnnotations;

namespace FinancialAdvisorAI.API.Models
{
    public class HubSpotCompany
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        [Required]
        public string HubSpotId { get; set; } = string.Empty;

        public string? Name { get; set; }
        public string? Domain { get; set; }
        public string? Industry { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }

        public int? NumberOfEmployees { get; set; }
        public decimal? AnnualRevenue { get; set; }

        public DateTime? LastModifiedDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
