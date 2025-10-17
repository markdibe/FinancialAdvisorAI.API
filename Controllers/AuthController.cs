using FinancialAdvisorAI.API.Models;
using FinancialAdvisorAI.API.Models.DTOs;
using FinancialAdvisorAI.API.Repositories;
using FinancialAdvisorAI.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialAdvisorAI.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly GoogleAuthService _googleAuthService;
        private readonly AppDbContext _context;

        public AuthController(GoogleAuthService googleAuthService, AppDbContext context)
        {
            _googleAuthService = googleAuthService;
            _context = context;
        }


        [HttpGet("google/login")]
        public IActionResult GoogleLogin()
        {
            var state = Guid.NewGuid().ToString();
            var authUrl = _googleAuthService.GetAuthorizationUrl(state);

            return Ok(new { authUrl, state });
        }

        [HttpPost("google/callback")]
        public async Task<IActionResult> GoogleCallback([FromBody] GoogleCallbackRequest request)
        {
            try
            {
                // Exchange authorization code for tokens
                var token = await _googleAuthService.ExchangeCodeForTokenAsync(request.Code);

                // Get user email from token
                var email = await _googleAuthService.GetUserEmailFromTokenAsync(token.AccessToken);

                // Find or create user
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

                if (user == null)
                {
                    user = new User
                    {
                        Email = email,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Users.Add(user);
                }

                // Update tokens
                user.GoogleAccessToken = token.AccessToken;
                user.GoogleRefreshToken = token.RefreshToken;
                user.GoogleTokenExpiry = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds ?? 3600);
                user.LastLoginAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    userId = user.Id,
                    email = user.Email,
                    message = "Successfully authenticated with Google"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUser(int userId)
        {
            var user = await _context.Users.FindAsync(userId);

            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            return Ok(new
            {
                id = user.Id,
                email = user.Email,
                hasGoogleAuth = !string.IsNullOrEmpty(user.GoogleAccessToken),
                hasHubspotAuth = !string.IsNullOrEmpty(user.HubspotAccessToken),
                lastLogin = user.LastLoginAt
            });
        }


        [HttpPost("logout/{userId}")]
        public async Task<IActionResult> Logout(int userId)
        {
            var user = await _context.Users.FindAsync(userId);

            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            // Clear tokens (optional - you might want to keep them for background tasks)
            // For now, we'll just update last login
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Logged out successfully" });
        }
    }


}
}
