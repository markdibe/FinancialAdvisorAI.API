using Microsoft.OpenApi.Extensions;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace FinancialAdvisorAI.API.Services
{
    public class QdrantService
    {
        private readonly QdrantClient _client;
        private readonly IConfiguration _configuration;
        private readonly ILogger<QdrantService> _logger;
        private readonly string _collectionName;

        public QdrantService(
            IConfiguration configuration,
            ILogger<QdrantService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _collectionName = _configuration["Qdrant:CollectionName"] ?? "financial_advisor_data";

            var qdrantUrl = _configuration["Qdrant:Url"];
            var apiKey = _configuration["Qdrant:ApiKey"];

            var uri = new Uri(qdrantUrl!);
            var host = uri.Host;
            _client = new QdrantClient(
                host: host,
                apiKey: apiKey,
                https: true
            );

            InitializeCollectionAsync().Wait();
        }

        private async Task InitializeCollectionAsync()
        {
            try
            {
                // Check if collection exists
                var collections = await _client.ListCollectionsAsync();
                var collectionExists = collections.Any(c => c == _collectionName);

                if (!collectionExists)
                {
                    _logger.LogInformation("Creating Qdrant collection: {CollectionName}", _collectionName);

                    // Create collection with 1536 dimensions (text-embedding-3-small)
                    await _client.CreateCollectionAsync(
                        collectionName: _collectionName,
                        vectorsConfig: new VectorParams
                        {
                            Size = 1536,
                            Distance = Distance.Cosine
                        }
                    );

                    _logger.LogInformation("Collection created successfully");
                }
                else
                {
                    _logger.LogInformation("Collection {CollectionName} already exists", _collectionName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Qdrant collection");
                throw;
            }
        }

        public async Task UpsertPointAsync(
            string id,
            float[] vector,
            Dictionary<string, object> payload)
        {
            try
            {
                var point = new PointStruct
                {
                    Id = new PointId { Uuid = id },
                    Vectors = vector,
                    Payload = { }
                };

                foreach (var kvp in payload)
                {
                    point.Payload[kvp.Key] = (Value)kvp.Value;
                }

                await _client.UpsertAsync(
                    collectionName: _collectionName,
                    points: new[] { point }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error upserting point to Qdrant");
                throw;
            }
        }

        public async Task UpsertPointsAsync(List<(string id, float[] vector, Dictionary<string, object> payload)> points)
        {
            try
            {
                var qdrantPoints = points.Select(p =>
                {
                    var point = new PointStruct
                    {
                        Id = new PointId { Uuid = p.id },
                        Vectors = p.vector,
                        Payload = { }
                    };

                    foreach (var kvp in p.payload)
                    {
                        point.Payload[kvp.Key] = (Value)kvp.Value;
                    }

                    return point;
                }).ToList();

                // Batch upsert (100 at a time to avoid limits)
                var batchSize = 100;
                for (int i = 0; i < qdrantPoints.Count; i += batchSize)
                {
                    var batch = qdrantPoints.Skip(i).Take(batchSize).ToList();
                    await _client.UpsertAsync(
                        collectionName: _collectionName,
                        points: batch
                    );

                    _logger.LogInformation("Upserted batch {Batch} ({Count} points)", i / batchSize + 1, batch.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error upserting points to Qdrant");
                throw;
            }
        }

        public async Task<List<ScoredPoint>> SearchAsync(
            float[] queryVector,
            int limit = 10,
            Dictionary<string, object>? filter = null)
        {
            try
            {
                var searchResult = await _client.SearchAsync(
                    collectionName: _collectionName,
                    vector: queryVector,
                    limit: (ulong)limit,
                    filter: filter != null ? BuildFilter(filter) : null
                );

                return searchResult.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching in Qdrant");
                throw;
            }
        }

        public async Task DeletePointsByFilterAsync(Dictionary<string, object> filter)
        {
            try
            {
                await _client.DeleteAsync(
                    collectionName: _collectionName,
                    filter: BuildFilter(filter)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting points from Qdrant");
                throw;
            }
        }

        private Filter BuildFilter(Dictionary<string, object> filterDict)
        {
            var conditions = new List<Condition>();

            foreach (var kvp in filterDict)
            {
                conditions.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = kvp.Key,
                        Match = new Match { Keyword = kvp.Value.ToString() }
                    }
                });
            }

            return new Filter
            {
                Must = { conditions }
            };
        }
    }
}