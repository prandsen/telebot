using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Telebot.Bot;

public class BotWorker(TelebotService service, ILogger<BotWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BotWorker starting...");
        try
        {
            await service.StartAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("BotWorker cancellation requested.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in BotWorker.ExecuteAsync");
            throw;
        }
        finally
        {
            logger.LogInformation("BotWorker stopped.");
        }
    }
}
