using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telebot.Settings;
using Telegram.Bot;
using System.Text.RegularExpressions;
using Telebot.Errors;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;

namespace Telebot.Bot;

public partial class TelebotService
{
    private readonly TelegramBotClient _bot;
    private readonly VideoDownloader _downloader;
    private readonly ILogger<TelebotService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Lazy<Task<string>> _triggers;
    private readonly TimeSpan _spamDelay;
    private readonly ConcurrentDictionary<string, DateTime> _triggerTimeouts = new();
    private readonly ConcurrentDictionary<string, int> _triggerCounters = new();
    private readonly Random _random = new(69);

    public TelebotService(IOptions<TelebotSettings> options, IHttpClientFactory httpClientFactory, VideoDownloader downloader, ILogger<TelebotService> logger)
    {
        _logger = logger;
        _downloader = downloader;
        _httpClientFactory = httpClientFactory;
        _spamDelay = options.Value.SpamDelay;
        var token = options.Value.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogCritical("Telegram bot token is missing in configuration (Bot:Token).");
            throw new ArgumentException("Bot token is required", nameof(options));
        }

        _triggers = new Lazy<Task<string>>(DownloadTriggers); 

        _bot = new TelegramBotClient(token);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting Telegram bot receiving.");
        _bot.StartReceiving(HandleUpdate, HandleError, cancellationToken: ct);
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("TelebotService stopping due to cancellation.");
        }
    }

    private async Task HandleUpdate(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            var handler = update.Type switch
            {
                UpdateType.Message => HandleMessage(update, ct),
                UpdateType.InlineQuery => HandleInlineQuery(update, ct),
                _ => new Task(() => _logger.LogDebug("Ignored update type: {Type}", update.Type))
            };
            await handler;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update of type {UpdateType}", update.Type);
        }
    }

    private async Task HandleMessage(Update update, CancellationToken ct)
    {
        var msg = update.Message;
        if (msg?.Text == null)
        {
            _logger.LogDebug("Ignored message without text or null message.");
            return;
        }

        _logger.LogInformation("Received message from chat {ChatId}: {Text}", msg.Chat.Id, msg.Text);

        var triggersText = await _triggers.Value;
        var triggers = await GetTriggers(msg.Text, triggersText);
        foreach (var trigger in triggers)
        {
            if (!trigger.IsTriggered) continue;

            if (!_triggerTimeouts.TryGetValue(trigger.Reply, out var triggerNextDate))
            {
                _triggerTimeouts.TryAdd(trigger.Reply, DateTime.UtcNow);
            }
            if (!_triggerCounters.TryGetValue(trigger.Reply, out var triggerCounter))
            {
                _triggerCounters.TryAdd(trigger.Reply, 0);
            }
            
            if (triggerNextDate > DateTime.UtcNow)
            {
                _triggerCounters.TryUpdate(trigger.Reply, 0, triggerCounter);
                continue;
            }

            if (triggerCounter >= 2)
            {
                await ReplyTo(msg, "Отпусти руки от клавы сдвгшник дерганный", ct);
                _triggerTimeouts.TryUpdate(trigger.Reply, DateTime.UtcNow + _spamDelay, triggerNextDate);
                _triggerCounters.TryUpdate(trigger.Reply, 0, triggerCounter);
                continue;
            }
            
            _triggerCounters.TryUpdate(trigger.Reply, triggerCounter + 1, triggerCounter);
            
            await ReplyTo(msg, trigger.Reply, ct);
            return;
        }
        
        var youtubeUrl = ExtractYoutubeUrl(msg.Text);
        var instagramUrl = ExtractInstagramUrl(msg.Text);
        var tiktokUrl = ExtractTikTokUrl(msg.Text);
        
        if (string.IsNullOrWhiteSpace(youtubeUrl) && 
            string.IsNullOrWhiteSpace(instagramUrl) && 
            string.IsNullOrWhiteSpace(tiktokUrl))
        {
            _logger.LogInformation("No supported video URL found in message {MessageId}.", msg.MessageId);
            return;
        }
        
        _logger.LogInformation("Download URLs: {Urls}", string.Join(", ", youtubeUrl, instagramUrl));

        await HandleUrl(msg, youtubeUrl, x => _downloader.DownloadYoutubeAsync(x), ct);
        await HandleUrl(msg, instagramUrl, x => _downloader.DownloadInstagramAsync(x), ct);
        await HandleUrl(msg, tiktokUrl, x => _downloader.DownloadTikTokAsync(x), ct);
    }
    
    private async Task HandleInlineQuery(Update update, CancellationToken ct)
    {
        var results = new List<InlineQueryResult>();
        
        var alko = 0.5 + _random.NextDouble() * _random.Next(1, 4);
        var alkoResult = new InlineQueryResultArticle(
            id: "alko",
            title: "Сколько выпить",
            inputMessageContent: new InputTextMessageContent(
                $"@{update.InlineQuery.From.Username} на сегодня твоя норма {alko:F1}л пивасика")
        );
        results.Add(alkoResult);

        var nahui = _random.NextDouble() > 0.5;
        var nahuiResult = new InlineQueryResultArticle(
            id: "nahui",
            title: "Иди нахуй",
            inputMessageContent: nahui
                ? new InputTextMessageContent($"@{update.InlineQuery.From.Username} сегодня ты идешь нахуй")
                : new InputTextMessageContent(
                    $"@{update.InlineQuery.From.Username} сегодня тебе повезло и ты не идешь нахуй")
        );
        results.Add(nahuiResult);

        await _bot.AnswerInlineQuery(
            inlineQueryId: update.InlineQuery!.Id,
            results: results,
            cacheTime: 0,
            isPersonal: true,
            cancellationToken: ct);
    }

    private async Task HandleUrl(Message msg, string url, Func<string, Task<DownloadResult>> downloadFunc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        
        var videoUrlResult = await downloadFunc(url);
        if (videoUrlResult.IsT1)
        {
            _logger.LogWarning("Video download failed or exceeded size limit for URL: {Url}", url);
            await ReplyTo(msg, videoUrlResult.AsT1.Message, ct);
            return;
        }

        var videoPath = videoUrlResult.AsT0;
        _logger.LogInformation("Downloaded video available at {Path}. Sending to chat {ChatId}", videoPath, msg.Chat.Id);

        try
        {
            await using var fs = File.OpenRead(videoPath);
            var input = new InputFileStream { Content = fs };
            var replyParams = new ReplyParameters{ MessageId =  msg.MessageId };
            await _bot.SendVideo(msg.Chat.Id, input, replyParameters: replyParams, cancellationToken: ct);
            _logger.LogInformation("Sent video to chat {ChatId}", msg.Chat.Id);
        }
        catch (Exception ex)
        {
            try
            {
                await ReplyTo(msg, "Чет я приуныл и не смог отправить видео", ct);
            }
            finally
            {
                _logger.LogError(ex, "Failed to send video to chat {ChatId}", msg.Chat.Id);
            }
        }
        finally
        {
            try
            {
                File.Delete(videoPath);
                _logger.LogDebug("Deleted temporary video file {Path}", videoPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temporary file {Path}", videoPath);
            }
        }
    }

    [GeneratedRegex(@"(https?://(?:www\.)?(?:youtube\.com)/shorts/[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex YoutubeRegex();
    
    private static string? ExtractYoutubeUrl(string text)
    {
        var match = YoutubeRegex().Match(text);
        var url = match.Success ? match.Groups[1].Value : null;
        return url;
    }
    
    [GeneratedRegex(@"(https?://(?:www\.)?instagram\.com/[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex InstagramRegex();
    
    [GeneratedRegex(@"https?://(?:www\.)?instagram\.com/(p|reel|tv)/([^/?#]+)/?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex InstagramShortRegex();
    
    private static string? ExtractInstagramUrl(string text)
    {
        var instMatch = InstagramRegex().Match(text);
        var instUrl = instMatch.Success ? instMatch.Groups[1].Value : null;
        
        var instShortMatch = InstagramShortRegex().Match(text);
        var instShortUrl = instShortMatch.Success ? instShortMatch.Groups[1].Value : null;
        
        return instUrl ?? instShortUrl;
    }
    
    [GeneratedRegex(@"(https?://(?:www\.)?(?:tiktok\.com)/@[^\s]+/video/[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TikTokRegex();
    [GeneratedRegex(@"(https?://(?:www\.)?(?:vt.tiktok\.com)/[^\s/@]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TikTokShortRegex();
    
    private static string? ExtractTikTokUrl(string text)
    {
        var match = TikTokRegex().Match(text);
        var url = match.Success ? match.Groups[1].Value : null;
        
        var matchShort = TikTokShortRegex().Match(text);
        var shortUrl = matchShort.Success ? matchShort.Groups[1].Value : null;
        
        return url ?? shortUrl;
    }

    private async Task<string> DownloadTriggers()
    {
        using var httpClient = _httpClientFactory.CreateClient();
        
        var stream = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            "https://raw.githubusercontent.com/prandsen/telebot/refs/heads/main/TriggerList.txt"));
        if (!stream.IsSuccessStatusCode)
        {
            _logger.LogError("Getting trigger list returned status code {StatusCode}", stream.StatusCode);
            return null;
        }

        var content = await stream.Content.ReadAsStringAsync();
        return content;
    }
    
    private async Task<IReadOnlyCollection<(bool IsTriggered, string Reply)>> GetTriggers(string msgText, string triggersText)
    {
        if (string.IsNullOrWhiteSpace(triggersText))
        {
            _logger.LogWarning("Trigger list is null or empty");
            return [];
        }
        
        var triggerRows = triggersText.Replace("\r\n","\n").Split("\n", StringSplitOptions.RemoveEmptyEntries);
        
        var triggers = new List<(bool IsTriggered, string Reply)>();
        for (var i = 0; i < triggerRows.Length; i++)
        {
            var item = triggerRows[i];
            var kv = item.Split("=", StringSplitOptions.RemoveEmptyEntries);
            if (kv.Length != 2)
            {
                LogError(i);
                continue;
            }

            var patterns = kv[0].Split(";", StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            var replies = kv[1].Split(";", StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            
            if (patterns.Length == 0 || replies.Length == 0)
            {
                LogError(i);
                continue;
            }
            
            var trigger = TriggeredReply(msgText, patterns, replies);
            triggers.Add(trigger);
        }
        return triggers;
        
        void LogError(int index) => _logger.LogWarning("Can't parse trigger row. Index: {Index}", index);
        
        (bool IsTriggered, string Reply) TriggeredReply(string msgText, IReadOnlyCollection<string> patterns, IReadOnlyList<string> replies)
        {
            var trigger = patterns.Any(x => msgText.Contains(x, StringComparison.InvariantCultureIgnoreCase));
            var randomIndex = _random.Next(0, replies.Count);
            return (trigger, replies[randomIndex]);
        }
    }

    private Task HandleError(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "Telegram client error");
        return Task.CompletedTask;
    }

    private async Task ReplyTo(Message msg, string text, CancellationToken ct)
    {
        var replyParams = new ReplyParameters { MessageId = msg.MessageId };
        await _bot.SendMessage(msg.Chat.Id, text, replyParameters: replyParams, cancellationToken: ct);
    }
}
