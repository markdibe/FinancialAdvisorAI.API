using Hangfire;
using FinancialAdvisorAI.API.Services;
using FinancialAdvisorAI.API.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FinancialAdvisorAI.API.Services.BackgroundJobs
{
    /// <summary>
    /// Background job that runs the proactive agent periodically
    /// </summary>
    public class ProactiveAgentJob
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ProactiveAgentJob> _logger;
        private readonly ProactiveAgentService _agentService;

        public ProactiveAgentJob(
            AppDbContext context,
            ILogger<ProactiveAgentJob> logger,
            ProactiveAgentService agentService)
        {
            _context = context;
            _logger = logger;
            _agentService = agentService;
        }

        /// <summary>
        /// Run proactive agent for all users with active instructions
        /// </summary>
        public async Task RunProactiveAgentAsync()
        {
            try
            {
                _logger.LogInformation("Starting proactive agent run at {Time}", DateTime.UtcNow);

                // Get all users with active instructions
                var usersWithInstructions = await _context.OngoingInstructions
                    .Where(i => i.IsActive)
                    .Select(i => i.UserId)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation("Found {Count} users with active instructions", usersWithInstructions.Count);

                foreach (var userId in usersWithInstructions)
                {
                    try
                    {
                        await ProcessUserInstructionsAsync(userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing instructions for user {UserId}", userId);
                        // Continue with next user even if one fails
                    }
                }

                _logger.LogInformation("Completed proactive agent run at {Time}", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in proactive agent run");
                throw;
            }
        }

        /// <summary>
        /// Process all instructions for a single user
        /// </summary>
        private async Task ProcessUserInstructionsAsync(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found", userId);
                    return;
                }

                _logger.LogInformation("Processing instructions for user {UserId} - {UserEmail}",
                    userId, user.Email);

                // Get user's active instructions grouped by trigger type
                var instructions = await _context.OngoingInstructions
                    .Where(i => i.UserId == userId && i.IsActive)
                    .ToListAsync();

                if (!instructions.Any())
                {
                    _logger.LogInformation("No active instructions for user {UserId}", userId);
                    return;
                }

                var hasEmailInstructions = instructions.Any(i =>
                    i.TriggerType == "Email" || i.TriggerType == "All");
                var hasCalendarInstructions = instructions.Any(i =>
                    i.TriggerType == "Calendar" || i.TriggerType == "All");
                var hasHubSpotInstructions = instructions.Any(i =>
                    i.TriggerType == "HubSpot" || i.TriggerType == "All");

                _logger.LogInformation(
                    "User {UserId} has {Total} active instructions: Email={Email}, Calendar={Calendar}, HubSpot={HubSpot}",
                    userId, instructions.Count, hasEmailInstructions, hasCalendarInstructions, hasHubSpotInstructions);

                // Process emails
                if (hasEmailInstructions)
                {
                    _logger.LogInformation("Processing email instructions for user {UserId}", userId);
                    await _agentService.ProcessNewEmailsAsync(userId);
                }

                // Process HubSpot
                if (hasHubSpotInstructions)
                {
                    _logger.LogInformation("Processing HubSpot instructions for user {UserId}", userId);
                    await _agentService.ProcessNewHubSpotContactsAsync(userId);
                }

                // Note: Calendar instructions are typically reactive (triggered by emails/requests)
                // but you can add calendar-specific processing here if needed
                // For example: Check for calendar events that need reminders, follow-ups, etc.

                _logger.LogInformation("Completed processing for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing instructions for user {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Schedule recurring proactive agent jobs
        /// Call this once during application startup
        /// </summary>
        public static void ScheduleRecurringJobs()
        {
            // Run proactive agent every 5 minutes
            RecurringJob.AddOrUpdate<ProactiveAgentJob>(
                "proactive-agent",
                job => job.RunProactiveAgentAsync(),
                "*/5 * * * *" // Cron: Every 5 minutes
            );

            Console.WriteLine("✅ Proactive Agent scheduled to run every 5 minutes");
        }
    }
}