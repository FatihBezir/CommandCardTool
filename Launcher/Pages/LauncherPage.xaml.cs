using LauncherWinUI.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace LauncherWinUI.Pages
{
    public partial class LauncherPage : Page
    {
        private static readonly HttpClient _httpClient = new();
        private LauncherSettingsFile _launcherSettings = new();
        private bool _initializing = true;

        public LauncherPage()
        {
            InitializeComponent();
            Loaded += LauncherPage_Loaded;
        }

        private void LauncherPage_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeLaunchOptionsFromArgs();

            // Load logo
            try
            {
                LogoImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logonew.png"));
            }
            catch { }

            if (File.Exists("GeneralsOnlineZH_TestEnvironment.exe"))
                ClientSelectorPanel.Visibility = Visibility.Visible;

            CreateGODataFolder();
            LoadLauncherSettings();
            _initializing = false;

            _ = LoadServerStatsAsync();

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\GeneralsOnline");
                key?.SetValue("InstallPath", Directory.GetCurrentDirectory(), RegistryValueKind.String);
            }
            catch { }
        }

        private async Task LoadServerStatsAsync()
        {
            try
            {
                string json = await _httpClient.GetStringAsync("https://api.playgenerals.online/env/prod/contract/1/Monitoring/BasicStats");
                var data = System.Text.Json.JsonSerializer.Deserialize<ServerStats>(json);
                if (data != null)
                    StatsLabel.Text = $"{data.Players} Players Online  •  {data.Lobbies} Lobbies";
            }
            catch
            {
                // Silently ignore if the server is unreachable
            }
        }

        private void CreateGODataFolder()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Command and Conquer Generals Zero Hour Data",
                "GeneralsOnlineData");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        private void LoadLauncherSettings()
        {
            try
            {
                string filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Command and Conquer Generals Zero Hour Data",
                    "GeneralsOnlineData",
                    "launcher.json");

                if (!File.Exists(filePath))
                {
                    _launcherSettings = new LauncherSettingsFile();
                    File.WriteAllText(filePath, System.Text.Json.JsonSerializer.Serialize(
                        _launcherSettings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    _launcherSettings = System.Text.Json.JsonSerializer.Deserialize<LauncherSettingsFile>(
                        File.ReadAllText(filePath)) ?? new();
                }

                rbTestEnv.IsChecked = !_launcherSettings.prefer_experiemental_client;
                rbLiveClient.IsChecked = _launcherSettings.prefer_experiemental_client;

                LaunchOptions.Windowed = _launcherSettings.windowed;
                LaunchOptions.WindowedWidth = _launcherSettings.windowed_width;
                LaunchOptions.WindowedHeight = _launcherSettings.windowed_height;
            }
            catch
            {
                MessageBox.Show("Failed to load launcher settings.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveLauncherSettings()
        {
            try
            {
                string filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Command and Conquer Generals Zero Hour Data",
                    "GeneralsOnlineData",
                    "launcher.json");

                _launcherSettings.prefer_experiemental_client = rbLiveClient.IsChecked == true;
                File.WriteAllText(filePath, System.Text.Json.JsonSerializer.Serialize(
                    _launcherSettings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void ClientSelector_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initializing)
                SaveLauncherSettings();
        }

        private void BtnOptions_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(new OptionsPage());
        }

        private static void InitializeLaunchOptionsFromArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("-win", StringComparison.OrdinalIgnoreCase))
                    LaunchOptions.Windowed = true;
                else if (args[i].Equals("-xres", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                { if (int.TryParse(args[i + 1], out int w)) LaunchOptions.WindowedWidth = w; }
                else if (args[i].Equals("-yres", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                { if (int.TryParse(args[i + 1], out int h)) LaunchOptions.WindowedHeight = h; }
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string[] incoming = Environment.GetCommandLineArgs()[1..];
                bool hasWin  = incoming.Any(a => a.Equals("-win",  StringComparison.OrdinalIgnoreCase));
                bool hasXRes = incoming.Any(a => a.Equals("-xres", StringComparison.OrdinalIgnoreCase));
                bool hasYRes = incoming.Any(a => a.Equals("-yres", StringComparison.OrdinalIgnoreCase));

                var argParts = incoming.ToList();

                if (LaunchOptions.Windowed && !hasWin)  argParts.Add("-win");
                if (LaunchOptions.Windowed && !hasXRes) { argParts.Add("-xres"); argParts.Add(LaunchOptions.WindowedWidth.ToString()); }
                if (LaunchOptions.Windowed && !hasYRes) { argParts.Add("-yres"); argParts.Add(LaunchOptions.WindowedHeight.ToString()); }

                string arguments = string.Join(" ", argParts);

                string exe;
                if (ClientSelectorPanel.Visibility == Visibility.Visible)
                    exe = rbTestEnv.IsChecked == true ? "LaunchGeneralsOnline.exe" : "GeneralsOnlineZH_60.exe";
                else
                    exe = "GeneralsOnlineZH_60.exe";

                var startedProcess = Process.Start(exe, arguments);

                Application.Current.MainWindow?.Hide();
                startedProcess?.WaitForExit();
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch game: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }

    file record ServerStats(
        [property: System.Text.Json.Serialization.JsonPropertyName("players")] int Players,
        [property: System.Text.Json.Serialization.JsonPropertyName("lobbies")] int Lobbies);
}
