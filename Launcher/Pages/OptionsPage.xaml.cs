using LauncherWinUI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LauncherWinUI.Pages
{
    public partial class OptionsPage : Page
    {
        private GameSettingsFile _settings = new();

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Ansi, Size = 220)]
        private struct DEVMODE
        {
            [FieldOffset(36)] public ushort dmSize;
            [FieldOffset(108)] public uint dmPelsWidth;
            [FieldOffset(112)] public uint dmPelsHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        private List<(int Width, int Height)> GetSupportedResolutions()
        {
            var seen = new HashSet<(int, int)>();
            var dm = new DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(dm);
            int i = 0;
            while (EnumDisplaySettings(null, i++, ref dm))
                seen.Add(((int)dm.dmPelsWidth, (int)dm.dmPelsHeight));

            return seen
                .Where(r => r.Item1 >= 640 && r.Item2 >= 480)
                .OrderByDescending(r => r.Item1).ThenByDescending(r => r.Item2)
                .ToList();
        }

        private void PopulateResolutions(int selectedWidth, int selectedHeight)
        {
            var resolutions = GetSupportedResolutions();
            cmbWindowedResolution.Items.Clear();

            int selectIndex = 0;
            for (int i = 0; i < resolutions.Count; i++)
            {
                var (w, h) = resolutions[i];
                cmbWindowedResolution.Items.Add(new ComboBoxItem
                {
                    Content = $"{w} x {h}",
                    Tag = (w, h)
                });
                if (w == selectedWidth && h == selectedHeight)
                    selectIndex = i;
            }

            if (cmbWindowedResolution.Items.Count > 0)
                cmbWindowedResolution.SelectedIndex = selectIndex;
        }

        public OptionsPage()
        {
            InitializeComponent();
            Loaded += OptionsPage_Loaded;
        }

        private void OptionsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadGameSettings();
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelCamera == null) return; // not yet loaded

            PanelCamera.Visibility = Visibility.Collapsed;
            PanelChat.Visibility = Visibility.Collapsed;
            PanelInput.Visibility = Visibility.Collapsed;
            PanelGraphics.Visibility = Visibility.Collapsed;
            PanelSocial.Visibility = Visibility.Collapsed;
            PanelNetwork.Visibility = Visibility.Collapsed;

            if (ReferenceEquals(sender, rbCamera)) PanelCamera.Visibility = Visibility.Visible;
            else if (ReferenceEquals(sender, rbChat)) PanelChat.Visibility = Visibility.Visible;
            else if (ReferenceEquals(sender, rbInput)) PanelInput.Visibility = Visibility.Visible;
            else if (ReferenceEquals(sender, rbGraphics)) PanelGraphics.Visibility = Visibility.Visible;
            else if (ReferenceEquals(sender, rbSocial)) PanelSocial.Visibility = Visibility.Visible;
            else if (ReferenceEquals(sender, rbNetwork)) PanelNetwork.Visibility = Visibility.Visible;
        }

        private void ChkLimitFramerate_Changed(object sender, RoutedEventArgs e)
        {
            if (txtFPSLimit != null)
                txtFPSLimit.IsEnabled = chkLimitFramerate.IsChecked == true;
        }

        private void ChkWindowed_Changed(object sender, RoutedEventArgs e)
        {
            if (cmbWindowedResolution != null)
                cmbWindowedResolution.IsEnabled = chkWindowed.IsChecked == true;
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            SaveGameSettings();
            NavigationService?.GoBack();
        }

        private string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Command and Conquer Generals Zero Hour Data",
            "GeneralsOnlineData",
            "settings.json");

        private string LauncherFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Command and Conquer Generals Zero Hour Data",
            "GeneralsOnlineData",
            "launcher.json");

        private string IniFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Command and Conquer Generals Zero Hour Data",
            "Options.ini");

        private static int ParseInt(TextBox tb, int defaultVal)
        {
            return int.TryParse(tb.Text, out int v) ? v : defaultVal;
        }

        private void LoadGameSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    _settings = new GameSettingsFile();
                    File.WriteAllText(SettingsFilePath, System.Text.Json.JsonSerializer.Serialize(
                        _settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    _settings = System.Text.Json.JsonSerializer.Deserialize<GameSettingsFile>(
                        File.ReadAllText(SettingsFilePath)) ?? new();
                }

                txtMaxCameraHeight.Text = ((int)_settings.camera.max_height_only_when_lobby_host).ToString();
                txtMinCameraHeight.Text = ((int)_settings.camera.min_height).ToString();
                txtCameraMovementSpeed.Text = ((int)(_settings.camera.move_speed_ratio * 100f)).ToString();

                txtChatDuration.Text = _settings.chat.duration_seconds_until_fade_out.ToString();

                chkLimitFramerate.IsChecked = _settings.render.limit_framerate;
                txtFPSLimit.Text = _settings.render.fps_limit.ToString();
                txtFPSLimit.IsEnabled = _settings.render.limit_framerate;
                chkStatsOverlay.IsChecked = _settings.render.stats_overlay;

                chkWindowed.IsChecked = LaunchOptions.Windowed;
                PopulateResolutions(LaunchOptions.WindowedWidth, LaunchOptions.WindowedHeight);
                cmbWindowedResolution.IsEnabled = LaunchOptions.Windowed;

                chkFriendOnlineInGame.IsChecked = _settings.social.notification_friend_comes_online_gameplay;
                chkFriendOnlineInMenus.IsChecked = _settings.social.notification_friend_comes_online_menus;
                chkFriendOfflineInGame.IsChecked = _settings.social.notification_friend_goes_offline_gameplay;
                chkFriendOfflineInMenus.IsChecked = _settings.social.notification_friend_goes_offline_menus;
                chkFriendRequestInGame.IsChecked = _settings.social.notification_player_sends_request_gameplay;
                chkFriendRequestInMenus.IsChecked = _settings.social.notification_player_sends_request_menus;
                chkFriendAcceptedInGame.IsChecked = _settings.social.notification_player_accepts_request_gameplay;
                chkFriendAcceptedInMenus.IsChecked = _settings.social.notification_player_accepts_request_menus;

                int httpVer = _settings.network.http_version;
                cmbHTTPVersion.SelectedIndex = httpVer < cmbHTTPVersion.Items.Count ? httpVer : 0;
                chkAlternativeEndpoint.IsChecked = _settings.network.use_alternative_endpoint;

                LoadIniSettings();
            }
            catch
            {
                MessageBox.Show("Your settings have been reset due to an update. Please reconfigure them.",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                NavigationService?.GoBack();
            }
        }

        private void LoadIniSettings()
        {
            if (!File.Exists(IniFilePath))
            {
                WriteDefaultIni();
                return;
            }
            try
            {
                var dict = File.ReadLines(IniFilePath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && l.Contains(" = "))
                    .Select(l => l.Split(" = ", 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

                bool GetBool(string key) => dict.TryGetValue(key, out var v) && v.ToLower().Contains("yes");

                chkCursorLockFullscreen.IsChecked = GetBool("CursorCaptureEnabledInFullscreenGame") ||
                                                    GetBool("CursorCaptureEnabledInFullscreenMenu");
                chkCursorLockWindowed.IsChecked = GetBool("CursorCaptureEnabledInWindowedGame") ||
                                                  GetBool("CursorCaptureEnabledInWindowedMenu");
                chkEdgeScrollingFullscreen.IsChecked = GetBool("ScreenEdgeScrollEnabledInFullscreenApp");
                chkEdgeScrollingWindowed.IsChecked = GetBool("ScreenEdgeScrollEnabledInWindowedApp");
            }
            catch
            {
                WriteDefaultIni();
            }
        }

        private void WriteDefaultIni()
        {
            string[] lines =
            {
                "ScreenEdgeScrollEnabledInFullscreenApp = yes",
                "ScreenEdgeScrollEnabledInWindowedApp = no",
                "CursorCaptureEnabledInFullscreenGame = yes",
                "CursorCaptureEnabledInFullscreenMenu = yes",
                "CursorCaptureEnabledInWindowedGame = no",
                "CursorCaptureEnabledInWindowedMenu = no"
            };
            File.WriteAllLines(IniFilePath, lines);
            chkCursorLockWindowed.IsChecked = false;
            chkCursorLockFullscreen.IsChecked = true;
            chkEdgeScrollingFullscreen.IsChecked = true;
            chkEdgeScrollingWindowed.IsChecked = false;
        }

        private void SaveGameSettings()
        {
            _settings.camera.max_height_only_when_lobby_host = ParseInt(txtMaxCameraHeight, 310);
            _settings.camera.min_height = ParseInt(txtMinCameraHeight, 210);
            _settings.camera.move_speed_ratio = ParseInt(txtCameraMovementSpeed, 100) / 100f;

            _settings.chat.duration_seconds_until_fade_out = ParseInt(txtChatDuration, 30);

            _settings.render.limit_framerate = chkLimitFramerate.IsChecked == true;
            _settings.render.fps_limit = ParseInt(txtFPSLimit, 60);
            _settings.render.stats_overlay = chkStatsOverlay.IsChecked == true;

            LaunchOptions.Windowed = chkWindowed.IsChecked == true;
            if (cmbWindowedResolution.SelectedItem is ComboBoxItem item && item.Tag is (int w, int h))
            {
                LaunchOptions.WindowedWidth = w;
                LaunchOptions.WindowedHeight = h;
            }

            SaveWindowedToLauncherJson();

            _settings.social.notification_friend_comes_online_gameplay = chkFriendOnlineInGame.IsChecked == true;
            _settings.social.notification_friend_comes_online_menus = chkFriendOnlineInMenus.IsChecked == true;
            _settings.social.notification_friend_goes_offline_gameplay = chkFriendOfflineInGame.IsChecked == true;
            _settings.social.notification_friend_goes_offline_menus = chkFriendOfflineInMenus.IsChecked == true;
            _settings.social.notification_player_sends_request_gameplay = chkFriendRequestInGame.IsChecked == true;
            _settings.social.notification_player_sends_request_menus = chkFriendRequestInMenus.IsChecked == true;
            _settings.social.notification_player_accepts_request_gameplay = chkFriendAcceptedInGame.IsChecked == true;
            _settings.social.notification_player_accepts_request_menus = chkFriendAcceptedInMenus.IsChecked == true;

            _settings.network.http_version = cmbHTTPVersion.SelectedIndex;
            _settings.network.use_alternative_endpoint = chkAlternativeEndpoint.IsChecked == true;

            File.WriteAllText(SettingsFilePath, System.Text.Json.JsonSerializer.Serialize(
                _settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            SaveIniSettings();
        }

        private void SaveWindowedToLauncherJson()
        {
            try
            {
                var launcher = File.Exists(LauncherFilePath)
                    ? System.Text.Json.JsonSerializer.Deserialize<LauncherSettingsFile>(
                        File.ReadAllText(LauncherFilePath)) ?? new()
                    : new LauncherSettingsFile();

                launcher.windowed = LaunchOptions.Windowed;
                launcher.windowed_width = LaunchOptions.WindowedWidth;
                launcher.windowed_height = LaunchOptions.WindowedHeight;

                File.WriteAllText(LauncherFilePath, System.Text.Json.JsonSerializer.Serialize(
                    launcher, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void SaveIniSettings()
        {
            bool cursorWin = chkCursorLockWindowed.IsChecked == true;
            bool cursorFull = chkCursorLockFullscreen.IsChecked == true;
            bool edgeFull = chkEdgeScrollingFullscreen.IsChecked == true;
            bool edgeWin = chkEdgeScrollingWindowed.IsChecked == true;

            var lines = File.Exists(IniFilePath)
                ? File.ReadAllLines(IniFilePath).ToList()
                : new List<string>();

            bool wroteEdgeFull = false, wroteEdgeWin = false;
            bool wroteCursorFull = false, wroteCursorWin = false;

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains("ScreenEdgeScrollEnabledInFullscreenApp")) { lines[i] = $"ScreenEdgeScrollEnabledInFullscreenApp = {Y(edgeFull)}"; wroteEdgeFull = true; }
                else if (lines[i].Contains("ScreenEdgeScrollEnabledInWindowedApp")) { lines[i] = $"ScreenEdgeScrollEnabledInWindowedApp = {Y(edgeWin)}"; wroteEdgeWin = true; }
                else if (lines[i].Contains("CursorCaptureEnabledInFullscreenGame")) { lines[i] = $"CursorCaptureEnabledInFullscreenGame = {Y(cursorFull)}"; wroteCursorFull = true; }
                else if (lines[i].Contains("CursorCaptureEnabledInFullscreenMenu")) { lines[i] = $"CursorCaptureEnabledInFullscreenMenu = {Y(cursorFull)}"; wroteCursorFull = true; }
                else if (lines[i].Contains("CursorCaptureEnabledInWindowedGame")) { lines[i] = $"CursorCaptureEnabledInWindowedGame = {Y(cursorWin)}"; wroteCursorWin = true; }
                else if (lines[i].Contains("CursorCaptureEnabledInWindowedMenu")) { lines[i] = $"CursorCaptureEnabledInWindowedMenu = {Y(cursorWin)}"; wroteCursorWin = true; }
            }

            if (!wroteEdgeFull) lines.Add($"ScreenEdgeScrollEnabledInFullscreenApp = {Y(edgeFull)}");
            if (!wroteEdgeWin) lines.Add($"ScreenEdgeScrollEnabledInWindowedApp = {Y(edgeWin)}");
            if (!wroteCursorFull) { lines.Add($"CursorCaptureEnabledInFullscreenGame = {Y(cursorFull)}"); lines.Add($"CursorCaptureEnabledInFullscreenMenu = {Y(cursorFull)}"); }
            if (!wroteCursorWin) { lines.Add($"CursorCaptureEnabledInWindowedGame = {Y(cursorWin)}"); lines.Add($"CursorCaptureEnabledInWindowedMenu = {Y(cursorWin)}"); }

            File.WriteAllLines(IniFilePath, lines);
        }

        private static string Y(bool b) => b ? "yes" : "no";
    }
}
