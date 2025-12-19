using System.Net;

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

        // Increase HTTP connection limit to allow concurrent API requests from Deal/Monitor tabs
        // Default is 2 connections per host, which causes blocking when both tabs query simultaneously
        ServicePointManager.DefaultConnectionLimit = 20;

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        // Run startup splash form which handles all validation checks
        // Using ApplicationContext allows us to switch from splash to main form
        Application.Run(new StartupApplicationContext());
    }
}
