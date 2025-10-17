using System.ComponentModel.DataAnnotations;

namespace FinancialAdvisorAI.API.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        public string Email { get; set; } = string.Empty;

        public string? GoogleAccessToken { get; set; }
        public string? GoogleRefreshToken { get; set; }
        public DateTime? GoogleTokenExpiry { get; set; }

        public string? HubspotAccessToken { get; set; }
        public string? HubspotRefreshToken { get; set; }
        public DateTime? HubspotTokenExpiry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
    }
}
