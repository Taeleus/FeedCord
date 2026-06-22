namespace FeedCord;

public class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 &&
            args[0].Equals("--health-check", StringComparison.OrdinalIgnoreCase))
        {
            return Infrastructure.Workers.HealthHeartbeatWorker.IsHealthy() ? 0 : 1;
        }

        Startup.Initialize(args);
        return 0;
    }
}
