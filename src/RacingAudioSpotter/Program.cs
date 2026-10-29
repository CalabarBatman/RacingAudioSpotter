using RacingAudioSpotter.Application;
using RacingAudioSpotter.Telemetry;

Console.WriteLine("Racing Audio Spotter");
Console.WriteLine("====================");
Console.WriteLine("Press Ctrl+C to exit.");
Console.WriteLine();

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Let the monitor loop finish and clean up instead of killing the process.
    e.Cancel = true;
    shutdown.Cancel();
};

try
{
    using var provider = new IRacingTelemetryAdapter();
    var monitor = new TelemetryMonitor(provider, Console.Out);

    await monitor.RunAsync(TelemetryMonitor.DefaultPollInterval, shutdown.Token);

    Console.WriteLine();
    Console.WriteLine("Shutting down...");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Fatal error: {exception}");
    return 1;
}

Console.WriteLine("Stopped.");
return 0;
