#if WINDOWS
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using static Utility;

class UOX3MonitorApp : Form
{
    private NotifyIcon trayIcon;
    private ContextMenuStrip trayMenu;
    private bool monitoring = false;
    private Thread monitorThread;
    private bool dontKillOnStop = false;
    private ToolStripMenuItem killToggleItem;
    private Process runningProcess;
    private System.Threading.Timer htmlStatusTimer;
    private int lastPlayers = -1, lastGMs = -1, lastCounselors = -1;

    public UOX3MonitorApp()
    {
        LoadConfig();
        LoadShardNameFromINI();
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Start Monitoring", null, OnStart);
        trayMenu.Items.Add("Stop Monitoring", null, OnStop);
        // Add toggle option
        killToggleItem = new ToolStripMenuItem("Don't kill UOX3 on Stop", null, OnToggleKillBehavior);
        killToggleItem.Checked = dontKillOnStop;
        trayMenu.Items.Add(killToggleItem);

        trayMenu.Items.Add("Set UOX3 Path", null, OnSetPath);
        trayMenu.Items.Add("Set INI Path", null, OnSetINIPath);
        trayMenu.Items.Add("Set HTML Path", null, OnSetHTMLPath);
        trayMenu.Items.Add("View Log", null, OnViewLog);
        trayMenu.Items.Add("Exit", null, OnExit);

        trayIcon = new NotifyIcon();
        trayIcon.Text = $"UOX3 Monitor {VersionInfo.Version}";
        var asm = typeof(UOX3MonitorApp).Assembly;
        using (var stream = asm.GetManifestResourceStream("UOX3TrayMonitor.uox3monitor.ico"))
        {
            trayIcon.Icon = new Icon(stream);
        }
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;

        LoadOrPromptForPath();
    }

    private void StartHtmlStatusTimer()
    {
        string htmlFile = Utility.statusHtmlPath;

        if (!Utility.htmlStatusEnabled)
        {
            Log("[HTML Timer] Disabled by config.");
            return;
        }

        if (!File.Exists(htmlFile))
        {
            Log("[HTML Timer] File not found: " + htmlFile);
            return;
        }

        htmlStatusTimer = new System.Threading.Timer(_ =>
        {
            ParseAndSendHtmlStats(htmlFile);
        }, null, 0, Utility.htmlStatusInterval * 1000);

        Log($"[HTML Timer] Monitoring every {Utility.htmlStatusInterval}s: {htmlFile}");
    }

    private void ParseAndSendHtmlStats(string path)
    {
        try
        {
            string html = File.ReadAllText(path);
            int players = ExtractCount(html, "<em>Player:</em>");
            int gms = ExtractCount(html, "<em>GMs:</em>");
            int counselors = ExtractCount(html, "<em>Counselors:</em>");

            // Only alert if values changed
            if (players != lastPlayers || gms != lastGMs || counselors != lastCounselors)
            {
                lastPlayers = players;
                lastGMs = gms;
                lastCounselors = counselors;

                SendDiscordAlert($"{shardName} Status Update:\nPlayers: {players}\nGMs: {gms}\nCounselors: {counselors}");
            }
        }
        catch (Exception ex)
        {
            Log("[HTML Parser] Error: " + ex.Message);
        }
    }

    private int ExtractCount(string html, string label)
    {
        int index = html.IndexOf(label);
        if (index == -1) return -1;
        string after = html.Substring(index + label.Length);
        string digits = "";
        foreach (char c in after)
        {
            if (char.IsDigit(c)) digits += c;
            else if (digits.Length > 0) break;
        }
        return int.TryParse(digits, out int result) ? result : -1;
    }

    protected override void OnLoad(EventArgs e)
    {
        Visible = false;
        ShowInTaskbar = false;
        base.OnLoad(e);
    }

    private void LoadOrPromptForPath()
    {
        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            return;

        PromptForPathUntilSet("Please select the path to UOX3.exe:");
    }

    private void OnToggleKillBehavior(object sender, EventArgs e)
    {
        dontKillOnStop = !dontKillOnStop;
        killToggleItem.Checked = dontKillOnStop;
        Log("Updated kill behavior: " + (dontKillOnStop ? "Will NOT kill UOX3 on Stop" : "Will kill UOX3 on Stop"));
    }

