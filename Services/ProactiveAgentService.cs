using FinancialAdvisorAI.API.Models;
using FinancialAdvisorAI.API.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using OpenAI.Interfaces;
using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels;
using Google.Apis.Calendar.v3.Data;
using ChatMessage = OpenAI.ObjectModels.RequestModels.ChatMessage;

namespace FinancialAdvisorAI.API.Services
{
    /// <summary>
    /// Proactive agent that monitors data and executes instructions automatically
    /// Uses existing ToolExecutorService for consistency
    /// </summary>
    public class ProactiveAgentService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ProactiveAgentService> _logger;
        private readonly IOpenAIService _openAIService;
        private readonly ToolExecutorService _toolExecutor;

        public ProactiveAgentService(
            AppDbContext context,
            ILogger<ProactiveAgentService> logger,
            IOpenAIService openAIService,
            ToolExecutorService toolExecutor)
        {
            _context = context;
            _logger = logger;
            _openAIService = openAIService;
            _toolExecutor = toolExecutor;
        }

        /// <summary>
        /// Check for new emails and process instructions
        /// </summary>
        public async Task ProcessNewEmailsAsync(int userId)
        {
            try
            {
                _logger.LogInformation("Processing new emails for user {UserId}", userId);

                var user = await _context.Users.FindAsync(userId);
                if (user == null) return;

                // Get active email instructions
                var emailInstructions = await _context.OngoingInstructions
                    .Where(i => i.UserId == userId && i.IsActive &&
                               (i.TriggerType == "Email" || i.TriggerType == "All"))
                    .OrderByDescending(i => i.Priority)
                    .ToListAsync();

                if (!emailInstructions.Any())
                {
                    _logger.LogInformation("No active email instructions for user {UserId}", userId);
                    return;
                }

                // Get recent unprocessed emails (last 5 minutes)
                var recentEmails = await _context.EmailCaches
                    .Where(e => e.UserId == userId &&
                               e.EmailDate >= DateTime.UtcNow.AddMinutes(-5) &&
                               !e.IsSent) // Only incoming emails
                    .OrderByDescending(e => e.EmailDate)
                    .Take(10)
                    .ToListAsync();

                _logger.LogInformation("Found {Count} recent emails to process", recentEmails.Count);

                foreach (var email in recentEmails)
                {
                    await ProcessEmailWithInstructionsAsync(user, email, emailInstructions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing new emails for user {UserId}", userId);
            }
        }

        /// <summary>
        /// Process a single email against all instructions
        /// </summary>
        private async Task ProcessEmailWithInstructionsAsync(
            User user,
            EmailCache email,
            List<OngoingInstruction> instructions)
        {
            try
            {
                _logger.LogInformation("Processing email {EmailId} with {Count} instructions",
                    email.Id, instructions.Count);

                foreach (var instruction in instructions)
                {
                    // Check if this instruction applies to this email
                    var shouldExecute = await ShouldExecuteInstructionAsync(
                        user,
                        instruction,
                        email);

                    if (shouldExecute)
                    {
                        _logger.LogInformation("Executing instruction {InstructionId} for email {EmailId}",
                            instruction.Id, email.Id);

                        await ExecuteEmailInstructionAsync(user, instruction, email);

                        // Update instruction stats
                        instruction.ExecutionCount++;
                        instruction.LastExecutedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing email {EmailId}", email.Id);
            }
        }

        /// <summary>
        /// Use AI to determine if instruction should execute for this email
        /// </summary>
        private async Task<bool> ShouldExecuteInstructionAsync(
            User user,
            OngoingInstruction instruction,
            EmailCache email)
        {
            try
            {
                var prompt = $@"You are an AI assistant helping to determine if an instruction should be executed.

INSTRUCTION: {instruction.InstructionText}

EMAIL DETAILS:
- From: {email.FromEmail}
- Subject: {email.Subject}
- Snippet: {email.Snippet}
- Date: {email.EmailDate}

Should this instruction be executed for this email?
Respond with ONLY 'YES' or 'NO' and a brief reason (max 20 words).

Format: YES/NO | reason";

                var completionResult = await _openAIService.ChatCompletion.CreateCompletion(
                    new ChatCompletionCreateRequest
                    {
                        Messages = new List<ChatMessage>
                        {
                            ChatMessage.FromSystem("You are a precise decision-making AI. Be concise."),
                            ChatMessage.FromUser(prompt)
                        },
                        Model = OpenAI.ObjectModels.Models.Gpt_4o_mini_2024_07_18,
                        MaxTokens = 100,
                        Temperature = 0.3f
                    });

                if (completionResult.Successful)
                {
                    var result = completionResult.Choices.First().Message.Content.Trim();

                    _logger.LogInformation("AI decision for instruction {InstructionId}: {Result}",
                        instruction.Id, result);

                    return result.StartsWith("YES", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    _logger.LogError("OpenAI API error: {Error}", completionResult.Error?.Message);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AI decision for instruction {InstructionId}", instruction.Id);
                return false;
            }
        }

        /// <summary>
        /// Execute email instruction using AI + existing ToolExecutorService
        /// </summary>
        private async Task ExecuteEmailInstructionAsync(
            User user,
            OngoingInstruction instruction,
            EmailCache email)
        {
            try
            {
                // Get calendar events for context
                string calendarContext = await GetCalendarContextAsync(user, instruction);

                var systemPrompt = @"You are a professional AI assistant that executes instructions automatically.

You have access to these tools:
- send_email: Send emails
- create_calendar_event: Create calendar events

When executing an instruction, use function calling to perform the appropriate actions.
Be professional, clear, and helpful in all communications.";

                var userPrompt = $@"INSTRUCTION TO EXECUTE: {instruction.InstructionText}

EMAIL RECEIVED:
- From: {email.FromEmail}
- Subject: {email.Subject}
- Body: {email.Body}
{calendarContext}

USER INFO:
- Name: {email.FromName}
- Email: {user.Email}

TASK: Execute the instruction by calling the appropriate tools (send_email, create_calendar_event, etc.).";

                var completionResult = await _openAIService.ChatCompletion.CreateCompletion(
                    new ChatCompletionCreateRequest
                    {
                        Messages = new List<ChatMessage>
                        {
                            ChatMessage.FromSystem(systemPrompt),
                            ChatMessage.FromUser(userPrompt)
                        },
                        Tools = ToolDefinitionService.GetAllTools(),
                        Model = OpenAI.ObjectModels.Models.Chatgpt_4o_latest,
                        MaxTokens = 1000,
                        Temperature = 0.7f
                    });

                if (!completionResult.Successful)
                {
                    _logger.LogError("OpenAI API error: {Error}", completionResult.Error?.Message);
                    return;
                }

                var choice = completionResult.Choices.First();

                // Check if AI wants to call a tool
                if (choice.Message.ToolCalls != null && choice.Message.ToolCalls.Any())
                {
                    _logger.LogInformation("AI requested {Count} tool calls for instruction {InstructionId}",
                        choice.Message.ToolCalls.Count, instruction.Id);

                    // Execute each tool call using existing ToolExecutorService
                    foreach (var toolCall in choice.Message.ToolCalls)
                    {
                        if (toolCall.FunctionCall == null) continue;

                        var toolName = toolCall.FunctionCall.Name;
                        var arguments = toolCall.FunctionCall.Arguments;

                        _logger.LogInformation("Executing tool: {ToolName} with args: {Args}",
                            toolName, arguments);

                        // Use existing ToolExecutorService
                        var result = await _toolExecutor.ExecuteToolAsync(user.Id, toolName, arguments);

                        _logger.LogInformation("Tool execution result: {Result}", result);

                        // Log the activity
                        await LogActivityAsync(
                            user.Id,
                            instruction.Id,
                            GetActivityType(toolName),
                            $"Executed {toolName} for email from {email.FromEmail}",
                            arguments,
                            email.MessageId,
                            result.StartsWith("✅") ? "Success" : "Failed",
                            result.StartsWith("Error") ? result : null
                        );
                    }
                }
                else
                {
                    _logger.LogWarning("AI did not call any tools for instruction {InstructionId}. Response: {Response}",
                        instruction.Id, choice.Message.Content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing email instruction {InstructionId}", instruction.Id);

                await LogActivityAsync(
                    user.Id,
                    instruction.Id,
                    "Error",
                    $"Failed to execute instruction for email from {email.FromEmail}",
                    null,
                    email.MessageId,
                    "Failed",
                    ex.Message
                );
            }
        }

        /// <summary>
        /// Get calendar context for AI
        /// </summary>
        private async Task<string> GetCalendarContextAsync(User user, OngoingInstruction instruction)
        {
            // Only fetch calendar if instruction mentions it
            if (!instruction.InstructionText.ToLower().Contains("calendar") &&
                !instruction.InstructionText.ToLower().Contains("meeting") &&
                !instruction.InstructionText.ToLower().Contains("available") &&
                !instruction.InstructionText.ToLower().Contains("schedule"))
            {
                return "";
            }

            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);
            var events = await _context.CalendarEventCaches
                .Where(e => e.UserId == user.Id &&
                           e.StartTime >= today &&
                           e.StartTime <= tomorrow.AddDays(1))
                .OrderBy(e => e.StartTime)
                .ToListAsync();

            if (!events.Any())
            {
                return "\n\nCALENDAR: No events scheduled for today or tomorrow. Fully available during working hours (9 AM - 5 PM).";
            }

            var context = "\n\nCALENDAR EVENTS (Today & Tomorrow):\n";
            foreach (var evt in events)
            {
                context += $"- {evt.StartTime:MMM d, h:mm tt}: {evt.Summary}";
                if (evt.EndTime != null)
                {
                    context += $" (until {evt.EndTime.Value})";
                }
                context += "\n";
            }

            // Calculate available slots
            var availableSlots = FindAvailableTimeSlots(events, today, tomorrow);
            if (availableSlots.Any())
            {
                context += "\nAVAILABLE TIME SLOTS (9 AM - 5 PM):\n";
                foreach (var slot in availableSlots)
                {
                    context += $"- {slot}\n";
                }
            }

            return context;
        }

        /// <summary>
        /// Find available time slots between events
        /// </summary>
        private List<string> FindAvailableTimeSlots(List<CalendarEventCache> events, DateTime today, DateTime tomorrow)
        {
            var slots = new List<string>();
            var workingHoursStart = 9; // 9 AM
            var workingHoursEnd = 17; // 5 PM

            // Check today
            var todayStart = today.AddHours(workingHoursStart);
            var todayEnd = today.AddHours(workingHoursEnd);
            var todayEvents = events.Where(e => e.StartTime == today)
                .OrderBy(e => e.StartTime).ToList();

            var currentTime = DateTime.UtcNow > todayStart ? DateTime.UtcNow.AddHours(1) : todayStart;

            foreach (var evt in todayEvents)
            {
                var eventStart = evt.StartTime;
                if ((eventStart - currentTime).Value.TotalMinutes >= 60)
                {
                    slots.Add($"Today, {currentTime:h:mm tt} - {eventStart:h:mm tt}");
                }
                var eventEnd = evt.EndTime != null ? evt.EndTime : eventStart.Value.AddHours(1);
                currentTime = DateTime.Parse(eventEnd.ToString());
            }

            if ((todayEnd - currentTime).TotalMinutes >= 60)
            {
                slots.Add($"Today, {currentTime:h:mm tt} - {todayEnd:h:mm tt}");
            }

            // Check tomorrow
            var tomorrowStart = tomorrow.AddHours(workingHoursStart);
            var tomorrowEnd = tomorrow.AddHours(workingHoursEnd);
            var tomorrowEvents = events.Where(e => e.StartTime == tomorrow)
                .OrderBy(e => e.StartTime).ToList();

            currentTime = tomorrowStart;

            foreach (var evt in tomorrowEvents)
            {
                var eventStart = evt.StartTime;
                if ((eventStart - currentTime).Value.TotalMinutes >= 60)
                {
                    slots.Add($"Tomorrow, {currentTime:h:mm tt} - {eventStart:h:mm tt}");
                }
                var eventEnd = evt.EndTime != null ? evt.EndTime : eventStart.Value.AddHours(1);
                currentTime = DateTime.Parse(eventEnd.ToString()); ;
            }

            if ((tomorrowEnd - currentTime).TotalMinutes >= 60)
            {
                slots.Add($"Tomorrow, {currentTime:h:mm tt} - {tomorrowEnd:h:mm tt}");
            }

            return slots.Take(5).ToList();
        }

        /// <summary>
        /// Process new HubSpot contacts
        /// </summary>
        public async Task ProcessNewHubSpotContactsAsync(int userId)
        {
            try
            {
                _logger.LogInformation("Processing new HubSpot contacts for user {UserId}", userId);

                var user = await _context.Users.FindAsync(userId);
                if (user == null) return;

                // Get active HubSpot instructions
                var hubspotInstructions = await _context.OngoingInstructions
                    .Where(i => i.UserId == userId && i.IsActive &&
                               (i.TriggerType == "HubSpot" || i.TriggerType == "All"))
                    .OrderByDescending(i => i.Priority)
                    .ToListAsync();

                if (!hubspotInstructions.Any())
                {
                    _logger.LogInformation("No active HubSpot instructions for user {UserId}", userId);
                    return;
                }

                // Get recent contacts (last 5 minutes)
                var recentContacts = await _context.HubSpotContacts
                    .Where(c => c.UserId == userId &&
                               c.CreatedAt >= DateTime.UtcNow.AddMinutes(-5))
                    .OrderByDescending(c => c.CreatedAt)
                    .Take(10)
                    .ToListAsync();

                _logger.LogInformation("Found {Count} recent HubSpot contacts", recentContacts.Count);

                foreach (var contact in recentContacts)
                {
                    await ProcessHubSpotContactAsync(user, contact, hubspotInstructions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing HubSpot contacts for user {UserId}", userId);
            }
        }

        /// <summary>
        /// Process HubSpot contact with instructions using AI + ToolExecutorService
        /// </summary>
        private async Task ProcessHubSpotContactAsync(
            User user,
            HubSpotContact contact,
            List<OngoingInstruction> instructions)
        {
            try
            {
                foreach (var instruction in instructions)
                {
                    _logger.LogInformation("Executing HubSpot instruction {InstructionId} for contact {ContactEmail}",
                        instruction.Id, contact.Email);

                    var systemPrompt = @"You are a professional AI assistant that executes instructions automatically.

You have access to these tools:
- send_email: Send emails
- create_calendar_event: Create calendar events
- add_hubspot_note: Add notes to contacts

When executing an instruction for a new HubSpot contact, use function calling to perform the appropriate actions.";

                    var userPrompt = $@"INSTRUCTION TO EXECUTE: {instruction.InstructionText}

NEW HUBSPOT CONTACT:
- Name: {contact.FirstName} {contact.LastName}
- Email: {contact.Email}
- Company: {contact.Company}
- Job Title: {contact.JobTitle}

USER INFO:
- Name: {contact.FirstName} {contact.LastName}
- Email: {user.Email}

TASK: Execute the instruction by calling the appropriate tools.";

                    var completionResult = await _openAIService.ChatCompletion.CreateCompletion(
                        new ChatCompletionCreateRequest
                        {
                            Messages = new List<OpenAI.ObjectModels.RequestModels.ChatMessage>
                            {
                                OpenAI.ObjectModels.RequestModels.ChatMessage.FromSystem(systemPrompt),
                                OpenAI.ObjectModels.RequestModels.ChatMessage.FromUser(userPrompt)
                            },
                            Tools = ToolDefinitionService.GetAllTools(),
                            Model = OpenAI.ObjectModels.Models.Gpt_4,
                            MaxTokens = 1000,
                            Temperature = 0.7f
                        });

                    if (!completionResult.Successful)
                    {
                        _logger.LogError("OpenAI API error: {Error}", completionResult.Error?.Message);
                        continue;
                    }

                    var choice = completionResult.Choices.First();

                    // Execute tool calls
                    if (choice.Message.ToolCalls != null && choice.Message.ToolCalls.Any())
                    {
                        foreach (var toolCall in choice.Message.ToolCalls)
                        {
                            if (toolCall.FunctionCall == null) continue;

                            var result = await _toolExecutor.ExecuteToolAsync(
                                user.Id,
                                toolCall.FunctionCall.Name,
                                toolCall.FunctionCall.Arguments);

                            _logger.LogInformation("Tool {ToolName} result: {Result}",
                                toolCall.FunctionCall.Name, result);

                            await LogActivityAsync(
                                user.Id,
                                instruction.Id,
                                GetActivityType(toolCall.FunctionCall.Name),
                                $"Executed {toolCall.FunctionCall.Name} for new contact {contact.FirstName} {contact.LastName}",
                                toolCall.FunctionCall.Arguments,
                                contact.HubSpotId,
                                result.StartsWith("✅") ? "Success" : "Failed",
                                result.StartsWith("Error") ? result : null
                            );
                        }

                        // Update instruction stats
                        instruction.ExecutionCount++;
                        instruction.LastExecutedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing HubSpot contact {ContactId}", contact.Id);
            }
        }

        /// <summary>
        /// Map tool name to activity type
        /// </summary>
        private string GetActivityType(string toolName)
        {
            return toolName switch
            {
                "send_email" => "EmailSent",
                "create_calendar_event" => "CalendarEventCreated",
                "create_hubspot_contact" => "HubSpotContactCreated",
                "update_hubspot_deal" => "HubSpotDealUpdated",
                "add_hubspot_note" => "HubSpotNoteAdded",
                _ => "ToolExecuted"
            };
        }

        /// <summary>
        /// Log agent activity
        /// </summary>
        private async Task LogActivityAsync(
            int userId,
            int? instructionId,
            string activityType,
            string description,
            string? details,
            string? triggeredBy,
            string status,
            string? errorMessage)
        {
            try
            {
                var activity = new AgentActivity
                {
                    UserId = userId,
                    OngoingInstructionId = instructionId,
                    ActivityType = activityType,
                    Description = description,
                    Details = details,
                    TriggeredBy = triggeredBy,
                    Status = status,
                    ErrorMessage = errorMessage,
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                };

                _context.AgentActivities.Add(activity);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging activity");
            }
        }
    }
}