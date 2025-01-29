using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace DeepSeek.Bot.Models;

public class OpenRouterResponse
{
    [JsonPropertyName("choices")]
    public List<Choice> Choices { get; set; } = new List<Choice>();
}
