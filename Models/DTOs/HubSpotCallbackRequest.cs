namespace FinancialAdvisorAI.API.Models.DTOs
{
    public class HubSpotCallbackRequest
    {
        public int UserId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }
}
