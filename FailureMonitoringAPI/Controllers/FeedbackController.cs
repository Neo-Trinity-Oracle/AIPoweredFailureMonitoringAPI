using FailureMonitoringAPI.Models;
using FailureMonitoringAPI.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("[controller]")]
public class FeedbackController : ControllerBase
{
    private readonly ElasticService _elastic;

    public FeedbackController(ElasticService elastic)
    {
        _elastic = elastic;
    }

    [HttpPost]
    public async Task<IActionResult> SaveFeedback(FeedbackRequest request)
    {
        if (request.Helpful)
        {
            if (!string.IsNullOrWhiteSpace(request.Id))
            {
                await _elastic.UpdateHelpfulAsync(request.Id);
            }
            else
            {
                await _elastic.UpdateHelpfulByQuestionAsync(request.Question);
            }
        }

        return Ok(new
        {
            Message = "Feedback saved successfully"
        });
    }
}