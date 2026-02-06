using System;

namespace Telebot.Settings;

public class TelebotSettings
{
    public required string Token { get; set; }
    public DownloaderSettings Downloader { get; set; } = new ();
}

public class DownloaderSettings
{
    public string? Proxy { get; set; }
    public string? Cookies { get; set; }
    public int MaxSizeMb { get; set; } = 50;
}
