namespace FinancialAdvisorAI.API.Models.DTOs
{
    public class SendMessageRequest
    {
        public int UserId { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class ChatMessageResponse
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class ChatHistoryResponse
    {
        public List<ChatMessageResponse> Messages { get; set; } = new();
        public int TotalCount { get; set; }
    }
}
