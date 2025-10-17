using FinancialAdvisorAI.API.Models;
using FinancialAdvisorAI.API.Repositories;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;

namespace FinancialAdvisorAI.API.Services
{
    public class GoogleAuthService
    {

        private readonly IConfiguration _configuration;
        private readonly AppDbContext _context;
        private readonly string[] _scopes = new[]
        {
            "https://www.googleapis.com/auth/gmail.readonly",
            "https://www.googleapis.com/auth/gmail.send",
            "https://www.googleapis.com/auth/gmail.modify",
            "https://www.googleapis.com/auth/calendar",
            "https://www.googleapis.com/auth/calendar.events",
            "https://www.googleapis.com/auth/userinfo.email",
            "https://www.googleapis.com/auth/userinfo.profile"
        };
        public GoogleAuthService(IConfiguration configuration, AppDbContext context)
        {
            _configuration = configuration;
            _context = context;
        }

        public string GetAuthorizationUrl(string state)
        {
            var clientId = _configuration["Google:ClientId"];
            var redirectUri = _configuration["Google:RedirectUri"];

            var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                $"client_id={Uri.EscapeDataString(clientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                $"&response_type=code" +
                $"&scope={Uri.EscapeDataString(string.Join(" ", _scopes))}" +
                $"&access_type=offline" +
                $"&prompt=consent" +
                $"&state={Uri.EscapeDataString(state)}";

            return authUrl;
        }

        public async Task<TokenResponse> ExchangeCodeForTokenAsync(string code)
        {
            var clientId = _configuration["Google:ClientId"];
            var clientSecret = _configuration["Google:ClientSecret"];
            var redirectUri = _configuration["Google:RedirectUri"];

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                },
                Scopes = _scopes
            });

            var token = await flow.ExchangeCodeForTokenAsync(
                "user",
                code,
                redirectUri,
                CancellationToken.None
            );

            return token;
        }

        public async Task<UserCredential> GetUserCredentialAsync(User user)
        {
            var clientId = _configuration["Google:ClientId"];
            var clientSecret = _configuration["Google:ClientSecret"];

            var token = new TokenResponse
            {
                AccessToken = user.GoogleAccessToken,
                RefreshToken = user.GoogleRefreshToken,
                ExpiresInSeconds = (long)(user.GoogleTokenExpiry - DateTime.UtcNow)?.TotalSeconds,
                IssuedUtc = DateTime.UtcNow
            };

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                },
                Scopes = _scopes,
                DataStore = new NullDataStore()
            });

            var credential = new UserCredential(flow, "user", token);

            // Refresh token if expired
            if (token.IsExpired(Google.Apis.Util.SystemClock.Default))
            {
                await credential.RefreshTokenAsync(CancellationToken.None);

                // Update user tokens in database
                user.GoogleAccessToken = credential.Token.AccessToken;
                user.GoogleTokenExpiry = DateTime.UtcNow.AddSeconds(credential.Token.ExpiresInSeconds ?? 3600);
                await _context.SaveChangesAsync();
            }

            return credential;
        }

        public async Task<string> GetUserEmailFromTokenAsync(string accessToken)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            var response = await httpClient.GetAsync("https://www.googleapis.com/oauth2/v2/userinfo");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var userInfo = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            return userInfo?["email"]?.ToString() ?? string.Empty;
        }


    }
}
