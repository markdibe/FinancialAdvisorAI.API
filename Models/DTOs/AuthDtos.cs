namespace FinancialAdvisorAI.API.Models.DTOs
{
    public class GoogleCallbackRequest
    {
        public string Code { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }

    public class GoogleLoginResponse
    {
        public string AuthUrl { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }

    public class AuthCallbackResponse
    {
        public bool Success { get; set; }
        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Error { get; set; }
    }

    public class UserResponse
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public bool HasGoogleAuth { get; set; }
        public bool HasHubspotAuth { get; set; }
        public DateTime? LastLogin { get; set; }
    }
}
