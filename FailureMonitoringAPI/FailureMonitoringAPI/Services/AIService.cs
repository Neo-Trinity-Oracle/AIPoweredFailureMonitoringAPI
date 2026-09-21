using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FailureMonitoringAPI.Models;

namespace FailureMonitoringAPI.Services
{

    public class ChatResponse
    {
        public List<Choice> Choices { get; set; }
    }
    public class Choice
    {
        public Message Message { get; set; }
    }
    public class Message
    {
        public string Content { get; set; }
    }

    public class AIService
    {
        private readonly HttpClient _client;
        private readonly IConfiguration _config;

        public AIService(HttpClient client, IConfiguration configuration)
        {
            _client = client;
            _config = configuration;
        }    

        //public async Task<string> Generate(
        //    string question,
        //    string context,
        //    List<KnowledgeEntry> learnedKnowledge)
        //{
        //    var endpoint =_config["AzureAI:ResponsesEndpoint"];
        //    var apiKey =_config["AzureAI:ApiKey"];
        //    var model =_config["AzureAI:Model"];

        //    _client.DefaultRequestHeaders.Clear();
        //    _client.DefaultRequestHeaders.Add("api-key", apiKey);



        //    //var requestBody = new
        //    //{
        //    //    model = model,
        //    //    input =
        //    //        $"""
        //    //        You are an AI Failure Monitoring Assistant.

        //    //        Your responsibility is to analyze the provided wire data and answer user questions.

        //    //        Guidelines:

        //    //        - Use the supplied records as the primary source.
        //    //        - Understand the user's intent.
        //    //        - Answer naturally in business language.
        //    //        - Provide direct answers when specific values are requested.
        //    //        - For analytical questions, identify trends, patterns, causes and impacts.
        //    //        - For RCA requests, explain likely causes using available evidence.
        //    //        - For comparison requests, compare relevant records.
        //    //        - For summary requests, summarize key findings.
        //    //        - If multiple records exist, aggregate information when helpful.
        //    //        - If information is unavailable, clearly state what is missing.
        //    //        - Never invent values not found in the data.

        //    //        Question:
        //    //        {question}

        //    //        Records:
        //    //        {context}

        //    //        Provide the best possible answer.
        //    //        """
        //    //                };


        //    var requestBody = new
        //    {
        //        model = model,
        //        input =
        //        $"""
        //        You are an AI Failure Monitoring Assistant.

        //        Your responsibility is to analyze the provided wire data and answer user questions accurately and professionally.

        //        Guidelines:

        //        Data Usage
        //        ----------
        //        - Use the supplied records as the primary source of truth.
        //        - Understand the user's intent before answering.
        //        - Never invent values, records, causes, or resolutions that are not supported by the provided data.
        //        - If information is unavailable, clearly state what is missing.

        //        Answering Behaviour
        //        -------------------
        //        - Answer naturally using clear business language.
        //        - Provide direct answers when specific values are requested.
        //        - If multiple records exist, aggregate information where appropriate.
        //        - For comparison requests, compare relevant records.
        //        - For summary requests, summarize the most important findings.

        //        Failure Analysis
        //        ----------------
        //        - For analytical questions, identify trends, patterns, causes, and operational impacts.
        //        - For RCA (Root Cause Analysis) requests, explain likely causes using available evidence from the records.
        //        - Where possible, identify recurring failure patterns.

        //        Self-Learning & Historical Knowledge
        //        ------------------------------------
        //        - If historical RCA information, learned knowledge, or previously validated resolutions are provided, consider them as supporting evidence.
        //        - Prefer historically successful resolutions when they are relevant to the current scenario.
        //        - Explain how historical patterns relate to the current issue.
        //        - Suggest corrective actions when known from historical resolutions.
        //        - Distinguish clearly between:
        //            * Information found in current records.
        //            * Historical learnings.
        //            * Recommendations based on previous resolutions.
        //        - Do not present suggestions as facts unless supported by the supplied data.

        //        Response Quality
        //        ----------------
        //        - Keep answers concise, relevant, and actionable.
        //        - When appropriate, include:
        //            * Root Cause
        //            * Impact
        //            * Recommended Action
        //            * Business Risk
        //        - Focus on helping operations teams investigate and resolve issues efficiently.

        //        Question:
        //        {question}

        //        Records:
        //        {context}

        //        Provide the best possible answer.
        //        """
        //    };


        //    var response = await _client.PostAsJsonAsync(endpoint,requestBody);

        //    var json = await response.Content.ReadAsStringAsync();

        //    if (!response.IsSuccessStatusCode)
        //    {
        //        throw new Exception(json);
        //    }

        //    dynamic obj = JsonConvert.DeserializeObject(json);

        //    return obj.output[0].content[0].text.ToString();
        //}



        //==============================================

        public async Task<string> Generate(string question,string context,List<KnowledgeEntry> learnedKnowledge)
        {
            var endpoint = _config["AzureAI:ResponsesEndpoint"];
            var apiKey = _config["AzureAI:ApiKey"];
            var model = _config["AzureAI:Model"];

            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add("api-key", apiKey);

            var historicalContext =
                learnedKnowledge.Any()
                ? string.Join(
                    "\n\n------------------------\n\n",
                    learnedKnowledge
                        .OrderByDescending(x => x.HelpfulCount)
                        .Take(5)
                        .Select(x =>
                            $"Previous Question: {x.Question}\n" +
                            $"Previous Answer: {x.AiAnswer}\n" +
                            $"Root Cause: {x.RootCause}\n" +
                            $"Resolution: {x.Resolution}")
                  )
                : "No historical knowledge available.";

            var requestBody = new
            {
                model = model,

                input =
                $"""
                You are an AI Failure Monitoring Assistant.

                Your responsibility is to analyze the provided wire data and answer user questions accurately and professionally.

                Guidelines:

                Data Usage
                ----------
                - Use the supplied records as the primary source of truth.
                - Understand the user's intent before answering.
                - Never invent values, records, causes, or resolutions that are not supported by the provided data.
                - If information is unavailable, clearly state what is missing.

                Answering Behaviour
                -------------------
                - Answer naturally using clear business language.
                - Provide direct answers when specific values are requested.
                - If multiple records exist, aggregate information where appropriate.
                - For comparison requests, compare relevant records.
                - For summary requests, summarize the most important findings.

                Failure Analysis
                ----------------
                - For analytical questions, identify trends, patterns, causes, and operational impacts.
                - For RCA (Root Cause Analysis) requests, explain likely causes using available evidence from the records.
                - Where possible, identify recurring failure patterns.

                Self-Learning & Historical Knowledge
                ------------------------------------
                - Historical learnings are provided below.
                - Use them only as supporting evidence.
                - Current records always take precedence.
                - If a historical resolution is relevant, mention it as a recommendation.
                - Never treat historical learnings as facts unless supported by current records.
                - Clearly distinguish:
                    * Current Record Findings
                    * Historical Learnings
                    * Recommendations

                Historical Learnings
                --------------------

                {historicalContext}

                Response Quality
                ----------------
                - Keep answers concise, relevant, and actionable.
                - When appropriate, include:
                    * Root Cause
                    * Impact
                    * Recommended Action
                    * Business Risk
                - Focus on helping operations teams investigate and resolve issues efficiently.

                Question:
                {question}

                Records:
                {context}

                Provide the best possible answer.
                """
            };

            var response =
                await _client.PostAsJsonAsync(
                    endpoint,
                    requestBody);

            var json =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(json);
            }

            dynamic obj = JsonConvert.DeserializeObject(json);

            return obj.output[0].content[0].text.ToString();
        }

    }
}