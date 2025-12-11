namespace RoMarketCrawler;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // Run test mode if --test argument is passed
        if (args.Length > 0 && args[0] == "--test")
        {
            // Run async test synchronously to maintain STA thread
            TestParser.RunTest().GetAwaiter().GetResult();
            return;
        }

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}
