namespace Delegat.Services;

/// <summary>
/// Polls operation status and cancels a linked token when the user clicks Stop.
/// First check is immediate so Stop is not delayed by the poll interval.
/// </summary>
public static class OperationStopWatcher
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static Task WatchAsync(
        IServiceScopeFactory scopeFactory,
        string operationId,
        CancellationTokenSource cts)
    {
        return Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    using (var scope = scopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                        if (await OperationLifecycle.IsStopRequestedAsync(db, operationId, cts.Token))
                        {
                            await cts.CancelAsync();
                            return;
                        }
                    }

                    await Task.Delay(PollInterval, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }
}
