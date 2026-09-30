using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PboSpy.Services.Mcp;

/// <summary>Adds or shows the Claude Desktop entry that starts "PboSpy.exe --mcp".</summary>
public static class ClaudeSetup
{
    public const string ServerName = "pbospy";

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PboSpy.exe");

    public static JsonObject Entry => new()
    {
        ["command"] = ExePath,
        ["args"] = new JsonArray("--mcp")
    };

    public static string Snippet => new JsonObject
    {
        ["mcpServers"] = new JsonObject { [ServerName] = Entry }
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Config files of the installed Claude Desktop (normal and Microsoft Store builds).</summary>
    public static List<string> ConfigFiles()
    {
        var result = new List<string>();
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
        if (Directory.Exists(roaming))
        {
            result.Add(Path.Combine(roaming, "claude_desktop_config.json"));
        }
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var package in Directory.EnumerateDirectories(packages, "Claude_*"))
            {
                var folder = Path.Combine(package, "LocalCache", "Roaming", "Claude");
                if (Directory.Exists(folder))
                {
                    result.Add(Path.Combine(folder, "claude_desktop_config.json"));
                }
            }
        }
        if (result.Count == 0)
        {
            result.Add(Path.Combine(roaming, "claude_desktop_config.json"));
        }
        return result;
    }

    /// <summary>Writes the entry, keeping everything else in the file and a .bak of the old one.</summary>
    public static void Install(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        JsonObject root = null;
        if (File.Exists(file))
        {
            var text = File.ReadAllText(file);
            File.WriteAllText(file + ".bak", text);
            root = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
        }
        root ??= new JsonObject();
        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }
        servers[ServerName] = Entry;
        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
