using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

public static class Utility
{
    public static string exePath = "";
    public static bool useDiscord = false;
    public static string discordWebhook = "";
    public static string configPath = "config.txt";
    public static string logPath = "restart.log";
    public static string shardName = "Unknown";
    public static string iniPath = "uox3.ini"; // Default fallback
    public static string statusHtmlPath = "serverstatus.html";
    public static bool htmlStatusEnabled = true;
    public static int htmlStatusInterval = 60; // seconds

    public static void LoadConfig()
    {
        if (!File.Exists(configPath))
            return;

        var lines = File.ReadAllLines(configPath);
        foreach (var line in lines)
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                continue;

            var key = parts[0].Trim().ToLower();
            var value = parts[1].Trim();

            switch (key)
            {
                case "exepath":
                    exePath = value;
                    break;
                case "inipath":
                    iniPath = value;
                    break;
                case "usediscord":
                    useDiscord = value.ToLower() == "true";
                    break;
                case "discordwebhook":
                    discordWebhook = value;
                    break;
                case "statushtmlpath":
                    statusHtmlPath = value;
                    break;
                case "htmlstatusenabled":
                    htmlStatusEnabled = value.ToLower() == "true";
                    break;
                case "htmlstatusinterval":
                    int.TryParse(value, out htmlStatusInterval);
                    if (htmlStatusInterval < 10) htmlStatusInterval = 10; // safety lower bound
                    break;
            }
        }


    }

    public static void SaveConfig()
    {
        var lines = new[]
        {
            $"exePath={exePath}",
            $"iniPath={iniPath}",
            $"useDiscord={useDiscord.ToString().ToLower()}",
            $"discordWebhook={discordWebhook}",
            $"statusHtmlPath={statusHtmlPath}",
            $"htmlStatusEnabled={htmlStatusEnabled.ToString().ToLower()}",
            $"htmlStatusInterval={htmlStatusInterval}"
        };
        File.WriteAllLines(configPath, lines);
    }

    public static void SendDiscordAlert(string message)
    {
        if (!useDiscord || string.IsNullOrWhiteSpace(discordWebhook))
            return;

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { content = message });
            using (var client = new System.Net.Http.HttpClient())
            {
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var result = client.PostAsync(discordWebhook, content).Result;

                Log($"Discord webhook status: {result.StatusCode}");
                if (!result.IsSuccessStatusCode)
                {
                    Log($"Failed to send: {result.Content.ReadAsStringAsync().Result}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to send Discord webhook: {ex.Message}");
        }
    }


    public static void LoadShardNameFromINI()
    {
        if (!File.Exists(iniPath))
        {
            Log($"INI file not found at: {iniPath}. Using default shard name.");
            return;
        }

        string[] lines = File.ReadAllLines(iniPath);
        bool inSystemSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("[") && trimmed.Contains("system"))
            {
                inSystemSection = true;
                continue;
            }

            if (inSystemSection && trimmed.StartsWith("}"))
            {
                break; // End of [system] section
            }

            if (inSystemSection && trimmed.StartsWith("SERVERNAME", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                {
                    shardName = parts[1].Trim();
                    Log($"Shard name loaded from INI: {shardName}");
                    return;
                }
            }
        }

        Log("SERVERNAME not found in [system] section of INI.");
    }

    public static bool IsExecutable(string path)
    {
        try
        {
            return File.Exists(path) && (new FileInfo(path).Extension == "" || path.EndsWith(".sh"));
        }
        catch
        {
            return false;
        }
    }

    public static void Log(string msg)
    {
        File.AppendAllText("restart.log", $"[{DateTime.Now}] {msg}{Environment.NewLine}");
    }
}