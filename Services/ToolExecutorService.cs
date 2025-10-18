using FinancialAdvisorAI.API.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FinancialAdvisorAI.API.Services
{
    /// <summary>
    /// Executes tools/functions that the AI requests.
    /// This is a NEW service for tool calling functionality.
    /// </summary>
    public class ToolExecutorService
    {
        private readonly EmailService _emailService;
        private readonly AppDbContext _context;
        private readonly ILogger<ToolExecutorService> _logger;

        public ToolExecutorService(
            EmailService emailService,
            AppDbContext context,
            ILogger<ToolExecutorService> logger)
        {
            _emailService = emailService;
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Main entry point for executing any tool
        /// </summary>
        public async Task<string> ExecuteToolAsync(
            int userId,
            string toolName,
            string argumentsJson)
        {
            _logger.LogInformation("Executing tool: {ToolName} for user {UserId}", toolName, userId);
            _logger.LogDebug("Tool arguments: {Arguments}", argumentsJson);

            try
            {
                // Parse arguments
                var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
                if (arguments == null)
                {
                    return "Error: Invalid tool arguments";
                }

                // Route to appropriate tool handler
                switch (toolName)
                {
                    case "send_email":
                        return await ExecuteSendEmailAsync(userId, arguments);

                    // Future: Add more tools here
                    // case "create_calendar_event":
                    //     return await ExecuteCreateCalendarEventAsync(userId, arguments);
                    // case "create_hubspot_contact":
                    //     return await ExecuteCreateHubSpotContactAsync(userId, arguments);

                    default:
                        _logger.LogWarning("Unknown tool requested: {ToolName}", toolName);
                        return $"Error: Unknown tool '{toolName}'";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
                return $"Error executing {toolName}: {ex.Message}";
            }
        }

        /// <summary>
        /// Execute the send_email tool
        /// </summary>
        private async Task<string> ExecuteSendEmailAsync(
            int userId,
            Dictionary<string, JsonElement> arguments)
        {
            try
            {
                // Get user
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return "Error: User not found";
                }

                // Extract arguments
                if (!arguments.TryGetValue("to", out var toElement))
                {
                    return "Error: Missing 'to' email address";
                }

                if (!arguments.TryGetValue("subject", out var subjectElement))
                {
                    return "Error: Missing email subject";
                }

                if (!arguments.TryGetValue("body", out var bodyElement))
                {
                    return "Error: Missing email body";
                }

                var to = toElement.GetString();
                var subject = subjectElement.GetString();
                var body = bodyElement.GetString();

                // Validate
                if (string.IsNullOrWhiteSpace(to))
                {
                    return "Error: Recipient email address is empty";
                }

                if (string.IsNullOrWhiteSpace(subject))
                {
                    return "Error: Email subject is empty";
                }

                if (string.IsNullOrWhiteSpace(body))
                {
                    return "Error: Email body is empty";
                }

                _logger.LogInformation("Sending email to {To} with subject: {Subject}", to, subject);

                // Send the email using your existing EmailService
                var message = await _emailService.SendMessageAsync(user, to, subject, body);

                // Return success message
                return $"✅ Email sent successfully to {to} with subject '{subject}'. Message ID: {message.Id}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ExecuteSendEmailAsync");
                return $"Error sending email: {ex.Message}";
            }
        }

        // Future: Add more tool executors here
        /*
        private async Task<string> ExecuteCreateCalendarEventAsync(
            int userId,
            Dictionary<string, JsonElement> arguments)
        {
            // Implementation for creating calendar events
            throw new NotImplementedException();
        }

        private async Task<string> ExecuteCreateHubSpotContactAsync(
            int userId,
            Dictionary<string, JsonElement> arguments)
        {
            // Implementation for creating HubSpot contacts
            throw new NotImplementedException();
        }
        */
    }
}