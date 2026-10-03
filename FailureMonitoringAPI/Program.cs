using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
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

// ✅ Chat client (replaces raw HttpClient calls in AIService)
var aiEndpoint = builder.Configuration["AzureAI:Endpoint"]!;
var aiModel = builder.Configuration["AzureAI:Model"]!;


var azureTenantId = builder.Configuration["Azure:TenantId"]!; // put the Tenant ID from the portal here

var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    TenantId = azureTenantId
});


builder.Services.AddSingleton<IChatClient>(sp =>
    new AzureOpenAIClient(new Uri(aiEndpoint), credential)
        .GetChatClient(aiModel)
        .AsIChatClient());


// ✅ Embedding client (replaces raw HttpClient calls in EmbeddingService)
var embeddingEndpoint = builder.Configuration["AzureOpenAI:Endpoint"]!;
var embeddingModel = builder.Configuration["AzureOpenAI:EmbeddingModel"]!;

builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
    new AzureOpenAIClient(new Uri(embeddingEndpoint), credential)
        .GetEmbeddingClient(embeddingModel)
        .AsIEmbeddingGenerator());

builder.Services.AddSingleton<ElasticService>();
builder.Services.AddSingleton<AIService>();
builder.Services.AddSingleton<EmbeddingService>();

builder.Services.AddHostedService<LearningBackgroundService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowAll");
app.MapControllers();
app.Run();