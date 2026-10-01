namespace PboSpy;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--mcp", StringComparison.OrdinalIgnoreCase)))
        {
            return Services.Mcp.McpServer.Run();
        }
        if (!Services.TestMode.On && Services.SingleInstance.ForwardToRunning(args))
        {
            return 0;
        }
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
