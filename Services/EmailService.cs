using FinancialAdvisorAI.API.Models;
using FinancialAdvisorAI.API.Repositories;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Requests;
using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;

namespace FinancialAdvisorAI.API.Services
{
    public class EmailService
    {
        private readonly GoogleAuthService _googleAuthService;
        private readonly AppDbContext _context;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            GoogleAuthService googleAuthService,
            AppDbContext context,
            ILogger<EmailService> logger)
        {
            _googleAuthService = googleAuthService;
            _context = context;
            _logger = logger;
        }

        public async Task<GmailService> GetGmailServiceAsync(User user)
        {
            var credential = await _googleAuthService.GetUserCredentialAsync(user);

            var service = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Financial Advisor AI"
            });

            return service;
        }


        /// <summary>
        /// Full sync - Fetch ALL emails incrementally, processing and saving in batches
        /// This avoids memory buildup and saves data as we go
        /// </summary>
        public async Task<(int newEmails, int updatedEmails, int totalProcessed)> SyncAllMessagesIncrementallyAsync(
            User user,
            string? query = null,
            DateTime? since = null,
            IProgress<SyncProgress>? progress = null)
        {
            try
            {
                var service = await GetGmailServiceAsync(user);
                string? pageToken = null;
                var pageCount = 0;
                var totalNewEmails = 0;
                var totalUpdatedEmails = 0;
                var totalProcessed = 0;

                _logger.LogInformation("Starting INCREMENTAL Gmail sync for user {UserId}", user.Id);

                // Build query
                var searchQuery = query;
                if (since.HasValue)
                {
                    var sinceStr = since.Value.ToString("yyyy/MM/dd");
                    searchQuery = string.IsNullOrEmpty(searchQuery)
                        ? $"after:{sinceStr}"
                        : $"{searchQuery} after:{sinceStr}";
                }

                do
                {
                    pageCount++;

                    // ✅ STEP 1: List message IDs (fast)
                    var listRequest = service.Users.Messages.List("me");
                    listRequest.MaxResults = 100; // Process 100 at a time
                    listRequest.PageToken = pageToken;

                    if (!string.IsNullOrEmpty(searchQuery))
                    {
                        listRequest.Q = searchQuery;
                    }

                    var response = await listRequest.ExecuteAsync();

                    if (response.Messages != null && response.Messages.Any())
                    {
                        _logger.LogInformation(
                            "Page {Page}: Found {Count} message IDs",
                            pageCount,
                            response.Messages.Count);

                        // ✅ STEP 2: Fetch and process messages immediately
                        var (newEmails, updatedEmails) = await FetchAndSaveMessagesAsync(
                            service,
                            user.Id,
                            response.Messages.ToList());

                        totalNewEmails += newEmails;
                        totalUpdatedEmails += updatedEmails;
                        totalProcessed += response.Messages.Count;

                        // ✅ STEP 3: Report progress after each batch
                        progress?.Report(new SyncProgress
                        {
                            TotalProcessed = totalProcessed,
                            CurrentPage = pageCount,
                            Status = $"Processed {totalProcessed} emails (Page {pageCount}): {newEmails} new, {updatedEmails} updated"
                        });

                        _logger.LogInformation(
                            "Page {Page} completed: {New} new, {Updated} updated",
                            pageCount, newEmails, updatedEmails);
                    }

                    pageToken = response.NextPageToken;

                    // Small delay between pages to respect rate limits
                    if (!string.IsNullOrEmpty(pageToken))
                    {
                        await Task.Delay(1000);
                    }

                } while (!string.IsNullOrEmpty(pageToken));

                _logger.LogInformation(
                    "Completed incremental Gmail sync: {Total} messages processed, {New} new, {Updated} updated across {Pages} pages",
                    totalProcessed, totalNewEmails, totalUpdatedEmails, pageCount);

                return (totalNewEmails, totalUpdatedEmails, totalProcessed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in incremental Gmail sync for user {UserId}", user.Id);
                throw;
            }
        }

        public async Task<List<Message>> ListMessagesAsync(User user, int maxResults = 100, string? query = null)
        {
            try
            {
                var service = await GetGmailServiceAsync(user);
                var request = service.Users.Messages.List("me");
                request.MaxResults = maxResults;

                if (!string.IsNullOrEmpty(query))
                {
                    request.Q = query;
                }

                var response = await request.ExecuteAsync();
                var messages = new List<Message>();

                if (response.Messages != null)
                {
                    foreach (var messageInfo in response.Messages)
                    {
                        var messageRequest = service.Users.Messages.Get("me", messageInfo.Id);
                        var message = await messageRequest.ExecuteAsync();
                        messages.Add(message);
                    }
                }

                return messages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing Gmail messages for user {UserId}", user.Id);
                throw;
            }
        }

        /// <summary>
        /// Quick sync - Fetch only recent messages (latest 300) for immediate user feedback
        /// This is much faster and respects rate limits better than full sync
        /// </summary>
        public async Task<List<Message>> ListRecentMessagesAsync(
            User user,
            int maxResults = 300,
            IProgress<SyncProgress>? progress = null)
        {
            try
            {
                var service = await GetGmailServiceAsync(user);
                var allMessages = new List<Message>();

                _logger.LogInformation("Fetching latest {MaxResults} emails for user {UserId}",
                    maxResults, user.Id);

                // Get message IDs (fast, single request)
                var listRequest = service.Users.Messages.List("me");
                listRequest.MaxResults = Math.Min(maxResults, 500); // Gmail API max is 500

                var response = await listRequest.ExecuteAsync();

                if (response.Messages == null || !response.Messages.Any())
                {
                    _logger.LogInformation("No messages found for user {UserId}", user.Id);
                    return allMessages;
                }

                _logger.LogInformation("Found {Count} message IDs, fetching details...",
                    response.Messages.Count);

                // Fetch message details with controlled concurrency
                var semaphore = new SemaphoreSlim(5); // Max 5 concurrent requests
                var fetchTasks = new List<Task<Message?>>();
                var processedCount = 0;

                foreach (var messageInfo in response.Messages)
                {
                    await semaphore.WaitAsync();

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            var msgRequest = service.Users.Messages.Get("me", messageInfo.Id);
                            var message = await msgRequest.ExecuteAsync();

                            // Rate limiting: 5 concurrent × 50ms = ~100 requests/second
                            await Task.Delay(50);

                            Interlocked.Increment(ref processedCount);

                            // Report progress every 10 messages
                            if (processedCount % 10 == 0)
                            {
                                progress?.Report(new SyncProgress
                                {
                                    TotalProcessed = processedCount,
                                    CurrentPage = 1,
                                    Status = $"Fetched {processedCount}/{response.Messages.Count} emails..."
                                });
                            }

                            return message;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error fetching message {MessageId}", messageInfo.Id);
                            return null;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    fetchTasks.Add(task);

                    // Process in chunks of 20 to avoid memory buildup
                    if (fetchTasks.Count >= 20)
                    {
                        var messages = await Task.WhenAll(fetchTasks);
                        allMessages.AddRange(messages.Where(m => m != null)!);
                        fetchTasks.Clear();
                    }
                }

                // Process remaining messages
                if (fetchTasks.Any())
                {
                    var messages = await Task.WhenAll(fetchTasks);
                    allMessages.AddRange(messages.Where(m => m != null)!);
                }

                progress?.Report(new SyncProgress
                {
                    TotalProcessed = allMessages.Count,
                    CurrentPage = 1,
                    Status = $"Completed! Fetched {allMessages.Count} emails"
                });

                _logger.LogInformation(
                    "Completed quick sync: {Count} messages fetched for user {UserId}",
                    allMessages.Count,
                    user.Id);

                return allMessages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in quick sync for user {UserId}", user.Id);
                throw;
            }
        }
        public async Task<Message> GetMessageAsync(User user, string messageId)
        {
            try
            {
                var service = await GetGmailServiceAsync(user);
                var request = service.Users.Messages.Get("me", messageId);
                var message = await request.ExecuteAsync();
                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Gmail message {MessageId} for user {UserId}", messageId, user.Id);
                throw;
            }
        }

        public async Task<Message> SendMessageAsync(User user, string to, string subject, string body)
        {
            try
            {
                var service = await GetGmailServiceAsync(user);

                var message = new Message
                {
                    Raw = CreateRawMessage(to, subject, body)
                };

                var request = service.Users.Messages.Send(message, "me");
                var sentMessage = await request.ExecuteAsync();

                _logger.LogInformation("Sent email from user {UserId} to {To}", user.Id, to);
                return sentMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Gmail message for user {UserId}", user.Id);
                throw;
            }
        }

        public string GetMessageBody(Message message)
        {
            if (message.Payload == null) return string.Empty;

            // Try to get plain text body
            if (message.Payload.Body?.Data != null)
            {
                return DecodeBase64(message.Payload.Body.Data);
            }

            // Check parts for text/plain
            if (message.Payload.Parts != null)
            {
                foreach (var part in message.Payload.Parts)
                {
                    if (part.MimeType == "text/plain" && part.Body?.Data != null)
                    {
                        return DecodeBase64(part.Body.Data);
                    }
                }

                // If no plain text, try text/html
                foreach (var part in message.Payload.Parts)
                {
                    if (part.MimeType == "text/html" && part.Body?.Data != null)
                    {
                        return DecodeBase64(part.Body.Data);
                    }
                }
            }

            return string.Empty;
        }

        public string GetMessageSubject(Message message)
        {
            var subject = message.Payload?.Headers?.FirstOrDefault(h => h.Name == "Subject");
            return subject?.Value ?? "(No Subject)";
        }

        public string GetMessageFrom(Message message)
        {
            var from = message.Payload?.Headers?.FirstOrDefault(h => h.Name == "From");
            return from?.Value ?? "Unknown";
        }

        public DateTime? GetMessageDate(Message message)
        {
            if (message.InternalDate.HasValue)
            {
                var dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value);
                return dateTimeOffset.DateTime;
            }
            return null;
        }


        private string CreateRawMessage(string to, string subject, string body)
        {
            var message = $"To: {to}\r\n" +
                         $"Subject: {subject}\r\n" +
                         $"Content-Type: text/plain; charset=utf-8\r\n\r\n" +
                         $"{body}";

            var encodedMessage = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(message))
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");

            return encodedMessage;
        }

        /// <summary>
        /// Fetch message details and save to database immediately
        /// </summary>
        private async Task<(int newEmails, int updatedEmails)> FetchAndSaveMessagesAsync(
            GmailService service,
            int userId,
            List<Google.Apis.Gmail.v1.Data.Message> messageInfoList)
        {
            var newEmails = 0;
            var updatedEmails = 0;

            // Use controlled concurrency to respect rate limits
            var semaphore = new SemaphoreSlim(5); // Max 5 concurrent requests
            var tasks = new List<Task<(Message? message, bool isNew, bool isUpdated)>>();

            foreach (var messageInfo in messageInfoList)
            {
                await semaphore.WaitAsync();

                var task = Task.Run(async () =>
                {
                    try
                    {
                        // Fetch full message details
                        var msgRequest = service.Users.Messages.Get("me", messageInfo.Id);
                        var message = await msgRequest.ExecuteAsync();

                        // Rate limiting delay
                        await Task.Delay(50);

                        // Save to database immediately
                        var (isNew, isUpdated) = await SaveMessageToDatabaseAsync(userId, message);

                        return (message, isNew, isUpdated);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error fetching/saving message {MessageId}", messageInfo.Id);
                        return (null, false, false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                tasks.Add(task);
            }

            // Wait for all tasks to complete
            var results = await Task.WhenAll(tasks);

            // Count new and updated emails
            foreach (var (message, isNew, isUpdated) in results)
            {
                if (isNew) newEmails++;
                if (isUpdated) updatedEmails++;
            }

            return (newEmails, updatedEmails);
        }

        /// <summary>
        /// Save a single message to database (insert or update)
        /// </summary>
        private async Task<(bool isNew, bool isUpdated)> SaveMessageToDatabaseAsync(
            int userId,
            Message message)
        {
            using var scope = _context.Database.BeginTransaction();
            try
            {
                var messageId = message.Id;

                // Check if email already exists
                var existingEmail = await _context.EmailCaches
                    .FirstOrDefaultAsync(e => e.UserId == userId && e.MessageId == messageId);

                bool isNew = false;
                bool isUpdated = false;

                if (existingEmail == null)
                {
                    // Insert new email
                    var emailCache = new EmailCache
                    {
                        UserId = userId,
                        MessageId = messageId,
                        ThreadId = message.ThreadId,
                        Subject = GetMessageSubject(message),
                        FromEmail = GetMessageFrom(message),
                        Body = GetMessageBody(message),
                        Snippet = message.Snippet,
                        EmailDate = GetMessageDate(message),
                        IsRead = !message.LabelIds?.Contains("UNREAD") ?? true,
                        IsSent = message.LabelIds?.Contains("SENT") ?? false,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _context.EmailCaches.Add(emailCache);
                    isNew = true;
                }
                else
                {
                    // Update existing email (in case labels changed, etc.)
                    existingEmail.Subject = GetMessageSubject(message);
                    existingEmail.FromEmail = GetMessageFrom(message);
                    existingEmail.Body = GetMessageBody(message);
                    existingEmail.Snippet = message.Snippet;
                    existingEmail.EmailDate = GetMessageDate(message);
                    existingEmail.IsRead = !message.LabelIds?.Contains("UNREAD") ?? true;
                    existingEmail.IsSent = message.LabelIds?.Contains("SENT") ?? false;
                    existingEmail.UpdatedAt = DateTime.UtcNow;

                    isUpdated = true;
                }

                await _context.SaveChangesAsync();
                await scope.CommitAsync();

                return (isNew, isUpdated);
            }
            catch (Exception ex)
            {
                await scope.RollbackAsync();
                _logger.LogError(ex, "Error saving message {MessageId} to database", message.Id);
                throw;
            }
        }
       
        private string DecodeBase64(string encodedString)
        {
            try
            {
                var data = encodedString.Replace('-', '+').Replace('_', '/');
                switch (data.Length % 4)
                {
                    case 2: data += "=="; break;
                    case 3: data += "="; break;
                }
                var bytes = Convert.FromBase64String(data);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}