using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telebot.Bot;
using Telebot.Settings;

Host.CreateDefaultBuilder(args)
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Debug);
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<TelebotSettings>(context.Configuration.GetSection("Telebot"));
        
        services.AddSingleton<VideoDownloader>();
        services.AddSingleton<TelebotService>();
        services.AddHostedService<BotWorker>();

        services.AddHttpClient();
    })
    .Build()
    .Run();
