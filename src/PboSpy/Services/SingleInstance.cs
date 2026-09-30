using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;

namespace PboSpy.Services;

/// <summary>"Open with PboSpy" on a running copy hands the files to it instead of starting another window.</summary>
public static class SingleInstance
{
    private const string PipeName = "PboSpy.Open";
    private static Mutex _mutex;

    /// <summary>True when another PboSpy took the files; this process should exit.</summary>
    public static bool ForwardToRunning(string[] args)
    {
        _mutex = new Mutex(true, "PboSpy.SingleInstance", out var first);
        if (first)
        {
            return false;
        }
        var paths = args.Where(a => !a.StartsWith("--") && (File.Exists(a) || Directory.Exists(a))).Select(Path.GetFullPath).ToList();
        if (paths.Count == 0)
        {
            return false;
        }
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            var data = Encoding.UTF8.GetBytes(string.Join("\n", paths));
            client.Write(data, 0, data.Length);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Listen(Action<List<string>> open)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var paths = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (paths.Count > 0)
                    {
                        Application.Current?.Dispatcher.BeginInvoke(() => open(paths));
                    }
                }
                catch (Exception)
                {
                    Thread.Sleep(500);
                }
            }
        })
        { IsBackground = true, Name = "PboSpy open pipe" };
        thread.Start();
    }
}
