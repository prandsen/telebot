using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telebot.Settings;
using System.Diagnostics;
using Telebot.Errors;

namespace Telebot.Bot;

public class VideoDownloader
{
    private readonly string _proxy;
    private readonly string _cookies;
    private readonly int _maxSizeMb;
    private readonly ILogger<VideoDownloader> _logger;

    public VideoDownloader(IOptions<TelebotSettings> opts, ILogger<VideoDownloader> logger)
    {
        _logger = logger;
        var settings = opts.Value;
        _cookies = settings.Downloader.Cookies;
        _maxSizeMb = settings.Downloader.MaxSizeMb;
    }
    
    public async Task<DownloadResult> DownloadYoutubeAsync(string url)
    {
        var output = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");
        var args = GetDefaulyArgs(url, output);
        return await DownloadAsync(url, output, args); 
    }
    
    public async Task<DownloadResult> DownloadInstagramAsync(string url)
    {
        var output = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");
        
        var args =
            "-f mp4 --no-playlist " +
            (string.IsNullOrEmpty(_cookies) ? string.Empty : $"--cookies \"{_cookies}\" ") +
            $"-o \"{output}\" \"{url}\"";
        
        return await DownloadAsync(url, output, args);
    }
    
    public async Task<DownloadResult> DownloadTikTokAsync(string url)
    {
        var output = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");
        var args = GetDefaulyArgs(url, output);
        return await DownloadAsync(url, output, args);
    }

    private async Task<DownloadResult> DownloadAsync(string url, string output, string args)
    {
        _logger.LogInformation("Starting yt-dlp for URL {Url} -> {Output}. Args: {Args}", url, output, args);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start yt-dlp process.");
            return new DownloadResult(new Error("yt-dlp упал :("));
        }

        await process.WaitForExitAsync();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        _logger.LogDebug("yt-dlp stdout: {StdOut}", stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogWarning("yt-dlp stderr: {StdErr}", stderr);
        }

        if (!File.Exists(output))
        {
            _logger.LogWarning("yt-dlp did not produce an output file for URL {Url}", url);
            return new DownloadResult(new Error("yt-dlp вернул пустой output :("));
        }

        var fileLen = new FileInfo(output).Length;
        if (fileLen > _maxSizeMb * 1024L * 1024L)
        {
            _logger.LogWarning("Downloaded file {Path} is too large ({Size} bytes) > configured max {MaxMb} MB. Deleting.", output, fileLen, _maxSizeMb);
            try { File.Delete(output); } catch { /* best-effort */ }
            return new DownloadResult(new Error("Слишком жирное видео, не по шансам :("));
        }

        _logger.LogInformation("Downloaded video to {Path} ({Size} bytes)", output, fileLen);
        return new DownloadResult(output);
    }

    private string GetDefaulyArgs(string url, string output)
    {
        return $"-f \"bv*[filesize_approx<={_maxSizeMb}M]/bv*+ba/b\" " +
               "--merge-output-format mp4 " +
               "--no-playlist " +
               "--remux-video mp4 " +
               $"-o \"{output}\" \"{url}\"";
    }
}
