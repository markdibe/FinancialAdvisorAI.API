using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels.SharedModels;

namespace FinancialAdvisorAI.API.Services
{
    /// <summary>
    /// Defines tools/functions that the AI can use.
    /// This is a NEW service for tool calling functionality.
    /// </summary>
    public static class ToolDefinitionService
    {
        /// <summary>
        /// Returns email-related tools for GPT-4 function calling
        /// </summary>
        public static List<ToolDefinition> GetEmailTools()
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Type = "function",
                    Function = new FunctionDefinition
                    {
                        Name = "send_email",
                        Description = "Send an email to a recipient. Use this when the user explicitly asks to send, email, or message someone.",
                        Parameters = PropertyDefinition.DefineObject(
                            new Dictionary<string, PropertyDefinition>
                            {
                                { "to", PropertyDefinition.DefineString("Email address of the recipient (e.g., john@example.com). Extract this from the user's request or look it up from HubSpot contacts or previous emails.") },
                                { "subject", PropertyDefinition.DefineString("Subject line of the email. Generate an appropriate subject based on the email content.") },
                                { "body", PropertyDefinition.DefineString("Body content of the email. Should be professional and clear.") }
                            },
                            new List<string> { "to", "subject", "body" },
                            null,
                            null,
                            null
                        )
                    }
                }
            };
        }

        /// <summary>
        /// Returns calendar-related tools for GPT-4 function calling
        /// </summary>
        public static List<ToolDefinition> GetCalendarTools()
        {
            return new List<ToolDefinition>
    {
        new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "create_calendar_event",
                Description = "Create a calendar event/meeting. Use this when the user asks to schedule, book, or create a meeting or appointment.",
                Parameters = PropertyDefinition.DefineObject(
                    new Dictionary<string, PropertyDefinition>
                    {
                        {
                            "summary",
                            PropertyDefinition.DefineString("Title/subject of the meeting (e.g., 'Q4 Review Meeting', 'Call with John')")
                        },
                        {
                            "description",
                            PropertyDefinition.DefineString("Optional description or agenda for the meeting")
                        },
                        {
                            "start_time",
                            PropertyDefinition.DefineString("Start time in ISO 8601 format (e.g., '2024-10-20T14:00:00'). Parse from user's natural language like 'tomorrow at 2pm', 'next Tuesday at 10am', 'Friday at 3:30pm'.")
                        },
                        {
                            "end_time",
                            PropertyDefinition.DefineString("End time in ISO 8601 format. If not specified, default to 30 minutes after start time.")
                        },
                        {
                            "attendees",
                            PropertyDefinition.DefineString("Comma-separated list of attendee email addresses (e.g., 'john@example.com,sara@example.com'). Look up from HubSpot contacts or previous emails if only names are provided.")
                        },
                        {
                            "location",
                            PropertyDefinition.DefineString("Optional location for the meeting (e.g., 'Conference Room A', 'Zoom', '123 Main St')")
                        }
                    },
                    new List<string> { "summary", "start_time" }, // Only summary and start_time are required
                    null,
                    null,
                    null
                )
            }
        }
    };
        }

        /// <summary>
        /// Returns HubSpot-related tools for GPT-4 function calling
        /// </summary>
        public static List<ToolDefinition> GetHubSpotTools()
        {
            return new List<ToolDefinition>
    {
        // Tool 1: Create Contact
        new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "create_hubspot_contact",
                Description = "Create a new contact in HubSpot CRM. Use this when the user asks to add, create, or save a new contact.",
                Parameters = PropertyDefinition.DefineObject(
                    new Dictionary<string, PropertyDefinition>
                    {
                        {
                            "email",
                            PropertyDefinition.DefineString("Email address of the contact (required, e.g., 'john@example.com')")
                        },
                        {
                            "first_name",
                            PropertyDefinition.DefineString("First name of the contact")
                        },
                        {
                            "last_name",
                            PropertyDefinition.DefineString("Last name of the contact")
                        },
                        {
                            "company",
                            PropertyDefinition.DefineString("Company name where the contact works")
                        },
                        {
                            "job_title",
                            PropertyDefinition.DefineString("Job title or role of the contact")
                        },
                        {
                            "phone",
                            PropertyDefinition.DefineString("Phone number of the contact")
                        },
                        {
                            "lifecycle_stage",
                            PropertyDefinition.DefineString("Lifecycle stage (e.g., 'lead', 'opportunity', 'customer')")
                        }
                    },
                    new List<string> { "email" }, // Only email is required
                    null,
                    null,
                    null
                )
            }
        },

        // Tool 2: Update Deal
        new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "update_hubspot_deal",
                Description = "Update an existing deal in HubSpot. Use this when the user asks to update, change, or modify a deal's status or information.",
                Parameters = PropertyDefinition.DefineObject(
                    new Dictionary<string, PropertyDefinition>
                    {
                        {
                            "deal_name",
                            PropertyDefinition.DefineString("Name or title of the deal to update (used to search for the deal)")
                        },
                        {
                            "deal_stage",
                            PropertyDefinition.DefineString("New deal stage (e.g., 'qualifiedtobuy', 'presentationscheduled', 'decisionmakerboughtin', 'contractsent', 'closedwon', 'closedlost')")
                        },
                        {
                            "amount",
                            PropertyDefinition.DefineString("Deal amount/value as a number (e.g., '50000')")
                        },
                        {
                            "close_date",
                            PropertyDefinition.DefineString("Expected close date in ISO format (e.g., '2024-12-31')")
                        },
                        {
                            "priority",
                            PropertyDefinition.DefineString("Deal priority (e.g., 'high', 'medium', 'low')")
                        }
                    },
                    new List<string> { "deal_name" }, // deal_name is required to find the deal
                    null,
                    null,
                    null
                )
            }
        },

        // Tool 3: Add Note
        new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "add_hubspot_note",
                Description = "Add a note to a contact in HubSpot. Use this when the user asks to log, record, or add information about a contact.",
                Parameters = PropertyDefinition.DefineObject(
                    new Dictionary<string, PropertyDefinition>
                    {
                        {
                            "contact_email",
                            PropertyDefinition.DefineString("Email address of the contact to add the note to. Look this up from previous context if only a name is provided.")
                        },
                        {
                            "note",
                            PropertyDefinition.DefineString("The note content to add to the contact")
                        }
                    },
                    new List<string> { "contact_email", "note" },
                    null,
                    null,
                    null
                )
            }
        }
    };
        }



        /// <summary>
        /// Get all available tools (currently just email, but will expand)
        /// </summary>
        public static List<ToolDefinition> GetAllTools()
        {
            var tools = new List<ToolDefinition>();
            tools.AddRange(GetEmailTools());
            tools.AddRange(GetCalendarTools());
            tools.AddRange(GetHubSpotTools());
            // Future: Add calendar tools, HubSpot tools, etc.
            return tools;
        }
    }
}