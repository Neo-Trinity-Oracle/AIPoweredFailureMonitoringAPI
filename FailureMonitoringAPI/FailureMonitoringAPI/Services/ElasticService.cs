using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using FailureMonitoringAPI.Models;

namespace FailureMonitoringAPI.Services
{
    public class ElasticService
    {
        private readonly ElasticsearchClient _client;

        private const string FailureIndex = "wire_failure_data";
        private const string KnowledgeIndex = "knowledge_repository";

        public ElasticService(IConfiguration config)
        {
            var settings = new ElasticsearchClientSettings(
                    new Uri(config["Elastic:Url"]!));

            _client =
                new ElasticsearchClient(settings);
        }

        #region Failure Log Operations

        public async Task IndexAsync(FailureLog log)
        {
            var response =
                await _client.IndexAsync(
                    log,
                    idx => idx.Index(FailureIndex));

            if (!response.IsValidResponse)
            {
                throw new Exception(
                    $"Indexing failed: {response.DebugInformation}");
            }
        }

        public async Task BulkIndexAsync(IEnumerable<FailureLog> logs)
        {
            var request = new BulkRequest(FailureIndex)
                {
                    Operations = logs
                        .Select(x =>
                            new BulkIndexOperation<FailureLog>(x))
                        .Cast<IBulkOperation>()
                        .ToList()
                };

            var response = await _client.BulkAsync(request);

            if (response.Errors)
            {
                foreach (var item in response.Items)
                {
                    Console.WriteLine($"ID={item.Id} " + $"Error={item.Error?.Reason}");
                }
            }

            if (!response.IsValidResponse)
            {
                throw new Exception(
                    response.DebugInformation);
            }
        }

        #endregion

        #region Hybrid Search

        public async Task<SearchResponse<FailureLog>>HybridSearch(string query,float[] vector)
        {
            var q = query.ToLower();

            // show all queries
            if (q.Contains("all") ||
                q.Contains("list") ||
                q.Contains("show") ||
                q.Contains("available") ||
                q.Contains("complete") ||
                q.Contains("details") ||
                q.Contains("records") ||
                q.Contains("wire ids") ||
                q.Contains("how many"))
            {
                return await _client.SearchAsync<FailureLog>(
                    s => s
                        .Indices(FailureIndex)
                        .Query(q => q.MatchAll())
                        .Size(1000));
            }

            var response = await _client.SearchAsync<FailureLog>(
                    s => s
                        .Indices(FailureIndex)
                        .Size(10)
                        .Query(q => q
                            .ScriptScore(ss => ss
                                .Query(inner => inner
                                    .Match(m => m
                                        .Field(f => f.FullText)
                                        .Query(query)))
                                .Script(sc => sc
                                    .Source(
                                        "cosineSimilarity(params.query_vector, 'embedding') + 1.0")
                                    .Params(
                                        new Dictionary<string, object>
                                        {
                                            {
                                                "query_vector",
                                                vector
                                            }
                                        }))
                            ))
                );

            if (!response.Documents.Any())
            {
                return await _client.SearchAsync<FailureLog>(
                    s => s
                        .Indices(FailureIndex)
                        .Query(q => q.MatchAll())
                        .Size(1000));
            }

            if (!response.IsValidResponse)
            {
                throw new Exception(
                    $"Search failed: {response.DebugInformation}");
            }

            return response;
        }

        #endregion

        #region Knowledge Repository

        public async Task SaveKnowledgeAsync(KnowledgeEntry entry)
        {
            var response =
                await _client.IndexAsync(
                    entry,
                    idx => idx.Index(KnowledgeIndex));

            if (!response.IsValidResponse)
            {
                throw new Exception(
                    $"Knowledge save failed: {response.DebugInformation}");
            }
        }

        public async Task<List<KnowledgeEntry>> SearchKnowledgeAsync(string question, string category)
        {
            var response = await _client.SearchAsync<KnowledgeEntry>(
                    s => s
                        .Indices(KnowledgeIndex)
                        .Size(10)
                        .Query(q =>
                            q.Bool(b => b.Must(
                                m => m.Match(mm =>
                                    mm.Field(f => f.Question)
                                      .Query(question)),
                                m => m.Term(t =>
                                    t.Field("category.keyword")
                                     .Value(category))
                            ))
                        )
                );

            return response.Documents.ToList();
        }

        public async Task<List<KnowledgeEntry>> SearchKnowledgeByVectorAsync( float[] vector,string category)
        {
            var response = await _client.SearchAsync<KnowledgeEntry>( s => s
                        .Indices(KnowledgeIndex)
                        .Size(5)
                        .Query(q =>
                            q.ScriptScore(ss => ss
                                .Query(inner =>
                                    inner.Bool(b => b.Must(
                                        m => m.Term(t =>
                                            t.Field("helpful")
                                            .Value(true)),
                                        m => m.Term(t =>
                                            t.Field("category.keyword")
                                            .Value(category))
                                    ))
                                )
                                .Script(sc => sc
                                    .Source(
                                        "cosineSimilarity(params.query_vector,'embedding') + 1.0")
                                    .Params(
                                        new Dictionary<string, object>
                                        {
                                    { "query_vector", vector }
                                        }
                                    )
                                )
                            )
                        )
                );

            return response.Documents.ToList();
        }

        public async Task<List<KnowledgeEntry>> GetHelpfulKnowledgeAsync()
        {
            var response =
                await _client.SearchAsync<KnowledgeEntry>(
                    s => s
                        .Indices(KnowledgeIndex)
                        .Size(100)
                        .Query(q => q
                            .Term(t => t
                                .Field("helpful")
                                .Value(true)))
                );

            return response.Documents.ToList();
        }

        #endregion

        #region RCA Learning

        public async Task SaveVerifiedRcaAsync(string question,string aiAnswer,string rootCause,string resolution)
        {
            var entry =
                new KnowledgeEntry
                {
                    Question = question,
                    AiAnswer = aiAnswer,
                    RootCause = rootCause,
                    Resolution = resolution,
                    Helpful = true,
                    CreatedOn = DateTime.UtcNow
                };

            await SaveKnowledgeAsync(entry);
        }

        #endregion

        public async Task UpdateHelpfulAsync(
     string question)
        {
            var search =
                await _client.SearchAsync<KnowledgeEntry>(
                    s => s
                    .Indices(KnowledgeIndex)
                    .Size(1)
                    .Query(q => q.Match(
                        m => m
                            .Field(f => f.Question)
                            .Query(question)
                    )));

            var item =
                search.Documents.FirstOrDefault();

            if (item == null)
                return;

            item.Helpful = true;

            item.HelpfulCount++;

            item.Confidence =
                Math.Min(
                    1.0,
                    item.HelpfulCount / 10.0);

            await _client.IndexAsync(
                item,
                i => i.Index(KnowledgeIndex));
        }



        #region Health Check

        public async Task<bool> PingAsync()
        {
            var response =
                await _client.PingAsync();

            return response.IsValidResponse;
        }

        #endregion
    }
}