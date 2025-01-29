using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace DeepSeek.Bot.Services
{
    public class OpenRouterService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        public OpenRouterService(IConfiguration configuration)
        {
            _apiKey = configuration["ApiKey"]
                ?? throw new ArgumentNullException("ApiKey not found in configuration");
            _model = configuration["OpenRouter:Model"] ?? "deepseek/deepseek-r1";
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://openrouter.ai/api/v1/")
            };
            ConfigureHttpClient();
        }

        private void ConfigureHttpClient()
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "YOUR_SITE_URL");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "YOUR_SITE_NAME");
        }

        public async IAsyncEnumerable<string> GetChatResponseStreamAsync(string userMessage)
        {
            var requestData = new
            {
                model = _model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = userMessage
                    }
                },
                stream = true
            };

            var json = JsonSerializer.Serialize(requestData, JsonSerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            var chunks = new List<string>();

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                {
                    continue;
                }

                Console.WriteLine($"Received line: {line}"); 

                var jsonString = line.Substring(6).Trim(); 
                if (jsonString == "[DONE]")
                {
                    break;
                }

                try
                {
                    var chunk = JsonSerializer.Deserialize<Chunk>(jsonString, JsonSerializerOptions);
                    if (chunk?.Choices != null && chunk.Choices.Length > 0)
                    {
                        var delta = chunk.Choices[0].Delta.Content;
                        if (!string.IsNullOrEmpty(delta))
                        {
                            chunks.Add(delta);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Error deserializing JSON: {ex.Message}");
                }
            }

            foreach (var chunk in chunks)
            {
                yield return chunk;
            }
        }

        private class Chunk
        {
            [JsonPropertyName("choices")]
            public Choice[] Choices
            {
                get; set;
            }
        }

        private class Choice
        {
            [JsonPropertyName("delta")]
            public Delta Delta
            {
                get; set;
            }
        }

        private class Delta
        {
            [JsonPropertyName("content")]
            public string Content
            {
                get; set;
            }
        }
    }
}