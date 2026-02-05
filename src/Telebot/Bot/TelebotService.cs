using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telebot.Settings;
using Telegram.Bot;
using System.Text.RegularExpressions;
using Telebot.Errors;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telebot.Bot;

public partial class TelebotService
{
    private readonly TelegramBotClient _bot;
    private readonly VideoDownloader _downloader;
    private readonly ILogger<TelebotService> _logger;
    private readonly TelebotSettings _botSettings;

    public TelebotService(IOptions<TelebotSettings> options, VideoDownloader downloader, ILogger<TelebotService> logger)
    {
        _logger = logger;
        _downloader = downloader;
        _botSettings = options.Value;
        var token = options.Value.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogCritical("Telegram bot token is missing in configuration (Bot:Token).");
            throw new ArgumentException("Bot token is required", nameof(options));
        }

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
            if (update.Type != UpdateType.Message)
            {
                _logger.LogDebug("Ignored update type: {Type}", update.Type);
                return;
            }

            var msg = update.Message;
            if (msg?.Text == null)
            {
                _logger.LogDebug("Ignored message without text or null message.");
                return;
            }

            _logger.LogInformation("Received message from chat {ChatId}: {Text}", msg.Chat.Id, msg.Text);

            var isLibaraha = IsLiberaha(msg.Text);
            if (_botSettings.GloryRussia.Enabled && isLibaraha)
            {
                await ReplyTo(msg, "Слава России!!! Либерахи сосать!!!", ct);
            }
            
            var isTagged = IsTagged(msg.Text);
            if (isTagged)
            {
                await ReplyTo(msg, "Хули ты меня тегаешь долбоеб", ct);
                return;
            }
            
            var isDown = IsDown(msg.Text);
            if (isDown)
            {
                await ReplyTo(msg, "Единственный тут даун это ты", ct);
                return;
            }
            
            var youtubeUrl = ExtractYoutubeUrl(msg.Text);
            var instagramUrl = ExtractInstagramUrl(msg.Text);
            if (string.IsNullOrWhiteSpace(youtubeUrl) && string.IsNullOrWhiteSpace(instagramUrl))
            {
                _logger.LogInformation("No supported video URL found in message {MessageId}.", msg.MessageId);
                return;
            }
            
            _logger.LogInformation("Download URLs: {Urls}", string.Join(", ", youtubeUrl, instagramUrl));

            await HandleUrl(msg, youtubeUrl, x => _downloader.DownloadYoutubeAsync(x), ct);
            await HandleUrl(msg, instagramUrl, x => _downloader.DownloadInstagramAsync(x), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update of type {UpdateType}", update.Type);
        }
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

    private static string? ExtractYoutubeUrl(string text)
    {
        var ytMatch = YoutubeRegex().Match(text);
        var youtubeUrl = ytMatch.Success ? ytMatch.Groups[1].Value : null;
        return youtubeUrl;
    }
    
    private static string? ExtractInstagramUrl(string text)
    {
        var instMatch = InstagramRegex().Match(text);
        var instUrl = instMatch.Success ? instMatch.Groups[1].Value : null;
        
        var instShortMatch = InstagramShortRegex().Match(text);
        var instShortUrl = instShortMatch.Success ? instShortMatch.Groups[1].Value : null;
        
        return instUrl ?? instShortUrl;
    }
    
    private static bool IsLiberaha(string text)
    {
        var patterns = new[]
        {
            "либераха", "либерал", "либерашка", "1984"
        };
        return patterns.Any(x => text.Contains(x, StringComparison.InvariantCultureIgnoreCase));
    }
    
    private static bool IsTagged(string text)
    {
        var patterns = new[] { "@down_yt_ig_bot" };
        return patterns.Any(x => text.Contains(x, StringComparison.InvariantCultureIgnoreCase));
    }
    
    private static bool IsDown(string text)
    {
        var patterns = new[] { "даун" };
        return patterns.Any(x => text.Contains(x, StringComparison.InvariantCultureIgnoreCase));
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

    [GeneratedRegex(@"(https?://(?:www\.)?(?:youtube\.com|youtu\.be)/[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex YoutubeRegex();
    
    [GeneratedRegex(@"(https?://(?:www\.)?instagram\.com/[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex InstagramRegex();
    
    [GeneratedRegex(@"instagram\.com/(p|reel|tv)/([^/?#]+)/?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex InstagramShortRegex();
}
