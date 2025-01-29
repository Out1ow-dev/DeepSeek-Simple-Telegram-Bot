using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace DeepSeek.Bot.Models;

public class Choice
{
    [JsonPropertyName("message")]
    public Message Message { get; set; } = new Message();
}
