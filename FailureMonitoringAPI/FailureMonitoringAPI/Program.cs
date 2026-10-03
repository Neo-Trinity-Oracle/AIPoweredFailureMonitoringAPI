
using FailureMonitoringAPI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// ✅ Dependency Injection
builder.Services.AddSingleton<ElasticService>();

builder.Services.AddHttpClient<EmbeddingService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<AIService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedService<LearningBackgroundService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAll");

app.MapControllers();
app.Run();
