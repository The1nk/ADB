using BotRunner;

// Must run before any screen capture / cursor positioning so capture and click share one pixel space.
NativeDpi.EnsurePerMonitorV2();

return await Cli.RunAsync(args, Console.Out, Console.Error, CancellationToken.None);
