using FailureMonitoringAPI.Models;
using FailureMonitoringAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

[ApiController]
[Route("[controller]")]
public class LogsController : ControllerBase
{
    private readonly ElasticService _elastic;
    private readonly EmbeddingService _embedding;
    private readonly AIService _ai;

    public LogsController(
        ElasticService elastic,
        EmbeddingService embedding,
        AIService ai)
    {
        _elastic = elastic;
        _embedding = embedding;
        _ai = ai;
    }

    // ✅ INSERT
    [HttpPost]
    public async Task<IActionResult> AddLog(List<FailureLog> logs)
    {
        foreach (var log in logs)
        {
            log.FullText =
                $"WireId:{log.WireId} " +
                $"Status:{log.Status} " +
                $"ErrorCategory:{log.ErrorCategory} " +
                $"Stage:{log.Stage} " +
                $"Institution:{log.Institution} " +
                $"Source:{log.Source}";

            log.Embedding =
                await _embedding.GetEmbedding(log.FullText);
        }

        await _elastic.BulkIndexAsync(logs);

        return Ok(new
        {
            Message = $"{logs.Count} logs inserted successfully"
        });
    }




    #region Search_Commented
    //// ✅ SEARCH (Hybrid + AI)
    //[HttpGet("search")]
    //public async Task<IActionResult> Search(string query)
    //{

    //    bool isWireLookup = IsWireLookup(query);

    //    var learnedKnowledge = await _elastic.SearchKnowledgeAsync(query);

    //    //if (learnedKnowledge.Any(
    //    //    x => x.Helpful))
    //    //{
    //    //    return Ok(new
    //    //    {
    //    //        Results = new List<FailureLog>(),

    //    //        Insight =
    //    //            "Based on previous resolutions:\n\n" +
    //    //            string.Join(
    //    //                "\n\n",
    //    //                learnedKnowledge
    //    //                .Where(x => x.Helpful)
    //    //                .Select(x =>
    //    //                    $"Root Cause: {x.RootCause}\n" +
    //    //                    $"Resolution: {x.Resolution}")
    //    //            )
    //    //    });
    //    //}

    //    var helpfulAnswers = learnedKnowledge
    //        .Where(x => x.Helpful)
    //        .Select(x => x.AiAnswer)
    //        .ToList();

    //        if (helpfulAnswers.Any())
    //        {
    //            return Ok(new
    //            {
    //                Results = new List<FailureLog>(),

    //                Insight =
    //                    "Based on previous learnings:\n\n" +
    //                    string.Join(
    //                        "\n\n----------------------------------\n\n",
    //                        helpfulAnswers)
    //            });
    //        }

    //    var vector = await _embedding.GetEmbedding(query);

    //    var result = await _elastic.HybridSearch(query, vector);


    //    if (!result.Documents.Any())
    //    {
    //        return Ok(new
    //        {
    //            Results = new List<FailureLog>(),
    //            Insight = "No matching records were found for your query."
    //        });
    //    }



    //    var context = JsonConvert.SerializeObject(
    //    result.Documents,
    //    Formatting.Indented);

    //    var aiResponse = await _ai.Generate(query, context);

    //    return Ok(new
    //    {
    //        Results = result.Documents,
    //        Insight = aiResponse
    //    });
    //}

    #endregion

