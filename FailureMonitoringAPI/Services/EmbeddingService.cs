
using FailureMonitoringAPI.Models;
using Microsoft.Extensions.AI;
using Newtonsoft.Json;

public class EmbeddingService
{
    //private readonly HttpClient _client;
    //private readonly IConfiguration _config;

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
    }

    //public EmbeddingService(HttpClient client,IConfiguration config)
    //{
    //    _client = client;
    //    _config = config;
    //}
    // Same signature as before — no callers need to change.
    public async Task<float[]> GetEmbedding(string text)
    {
        var result = await _embeddingGenerator.GenerateAsync(text);
        return result.Vector.ToArray();
    }
    //public async Task<float[]> GetEmbedding(string text)
    //{
    //    var endpoint =_config["AzureOpenAI:Endpoint"];
    //    var apiKey =_config["AzureOpenAI:ApiKey"];
    //    var model =_config["AzureOpenAI:EmbeddingModel"];
    //    var url = $"{endpoint}/embeddings";

    //    _client.DefaultRequestHeaders.Clear();
    //    _client.DefaultRequestHeaders.Add("api-key", apiKey);

    //    var body = new
    //    {
    //        model = model,
    //        input = text
    //    };

    //    var response =await _client.PostAsJsonAsync(url, body);

    //    var json =await response.Content.ReadAsStringAsync();

    //    if (!response.IsSuccessStatusCode)
    //    {
    //        throw new Exception($"Embedding API Error: {json}");
    //    }

    //    var result =JsonConvert.DeserializeObject<EmbeddingResponse>(json);
    //    if (result?.Data == null || !result.Data.Any())
    //    {
    //        throw new Exception("Embedding response is empty or invalid.");
    //    }
    //    return result.Data.First().Embedding.ToArray();
    //}
}
