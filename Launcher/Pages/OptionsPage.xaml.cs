using LauncherWinUI.Models;
using LauncherWinUI.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace LauncherWinUI.Pages
{
    public partial class OptionsPage : Page
    {
        private GameSettingsFile _settings = new();
        private NetworkDiagnosticsResult? _lastDiagResult;
        private CancellationTokenSource? _diagCts;

        // Diagnostic colour palette
        private static SolidColorBrush DiagGreen  => new(Color.FromRgb(0x55, 0xCC, 0x55));
        private static SolidColorBrush DiagYellow => new(Color.FromRgb(0xFF, 0xAA, 0x00));
        private static SolidColorBrush DiagRed    => new(Color.FromRgb(0xFF, 0x44, 0x44));
        private static SolidColorBrush DiagGray   => new(Color.FromRgb(0x55, 0x55, 0x88));

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

        // ─── Network Diagnostics ─────────────────────────────────────────────────

        private async void BtnRunDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            _diagCts?.Cancel();
            _diagCts = new CancellationTokenSource();

            btnRunDiagnostics.IsEnabled = false;
            btnRunDiagnostics.Content   = "⟳  RUNNING...";
            btnCopyDiag.IsEnabled       = false;
            panelDiagResults.Visibility = Visibility.Visible;
            ResetDiagnosticsUI();

            try
            {
                var result = await new NetworkDiagnosticsService().RunAsync(_diagCts.Token);
                _lastDiagResult = result;
                UpdateDiagnosticsUI(result);
                AppendDiagLog(result);
                btnCopyDiag.IsEnabled = true;
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                btnRunDiagnostics.IsEnabled = true;
                btnRunDiagnostics.Content   = "▶  RUN DIAGNOSTICS";
            }
        }

        private void BtnCopyDiag_Click(object sender, RoutedEventArgs e)
        {
            if (_lastDiagResult is not { } r) return;
            var sb = new StringBuilder();
            sb.AppendLine("═══ Generals Online Network Diagnostics ═══");
            sb.AppendLine($"Time:         {r.Timestamp:yyyy-MM-dd HH:mm:ss}");
            if (r.GeoSuccess)
            {
                string location = r.GeoCountryCode == "US" && r.GeoCity.Length > 0
                    ? $"{r.GeoCity}, {r.GeoCountry}" : r.GeoCountry;
                sb.AppendLine($"Location:     {location} - {r.GeoContinent}");
                sb.AppendLine($"ISP:          {r.GeoIsp}");
                sb.AppendLine($"External IP:  {RedactIp(r.GeoIp)}");
            }
            sb.AppendLine();
            sb.AppendLine("─ Internet ─");
            sb.AppendLine($"Cloudflare:   {(r.CloudflareSuccess ? $"{r.CloudflareAvgMs}ms avg, {r.CloudflareLossPercent}% loss" : "Unreachable")}");
            sb.AppendLine($"Download:     {(r.DownloadMbps > 0 ? $"{r.DownloadMbps:F1} Mbps" : "Failed")}");
            sb.AppendLine($"Upload:       {(r.UploadMbps > 0 ? $"{r.UploadMbps:F1} Mbps" : "Failed")}");
            sb.AppendLine();
            sb.AppendLine("─ Game Server ─");
            sb.AppendLine($"DNS:          {(r.DnsSuccess ? $"OK — {r.DnsAddresses}" : "FAILED")}");
            sb.AppendLine($"Ping (ICMP):  {(r.PingSuccess ? $"{r.PingAvgMs}ms avg (min {r.PingMinMs} / max {r.PingMaxMs}), {r.PingLossPercent}% loss" : "Unreachable")}");
            sb.AppendLine($"Protocol:     {r.Protocol}");
            sb.AppendLine($"HTTP Latency: {(r.HttpSuccess ? $"{r.HttpLatencyMs}ms" : "Failed")}");
            sb.AppendLine($"Server:       {(r.ServerOnline ? $"Online — {r.PlayersOnline} players, {r.Lobbies} lobbies" : "Offline / Unreachable")}");
            sb.AppendLine();
            sb.AppendLine("─ Connectivity ─");
            sb.AppendLine($"CDN:          {(r.CdnSuccess ? $"{r.CdnLatencyMs}ms" : "Unreachable")}");
            sb.AppendLine($"STUN:         {(r.StunSuccess ? $"OK — {RedactEndpoint(r.StunExternalEndpoint)} ({r.StunLatencyMs}ms)" : "Unreachable")}");
            sb.AppendLine($"TURN:         {(r.TurnSuccess ? $"OK — {r.TurnEndpoint} ({r.TurnLatencyMs}ms)" : "Unreachable")}");
            sb.AppendLine($"NAT Type:     {r.NatType} — {r.NatTypeDetail}");
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }

        private void ResetDiagnosticsUI()
        {
            var gray = DiagGray;
            foreach (var dot in new[] { dotGeo, dotCF, dotDl, dotUl, dotDns, dotPing, dotProto, dotHttp, dotServer, dotCdn, dotStun, dotTurn, dotNat })
                dot.Fill = gray;
            foreach (var val in new[] { valGeo, valCF, valDl, valUl, valDns, valPing, valProto, valHttp, valServer, valCdn, valStun, valTurn, valNat })
            {
                val.Text       = "checking…";
                val.Foreground = gray;
            }
        }

        private void UpdateDiagnosticsUI(NetworkDiagnosticsResult r)
        {
            // Geolocation
            if (r.GeoSuccess)
            {
                string location = r.GeoCountryCode == "US" && r.GeoCity.Length > 0
                    ? $"{r.GeoCity}, {r.GeoCountry}"
                    : r.GeoCountry;
                string geoText = location;
                if (r.GeoContinent.Length > 0) geoText += $" - {r.GeoContinent}";
                if (r.GeoIsp.Length > 0)       geoText += $" - {r.GeoIsp}";
                SetRow(dotGeo, valGeo, true, geoText, DiagGreen);
            }
            else
                SetRow(dotGeo, valGeo, false, "Could not determine location", DiagGray);

            // Cloudflare internet baseline
            SetRow(dotCF, valCF,
                r.CloudflareSuccess,
                r.CloudflareSuccess
                    ? $"{r.CloudflareAvgMs}ms avg  {r.CloudflareLossPercent}% loss"
                    : "Unreachable",
                r.CloudflareSuccess ? LossColor(r.CloudflareLossPercent) : DiagRed);

            // Download speed
            SetRow(dotDl, valDl,
                r.SpeedTestSuccess && r.DownloadMbps > 0,
                r.DownloadMbps > 0 ? $"{r.DownloadMbps:F1} Mbps" : "Failed",
                r.DownloadMbps > 0 ? SpeedColor(r.DownloadMbps) : DiagRed);

            // Upload speed
            SetRow(dotUl, valUl,
                r.SpeedTestSuccess && r.UploadMbps > 0,
                r.UploadMbps > 0 ? $"{r.UploadMbps:F1} Mbps" : "Failed",
                r.UploadMbps > 0 ? SpeedColor(r.UploadMbps) : DiagRed);

            // DNS
            SetRow(dotDns, valDns,
                r.DnsSuccess,
                r.DnsSuccess ? r.DnsAddresses : "Resolution failed",
                r.DnsSuccess ? DiagGreen : DiagRed);

            // Ping
            SetRow(dotPing, valPing,
                r.PingSuccess,
                r.PingSuccess
                    ? $"{r.PingAvgMs}ms avg  (min {r.PingMinMs} / max {r.PingMaxMs})  {r.PingLossPercent}% loss"
                    : "Unreachable (ICMP may be blocked)",
                r.PingSuccess ? LatencyColor(r.PingAvgMs) : DiagRed);

            // Protocol
            bool protoOk = r.Protocol is "IPv4" or "IPv6";
            SetRow(dotProto, valProto, protoOk, r.Protocol, protoOk ? DiagGreen : DiagGray);

            // HTTP latency
            SetRow(dotHttp, valHttp,
                r.HttpSuccess,
                r.HttpSuccess ? $"{r.HttpLatencyMs}ms" : "Request failed",
                r.HttpSuccess ? LatencyColor(r.HttpLatencyMs) : DiagRed);

            // Server status
            if (r.ServerOnline)
                SetRow(dotServer, valServer, true,
                    $"Online — {r.PlayersOnline} players  •  {r.Lobbies} lobbies", DiagGreen);
            else
                SetRow(dotServer, valServer, false,
                    r.HttpSuccess ? "Degraded (HTTP error)" : "Offline / Unreachable", DiagRed);

            // CDN
            SetRow(dotCdn, valCdn,
                r.CdnSuccess,
                r.CdnSuccess ? $"{r.CdnLatencyMs}ms" : "Unreachable",
                r.CdnSuccess ? LatencyColor(r.CdnLatencyMs) : DiagRed);

            // STUN
            SetRow(dotStun, valStun,
                r.StunSuccess,
                r.StunSuccess ? $"{r.StunLatencyMs}ms  ({RedactEndpoint(r.StunExternalEndpoint)})" : "Unreachable",
                r.StunSuccess ? LatencyColor(r.StunLatencyMs) : DiagRed);

            // TURN
            SetRow(dotTurn, valTurn,
                r.TurnSuccess,
                r.TurnSuccess ? $"{r.TurnLatencyMs}ms  ({r.TurnEndpoint})" : "Unreachable",
                r.TurnSuccess ? LatencyColor(r.TurnLatencyMs) : DiagRed);

            // NAT type
            var (natColor, natText) = r.NatType switch
            {
                NatType.Open or NatType.FullCone or NatType.RestrictedCone
                    => (DiagGreen,  $"{NatTypeLabel(r.NatType)} — {r.NatTypeDetail}"),
                NatType.PortRestrictedCone
                    => (DiagYellow, $"Port Restricted Cone — {r.NatTypeDetail}"),
                NatType.Symmetric
                    => (DiagRed,    $"Symmetric — {r.NatTypeDetail}"),
                _   => (DiagGray,   $"Unknown — {r.NatTypeDetail}")
            };
            SetRow(dotNat, valNat, r.NatType != NatType.Unknown, natText, natColor);
        }

        private void AppendDiagLog(NetworkDiagnosticsResult r)
        {
            const int MaxEntries = 10;

            string entry = $"[{r.Timestamp:HH:mm:ss}]  " +
                $"↓ {(r.DownloadMbps > 0 ? $"{r.DownloadMbps:F1}Mbps" : "✗")}  " +
                $"↑ {(r.UploadMbps > 0 ? $"{r.UploadMbps:F1}Mbps" : "✗")}  |  " +
                $"CF: {(r.CloudflareSuccess ? $"{r.CloudflareAvgMs}ms" : "✗")}  |  " +
                $"Ping: {(r.PingSuccess ? $"{r.PingAvgMs}ms {r.PingLossPercent}% loss" : "✗")}  |  " +
                $"HTTP: {(r.HttpSuccess ? $"{r.HttpLatencyMs}ms" : "✗")}  |  " +
                $"STUN: {(r.StunSuccess ? "✓" : "✗")}  |  " +
                $"TURN: {(r.TurnSuccess ? "✓" : "✗")}  |  " +
                $"NAT: {NatTypeLabel(r.NatType)}  |  " +
                $"{(r.ServerOnline ? "Online" : "Offline")}";

            diagLog.Children.Insert(0, new TextBlock
            {
                Text        = entry,
                Foreground  = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x99)),
                FontSize    = 11,
                FontFamily  = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                Margin      = new Thickness(0, 0, 0, 3)
            });

            while (diagLog.Children.Count > MaxEntries)
                diagLog.Children.RemoveAt(diagLog.Children.Count - 1);
        }

        private static void SetRow(Ellipse dot, TextBlock val, bool ok, string text, SolidColorBrush color)
        {
            dot.Fill       = color;
            val.Text       = text;
            val.Foreground = color;
        }

        private static SolidColorBrush LatencyColor(int ms) =>
            ms <= 60 ? DiagGreen : ms <= 150 ? DiagYellow : DiagRed;

        private static SolidColorBrush LossColor(int pct) =>
            pct == 0 ? DiagGreen : pct < 25 ? DiagYellow : DiagRed;

        private static SolidColorBrush SpeedColor(double mbps) =>
            mbps >= 10 ? DiagGreen : mbps >= 1 ? DiagYellow : DiagRed;

        private static string NatTypeLabel(NatType t) => t switch
        {
            NatType.Open              => "Open",
            NatType.FullCone          => "Full Cone",
            NatType.RestrictedCone    => "Restricted",
            NatType.PortRestrictedCone => "Port Restr.",
            NatType.Symmetric         => "Symmetric",
            _                         => "Unknown"
        };

        private static string RedactIp(string ip)
        {
            int last = ip.LastIndexOf('.');
            return last >= 0 ? ip[..last] + ".XXX" : ip;
        }

        // Redacts the IP portion of an "ip:port" endpoint string
        private static string RedactEndpoint(string endpoint)
        {
            int colon = endpoint.LastIndexOf(':');
            string ip   = colon >= 0 ? endpoint[..colon] : endpoint;
            string port = colon >= 0 ? endpoint[colon..]  : "";
            return RedactIp(ip) + port;
        }
    }
}