    [HttpGet("search")]
    public async Task<IActionResult> Search(string query)
    {
        bool isWireLookup = IsWireLookup(query);

        var queryVector =
            await _embedding.GetEmbedding(query);

        // ===================================
        // CHECK LEARNED KNOWLEDGE FIRST
        // ===================================

        if (!isWireLookup)
        {
            var category =
                GetQuestionCategory(query);

            var learnedKnowledge =
                await _elastic.SearchKnowledgeByVectorAsync(
                    queryVector,
                    category);

            var currentIdSignature = ExtractIdSignature(query);

            // Confidence gate removed: a single helpful mark (Helpful == true)
            // is enough to be reused, matching "each request that was helpful
            // needs to be learned." HelpfulCount/Confidence still used to
            // rank between multiple learned candidates.
            //
            // ID guard added: a learned answer can only be reused if it
            // references the exact same record ID(s) as the current query
            // (or neither query mentions any ID at all). Embedding similarity
            // alone treats "wire 2001" and "wire 2003" as nearly identical,
            // which would otherwise let one wire's specific findings leak
            // into the answer for a completely different wire.
            var learnedAnswer = learnedKnowledge
                    .Where(x => x.Helpful)
                    .Where(x => ExtractIdSignature(x.Question) == currentIdSignature)
                    .OrderByDescending(x => x.HelpfulCount)
                    .ThenByDescending(x => x.CreatedOn)
                    .FirstOrDefault();

            if (learnedAnswer != null)
            {
                return Ok(new
                {
                    Results = new List<FailureLog>(),
                    KnowledgeId = learnedAnswer.Id,

                    Insight =
                        "Based on previous validated learnings:\n\n" +
                        learnedAnswer.AiAnswer,

                    Source = "Knowledge Repository"
                });
            }
        }

        // ===================================
        // SEARCH FAILURE LOGS
        // ===================================

        var result =
            await _elastic.HybridSearch(
                query,
                queryVector);

        if (!result.Documents.Any())
        {
            return Ok(new
            {
                Results = new List<FailureLog>(),
                Insight = "No matching records were found."
            });
        }

        var context =
            JsonConvert.SerializeObject(
                result.Documents,
                Formatting.Indented);

        // ===================================
        // FETCH HISTORICAL RCA
        // ===================================

        var historicalKnowledge =
            await _elastic.GetHelpfulKnowledgeAsync();

        var aiResponse =
            await _ai.Generate(
                query,
                context,
                historicalKnowledge);

        // ===================================
        // SAVE QUESTION
        // ===================================

        string? knowledgeId = null;

        if (!isWireLookup)
        {
            var entry = new KnowledgeEntry
            {
                Question = query,
                Category = GetQuestionCategory(query),
                AiAnswer = aiResponse,
                Helpful = false,
                HelpfulCount = 0,
                Confidence = 0,
                CreatedOn = DateTime.UtcNow,
                Embedding = queryVector
            };

            knowledgeId = await _elastic.SaveKnowledgeAsync(entry);
        }

        return Ok(new
        {
            Results = result.Documents,
            Insight = aiResponse,
            KnowledgeId = knowledgeId
        });
    }

    private bool IsWireLookup(string query)
    {
        var wireMatch = Regex.Match(query, @"\b\d+\b");

        if (!wireMatch.Success)
        {
            return false;
        }

        // Analytical questions (RCA, trends, patterns) are learnable
        // even when they reference a specific wire ID — the *reasoning*
        // doesn't go stale the way raw field values do. These must be
        // excluded from the "wire lookup" bucket first, before the
        // generic field-lookup keywords below get a chance to match.
        bool isAnalytical =
            query.Contains("rca", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("root cause", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("why", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("trend", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("pattern", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("summary", StringComparison.OrdinalIgnoreCase);

        if (isAnalytical)
        {
            return false;
        }

        // Genuine raw-field lookups: these describe current record state
        // (status, amount, institution) which can change over time, so
        // they intentionally stay live and are never cached.
        return query.Contains("wire", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("details", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("status", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("amount", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("institution", StringComparison.OrdinalIgnoreCase);
    }

    private string GetQuestionCategory(string query)
    {
        query = query.ToLower();

        if (query.Contains("why") ||
            query.Contains("root cause") ||
            query.Contains("rca"))
        {
            return "RCA";
        }

        if (query.Contains("trend") ||
            query.Contains("most") ||
            query.Contains("count") ||
            query.Contains("year") ||
            query.Contains("month"))
        {
            return "Trend";
        }

        if (query.Contains("summary"))
        {
            return "Summary";
        }

        return "General";
    }

    
    // Add near IsWireLookup/GetQuestionCategory
    private static string ExtractIdSignature(string text)
    {
        // 3+ digit numbers only — avoids false triggers on small counts/percentages
        var matches = Regex.Matches(text, @"\b\d{3,}\b");
        return string.Join(",", matches.Select(m => m.Value).OrderBy(x => x));
    }
}