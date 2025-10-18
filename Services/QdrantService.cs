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

            // Remove https:// and port from URL if present
            var uri = new Uri(qdrantUrl!);
            var host = uri.Host;

            _logger.LogInformation("Connecting to Qdrant at: {Host}", host);

            _client = new QdrantClient(
                host: host,
                https: true,
                apiKey: apiKey
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

                var collectionInfos = await _client.GetCollectionInfoAsync(_collectionName);

                //await _client.DeletePayloadIndexAsync(_collectionName, "user_id");


                if (!collectionInfos.PayloadSchema.ContainsKey("user_id"))
                {

                    await _client.CreatePayloadIndexAsync(_collectionName, "user_id", PayloadSchemaType.Integer);
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
                // Generate a numeric ID from the string
                var numericId = GenerateNumericId(id);

                var point = new PointStruct
                {
                    Id = numericId,
                    Vectors = vector,
                    Payload = { }
                };

                // Add the original ID as a payload field
                point.Payload["original_id"] = ConvertToValue(id);

                foreach (var kvp in payload)
                {
                    point.Payload[kvp.Key] = ConvertToValue(kvp.Value);
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
                _logger.LogInformation("Starting to upsert {Count} points", points.Count);

                var qdrantPoints = new List<PointStruct>();

                foreach (var p in points)
                {
                    try
                    {
                        // Generate a numeric ID from the string
                        var numericId = GenerateNumericId(p.id);

                        var point = new PointStruct
                        {
                            Id = numericId,
                            Vectors = p.vector,
                            Payload = { }
                        };

                        // Add the original ID as a payload field so we can search by it
                        point.Payload["original_id"] = ConvertToValue(p.id);

                        foreach (var kvp in p.payload)
                        {
                            point.Payload[kvp.Key] = ConvertToValue(kvp.Value);
                        }

                        qdrantPoints.Add(point);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error creating point {Id}", p.id);
                        throw;
                    }
                }

                _logger.LogInformation("Created {Count} point structures", qdrantPoints.Count);

                // Batch upsert (100 at a time to avoid limits)
                var batchSize = 100;
                for (int i = 0; i < qdrantPoints.Count; i += batchSize)
                {
                    var batch = qdrantPoints.Skip(i).Take(batchSize).ToList();

                    _logger.LogInformation("Upserting batch {Batch} with {Count} points", i / batchSize + 1, batch.Count);

                    await _client.UpsertAsync(
                        collectionName: _collectionName,
                        points: batch
                    );

                    _logger.LogInformation("✅ Upserted batch {Batch} ({Count} points)", i / batchSize + 1, batch.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error upserting points to Qdrant. Exception: {Message}", ex.Message);
                throw;
            }
        }

        // Helper method to generate a numeric ID from a string
        private ulong GenerateNumericId(string id)
        {
            // Use GetHashCode and convert to positive ulong
            var hash = id.GetHashCode();
            if (hash < 0)
            {
                hash = -hash;
            }
            return (ulong)hash;
        }

        // Helper method to convert C# objects to Qdrant Value objects
        private Value ConvertToValue(object obj)
        {
            return obj switch
            {
                string s => new Value { StringValue = s },
                int i => new Value { IntegerValue = i },
                long l => new Value { IntegerValue = l },
                double d => new Value { DoubleValue = d },
                float f => new Value { DoubleValue = f },
                bool b => new Value { BoolValue = b },
                _ => new Value { StringValue = obj?.ToString() ?? "" }
            };
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
                var match = kvp.Value switch
                {
                    string s => new Match { Keyword = s },
                    int i => new Match { Integer = 1 },
                    long l => new Match { Integer = l },
                    double d => new Match { Keyword = d.ToString() },
                    float f => new Match { Keyword = f.ToString() },
                    bool b => new Match { Boolean = b },
                    _ => new Match { Keyword = "" }
                };

                conditions.Add(new Condition
                {

                    Field = new FieldCondition
                    {
                        Key = kvp.Key,
                        Match = match
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