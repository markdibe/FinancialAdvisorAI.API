namespace FinancialAdvisorAI.API.Models
{
    public class SyncProgress
    {
        public int TotalProcessed { get; set; }
        public int CurrentPage { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
