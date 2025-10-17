using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using FinancialAdvisorAI.API.Models;
using Microsoft.EntityFrameworkCore;
using FinancialAdvisorAI.API.Repositories;

namespace FinancialAdvisorAI.API.Services
{
    public class EmailService
    {
        private readonly GoogleAuthService _googleAuthService;
        private readonly AppDbContext _context;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            GoogleAuthService googleAuthService,
            AppDbContext context,
            ILogger<EmailService> logger)
        {
            _googleAuthService = googleAuthService;
            _context = context;
            _logger = logger;
        }

        public async Task<GmailService> GetGmailServiceAsync(User user)
        {
            var credential = await _googleAuthService.GetUserCredentialAsync(user);

            var service = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Financial Advisor AI"
            });

            return service;
        }

        public async Task<List<Message>> ListMessagesAsync(User user, int maxResults = 100, string? query = null)
        {
            try
            {
                var service = await GetGmailServiceAsync(user);
                var request = service.Users.Messages.List("me");
                request.MaxResults = maxResults;

                if (!string.IsNullOrEmpty(query))
                {
                    request.Q = query;
                }

                var response = await request.ExecuteAsync();
                var messages = new List<Message>();

                if (response.Messages != null)
                {
                    foreach (var messageInfo in response.Messages)
                    {
                        var messageRequest = service.Users.Messages.Get("me", messageInfo.Id);
                        var message = await messageRequest.ExecuteAsync();
                        messages.Add(message);
                    }
                }

                return messages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing Gmail messages for user {UserId}", user.Id);
                throw;
            }
        }

        public async Task<Message> GetMessageAsync(User user, string messageId)
        {
            try
            {
                var service = await GetGmailServiceAsync(user);
                var request = service.Users.Messages.Get("me", messageId);
                var message = await request.ExecuteAsync();
                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Gmail message {MessageId} for user {UserId}", messageId, user.Id);
                throw;
            }
        }

        public async Task<Message> SendMessageAsync(User user, string to, string subject, string body)
        {
            try
            {
                var service = await GetGmailServiceAsync(user);

                var message = new Message
                {
                    Raw = CreateRawMessage(to, subject, body)
                };

                var request = service.Users.Messages.Send(message, "me");
                var sentMessage = await request.ExecuteAsync();

                _logger.LogInformation("Sent email from user {UserId} to {To}", user.Id, to);
                return sentMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Gmail message for user {UserId}", user.Id);
                throw;
            }
        }

        private string CreateRawMessage(string to, string subject, string body)
        {
            var message = $"To: {to}\r\n" +
                         $"Subject: {subject}\r\n" +
                         $"Content-Type: text/plain; charset=utf-8\r\n\r\n" +
                         $"{body}";

            var encodedMessage = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(message))
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");

            return encodedMessage;
        }

        public string GetMessageBody(Message message)
        {
            if (message.Payload == null) return string.Empty;

            // Try to get plain text body
            if (message.Payload.Body?.Data != null)
            {
                return DecodeBase64(message.Payload.Body.Data);
            }

            // Check parts for text/plain
            if (message.Payload.Parts != null)
            {
                foreach (var part in message.Payload.Parts)
                {
                    if (part.MimeType == "text/plain" && part.Body?.Data != null)
                    {
                        return DecodeBase64(part.Body.Data);
                    }
                }

                // If no plain text, try text/html
                foreach (var part in message.Payload.Parts)
                {
                    if (part.MimeType == "text/html" && part.Body?.Data != null)
                    {
                        return DecodeBase64(part.Body.Data);
                    }
                }
            }

            return string.Empty;
        }

        public string GetMessageSubject(Message message)
        {
            var subject = message.Payload?.Headers?.FirstOrDefault(h => h.Name == "Subject");
            return subject?.Value ?? "(No Subject)";
        }

        public string GetMessageFrom(Message message)
        {
            var from = message.Payload?.Headers?.FirstOrDefault(h => h.Name == "From");
            return from?.Value ?? "Unknown";
        }

        public DateTime? GetMessageDate(Message message)
        {
            if (message.InternalDate.HasValue)
            {
                var dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value);
                return dateTimeOffset.DateTime;
            }
            return null;
        }

        private string DecodeBase64(string encodedString)
        {
            try
            {
                var data = encodedString.Replace('-', '+').Replace('_', '/');
                switch (data.Length % 4)
                {
                    case 2: data += "=="; break;
                    case 3: data += "="; break;
                }
                var bytes = Convert.FromBase64String(data);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}