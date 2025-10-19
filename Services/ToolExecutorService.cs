using FinancialAdvisorAI.API.Repositories;
using Microsoft.EntityFrameworkCore;
using RestSharp;
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
        private readonly EventService _eventService;
        private readonly HubSpotService _hubSpotService;

        public ToolExecutorService(
            EmailService emailService,
            AppDbContext context,
            EventService eventService,
            HubSpotService hubSpotService,
            ILogger<ToolExecutorService> logger)
        {
            _emailService = emailService;
            _context = context;
            _logger = logger;
            _eventService = eventService;
            _hubSpotService = hubSpotService;
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
                    
                    case "create_calendar_event":  
                        return await ExecuteCreateCalendarEventAsync(userId, arguments);
                    
                    case "create_hubspot_contact":  
                        return await ExecuteCreateHubSpotContactAsync(userId, arguments);

                    case "update_hubspot_deal":  
                        return await ExecuteUpdateHubSpotDealAsync(userId, arguments);

                    case "add_hubspot_note":  
                        return await ExecuteAddHubSpotNoteAsync(userId, arguments);

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

        /// <summary>
        /// Execute the create_calendar_event tool
        /// </summary>
        private async Task<string> ExecuteCreateCalendarEventAsync(
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

                // Extract required arguments
                if (!arguments.TryGetValue("summary", out var summaryElement))
                {
                    return "Error: Missing meeting title/summary";
                }

                if (!arguments.TryGetValue("start_time", out var startTimeElement))
                {
                    return "Error: Missing start time";
                }

                var summary = summaryElement.GetString();
                var startTimeStr = startTimeElement.GetString();

                // Validate required fields
                if (string.IsNullOrWhiteSpace(summary))
                {
                    return "Error: Meeting title/summary is empty";
                }

                if (string.IsNullOrWhiteSpace(startTimeStr))
                {
                    return "Error: Start time is empty";
                }

                // Parse start time
                if (!DateTime.TryParse(startTimeStr, out var startTime))
                {
                    return $"Error: Invalid start time format: {startTimeStr}. Please use ISO 8601 format (e.g., '2024-10-20T14:00:00')";
                }

                // Extract optional arguments
                string? description = null;
                if (arguments.TryGetValue("description", out var descElement))
                {
                    description = descElement.GetString();
                }

                // Parse end time or default to 30 minutes after start
                DateTime endTime;
                if (arguments.TryGetValue("end_time", out var endTimeElement))
                {
                    var endTimeStr = endTimeElement.GetString();
                    if (!DateTime.TryParse(endTimeStr, out endTime))
                    {
                        endTime = startTime.AddMinutes(30); // Default if parse fails
                        _logger.LogWarning("Could not parse end time '{EndTime}', using 30-minute default", endTimeStr);
                    }
                }
                else
                {
                    endTime = startTime.AddMinutes(30); // Default duration
                }

                // Extract attendees
                List<string>? attendeeEmails = null;
                if (arguments.TryGetValue("attendees", out var attendeesElement))
                {
                    var attendeesStr = attendeesElement.GetString();
                    if (!string.IsNullOrWhiteSpace(attendeesStr))
                    {
                        attendeeEmails = attendeesStr
                            .Split(',')
                            .Select(e => e.Trim())
                            .Where(e => !string.IsNullOrWhiteSpace(e))
                            .ToList();
                    }
                }

                // Extract location
                string? location = null;
                if (arguments.TryGetValue("location", out var locationElement))
                {
                    location = locationElement.GetString();
                }

                _logger.LogInformation(
                    "Creating calendar event: '{Summary}' from {Start} to {End}",
                    summary, startTime, endTime);

                // Create the event using EventService

                var calendarService = await _eventService.GetCalendarServiceAsync(user);

                // Build the event
                var newEvent = new Google.Apis.Calendar.v3.Data.Event
                {
                    Summary = summary,
                    Description = description,
                    Location = location,
                    Start = new Google.Apis.Calendar.v3.Data.EventDateTime
                    {
                        DateTime = startTime,
                        TimeZone = "UTC"
                    },
                    End = new Google.Apis.Calendar.v3.Data.EventDateTime
                    {
                        DateTime = endTime,
                        TimeZone = "UTC"
                    }
                };

                // Add attendees if provided
                if (attendeeEmails != null && attendeeEmails.Any())
                {
                    newEvent.Attendees = attendeeEmails
                        .Select(email => new Google.Apis.Calendar.v3.Data.EventAttendee { Email = email })
                        .ToList();
                }

                // Insert the event
                var request = calendarService.Events.Insert(newEvent, "primary");
                request.SendUpdates = Google.Apis.Calendar.v3.EventsResource.InsertRequest.SendUpdatesEnum.All;
                var createdEvent = await request.ExecuteAsync();

                // Build success message
                var successMessage = $"✅ Meeting '{summary}' created successfully!\n" +
                                   $"📅 Start: {startTime:MMM dd, yyyy h:mm tt}\n" +
                                   $"📅 End: {endTime:MMM dd, yyyy h:mm tt}";

                if (!string.IsNullOrEmpty(location))
                {
                    successMessage += $"\n📍 Location: {location}";
                }

                if (attendeeEmails != null && attendeeEmails.Any())
                {
                    successMessage += $"\n👥 Attendees: {string.Join(", ", attendeeEmails)}\n" +
                                    $"✉️ Calendar invites sent to all attendees";
                }

                successMessage += $"\n🔗 Event ID: {createdEvent.Id}";

                return successMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ExecuteCreateCalendarEventAsync");
                return $"Error creating calendar event: {ex.Message}";
            }
        }

        /// <summary>
        /// Execute the create_hubspot_contact tool
        /// </summary>
        private async Task<string> ExecuteCreateHubSpotContactAsync(
            int userId,
            Dictionary<string, JsonElement> arguments)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return "Error: User not found";
                }

                // Check if HubSpot is connected
                if (string.IsNullOrEmpty(user.HubspotAccessToken))
                {
                    return "Error: HubSpot is not connected. Please connect your HubSpot account first.";
                }

                // Extract required email
                if (!arguments.TryGetValue("email", out var emailElement))
                {
                    return "Error: Email address is required to create a contact";
                }

                var email = emailElement.GetString();
                if (string.IsNullOrWhiteSpace(email))
                {
                    return "Error: Email address cannot be empty";
                }

                // Extract optional fields
                var firstName = arguments.TryGetValue("first_name", out var fnElement) ? fnElement.GetString() : null;
                var lastName = arguments.TryGetValue("last_name", out var lnElement) ? lnElement.GetString() : null;
                var company = arguments.TryGetValue("company", out var compElement) ? compElement.GetString() : null;
                var jobTitle = arguments.TryGetValue("job_title", out var jtElement) ? jtElement.GetString() : null;
                var phone = arguments.TryGetValue("phone", out var phElement) ? phElement.GetString() : null;
                var lifecycleStage = arguments.TryGetValue("lifecycle_stage", out var lsElement) ? lsElement.GetString() : null;

                _logger.LogInformation("Creating HubSpot contact for email: {Email}", email);

                // Use HubSpot API to create contact
                var accessToken = await _hubSpotService.GetValidAccessTokenAsync(user);
                var client = new RestSharp.RestClient("https://api.hubapi.com");
                var request = new RestSharp.RestRequest("/crm/v3/objects/contacts", RestSharp.Method.Post);
                request.AddHeader("Authorization", $"Bearer {accessToken}");
                request.AddHeader("Content-Type", "application/json");

                // Build properties object
                var properties = new Dictionary<string, string>
        {
            { "email", email }
        };

                if (!string.IsNullOrWhiteSpace(firstName)) properties["firstname"] = firstName;
                if (!string.IsNullOrWhiteSpace(lastName)) properties["lastname"] = lastName;
                if (!string.IsNullOrWhiteSpace(company)) properties["company"] = company;
                if (!string.IsNullOrWhiteSpace(jobTitle)) properties["jobtitle"] = jobTitle;
                if (!string.IsNullOrWhiteSpace(phone)) properties["phone"] = phone;
                if (!string.IsNullOrWhiteSpace(lifecycleStage)) properties["lifecyclestage"] = lifecycleStage;

                var body = new { properties };
                request.AddJsonBody(body);

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                {
                    _logger.LogError("Failed to create HubSpot contact: {Error}", response.Content);
                    return $"Error creating HubSpot contact: {response.ErrorMessage ?? response.Content}";
                }

                var contactName = !string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName)
                    ? $"{firstName} {lastName}".Trim()
                    : email;

                var successMessage = $"✅ Contact '{contactName}' created successfully in HubSpot!\n";
                successMessage += $"📧 Email: {email}";

                if (!string.IsNullOrWhiteSpace(company))
                    successMessage += $"\n🏢 Company: {company}";

                if (!string.IsNullOrWhiteSpace(jobTitle))
                    successMessage += $"\n💼 Title: {jobTitle}";

                return successMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ExecuteCreateHubSpotContactAsync");
                return $"Error creating HubSpot contact: {ex.Message}";
            }
        }

        /// <summary>
        /// Execute the update_hubspot_deal tool
        /// </summary>
        private async Task<string> ExecuteUpdateHubSpotDealAsync(
            int userId,
            Dictionary<string, JsonElement> arguments)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return "Error: User not found";
                }

                if (string.IsNullOrEmpty(user.HubspotAccessToken))
                {
                    return "Error: HubSpot is not connected";
                }

                // Extract deal name
                if (!arguments.TryGetValue("deal_name", out var dealNameElement))
                {
                    return "Error: Deal name is required";
                }

                var dealName = dealNameElement.GetString();
                if (string.IsNullOrWhiteSpace(dealName))
                {
                    return "Error: Deal name cannot be empty";
                }

                // Find the deal in database
                var deal = await _context.HubSpotDeals
                    .FirstOrDefaultAsync(d => d.UserId == userId &&
                        d.DealName != null &&
                        d.DealName.ToLower().Contains(dealName.ToLower()));

                if (deal == null)
                {
                    return $"Error: Could not find a deal matching '{dealName}'. Please check the deal name and try again.";
                }

                _logger.LogInformation("Updating HubSpot deal: {DealName} (ID: {DealId})", deal.DealName, deal.HubSpotId);

                // Extract update fields
                var dealStage = arguments.TryGetValue("deal_stage", out var dsElement) ? dsElement.GetString() : null;
                var amount = arguments.TryGetValue("amount", out var amElement) ? amElement.GetString() : null;
                var closeDate = arguments.TryGetValue("close_date", out var cdElement) ? cdElement.GetString() : null;
                var priority = arguments.TryGetValue("priority", out var prElement) ? prElement.GetString() : null;

                // Build update properties
                var properties = new Dictionary<string, string>();

                if (!string.IsNullOrWhiteSpace(dealStage)) properties["dealstage"] = dealStage;
                if (!string.IsNullOrWhiteSpace(amount)) properties["amount"] = amount;
                if (!string.IsNullOrWhiteSpace(closeDate)) properties["closedate"] = closeDate;
                if (!string.IsNullOrWhiteSpace(priority)) properties["hs_priority"] = priority;

                if (properties.Count == 0)
                {
                    return "Error: No update fields provided. Please specify what to update (stage, amount, close date, or priority).";
                }

                // Update via HubSpot API
                var accessToken = await _hubSpotService.GetValidAccessTokenAsync(user);
                var client = new RestSharp.RestClient("https://api.hubapi.com");
                var request = new RestSharp.RestRequest($"/crm/v3/objects/deals/{deal.HubSpotId}", RestSharp.Method.Patch);
                request.AddHeader("Authorization", $"Bearer {accessToken}");
                request.AddHeader("Content-Type", "application/json");

                var body = new { properties };
                request.AddJsonBody(body);

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                {
                    _logger.LogError("Failed to update HubSpot deal: {Error}", response.Content);
                    return $"Error updating deal: {response.ErrorMessage ?? response.Content}";
                }

                // Update local database
                if (!string.IsNullOrWhiteSpace(dealStage)) deal.DealStage = dealStage;
                if (!string.IsNullOrWhiteSpace(amount) && decimal.TryParse(amount, out var amountValue))
                    deal.Amount = amountValue;
                if (!string.IsNullOrWhiteSpace(closeDate) && DateTime.TryParse(closeDate, out var closeDateValue))
                    deal.CloseDate = closeDateValue;
                if (!string.IsNullOrWhiteSpace(priority)) deal.Priority = priority;

                deal.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                var successMessage = $"✅ Deal '{deal.DealName}' updated successfully!\n";

                if (!string.IsNullOrWhiteSpace(dealStage))
                    successMessage += $"📊 Stage: {dealStage}\n";

                if (!string.IsNullOrWhiteSpace(amount))
                    successMessage += $"💰 Amount: ${amount}\n";

                if (!string.IsNullOrWhiteSpace(priority))
                    successMessage += $"⭐ Priority: {priority}";

                return successMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ExecuteUpdateHubSpotDealAsync");
                return $"Error updating deal: {ex.Message}";
            }
        }

        /// <summary>
        /// Execute the add_hubspot_note tool
        /// </summary>
        private async Task<string> ExecuteAddHubSpotNoteAsync(
            int userId,
            Dictionary<string, JsonElement> arguments)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return "Error: User not found";
                }

                if (string.IsNullOrEmpty(user.HubspotAccessToken))
                {
                    return "Error: HubSpot is not connected";
                }

                // Extract contact email
                if (!arguments.TryGetValue("contact_email", out var emailElement))
                {
                    return "Error: Contact email is required";
                }

                var contactEmail = emailElement.GetString();
                if (string.IsNullOrWhiteSpace(contactEmail))
                {
                    return "Error: Contact email cannot be empty";
                }

                // Extract note
                if (!arguments.TryGetValue("note", out var noteElement))
                {
                    return "Error: Note content is required";
                }

                var note = noteElement.GetString();
                if (string.IsNullOrWhiteSpace(note))
                {
                    return "Error: Note content cannot be empty";
                }

                // Find contact in database
                var contact = await _context.HubSpotContacts
                    .FirstOrDefaultAsync(c => c.UserId == userId &&
                        c.Email != null &&
                        c.Email.ToLower() == contactEmail.ToLower());

                if (contact == null)
                {
                    return $"Error: Could not find contact with email '{contactEmail}'. Make sure the contact exists in HubSpot.";
                }

                _logger.LogInformation("Adding note to HubSpot contact: {Email} (ID: {ContactId})",
                    contactEmail, contact.HubSpotId);

                // Add note via HubSpot API
                var accessToken = await _hubSpotService.GetValidAccessTokenAsync(user);
                var client = new RestSharp.RestClient("https://api.hubapi.com");
                var request = new RestSharp.RestRequest("/crm/v3/objects/notes", RestSharp.Method.Post);
                request.AddHeader("Authorization", $"Bearer {accessToken}");
                request.AddHeader("Content-Type", "application/json");

                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var body = new
                {
                    properties = new
                    {
                        hs_timestamp = timestamp.ToString(),
                        hs_note_body = note
                    },
                    associations = new[]
                    {
                new
                {
                    to = new { id = contact.HubSpotId },
                    types = new[]
                    {
                        new
                        {
                            associationCategory = "HUBSPOT_DEFINED",
                            associationTypeId = 202  // Note to Contact association
                        }
                    }
                }
            }
                };

                request.AddJsonBody(body);

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                {
                    _logger.LogError("Failed to add HubSpot note: {Error}", response.Content);
                    return $"Error adding note: {response.ErrorMessage ?? response.Content}";
                }

                var contactName = !string.IsNullOrWhiteSpace(contact.FirstName) || !string.IsNullOrWhiteSpace(contact.LastName)
                    ? $"{contact.FirstName} {contact.LastName}".Trim()
                    : contactEmail;

                return $"✅ Note added to {contactName}'s HubSpot contact!\n📝 Note: {note}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ExecuteAddHubSpotNoteAsync");
                return $"Error adding note: {ex.Message}";
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