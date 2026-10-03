using FailureMonitoringAPI.Services;

public class LearningBackgroundService: BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public LearningBackgroundService( IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope =
                _scopeFactory.CreateScope();

            var elastic =
                scope.ServiceProvider
                .GetRequiredService<ElasticService>();

            var knowledge =
                await elastic.GetHelpfulKnowledgeAsync();

            var grouped =
                knowledge
                    .GroupBy(x => x.Category);

            foreach (var group in grouped)
            {
                Console.WriteLine(
                    $"Category:{group.Key}");

                Console.WriteLine(
                    $"Learned:{group.Count()}");
            }

            await Task.Delay(
                TimeSpan.FromHours(6),
                stoppingToken);
        }
    }
}