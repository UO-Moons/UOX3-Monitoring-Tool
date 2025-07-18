using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using static Utility;

#if WINDOWS
using System.Drawing;
using System.Windows.Forms;
#endif

class Monitoring
{
    static string mutexName = OperatingSystem.IsWindows()
    ? "Global\\UOX3Monitor_Mutex"
    : "UOX3Monitor_Mutex";

    [STAThread]
    static void Main()
    {
        try
        {
            using (Mutex mutex = new Mutex(true, mutexName, out bool isNewInstance))
            {
                if (!isNewInstance)
                {
#if WINDOWS
                    MessageBox.Show("UOX3 Monitor is already running.", "UOX3 Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
#else
                Console.WriteLine("UOX3 Monitor is already running.");
#endif
                    return;
                }

                RunApp();
            }
        }
        catch (Exception ex)
        {
            Log("Fatal error in Main(): " + ex.Message);
        }
    }

    static void RunApp()
    {
        if (OperatingSystem.IsWindows())
        {
#if WINDOWS
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UOX3MonitorApp());
#else
            Console.WriteLine("This build does not support Windows Forms.");
#endif
        }
        else
        {
            ConsoleMode();
        }
    }

    static void ConsoleMode()
    {
        if (File.Exists(configPath))
        {
            LoadConfig();
            LoadShardNameFromINI();
            Console.WriteLine($"Loaded UOX3 path: {exePath}");
        }
        else
        {
            Console.Write("Enter full path to UOX3: ");
            exePath = Console.ReadLine().Trim();

            while (!File.Exists(exePath))
            {
                Console.Write("Invalid path. Try again: ");
                exePath = Console.ReadLine().Trim();
            }

            useDiscord = false;
            discordWebhook = "";
            SaveConfig();
        }

        if (!OperatingSystem.IsWindows() && !IsExecutable(exePath))
        {
            Console.WriteLine("The specified file is not executable. Please check permissions (e.g., chmod +x).");
            return;
        }

        int restartAttempts = 0;
        const int maxRestarts = 5;

        while (restartAttempts < maxRestarts)
        {
            try
            {
                Console.WriteLine($"Starting {exePath}...");
                Process proc = new Process();
                proc.StartInfo.FileName = exePath;
                proc.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
                proc.Start();
                SendDiscordAlert($"UOX3 Monitor: {shardName} started.");
                Log($"Starting UOX3 Monitor {VersionInfo.Version}");
                proc.WaitForExit();

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

        Console.WriteLine("Max restart attempts reached. Monitoring stopped.");
        SendDiscordAlert("UOX3 failed 5 times. Monitoring stopped.");
        Log("Max restart attempts reached. Monitoring stopped.");
    }
}