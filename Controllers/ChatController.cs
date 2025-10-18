using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinancialAdvisorAI.API.Services;
using FinancialAdvisorAI.API.Repositories;
using FinancialAdvisorAI.API.Models;
using FinancialAdvisorAI.API.Models.DTOs;
using OpenAI.ObjectModels.RequestModels;

// Alias to avoid confusion between our ChatMessage entity and OpenAI's ChatMessage
using DbChatMessage = FinancialAdvisorAI.API.Models.ChatMessage;
using OpenAIChatMessage = OpenAI.ObjectModels.RequestModels.ChatMessage;

namespace FinancialAdvisorAI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly AiChatService _aiChatService;
        private readonly AppDbContext _context;

        public ChatController(AiChatService aiChatService, AppDbContext context)
        {
            _aiChatService = aiChatService;
            _context = context;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            try
            {
                // Verify user exists
                var user = await _context.Users.FindAsync(request.UserId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                // Save user message
                var userMessage = new DbChatMessage
                {
                    UserId = request.UserId,
                    Content = request.Message,
                    Role = "user",
                    CreatedAt = DateTime.UtcNow
                };
                _context.ChatMessages.Add(userMessage);
                await _context.SaveChangesAsync();

                // Get conversation history (last 10 messages for context)
                var recentMessages = await _context.ChatMessages
                    .Where(m => m.UserId == request.UserId)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(10)
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync();

                // Convert to OpenAI format
                var conversationHistory = recentMessages
                    .Take(recentMessages.Count - 1) // Exclude the message we just added
                    .Select(m => m.Role == "user"
                        ? OpenAIChatMessage.FromUser(m.Content)
                        : OpenAIChatMessage.FromAssistant(m.Content))
                    .ToList();

                // Get AI response WITH email context
                var aiResponse = await _aiChatService.GetResponseWithToolsAsync(
                    request.UserId,
                    request.Message,
                    conversationHistory
                );

                // Save AI response
                var assistantMessage = new DbChatMessage
                {
                    UserId = request.UserId,
                    Content = aiResponse,
                    Role = "assistant",
                    CreatedAt = DateTime.UtcNow
                };
                _context.ChatMessages.Add(assistantMessage);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    userMessage = new ChatMessageResponse
                    {
                        Id = userMessage.Id,
                        Content = userMessage.Content,
                        Role = userMessage.Role,
                        CreatedAt = userMessage.CreatedAt
                    },
                    assistantMessage = new ChatMessageResponse
                    {
                        Id = assistantMessage.Id,
                        Content = assistantMessage.Content,
                        Role = assistantMessage.Role,
                        CreatedAt = assistantMessage.CreatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpGet("history/{userId}")]
        public async Task<IActionResult> GetChatHistory(int userId, [FromQuery] int limit = 50)
        {
            try
            {
                var messages = await _context.ChatMessages
                    .Where(m => m.UserId == userId)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(limit)
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => new ChatMessageResponse
                    {
                        Id = m.Id,
                        Content = m.Content,
                        Role = m.Role,
                        CreatedAt = m.CreatedAt
                    })
                    .ToListAsync();

                return Ok(new ChatHistoryResponse
                {
                    Messages = messages,
                    TotalCount = messages.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("clear/{userId}")]
        public async Task<IActionResult> ClearChatHistory(int userId)
        {
            try
            {
                var messages = await _context.ChatMessages
                    .Where(m => m.UserId == userId)
                    .ToListAsync();

                _context.ChatMessages.RemoveRange(messages);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Chat history cleared",
                    deletedCount = messages.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}