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
        /// Get all available tools (currently just email, but will expand)
        /// </summary>
        public static List<ToolDefinition> GetAllTools()
        {
            var tools = new List<ToolDefinition>();
            tools.AddRange(GetEmailTools());
            // Future: Add calendar tools, HubSpot tools, etc.
            return tools;
        }
    }
}