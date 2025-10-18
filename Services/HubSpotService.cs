using FinancialAdvisorAI.API.Models;
using FinancialAdvisorAI.API.Repositories;
using Microsoft.EntityFrameworkCore;
using RestSharp;
using System.Text.Json;

namespace FinancialAdvisorAI.API.Services
{
    public class HubSpotService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<HubSpotService> _logger;
        private const string BASE_URL = "https://api.hubapi.com";

        public HubSpotService(
            AppDbContext context,
            IConfiguration configuration,
            ILogger<HubSpotService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public string GetAuthorizationUrl(string state)
        {
            var clientId = _configuration["Hubspot:ClientId"];
            var redirectUri = _configuration["Hubspot:RedirectUri"];
            var scopes = _configuration["Hubspot:Scopes"];

            return $"https://app.hubspot.com/oauth/authorize?" +
                   $"client_id={Uri.EscapeDataString(clientId)}" +
                   $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                   $"&scope={Uri.EscapeDataString(scopes)}" +
                   $"&state={state}";
        }

        public async Task ExchangeCodeForTokenAsync(string code, User user)
        {
            var client = new RestClient("https://api.hubapi.com");
            var request = new RestRequest("/oauth/v1/token", Method.Post);

            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddParameter("grant_type", "authorization_code");
            request.AddParameter("client_id", _configuration["Hubspot:ClientId"]);
            request.AddParameter("client_secret", _configuration["Hubspot:ClientSecret"]);
            request.AddParameter("redirect_uri", _configuration["Hubspot:RedirectUri"]);
            request.AddParameter("code", code);

            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                throw new Exception($"Failed to exchange code: {response.Content}");
            }

            var tokenData = JsonSerializer.Deserialize<JsonElement>(response.Content!);

            user.HubspotAccessToken = tokenData.GetProperty("access_token").GetString();
            user.HubspotRefreshToken = tokenData.GetProperty("refresh_token").GetString();
            user.HubspotTokenExpiry = DateTime.UtcNow.AddSeconds(tokenData.GetProperty("expires_in").GetInt32());

            await _context.SaveChangesAsync();
        }

        private async Task<string> GetValidAccessTokenAsync(User user)
        {
            if (string.IsNullOrEmpty(user.HubspotAccessToken))
            {
                throw new Exception("No HubSpot token found");
            }

            if (user.HubspotTokenExpiry <= DateTime.UtcNow.AddMinutes(5))
            {
                return await RefreshAccessTokenAsync(user);
            }

            return user.HubspotAccessToken;
        }

        private async Task<string> RefreshAccessTokenAsync(User user)
        {
            var client = new RestClient("https://api.hubapi.com");
            var request = new RestRequest("/oauth/v1/token", Method.Post);

            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddParameter("grant_type", "refresh_token");
            request.AddParameter("client_id", _configuration["Hubspot:ClientId"]);
            request.AddParameter("client_secret", _configuration["Hubspot:ClientSecret"]);
            request.AddParameter("refresh_token", user.HubspotRefreshToken);

            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                throw new Exception($"Failed to refresh token: {response.Content}");
            }

            var tokenData = JsonSerializer.Deserialize<JsonElement>(response.Content!);

            user.HubspotAccessToken = tokenData.GetProperty("access_token").GetString();
            user.HubspotRefreshToken = tokenData.GetProperty("refresh_token").GetString();
            user.HubspotTokenExpiry = DateTime.UtcNow.AddSeconds(tokenData.GetProperty("expires_in").GetInt32());

            await _context.SaveChangesAsync();

