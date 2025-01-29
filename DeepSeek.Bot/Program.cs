using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using DeepSeek.Bot.Services;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;

namespace DeepSeek.Bot
{
    internal class Program
    {
        private static IConfigurationRoot _configuration;
        private static TelegramBotClient _botClient;
        private static OpenRouterService _openRouterService;
        private static ConcurrentDictionary<long, Task> _processingTasks = new ConcurrentDictionary<long, Task>();

        static async Task Main(string[] args)
        {
            Console.WriteLine("Initializing bot...");
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appconfig.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
            _openRouterService = new OpenRouterService(_configuration);
            var token = _configuration["Token"]
                ?? throw new InvalidOperationException("Token not found in configuration");
            _botClient = new TelegramBotClient(token);
            using var cts = new CancellationTokenSource();
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };
            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );
            var me = await _botClient.GetMeAsync();
            Console.WriteLine($"Bot @{me.Username} started successfully");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            cts.Cancel();
        }

        private static async Task HandleUpdateAsync(
            ITelegramBotClient botClient,
            Update update,
            CancellationToken cancellationToken)
        {
            if (update.Message is not { } message || message.Text is not { } messageText)
                return;

            var chatId = message.Chat.Id;
            Console.WriteLine($"Processing: '{messageText}'");

            try
            {
                if (messageText == "/start")
                    return;

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Ваш запрос обрабатывается...",
                    cancellationToken: cancellationToken);

                if (_processingTasks.TryGetValue(chatId, out var existingTask))
                {
                    await existingTask;
                    return;
                }

                var processingTask = Task.Run(async () =>
                {
                    try
                    {
                        var responseStream = _openRouterService.GetChatResponseStreamAsync(messageText);
                        var stringBuilder = new StringBuilder();
                        var messageId = 0;

                        await foreach (var chunk in responseStream)
                        {
                            stringBuilder.Append(chunk);
                            if (messageId == 0)
                            {
                                var result = await botClient.SendTextMessageAsync(
                                    chatId: chatId,
                                    text: stringBuilder.ToString(),
                                    cancellationToken: cancellationToken);
                                messageId = result.MessageId;
                            }
                            else
                            {
                                await botClient.EditMessageTextAsync(
                                    chatId: chatId,
                                    messageId: messageId,
                                    text: stringBuilder.ToString(),
                                    cancellationToken: cancellationToken);
                            }
                        }

                        await botClient.EditMessageTextAsync(
                            chatId: chatId,
                            messageId: messageId,
                            text: stringBuilder.ToString() + "\n\nЗапрос завершен.",
                            cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Sorry, I encountered an error processing your request",
                            cancellationToken: cancellationToken);
                    }
                    finally
                    {
                        _processingTasks.TryRemove(chatId, out _);
                    }
                });

                _processingTasks[chatId] = processingTask;
                await processingTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Sorry, I encountered an error processing your request",
                    cancellationToken: cancellationToken);
            }
        }

        private static Task HandlePollingErrorAsync(
            ITelegramBotClient botClient,
            Exception exception,
            CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };
            Console.WriteLine(errorMessage);
            return Task.CompletedTask;
        }
    }
}