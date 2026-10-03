
using Newtonsoft.Json;
using FailureMonitoringAPI.Models;

public class EmbeddingService
{
    private readonly HttpClient _client;
    private readonly IConfiguration _config;

    public EmbeddingService(HttpClient client,IConfiguration config)
    {
        _client = client;
        _config = config;
    }

    public async Task<float[]> GetEmbedding(string text)
    {
        var endpoint =_config["AzureOpenAI:Endpoint"];
        var apiKey =_config["AzureOpenAI:ApiKey"];
        var model =_config["AzureOpenAI:EmbeddingModel"];
        var url = $"{endpoint}/embeddings";

        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("api-key", apiKey);

        var body = new
        {
            model = model,
            input = text
        };

        var response =await _client.PostAsJsonAsync(url, body);

        var json =await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Embedding API Error: {json}");
        }

        var result =JsonConvert.DeserializeObject<EmbeddingResponse>(json);
        if (result?.Data == null || !result.Data.Any())
        {
            throw new Exception("Embedding response is empty or invalid.");
        }
        return result.Data.First().Embedding.ToArray();
    }
}