            return user.HubspotAccessToken!;
        }

        public async Task SyncContactsAsync(User user)
        {
            var accessToken = await GetValidAccessTokenAsync(user);
            var client = new RestClient(BASE_URL);

            var allContacts = new List<JsonElement>();
            string? after = null;
            var hasMore = true;
            var pageCount = 0;

            _logger.LogInformation("Starting HubSpot contacts sync for user {UserId}", user.Id);

            // Paginate through all contacts
            while (hasMore)
            {
                pageCount++;
                var request = new RestRequest("/crm/v3/objects/contacts", Method.Get);
                request.AddHeader("Authorization", $"Bearer {accessToken}");
                request.AddParameter("limit", "100");
                request.AddParameter("properties", "firstname,lastname,email,phone,company,jobtitle,lifecyclestage,lastmodifieddate");

                if (!string.IsNullOrEmpty(after))
                {
                    request.AddParameter("after", after);
                }

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                {
                    _logger.LogError("Failed to fetch contacts page {Page}: {Error}", pageCount, response.Content);
                    throw new Exception($"Failed to fetch contacts: {response.Content}");
                }

                var data = JsonSerializer.Deserialize<JsonElement>(response.Content!);
                var results = data.GetProperty("results");

                foreach (var contact in results.EnumerateArray())
                {
                    allContacts.Add(contact);
                }

                _logger.LogInformation("Fetched page {Page} with {Count} contacts", pageCount, results.GetArrayLength());

                // Check if there are more pages
                if (data.TryGetProperty("paging", out var paging) &&
                    paging.TryGetProperty("next", out var next) &&
                    next.TryGetProperty("after", out var afterValue))
                {
                    after = afterValue.GetString();
                    hasMore = true;
                }
                else
                {
                    hasMore = false;
                }

                // Small delay to respect rate limits
                await Task.Delay(100);
            }

            _logger.LogInformation("Fetched {Count} total contacts from HubSpot in {Pages} pages", allContacts.Count, pageCount);

            // Now process all contacts
            var newCount = 0;
            var updateCount = 0;

            foreach (var contact in allContacts)
            {
                var properties = contact.GetProperty("properties");
                var hubspotId = contact.GetProperty("id").GetString()!;

                var existingContact = await _context.HubSpotContacts
                    .FirstOrDefaultAsync(c => c.UserId == user.Id && c.HubSpotId == hubspotId);

                if (existingContact != null)
                {
                    UpdateContactProperties(existingContact, properties);
                    existingContact.UpdatedAt = DateTime.UtcNow;
                    updateCount++;
                }
                else
                {
                    var newContact = new HubSpotContact
                    {
                        UserId = user.Id,
                        HubSpotId = hubspotId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    UpdateContactProperties(newContact, properties);
                    _context.HubSpotContacts.Add(newContact);
                    newCount++;
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Synced {Total} HubSpot contacts for user {UserId} (New: {New}, Updated: {Updated})",
                allContacts.Count, user.Id, newCount, updateCount);
        }

        private void UpdateContactProperties(HubSpotContact contact, JsonElement properties)
        {
            contact.FirstName = GetPropertyValue(properties, "firstname");
            contact.LastName = GetPropertyValue(properties, "lastname");
            contact.Email = GetPropertyValue(properties, "email");
            contact.Phone = GetPropertyValue(properties, "phone");
            contact.Company = GetPropertyValue(properties, "company");
            contact.JobTitle = GetPropertyValue(properties, "jobtitle");
            contact.LifecycleStage = GetPropertyValue(properties, "lifecyclestage");

            var lastModified = GetPropertyValue(properties, "lastmodifieddate");
            if (DateTime.TryParse(lastModified, out var date))
            {
                contact.LastModifiedDate = date;
            }
        }

        public async Task SyncCompaniesAsync(User user)
        {
            var accessToken = await GetValidAccessTokenAsync(user);
            var client = new RestClient(BASE_URL);

            var allCompanies = new List<JsonElement>();
            string? after = null;
            var hasMore = true;
            var pageCount = 0;

            _logger.LogInformation("Starting HubSpot companies sync for user {UserId}", user.Id);

            // Paginate through all companies
            while (hasMore)
            {
                pageCount++;
                var request = new RestRequest("/crm/v3/objects/companies", Method.Get);
                request.AddHeader("Authorization", $"Bearer {accessToken}");
                request.AddParameter("limit", "100");
                request.AddParameter("properties", "name,domain,industry,city,state,country,numberofemployees,annualrevenue,lastmodifieddate");

                if (!string.IsNullOrEmpty(after))
                {
                    request.AddParameter("after", after);
                }

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                {
                    _logger.LogError("Failed to fetch companies page {Page}: {Error}", pageCount, response.Content);
                    throw new Exception($"Failed to fetch companies: {response.Content}");
                }

                var data = JsonSerializer.Deserialize<JsonElement>(response.Content!);
                var results = data.GetProperty("results");

                foreach (var company in results.EnumerateArray())
                {
                    allCompanies.Add(company);
                }

                _logger.LogInformation("Fetched page {Page} with {Count} companies", pageCount, results.GetArrayLength());

                // Check if there are more pages
                if (data.TryGetProperty("paging", out var paging) &&
                    paging.TryGetProperty("next", out var next) &&
                    next.TryGetProperty("after", out var afterValue))
                {
                    after = afterValue.GetString();
                    hasMore = true;
                }
                else
                {
                    hasMore = false;
                }

                await Task.Delay(100);
            }

            _logger.LogInformation("Fetched {Count} total companies from HubSpot in {Pages} pages", allCompanies.Count, pageCount);

            var newCount = 0;
            var updateCount = 0;

            foreach (var company in allCompanies)
            {
                var properties = company.GetProperty("properties");
                var hubspotId = company.GetProperty("id").GetString()!;

                var existingCompany = await _context.HubSpotCompanies
                    .FirstOrDefaultAsync(c => c.UserId == user.Id && c.HubSpotId == hubspotId);

                if (existingCompany != null)
                {
                    UpdateCompanyProperties(existingCompany, properties);
                    existingCompany.UpdatedAt = DateTime.UtcNow;
                    updateCount++;
                }
                else
                {
                    var newCompany = new HubSpotCompany
                    {
                        UserId = user.Id,
                        HubSpotId = hubspotId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    UpdateCompanyProperties(newCompany, properties);
                    _context.HubSpotCompanies.Add(newCompany);
                    newCount++;
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Synced {Total} HubSpot companies for user {UserId} (New: {New}, Updated: {Updated})",
                allCompanies.Count, user.Id, newCount, updateCount);
        }

        private void UpdateCompanyProperties(HubSpotCompany company, JsonElement properties)
        {
            company.Name = GetPropertyValue(properties, "name");
            company.Domain = GetPropertyValue(properties, "domain");
            company.Industry = GetPropertyValue(properties, "industry");
            company.City = GetPropertyValue(properties, "city");
            company.State = GetPropertyValue(properties, "state");
            company.Country = GetPropertyValue(properties, "country");

            var employees = GetPropertyValue(properties, "numberofemployees");
            if (int.TryParse(employees, out var empCount))
            {
                company.NumberOfEmployees = empCount;
            }

            var revenue = GetPropertyValue(properties, "annualrevenue");
            if (decimal.TryParse(revenue, out var rev))
            {
                company.AnnualRevenue = rev;
            }

            var lastModified = GetPropertyValue(properties, "lastmodifieddate");
            if (DateTime.TryParse(lastModified, out var date))
            {
                company.LastModifiedDate = date;
            }
        }

        public async Task SyncDealsAsync(User user)
        {
            var accessToken = await GetValidAccessTokenAsync(user);
            var client = new RestClient(BASE_URL);

            var allDeals = new List<JsonElement>();
            string? after = null;
            var hasMore = true;
            var pageCount = 0;

            _logger.LogInformation("Starting HubSpot deals sync for user {UserId}", user.Id);

            // Paginate through all deals
            while (hasMore)
            {
                pageCount++;
                var request = new RestRequest("/crm/v3/objects/deals", Method.Get);
                request.AddHeader("Authorization", $"Bearer {accessToken}");
                request.AddParameter("limit", "100");
                request.AddParameter("properties", "dealname,dealstage,pipeline,amount,closedate,hs_priority,lastmodifieddate");

                if (!string.IsNullOrEmpty(after))
                {
                    request.AddParameter("after", after);
                }

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                {
                    _logger.LogError("Failed to fetch deals page {Page}: {Error}", pageCount, response.Content);
                    throw new Exception($"Failed to fetch deals: {response.Content}");
                }

                var data = JsonSerializer.Deserialize<JsonElement>(response.Content!);
                var results = data.GetProperty("results");

                foreach (var deal in results.EnumerateArray())
                {
                    allDeals.Add(deal);
                }

                _logger.LogInformation("Fetched page {Page} with {Count} deals", pageCount, results.GetArrayLength());

                // Check if there are more pages
                if (data.TryGetProperty("paging", out var paging) &&
                    paging.TryGetProperty("next", out var next) &&
                    next.TryGetProperty("after", out var afterValue))
                {
                    after = afterValue.GetString();
                    hasMore = true;
                }
                else
                {
                    hasMore = false;
                }

                await Task.Delay(100);
            }

            _logger.LogInformation("Fetched {Count} total deals from HubSpot in {Pages} pages", allDeals.Count, pageCount);

            var newCount = 0;
            var updateCount = 0;

            foreach (var deal in allDeals)
            {
                var properties = deal.GetProperty("properties");
                var hubspotId = deal.GetProperty("id").GetString()!;

                var existingDeal = await _context.HubSpotDeals
                    .FirstOrDefaultAsync(d => d.UserId == user.Id && d.HubSpotId == hubspotId);

                if (existingDeal != null)
                {
                    UpdateDealProperties(existingDeal, properties);
                    existingDeal.UpdatedAt = DateTime.UtcNow;
                    updateCount++;
                }
                else
                {
                    var newDeal = new HubSpotDeal
                    {
                        UserId = user.Id,
                        HubSpotId = hubspotId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    UpdateDealProperties(newDeal, properties);
                    _context.HubSpotDeals.Add(newDeal);
                    newCount++;
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Synced {Total} HubSpot deals for user {UserId} (New: {New}, Updated: {Updated})",
                allDeals.Count, user.Id, newCount, updateCount);
        }

        private void UpdateDealProperties(HubSpotDeal deal, JsonElement properties)
        {
            deal.DealName = GetPropertyValue(properties, "dealname");
            deal.DealStage = GetPropertyValue(properties, "dealstage");
            deal.Pipeline = GetPropertyValue(properties, "pipeline");
            deal.Priority = GetPropertyValue(properties, "hs_priority");

            var amount = GetPropertyValue(properties, "amount");
            if (decimal.TryParse(amount, out var amt))
            {
                deal.Amount = amt;
            }

            var closeDate = GetPropertyValue(properties, "closedate");
            if (DateTime.TryParse(closeDate, out var date))
            {
                deal.CloseDate = date;
            }

            var lastModified = GetPropertyValue(properties, "lastmodifieddate");
            if (DateTime.TryParse(lastModified, out var modDate))
            {
                deal.LastModifiedDate = modDate;
            }
        }

        private string? GetPropertyValue(JsonElement properties, string propertyName)
        {
            if (properties.TryGetProperty(propertyName, out var property))
            {
                return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
            }
            return null;
        }

        public async Task<bool> IsConnectedAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            return user != null && !string.IsNullOrEmpty(user.HubspotAccessToken);
        }
    }
}