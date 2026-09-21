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

            var learnedAnswer =
                learnedKnowledge
                    .Where(x =>
                        x.Helpful &&
                        x.Confidence >= 0.70)
                    .OrderByDescending(x =>
                        x.HelpfulCount)
                    .FirstOrDefault();

            if (learnedAnswer != null)
            {
                return Ok(new
                {
                    Results = new List<FailureLog>(),

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

        if (!isWireLookup)
        {
            await _elastic.SaveKnowledgeAsync(
                new KnowledgeEntry
                {
                    Question = query,
                    Category = GetQuestionCategory(query),
                    AiAnswer = aiResponse,
                    Helpful = false,
                    HelpfulCount = 0,
                    Confidence = 0,
                    CreatedOn = DateTime.UtcNow,
                    Embedding = queryVector
                });
        }

        return Ok(new
        {
            Results = result.Documents,
            Insight = aiResponse
        });
    }

    private bool IsWireLookup(string query)
        {
            var wireMatch =
                Regex.Match(query, @"\b\d+\b");

            return wireMatch.Success &&
                   (
                       query.Contains("wire",
                           StringComparison.OrdinalIgnoreCase)
                       ||
                       query.Contains("details",
                           StringComparison.OrdinalIgnoreCase)
                       ||
                       query.Contains("status",
                           StringComparison.OrdinalIgnoreCase)
                       ||
                       query.Contains("amount",
                           StringComparison.OrdinalIgnoreCase)
                       ||
                       query.Contains("institution",
                           StringComparison.OrdinalIgnoreCase)
                   );
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
}