namespace Telebot.Settings;

public class TelebotSettings
{
    public required string Token { get; set; }
    public required TimeSpan SpamDelay { get; set; } = TimeSpan.FromSeconds(60);
    public DownloaderSettings Downloader { get; set; } = new ();
}

public class DownloaderSettings
{
    public string? Cookies { get; set; }
    public int MaxSizeMb { get; set; } = 20;
}