    private void PromptForPathUntilSet(string message)
    {
        while (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            MessageBox.Show(message, "UOX3 Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);

            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Executable Files (*.exe)|*.exe";
                ofd.Title = "Select UOX3.exe";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    exePath = ofd.FileName;
                    SavePathToConfig();
                    Log("Path set to: " + exePath);
                    break;
                }
                else
                {
                    DialogResult retry = MessageBox.Show(
                        "You must select a valid UOX3 executable to continue.",
                        "UOX3 Monitor",
                        MessageBoxButtons.RetryCancel,
                        MessageBoxIcon.Warning
                    );

                    if (retry == DialogResult.Cancel)
                    {
                        Environment.Exit(0);
                    }
                }
            }
        }
    }

    private void SavePathToConfig()
    {
        SaveConfig();
    }

    private void OnStart(object sender, EventArgs e)
    {
        if (monitoring || string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            MessageBox.Show("Set a valid UOX3.exe path first.");
            return;
        }

        monitoring = true;
        StartHtmlStatusTimer();
        monitorThread = new Thread(() =>
        {
            int restartAttempts = 0;
            const int maxRestarts = 5;

            while (monitoring && restartAttempts < maxRestarts)
            {
                try
                {
                    runningProcess = new Process();
                    runningProcess.StartInfo.FileName = exePath;
                    runningProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
                    runningProcess.Start();
                    SendDiscordAlert($"UOX3 Monitor: {shardName} started.");
                    Log($"Starting UOX3 Monitor {VersionInfo.Version}");
                    runningProcess.WaitForExit();
                    runningProcess = null;

                    if (!monitoring)
                    {
                        Log("Monitoring stopped manually. No restart.");
                        return;
                    }

                    restartAttempts++;
                    Log($"UOX3 exited. Attempt #{restartAttempts} of {maxRestarts}. Restarting in 5 seconds...");
                    SendDiscordAlert($"UOX3 exited. Restarting attempt #{restartAttempts}/{maxRestarts}...");
                    Thread.Sleep(5000);
                }
                catch (Exception ex)
                {
                    restartAttempts++;
                    Log($"Error starting UOX3 (attempt #{restartAttempts}): {ex.Message}");
                    Thread.Sleep(5000);
                }
            }

            if (restartAttempts >= maxRestarts)
            {
                Log("Max restart attempts reached. Monitoring stopped.");
                SendDiscordAlert("UOX3 failed 5 times. Monitoring stopped.");
                MessageBox.Show("UOX3 failed to stay open after multiple attempts. Monitoring has stopped.", "UOX3 Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                monitoring = false;
            }
        });
        monitorThread.IsBackground = true;
        monitorThread.Start();
    }

    private void OnStop(object sender, EventArgs e)
    {
        monitoring = false;
        if (htmlStatusTimer != null)
        {
            htmlStatusTimer.Dispose();
            htmlStatusTimer = null;
            Log("Stopped HTML status timer.");
        }

        try
        {
            // Kill process only if allowed and it's still running
            if (!dontKillOnStop && runningProcess != null)
            {
                if (!runningProcess.HasExited)
                    runningProcess.Kill();

                runningProcess.Dispose();
                runningProcess = null;
            }

            // Wait for monitor thread to finish gracefully
            if (monitorThread != null)
            {
                if (monitorThread.IsAlive)
                    monitorThread.Join();

                monitorThread = null;
            }

            Log("Monitoring stopped.");
        }
        catch (Exception ex)
        {
            Log("Error while stopping monitor thread: " + ex.Message);
        }
    }

    private void OnSetPath(object sender, EventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog();
        ofd.Filter = "Executable Files (*.exe)|*.exe";
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            exePath = ofd.FileName;
            SavePathToConfig();
            Log("Path set to: " + exePath);
        }
    }

    private void OnSetINIPath(object sender, EventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog();
        ofd.Filter = "INI Files (*.ini)|*.ini";
        ofd.Title = "Select uox3.ini";

        if (ofd.ShowDialog() == DialogResult.OK)
        {
            iniPath = ofd.FileName;
            SaveConfig();
            Log("INI path set to: " + iniPath);
            MessageBox.Show("INI path updated.", "UOX3 Monitor");
        }
    }

    private void OnSetHTMLPath(object sender, EventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog();
        ofd.Filter = "HTML Files (*.html)|*.html";
        ofd.Title = "Select serverstatus.html";

        if (ofd.ShowDialog() == DialogResult.OK)
        {
            statusHtmlPath = ofd.FileName;
            SaveConfig();
            Log("HTML status path set to: " + statusHtmlPath);
            MessageBox.Show("HTML path updated.", "UOX3 Monitor");
        }
    }

    private void OnViewLog(object sender, EventArgs e)
    {
        if (File.Exists(logPath))
            Process.Start("notepad.exe", logPath);
    }

    private void OnExit(object sender, EventArgs e)
    {
        CleanupOnExit();
        trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CleanupOnExit();
        base.OnFormClosing(e);
    }

    private void CleanupOnExit()
    {
        monitoring = false;

        try
        {
            if (!dontKillOnStop && runningProcess != null && !runningProcess.HasExited)
            {
                runningProcess.Kill();
                runningProcess.Dispose();
                Log("Killed UOX3 on exit.");
            }
        }
        catch (Exception ex)
        {
            Log("Failed to kill UOX3 on exit: " + ex.Message);
        }

        if (monitorThread != null && monitorThread.IsAlive)
        {
            monitorThread.Join();
        }

        monitorThread = null;

        if (htmlStatusTimer != null)
        {
            htmlStatusTimer.Dispose();
            htmlStatusTimer = null;
            Log("Stopped HTML status timer.");
        }

        Log("Exited UOX3 Monitor.");
    }
}
#endif