using PboSpy.Localization;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;

namespace PboSpy.Services;

/// <summary>
/// Looks for a newer release on GitHub, downloads its zip and swaps the files in after PboSpy closes.
/// The repository is private, so it asks the GitHub CLI (gh) first, then an optional token, then anonymously.
/// </summary>
public static class Updater
{
    public const string Repo = "KizzyTheDog/PboSpy";

    private static string _pendingScript;

    public static Version Current => typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>Background check at startup; quiet unless there's an update.</summary>
    public static void CheckOnStartup()
    {
        if (AppSettings.Default.AutoUpdate)
        {
            _ = Task.Run(() => Check(quiet: true));
        }
        Application.Current.Exit += (_, _) =>
        {
            if (_pendingScript != null)
            {
                Run(_pendingScript);
            }
        };
    }

    public static async Task Check(bool quiet)
    {
        try
        {
            var release = await Latest();
            var tag = release?["tag_name"]?.GetValue<string>() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || latest <= Current)
            {
                if (!quiet)
                {
                    Show(Loc.F("Update.UpToDate", Current.ToString(3)), MessageBoxButton.OK);
                }
                return;
            }
            var asset = (release["assets"] as JsonArray)?.OfType<JsonObject>()
                .FirstOrDefault(a => a["name"]?.GetValue<string>()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
            if (asset == null)
            {
                return;
            }
            var folder = Path.Combine(Path.GetTempPath(), "PboSpyUpdate", tag);
            var zip = Path.Combine(folder, asset["name"].GetValue<string>());
            var files = Path.Combine(folder, "files");
            if (!Directory.Exists(files))
            {
                Directory.CreateDirectory(folder);
                await Download(tag, asset, zip);
                ZipFile.ExtractToDirectory(zip, files, true);
            }
            var script = WriteScript(files);
            var answer = Show(Loc.F("Update.Ready", latest.ToString(3), Current.ToString(3)), MessageBoxButton.YesNo);
            if (answer == MessageBoxResult.Yes)
            {
                Run(script + " restart");
                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            }
            else
            {
                _pendingScript = script;
            }
        }
        catch (Exception ex)
        {
            if (!quiet)
            {
                Show(Loc.F("Update.Failed", ex.Message), MessageBoxButton.OK);
            }
        }
    }

    private static MessageBoxResult Show(string text, MessageBoxButton buttons) =>
        Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(Application.Current.MainWindow, text, "PboSpy", buttons, MessageBoxImage.Information));

    private static async Task<JsonObject> Latest()
    {
        var viaCli = Gh($"api repos/{Repo}/releases/latest");
        if (viaCli != null)
        {
            return JsonNode.Parse(viaCli) as JsonObject;
        }
        using var client = Client();
        return JsonNode.Parse(await client.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest")) as JsonObject;
    }

    private static async Task Download(string tag, JsonObject asset, string zip)
    {
        var folder = Path.GetDirectoryName(zip);
        if (Gh($"release download {tag} -R {Repo} -p \"{asset["name"]}\" -D \"{folder}\" --clobber") != null && File.Exists(zip))
        {
            return;
        }
        using var client = Client();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
        await File.WriteAllBytesAsync(zip, await client.GetByteArrayAsync(asset["url"].GetValue<string>()));
    }

    private static HttpClient Client()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PboSpy-Updater");
        var token = AppSettings.Default.UpdateToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.Trim());
        }
        return client;
    }

    // Null when gh isn't installed, isn't logged in, or fails.
    private static string Gh(string arguments)
    {
        try
        {
            var info = new ProcessStartInfo("gh", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(info);
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(120_000);
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Waits for this PboSpy to close, copies the new files over the install folder, optionally starts it again.
    // Files that aren't in the release (BisDll.dll, settings) are left alone.
    private static string WriteScript(string files)
    {
        var install = AppContext.BaseDirectory.TrimEnd('\\');
        var pid = Environment.ProcessId;
        var script = Path.Combine(Path.GetDirectoryName(files), "update.cmd");
        File.WriteAllText(script, $"""
            @echo off
            :wait
            tasklist /fi "PID eq {pid}" | find "{pid}" >nul && (timeout /t 1 >nul & goto wait)
            robocopy "{files}" "{install}" /E /NFL /NDL /NJH /NJS /R:5 /W:1 >nul
            if "%1"=="restart" start "" "{install}\PboSpy.exe"
            """);
        return script;
    }

    private static void Run(string scriptAndArgs)
    {
        var parts = scriptAndArgs.Split(" restart");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{parts[0]}\"{(parts.Length > 1 ? " restart" : "")}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
