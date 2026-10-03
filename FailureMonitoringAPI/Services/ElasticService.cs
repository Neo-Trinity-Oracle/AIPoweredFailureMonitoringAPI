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

        // Returns the ES document id so the caller can round-trip it
        // back via feedback and update the SAME document (no duplicates).
        public async Task<string> SaveKnowledgeAsync(KnowledgeEntry entry)
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

            entry.Id = response.Id;
            return response.Id;
        }

        // Only "helpful = true" is a hard filter now. Category is no longer
        // a Must (an exact-category mismatch used to hide genuinely related
        // answers) — it's used only as a scoring boost via Should, while
        // cosine similarity on the embedding does the real "related question"
        // matching. MinScore trims out weak/unrelated matches.
        public async Task<List<KnowledgeEntry>> SearchKnowledgeByVectorAsync(float[] vector, string category)
        {
            var response = await _client.SearchAsync<KnowledgeEntry>(s => s
                .Indices(KnowledgeIndex)
                .Size(5)
                .MinScore(1.15)
                .Query(q => q
                    .ScriptScore(ss => ss
                        .Query(inner => inner
                            .Bool(b => b
                                .Filter(
                                    f => f.Term(t => t
                                        .Field("helpful")
                                        .Value(true)),
                                    f => f.Exists(e => e
                                        .Field("embedding"))
                                )
                                .Should(sh => sh
                                    .Term(t => t
                                        .Field("category.keyword")
                                        .Value(category)))
                            ))
                        .Script(sc => sc
                            .Source(
                                "cosineSimilarity(params.query_vector,'embedding') + 1.0")
                            .Params(
                                new Dictionary<string, object>
                                {
                            { "query_vector", vector }
                                }))
                    ))
            );

            if (!response.IsValidResponse && response.ElasticsearchServerError != null)
            {
                throw new Exception(
                    $"Knowledge search failed: {response.DebugInformation}");
            }

            var docs = new List<KnowledgeEntry>();
            foreach (var hit in response.Hits)
            {
                if (hit.Source == null) continue;
                hit.Source.Id = hit.Id!;
                docs.Add(hit.Source);
            }

            return docs;
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

            foreach (var hit in response.Hits)
            {
                if (hit.Source != null) hit.Source.Id = hit.Id!;
            }

            return response.Documents.ToList();
        }

        #endregion

        public async Task UpdateHelpfulAsync(string id)
        {
            var request = new UpdateRequest<KnowledgeEntry, object>(KnowledgeIndex, id)
            {
                Script = new Script
                {
                    Source = @"
                    ctx._source.helpful = true;
                    ctx._source.helpfulCount = (ctx._source.helpfulCount == null ? 0 : ctx._source.helpfulCount) + 1;
                    ctx._source.confidence = Math.min(1.0, ctx._source.helpfulCount / 10.0);
                    "
                }
            };

            var response = await _client.UpdateAsync(request);

            if (!response.IsValidResponse)
            {
                throw new Exception(
                    $"Feedback update failed: {response.DebugInformation}");
            }
        }

        public async Task UpdateHelpfulByQuestionAsync(string question)
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

            var hit = search.Hits.FirstOrDefault();
            if (hit?.Id == null)
                return;

            // Delegate to the id-based, script-only update —
            // guarantees embedding and every other field stay untouched.
            await UpdateHelpfulAsync(hit.Id);
        }

        #region RCA Learning

        public async Task SaveVerifiedRcaAsync(string question, string aiAnswer, string rootCause, string resolution, float[] embedding)
        {
            var entry =
                new KnowledgeEntry
                {
                    Question = question,
                    AiAnswer = aiAnswer,
                    RootCause = rootCause,
                    Resolution = resolution,
                    Helpful = true,
                    CreatedOn = DateTime.UtcNow,
                    Embedding = embedding
                };

            await SaveKnowledgeAsync(entry);
        }

        #endregion


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