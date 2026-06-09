using LauncherWinUI.Models;
using LauncherWinUI.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        private void PopulateAnticheatPlugins()
        {
            cmbAnticheatPlugin.Items.Clear();
            configDetailsPanel.Children.Clear();
            configDetailsPanel.Children.Add(new TextBlock 
            { 
                Text = "(No plugin selected)",
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xC0)),
                FontSize = 11
            });
            
            try
            {
                string pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
                
                if (!Directory.Exists(pluginsPath))
                {
                    return;
                }

                var pluginFolders = Directory.GetDirectories(pluginsPath)
                    .Where(dir => Directory.GetFiles(dir, "*.json").Any())
                    .OrderBy(dir => new DirectoryInfo(dir).Name)
                    .ToList();

                foreach (var pluginDir in pluginFolders)
                {
                    string folderName = new DirectoryInfo(pluginDir).Name;
                    string displayName = folderName; // default to folder name
                    
                    // Try to extract plugin_name from JSON
                    try
                    {
                        var jsonFiles = Directory.GetFiles(pluginDir, "*.json")
                            .OrderBy(f => Path.GetFileName(f))
                            .FirstOrDefault();
                        
                        if (jsonFiles != null)
                        {
                            string jsonContent = File.ReadAllText(jsonFiles);
                            var jsonData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
                            
                            if (jsonData != null && jsonData.TryGetValue("plugin_name", out var pluginNameObj))
                            {
                                displayName = pluginNameObj?.ToString() ?? folderName;
                            }
                        }
                    }
                    catch
                    {
                        // If JSON reading fails, just use folder name
                    }
                    
                    // Add to dropdown with folder name as Tag (for internal lookup)
                    cmbAnticheatPlugin.Items.Add(new ComboBoxItem 
                    { 
                        Content = displayName,
                        Tag = folderName
                    });
                }

                if (cmbAnticheatPlugin.Items.Count > 0)
                {
                    int selectIndex = 0;
                    for (int i = 0; i < cmbAnticheatPlugin.Items.Count; i++)
                    {
                        if (cmbAnticheatPlugin.Items[i] is ComboBoxItem item &&
                            item.Tag?.ToString() == _settings.plugins.anticheat)
                        {
                            selectIndex = i;
                            break;
                        }
                    }
                    cmbAnticheatPlugin.SelectedIndex = selectIndex;
                    DisplayAnticheatPluginDetails();
                }
            }
            catch
            {
                // silently fail if plugins folder doesn't exist or can't be read
            }
        }

        private readonly bool _toolsOnlyMode;

        public OptionsPage() : this(false)
        {
        }

        public OptionsPage(bool toolsOnlyMode)
        {
            _toolsOnlyMode = toolsOnlyMode;
            InitializeComponent();
            Loaded += OptionsPage_Loaded;
        }

        private void OptionsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_toolsOnlyMode)
            {
                ActivateToolsOnlyMode();
                return;
            }

            LoadGameSettings();
        }

        private void ActivateToolsOnlyMode()
        {
            if (PanelCamera == null) return;

            rbCamera.Visibility = Visibility.Collapsed;
            rbChat.Visibility = Visibility.Collapsed;
            rbInput.Visibility = Visibility.Collapsed;
            rbGraphics.Visibility = Visibility.Collapsed;
            rbSocial.Visibility = Visibility.Collapsed;
            rbNetwork.Visibility = Visibility.Collapsed;
            rbPlugins.Visibility = Visibility.Collapsed;

            btnBack.Content = "EXIT";
            rbCommandCard.IsChecked = true;
            Tab_Checked(rbCommandCard, new RoutedEventArgs());
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
            PanelPlugins.Visibility = Visibility.Collapsed;
            PanelCommandCard.Visibility = Visibility.Collapsed;
            PanelExtraSettings.Visibility = Visibility.Collapsed;

            if (MainScrollViewer != null)
            {
                MainScrollViewer.VerticalScrollBarVisibility = ReferenceEquals(sender, rbCommandCard)
                    ? System.Windows.Controls.ScrollBarVisibility.Disabled
                    : System.Windows.Controls.ScrollBarVisibility.Auto;
            }

            if (ReferenceEquals(sender, rbCamera)) PanelCamera.Visibility = Visibility.Visible;
            else if (ReferenceEquals(sender, rbChat)) PanelChat.Visibility = Visibility.Visible;
            else if (ReferenceEquals(sender, rbInput)) PanelInput.Visibility = Visibility.Visible;
            else if (ReferenceEquals(sender, rbGraphics)) PanelGraphics.Visibility = Visibility.Visible;
            else if (ReferenceEquals(sender, rbSocial)) PanelSocial.Visibility = Visibility.Visible;
            else if (ReferenceEquals(sender, rbNetwork)) PanelNetwork.Visibility = Visibility.Visible;
            else if (ReferenceEquals(sender, rbPlugins))
            {
                PanelPlugins.Visibility = Visibility.Visible;
                PopulateAnticheatPlugins();
            }
            else if (ReferenceEquals(sender, rbCommandCard))
            {
                PanelCommandCard.Visibility = Visibility.Visible;
                InitCommandCardTab();
            }
            else if (ReferenceEquals(sender, rbExtraSettings))
            {
                PanelExtraSettings.Visibility = Visibility.Visible;
                InitExtraSettingsTab();
            }
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

        private void CmbAnticheatPlugin_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DisplayAnticheatPluginDetails();
        }

        private void DisplayAnticheatPluginDetails()
        {
            configDetailsPanel.Children.Clear();

            if (cmbAnticheatPlugin.SelectedItem is not ComboBoxItem item)
            {
                configDetailsPanel.Children.Add(new TextBlock 
                { 
                    Text = "(No plugin selected)",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xC0)),
                    FontSize = 11
                });
                return;
            }

            // Use Tag (folder name) for file lookup, not Content (display name)
            string? folderName = item.Tag?.ToString();
            if (string.IsNullOrEmpty(folderName))
            {
                configDetailsPanel.Children.Add(new TextBlock 
                { 
                    Text = "(Invalid plugin folder)",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x88)),
                    FontSize = 11
                });
                return;
            }

            try
            {
                string pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", folderName);
                
                if (!Directory.Exists(pluginPath))
                {
                    configDetailsPanel.Children.Add(new TextBlock 
                    { 
                        Text = $"(Plugin directory not found: {folderName})",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x88)),
                        FontSize = 11
                    });
                    return;
                }

                var jsonFiles = Directory.GetFiles(pluginPath, "*.json")
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();

                if (jsonFiles.Count == 0)
                {
                    configDetailsPanel.Children.Add(new TextBlock 
                    { 
                        Text = "(No JSON files found in plugin)",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xC0)),
                        FontSize = 11
                    });
                    return;
                }

                // Parse and display each JSON file
                foreach (var jsonFile in jsonFiles)
                {
                    string fileName = Path.GetFileName(jsonFile);
                    
                    try
                    {
                        string jsonContent = File.ReadAllText(jsonFile);
                        var jsonData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
                        
                        if (jsonData != null)
                        {
                            AddFileSection(fileName, jsonData);
                        }
                    }
                    catch (Exception ex)
                    {
                        configDetailsPanel.Children.Add(new TextBlock 
                        { 
                            Text = $"Error parsing {fileName}: {ex.Message}",
                            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x88)),
                            FontSize = 10,
                            Margin = new Thickness(0, 8, 0, 8)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                configDetailsPanel.Children.Add(new TextBlock 
                { 
                    Text = $"(Error loading plugin details: {ex.Message})",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x88)),
                    FontSize = 11
                });
            }
        }

        private void AddFileSection(string fileName, Dictionary<string, object> data)
        {
            var filePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            // Use plugin_name field if available, otherwise use filename
            string headerText = fileName;
            if (data.TryGetValue("plugin_name", out var pluginNameObj) && pluginNameObj != null)
            {
                headerText = pluginNameObj.ToString() ?? fileName;
            }

            // File name header
            var fileHeader = new TextBlock
            {
                Text = headerText,
                Foreground = new SolidColorBrush(Color.FromRgb(0x41, 0xB6, 0xFF)),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            filePanel.Children.Add(fileHeader);

            // Properties grid
            var propertiesPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            
            foreach (var kvp in data.OrderBy(x => x.Key))
            {
                // Skip plugin_name if it was used as header
                if (kvp.Key == "plugin_name" && data.TryGetValue("plugin_name", out var _))
                    continue;
                
                AddPropertyRow(propertiesPanel, kvp.Key, kvp.Value, indent: 0);
            }

            filePanel.Children.Add(propertiesPanel);
            configDetailsPanel.Children.Add(filePanel);
        }

        private void AddPropertyRow(StackPanel container, string key, object value, int indent)
        {
            var rowPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(indent * 12, 4, 0, 4)
            };

            // Property name - convert to human readable format
            string displayKey = ToHumanReadableName(key);
            var keyBlock = new TextBlock
            {
                Text = displayKey + ":",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xBB, 0xFF)),
                FontSize = 11,
                MinWidth = 140,
                VerticalAlignment = VerticalAlignment.Top
            };
            rowPanel.Children.Add(keyBlock);

            // Property value
            if (value is Dictionary<string, object> dict)
            {
                var nestedPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
                var nestedHeader = new TextBlock
                {
                    Text = "{",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xC0)),
                    FontSize = 11
                };
                nestedPanel.Children.Add(nestedHeader);

                foreach (var nestedKvp in dict.OrderBy(x => x.Key))
                {
                    AddPropertyRow(nestedPanel, nestedKvp.Key, nestedKvp.Value, indent: 1);
                }

                var closeBrace = new TextBlock
                {
                    Text = "}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xC0)),
                    FontSize = 11
                };
                nestedPanel.Children.Add(closeBrace);
                rowPanel.Children.Add(nestedPanel);
            }
            else
            {
                var valueBlock = new TextBlock
                {
                    Text = FormatValue(value),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xC0, 0x88)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                };
                rowPanel.Children.Add(valueBlock);
            }

            container.Children.Add(rowPanel);
        }

        private string FormatValue(object value)
        {
            if (value == null)
                return "null";
            
            if (value is JsonElement elem)
            {
                return elem.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => $"\"{elem.GetString()}\"",
                    System.Text.Json.JsonValueKind.Number => elem.GetRawText(),
                    System.Text.Json.JsonValueKind.True => "true",
                    System.Text.Json.JsonValueKind.False => "false",
                    _ => elem.GetRawText()
                };
            }

            if (value is string str)
                return $"\"{str}\"";
            
            if (value is bool b)
                return b ? "true" : "false";
            
            if (value is JsonElement)
                return value.ToString() ?? "";

            return value.ToString() ?? "";
        }

        private string ToHumanReadableName(string fieldName)
        {
            // Convert snake_case to Title Case
            // e.g., "plugin_author" -> "Plugin Author"
            //       "last_update" -> "Last Update"
            //       "version" -> "Version"
            return string.Join(" ", 
                fieldName.Split('_')
                    .Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower()));
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_toolsOnlyMode)
            {
                Application.Current.Shutdown();
                return;
            }

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

        public void LoadGameSettings()
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
                
                // If anticheat was empty, default to first plugin and save
                if (string.IsNullOrEmpty(_settings.plugins.anticheat))
                {
                    DefaultAnticheatToFirstPlugin();
                }
            }
            catch
            {
                MessageBox.Show("Your settings have been reset due to an update. Please reconfigure them.",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                NavigationService?.GoBack();
            }
        }
        
        private void DefaultAnticheatToFirstPlugin()
        {
            try
            {
                string pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
                
                if (!Directory.Exists(pluginsPath))
                    return;

                // Check if current anticheat's plugin still exists with valid DLL
                if (!string.IsNullOrEmpty(_settings.plugins.anticheat))
                {
                    string currentPluginPath = Path.Combine(pluginsPath, _settings.plugins.anticheat);
                    var dllFiles = Directory.GetFiles(currentPluginPath, "*.dll");
                    
                    // If plugin folder exists and has a DLL, keep it
                    if (Directory.Exists(currentPluginPath) && dllFiles.Any())
                        return;
                    
                    // Plugin or DLL is missing, will default to first below
                }

                var firstPlugin = Directory.GetDirectories(pluginsPath)
                    .Where(dir => Directory.GetFiles(dir, "*.json").Any() && Directory.GetFiles(dir, "*.dll").Any())
                    .OrderBy(dir => new DirectoryInfo(dir).Name)
                    .FirstOrDefault();

                if (firstPlugin != null)
                {
                    _settings.plugins.anticheat = new DirectoryInfo(firstPlugin).Name;
                    File.WriteAllText(SettingsFilePath, System.Text.Json.JsonSerializer.Serialize(
                        _settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch
            {
                // silently fail if plugins folder doesn't exist or can't be read
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

            if (cmbAnticheatPlugin.SelectedItem is ComboBoxItem pluginItem)
                _settings.plugins.anticheat = pluginItem.Tag?.ToString() ?? "";

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

        // ─── Command Card Tab ──────────────────────────────────────────────────────

        private record CcSlotDef(int Row, int Col, string CsfId, string? ImageCsfId = null)
        {
            /// <summary>BIG/TGA lookup id (mirrors web slotDefImageId).</summary>
            public string ImageLookupId => ImageCsfId ?? CsfId;
        }

        /// <summary>
        /// Star power slot: LabelCsfId = CSF key to edit; ImageCsfId = TGA icon lookup (may differ).
        /// </summary>
        private record StarSlotRef(string LabelCsfId, string ImageCsfId)
        {
            public StarSlotRef(string csfId) : this(csfId, csfId) { }
        }

        private static string NormalizeCsfId(string id)
            => id.Contains(':') ? id : "controlbar:" + id;

        private static CcSlotDef CC(int n, string id)
            => new CcSlotDef((n - 1) / 7, (n - 1) % 7, NormalizeCsfId(id));

        /// <summary>Label CSF + separate icon CSF (web slotWithImage).</summary>
        private static CcSlotDef CCI(int n, string labelId, string imageId)
            => new CcSlotDef((n - 1) / 7, (n - 1) % 7, NormalizeCsfId(labelId), NormalizeCsfId(imageId));

        private static string BareCsfId(string csfId)
        {
            int ci = csfId.IndexOf(':');
            return ci >= 0 ? csfId[(ci + 1)..] : csfId;
        }

        private static readonly Dictionary<string, string[]> _shortcutLabelAliasesByBare
            = new(StringComparer.OrdinalIgnoreCase)
        {
            // General powers often use a separate SPECIAL_POWER_FROM_SHORTCUT button.
            // Keep that command's TextLabel hotkey in sync with the visible command-card label.
            ["a10thunderboltmissilestrike"] = new[] { "GUI:SuperweaponA10ThunderboltMissileStrike" },
            ["ambush"] = new[] { "GUI:SuperweaponRebelAmbush" },
            ["anthraxbomb"] = new[] { "OBJECT:AnthraxBomb" },
            ["artillerybarrage"] = new[] { "CONTROLBAR:NoHotKeyArtilleryBarrage" },
            ["carpetbomb"] = new[] { "OBJECT:CarpetBomb", "OBJECT:Nuke_CarpetBomb" },
            ["cashhack"] = new[] { "GUI:SuperweaponCashHack" },
            ["ciaintelligence"] = new[] { "CONTROLBAR:CIAIntelligenceShortcut" },
            ["clustermines"] = new[] { "OBJECT:ClusterMinesBomb" },
            ["communicationsdownload"] = new[] { "CONTROLBAR:CommunicationsDownloadShortcut" },
            ["daisycutter"] = new[] { "OBJECT:DaisyCutterBomb" },
            ["emergencyrepair"] = new[] { "GUI:SuperweaponEmergencyRepair" },
            ["emppulse"] = new[] { "OBJECT:EMPPulseBomb" },
            ["fireparticleuplinkcannon"] = new[] { "CONTROLBAR:FireParticleUplinkCannonShortcut" },
            ["frenzy"] = new[] { "CONTROLBAR:NoHotKeyFrenzy" },
            ["gpsscrambler"] = new[] { "GUI:SuperweaponGPSScrambler" },
            ["icbm"] = new[] { "CONTROLBAR:ICBMShortcut" },
            ["leafletdrop"] = new[] { "CONTROLBAR:LeafletDropShort" },
            ["napalmstrike"] = new[] { "GUI:SuperweaponNapalmStrike" },
            ["neutronmissile"] = new[] { "CONTROLBAR:NeutronMissileShortcut" },
            ["nukedrop"] = new[] { "OBJECT:NukeDrop" },
            ["paradrop"] = new[] { "GUI:SuperweaponParadropAmerica" },
            ["radarvanscan"] = new[] { "CONTROLBAR:RadarVanScanShortcut" },
            ["scudstorm"] = new[] { "CONTROLBAR:ScudStormShortcut" },
            ["spydrone"] = new[] { "OBJECT:SpyDrone" },
            ["spysatellite"] = new[] { "CONTROLBAR:NoHotKeySpySatellite" },
            ["tankparadrop"] = new[] { "GUI:SuperweaponTankParadrop" },
        };

        private static readonly string[] _usaSharedUnits = {
            "Ranger", "Colonel Burton", "Pathfinder", "Humvee", "Avenger",
            "Tomahawk", "Medic", "Sentry Drone", "Microwave Tank",
            "Comanche", "Stealth Fighter", "Aurora", "Raptor", "Chinook",
        };

        private static readonly string[] _chinaSharedUnits = {
            "Red Guard", "Tank Hunter", "Hacker", "Black Lotus",
            "Helix", "MIG", "Dragon Tank", "Troop Crawler",
            "Listening Outpost", "ECM Tank", "Supply Truck", "Overlord", "Nuke Launcher",
        };

        private static readonly string[] _glaSharedUnits = {
            "Rebel", "Terrorist", "Jarmen Kell", "Radar Van", "Toxin Truck",
            "Combat Bike", "Battle Bus", "Technical", "Scud Launcher", "Bomb Truck",
        };

        private static readonly Dictionary<string, (string[] Buildings, string[] Units)> _ccArmyData = new()
        {
            ["USA Laser (Townes)"] = (
                new[] { "USA Power Plant", "USA Barracks", "USA Supply Center", "Laser Patriot", "Firebase",
                        "USA War Factory", "USA Airfield", "Strategy Center", "Supply Drop Zone",
                        "Particle Cannon Uplink", "USA Command Center" },
                new[] { "USA Dozer", "S/Missile Defender" }
                    .Concat(_usaSharedUnits).Append("Laser Crusader").ToArray()
            ),
            ["USA Superweapon (Alexander)"] = (
                new[] { "SW Power Plant", "USA Barracks", "USA Supply Center", "EMP Patriot", "Firebase",
                        "USA War Factory", "USA Airfield", "Strategy Center", "Supply Drop Zone",
                        "SW Particle Cannon", "USA Command Center" },
                new[] { "USA Dozer", "S/Missile Defender" }
                    .Concat(_usaSharedUnits).Append("Fuel Air Aurora").ToArray()
            ),
            ["USA Air Force (Granger)"] = (
                new[] { "USA Power Plant", "USA Barracks", "USA Supply Center", "Patriot Battery", "Firebase",
                        "USA War Factory", "USA Airfield", "Strategy Center", "Supply Drop Zone",
                        "Particle Cannon Uplink", "USA Command Center" },
                new[] { "USA Dozer", "S/Missile Defender" }
                    .Concat(_usaSharedUnits).Concat(new[] { "King Raptor", "Chinook (Air Force)" }).ToArray()
            ),
            ["USA General"] = (
                new[] { "USA Power Plant", "USA Barracks", "USA Supply Center", "Patriot Battery", "Firebase",
                        "USA War Factory", "USA Airfield", "Strategy Center", "Supply Drop Zone",
                        "Particle Cannon Uplink", "USA Command Center" },
                new[] { "USA Dozer", "S/Missile Defender" }
                    .Concat(_usaSharedUnits).Concat(new[] { "Crusader", "Paladin" }).ToArray()
            ),
            ["China Tank (Kwai)"] = (
                new[] { "Power Plant", "Barracks (Advanced)", "Supply Center", "Bunker", "Gattling Cannon",
                        "War Factory", "Internet Center", "Airfield", "Propaganda Center", "Speaker Tower",
                        "Nuclear Missile Launcher", "Command Center" },
                new[] { "Dozer" }.Concat(_chinaSharedUnits).ToArray()
            ),
            ["China Infantry (Shin Fai)"] = (
                new[] { "Power Plant", "Barracks (Advanced)", "Supply Center", "Bunker", "Gattling Cannon",
                        "War Factory", "Internet Center", "Airfield", "Propaganda Center", "Speaker Tower",
                        "Nuclear Missile Launcher", "Command Center" },
                new[] { "Dozer", "Mini-Gunner" }
            ),
            ["China Nuke (Tao)"] = (
                new[] { "Power Plant", "Barracks (Advanced)", "Supply Center", "Bunker", "Gattling Cannon",
                        "War Factory", "Internet Center", "Airfield", "Propaganda Center", "Speaker Tower",
                        "Nuclear Missile Launcher", "Command Center" },
                new[] { "Dozer" }.Concat(_chinaSharedUnits).ToArray()
            ),
            ["China General"] = (
                new[] { "Power Plant", "Barracks (Advanced)", "Supply Center", "Bunker", "Gattling Cannon",
                        "War Factory", "Internet Center", "Airfield", "Propaganda Center", "Speaker Tower",
                        "Nuclear Missile Launcher", "Command Center" },
                new[] { "Dozer" }.Concat(_chinaSharedUnits).ToArray()
            ),
            ["GLA Toxin (Thrax)"] = (
                new[] { "Supply Stash", "Barracks", "Stinger Site", "Tunnel Network", "Arms Dealer",
                        "Demo Trap", "Palace", "Black Market", "Scud Storm", "GLA Command Center",
                        "Fake Arms Dealer", "Fake Barracks", "Fake Black Market", "Fake Command Center", "Fake Supply Stash" },
                new[] { "Worker (Toxin)" }.Concat(_glaSharedUnits).ToArray()
            ),
            ["GLA Demo (Juhziz)"] = (
                new[] { "Supply Stash", "Barracks", "Stinger Site", "Tunnel Network", "Arms Dealer",
                        "Demo Trap", "Palace", "Black Market", "Scud Storm", "GLA Command Center",
                        "Fake Arms Dealer", "Fake Barracks", "Fake Black Market", "Fake Command Center", "Fake Supply Stash" },
                new[] { "Worker (Demo)" }.Concat(_glaSharedUnits).ToArray()
            ),
            ["GLA Stealth (Kassad)"] = (
                new[] { "Supply Stash", "Barracks", "Stinger Site", "Tunnel Network", "Arms Dealer",
                        "Demo Trap", "Palace", "Black Market", "Scud Storm", "GLA Command Center",
                        "Fake Arms Dealer", "Fake Barracks", "Fake Black Market", "Fake Command Center", "Fake Supply Stash" },
                new[] { "Worker" }.Concat(_glaSharedUnits).ToArray()
            ),
            ["GLA General"] = (
                new[] { "Supply Stash", "Barracks", "Stinger Site", "Tunnel Network", "Arms Dealer",
                        "Demo Trap", "Palace", "Black Market", "Scud Storm", "GLA Command Center",
                        "Fake Arms Dealer", "Fake Barracks", "Fake Black Market", "Fake Command Center", "Fake Supply Stash" },
                new[] { "Worker" }.Concat(_glaSharedUnits).ToArray()
            ),
        };

        private static readonly Dictionary<string, List<CcSlotDef>> _ccSlotMap = new()
        {
            // ─── China Buildings ───────────────────────────────────────────────────────
            ["Power Plant"] = new() {
                CC(1,"overcharge"), CC(13,"upgradechinamines"), CC(14,"sell"),
            },
            ["Supply Center"] = new() {
                CC(1,"constructchinavehiclesupplytruck"), CC(7,"setrallypoint"),
                CC(13,"upgradechinamines"), CC(14,"sell"),
            },
            ["Barracks (Advanced)"] = new() {
                CC(1,"constructchinainfantryminigunner"), CC(2,"controlbar:infa_constructchinainfantryhacker"),
                CC(4,"capturebuilding"), CC(7,"setrallypoint"),
                CC(8,"constructchinainfantrytankhunter"), CC(9,"controlbar:infa_constructchinainfantryblacklotus"),
                CC(13,"upgradechinamines"), CC(14,"sell"),
            },
            ["Bunker"] = new() {
                CC(1,"structureexit"), CC(2,"structureexit"), CC(3,"structureexit"),
                CC(7,"stop"), CC(8,"structureexit"), CC(9,"structureexit"), CC(10,"evacuate"),
                CC(13,"upgradechinamines"), CC(14,"sell"),
            },
            ["Gattling Cannon"] = new() {
                CC(7,"stop"), CC(13,"upgradechinamines"), CC(14,"sell"),
            },
            ["War Factory"] = new() {
                CC(1,"constructglatankbattlemaster"), CC(2,"constructchinavehicletroopcrawler"),
                CC(3,"constructchinatankgattling"), CC(4,"constructchinatankdragon"),
                CC(5,"constructchinavehicleinfernocannon"), CC(6,"constructchinatankecm"),
                CC(7,"setrallypoint"), CC(8,"constructchinatankoverlord"),
                CC(9,"constructchinavehiclelisteningoutpost"), CC(10,"upgradechinachainguns"),
                CC(11,"upgradechinablacknapalm"), CC(12,"constructchinavehiclenukelauncher"),
                CC(13,"upgradechinamines"), CC(14,"sell"),
            },
            ["Internet Center"] = new() {
                CC(1,"structureexit"), CC(2,"structureexit"), CC(3,"structureexit"), CC(4,"structureexit"),
                CC(5,"evacuate"), CC(8,"structureexit"), CC(9,"structureexit"),
                CC(10,"structureexit"), CC(11,"structureexit"), CC(12,"upgradechinasatellitehackone"),
                CC(13,"upgradechinamines"), CC(14,"sell"),
            },
            ["Airfield"] = new() {
                CC(1,"constructchinajetmig"), CC(2,"constructchinavehiclehelix"),
                CC(7,"setrallypoint"), CC(8,"upgradechinaaircraftarmor"),
                CC(13,"upgradechinamines"), CC(14,"sell"),
            },
            ["Propaganda Center"] = new() {
                CC(1,"upgradechinanationalism"), CC(2,"upgradechinasubliminalmessaging"),
                CC(8,"upgrade:upgradechinaisotopestability"),
                CC(13,"upgradechinamines"), CC(14,"sell"),
            },
            ["Speaker Tower"] = new() {
                CC(14,"sell"),
            },
            ["Nuclear Missile Launcher"] = new() {
                CC(1,"neutronmissile"), CC(4,"upgradechinauraniumshells"),
                CC(11,"upgradechinanucleartanks"), CC(12,"upgradechinaneutronshells"),
                CC(13,"upgradechinamines"), CC(14,"sell"),
            },
            ["Command Center"] = new() {
                CC(1,"constructchinadozer"), CCI(3,"cashhack","science:chinacashhack1"), CC(4,"emergencyrepair"),
                CC(5,"upgradechinaradar"), CC(7,"setrallypoint"),
                CC(8,"science:chinacarpetbomb"), CCI(9,"science:chinaclustermines","clustermines"),
                CC(10,"science:chinaartillerybarrage"), CC(11,"emppulse"),
                CC(12,"frenzy"), CC(13,"upgradechinamines"), CC(14,"sell"),
            },
            // ─── USA Buildings ─────────────────────────────────────────────────────────
            ["USA Power Plant"] = new() {
                CC(1,"upgradeamericaadvancedcontrolrods"), CC(14,"sell"),
            },
            ["SW Power Plant"] = new() {
                CC(1,"supw_upgradeamericaadvancedcontrolrods"), CC(14,"sell"),
            },
            ["USA Barracks"] = new() {
                CC(1,"constructamericainfantryranger"), CC(2,"constructamericainfantrycolonelburton"),
                CC(4,"upgradeamericaflashbanggrenade"), CC(7,"setrallypoint"),
                CC(8,"constructamericainfantrymissiledefender"), CC(9,"constructamericainfantrypathfinder"),
                CC(11,"upgradeamericarangercapturebuilding"), CC(14,"sell"),
            },
            ["USA Supply Center"] = new() {
                CC(1,"constructamericavehiclechinook"), CC(14,"sell"),
            },
            ["Patriot Battery"] = new() {
                CC(7,"stop"), CC(14,"sell"),
            },
            ["Laser Patriot"] = new() {
                CC(7,"stop"), CC(14,"sell"),
            },
            ["EMP Patriot"] = new() {
                CC(7,"stop"), CC(14,"sell"),
            },
            ["Firebase"] = new() {
                CC(1,"structureexit"), CC(2,"structureexit"),
                CC(8,"structureexit"), CC(9,"structureexit"), CC(10,"evacuate"),
                CC(13,"sell"), CC(14,"stop"),
            },
            ["USA War Factory"] = new() {
                CC(1,"constructamericatankcrusader"), CC(2,"constructamericavehiclehumvee"),
                CC(3,"constructamericatankpaladin"), CC(4,"constructamericatankavenger"),
                CC(5,"upgradeamericasentrydronegun"), CC(6,"upgradeamericatowmissile"),
                CC(7,"setrallypoint"), CC(8,"constructamericavehicletomahawk"),
                CC(9,"constructamericavehiclemedic"), CC(10,"constructamericavehiclesentrydrone"),
                CC(11,"constructamericatankmicrowave"), CC(14,"sell"),
            },
            ["USA Airfield"] = new() {
                CC(1,"constructamericajetraptor"), CC(2,"constructamericajetaurora"),
                CC(4,"upgradecomancherocketpods"), CC(5,"upgradeamericacountermeasures"),
                CC(7,"setrallypoint"), CC(8,"constructamericavehiclecomanche"),
                CC(9,"constructamericajetstealthfighter"),
                CC(11,"upgradeamericalasermissiles"), CC(12,"upgradeamericabunkerbusters"),
                CC(14,"sell"),
            },
            ["Strategy Center"] = new() {
                CC(1,"initiatebattleplanbombardment"), CC(2,"initiatebattleplanholdtheline"),
                CC(3,"initiatebattleplansearchanddestroy"), CC(4,"upgradeamericamoab"),
                CC(5,"upgradeamericaadvancedtraining"), CC(6,"stop"),
                CC(7,"upgradeamericasupplylines"), CC(8,"ciaintelligence"),
                CC(10,"upgradeamericachemicalsuits"), CC(11,"upgradeamericacompositearmor"),
                CC(12,"upgradeamericadronearmor"), CC(14,"sell"),
            },
            ["Supply Drop Zone"] = new() {
                CC(1,"sell"),
            },
            ["Particle Cannon Uplink"] = new() {
                CC(1,"fireparticleuplinkcannon"), CC(14,"sell"),
            },
            ["SW Particle Cannon"] = new() {
                CC(1,"fireparticleuplinkcannon"), CC(14,"sell"),
            },
            ["USA Command Center"] = new() {
                CC(1,"constructamericadozer"), CC(3,"a10thunderboltmissilestrike"),
                CC(4,"spydrone"), CC(5,"daisycutter"), CC(7,"setrallypoint"),
                CCI(8,"spectregunshipfromshortcut","science:usaspectregunship1"),
                CCI(9,"leafletdrop","science:usaleafletdrop"),
                CC(10,"paradrop"), CCI(11,"emergencyrepair","controlbar:emergencyrepair1"),
                CC(12,"spysatellite"),
                CC(14,"sell"),
            },
            // ─── GLA Buildings ─────────────────────────────────────────────────────────
            ["Supply Stash"] = new() {
                CC(1,"constructglaworker"), CC(7,"setrallypoint"), CC(14,"sell"),
            },
            ["Barracks"] = new() {
                CC(1,"constructglainfantryrebel"), CC(2,"constructglainfantryterrorist"),
                CC(3,"constructglainfantryhijacker"), CC(4,"constructglainfantrysaboteur"),
                CC(6,"upgradeglarebelcapturebuilding"), CC(7,"setrallypoint"),
                CC(8,"constructglainfantryrpgtrooper"), CC(9,"constructglainfantryangrymob"),
                CC(10,"constructglainfantryjarmenkell"), CC(11,"upgradeglaboobytrap"),
                CC(14,"sell"),
            },
            ["Stinger Site"] = new() {
                CC(7,"stop"), CC(14,"sell"),
            },
            ["Tunnel Network"] = new() {
                CC(1,"structureexit"), CC(2,"structureexit"), CC(3,"structureexit"),
                CC(4,"structureexit"), CC(5,"structureexit"), CC(6,"evacuate"),
                CC(7,"setrallypoint"), CC(8,"structureexit"), CC(9,"structureexit"),
                CC(10,"structureexit"), CC(11,"structureexit"), CC(12,"structureexit"),
                CC(14,"sell"),
            },
            ["Arms Dealer"] = new() {
                CC(1,"constructglatankscorpion"), CC(2,"constructglavehicleradarvan"),
                CC(3,"constructglavehicletoxintruck"), CC(4,"constructglatankmarauder"),
                CC(5,"constructglavehiclescudlauncher"), CC(6,"constructglavehiclecombatbike"),
                CC(7,"setrallypoint"), CC(8,"constructglavehicletechnical"),
                CC(9,"constructglavehiclequadcannon"), CC(10,"constructglavehiclerocketbuggy"),
                CC(11,"constructglavehiclebombtruck"), CC(12,"upgradeglascorpionrocket"),
                CC(13,"constructglavehiclebattlebus"), CC(14,"sell"),
            },
            ["Demo Trap"] = new() {
                CC(1,"detonatefakebuilding"),
            },
            ["Palace"] = new() {
                CC(1,"constructglainfantryrebel"), CC(2,"constructglainfantryangrymob"),
                CC(14,"sell"),
            },
            ["Black Market"] = new() {
                CC(14,"sell"),
            },
            ["Scud Storm"] = new() {
                CC(1,"glascudstormlaunched"), CC(14,"sell"),
            },
            ["GLA Command Center"] = new() {
                CC(1,"constructglaworker"), CC(3,"science:glarebelambush1"), CC(4,"science:glaanthraxbomb"),
                CC(7,"setrallypoint"), CCI(9,"gpsscrambler","science:gpsscrambler"),
                CCI(10,"emergencyrepair","science:emergencyrepair1"), CC(11,"science:glasneakattack"),
                CC(14,"sell"),
            },
            ["Fake Arms Dealer"] = new() {
                CC(1,"detonatefakebuilding"), CC(8,"becomerealglaarmsdealer"), CC(14,"sell"),
            },
            ["Fake Barracks"] = new() {
                CC(1,"detonatefakebuilding"), CC(8,"becomerealglabarracks"), CC(14,"sell"),
            },
            ["Fake Black Market"] = new() {
                CC(1,"detonatefakebuilding"), CC(8,"becomerealglablackmarket"), CC(14,"sell"),
            },
            ["Fake Command Center"] = new() {
                CC(1,"detonatefakebuilding"), CC(14,"sell"),
            },
            ["Fake Supply Stash"] = new() {
                CC(1,"detonatefakebuilding"), CC(8,"becomerealglasupplystash"), CC(14,"sell"),
            },
            // ─── Units ────────────────────────────────────────────────────────────────
            ["USA Dozer"] = new() {
                CC(1,"constructamericapowerplant"), CC(2,"constructamericabarracks"),
                CC(3,"constructamericasupplycenter"), CC(4,"constructamericapatriotbattery"),
                CC(5,"constructamericafirebase"), CC(6,"constructamericawarfactory"),
                CC(7,"constructamericaairfield"), CC(8,"constructamericastrategycenter"),
                CC(9,"constructamericasupplydropzone"), CC(10,"constructamericaparticlecannonuplink"),
                CC(11,"constructamericacommandcenter"), CC(14,"disarmminesatposition"),
            },
            ["USA Dozer (Laser)"] = new() {
                CC(1,"constructamericapowerplant"), CC(2,"constructamericabarracks"),
                CC(3,"constructamericasupplycenter"), CC(4,"lazr_constructamericapatriotbattery"),
                CC(5,"constructamericafirebase"), CC(6,"constructamericawarfactory"),
                CC(7,"constructamericaairfield"), CC(8,"constructamericastrategycenter"),
                CC(9,"constructamericasupplydropzone"), CC(10,"constructamericaparticlecannonuplink"),
                CC(11,"constructamericacommandcenter"), CC(14,"disarmminesatposition"),
            },
            ["USA Dozer (SW)"] = new() {
                CC(1,"supw_constructamericapowerplant"), CC(2,"constructamericabarracks"),
                CC(3,"constructamericasupplycenter"), CC(4,"supw_constructamericapatriotbattery"),
                CC(5,"constructamericafirebase"), CC(6,"constructamericawarfactory"),
                CC(7,"constructamericaairfield"), CC(8,"constructamericastrategycenter"),
                CC(9,"constructamericasupplydropzone"), CC(10,"supw_constructamericaparticlecannonuplink"),
                CC(11,"constructamericacommandcenter"), CC(14,"disarmminesatposition"),
            },
            ["S/Missile Defender"] = new() {
                CC(1,"lasermissileattack"), CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Dozer"] = new() {
                CC(1,"constructchinapowerplant"), CC(2,"constructchinabarracks"),
                CC(3,"constructchinasupplycenter"), CC(4,"constructchinabunker"),
                CC(5,"constructchinagattlingcannon"), CC(6,"constructchinawarfactory"),
                CC(8,"constructchinainternetcenter"), CC(9,"constructchinaairfield"),
                CC(10,"constructchinapropagandacenter"), CC(11,"constructchinaspeakertower"),
                CC(12,"constructchinanuclearmissilelauncher"), CC(13,"constructchinacommandcenter"),
                CC(14,"disarmminesatposition"),
            },
            ["Dozer (Nuke)"] = new() {
                CC(1,"nuke_constructchinapowerplant"), CC(2,"constructchinabarracks"),
                CC(3,"constructchinasupplycenter"), CC(4,"constructchinabunker"),
                CC(5,"constructchinagattlingcannon"), CC(6,"constructchinawarfactory"),
                CC(8,"constructchinainternetcenter"), CC(9,"constructchinaairfield"),
                CC(10,"constructchinapropagandacenter"), CC(11,"constructchinaspeakertower"),
                CC(12,"constructchinanuclearmissilelauncher"), CC(13,"constructchinacommandcenter"),
                CC(14,"disarmminesatposition"),
            },
            ["Dozer (Infantry)"] = new() {
                CC(1,"constructchinapowerplant"), CC(2,"constructchinabarracks"),
                CC(3,"constructchinasupplycenter"), CC(4,"infa_constructchinabunker"),
                CC(5,"constructchinagattlingcannon"), CC(6,"constructchinawarfactory"),
                CC(8,"constructchinainternetcenter"), CC(9,"constructchinaairfield"),
                CC(10,"constructchinapropagandacenter"), CC(11,"constructchinaspeakertower"),
                CC(12,"constructchinanuclearmissilelauncher"), CC(13,"constructchinacommandcenter"),
                CC(14,"disarmminesatposition"),
            },
            ["Mini-Gunner"] = new() {
                CC(1,"capturebuilding"), CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Worker"] = new() {
                CC(1,"constructglasupplystash"), CC(2,"constructglabarracks"),
                CC(3,"constructglastingersite"), CC(4,"constructglatunnelnetwork"),
                CC(5,"constructglaarmsdealer"), CC(7,"upgradeglaworkerfakecommandset"),
                CC(8,"constructglademotrap"), CC(9,"constructglapalace"),
                CC(10,"constructglablackmarket"), CC(11,"constructglascudstorm"),
                CC(12,"constructglacommandcenter"), CC(14,"disarmminesatposition"),
            },
            ["Worker (Toxin)"] = new() {
                CC(1,"constructglasupplystash"), CC(2,"constructglabarracks"),
                CC(3,"constructglastingersite"), CC(4,"chem_constructglatunnelnetwork"),
                CC(5,"constructglaarmsdealer"), CC(7,"upgradeglaworkerfakecommandset"),
                CC(8,"constructglademotrap"), CC(9,"constructglapalace"),
                CC(10,"constructglablackmarket"), CC(11,"constructglascudstorm"),
                CC(12,"constructglacommandcenter"), CC(14,"disarmminesatposition"),
            },
            ["Worker (Demo)"] = new() {
                CC(1,"constructglasupplystash"), CC(2,"constructglabarracks"),
                CC(3,"constructglastingersite"), CC(4,"constructglatunnelnetwork"),
                CC(5,"constructglaarmsdealer"), CC(6,"suicideattack"), CC(7,"upgradeglaworkerfakecommandset"),
                CC(8,"demo_constructglademotrap"), CC(9,"constructglapalace"),
                CC(10,"constructglablackmarket"), CC(11,"constructglascudstorm"),
                CC(12,"constructglacommandcenter"), CC(14,"disarmminesatposition"),
            },
            // ─── USA Unit Cards ────────────────────────────────────────────────────────
            ["Ranger"] = new() {
                CC(1,"capturebuilding"), CC(6,"attackmove"), CC(7,"guard"),
                CC(8,"rangermachinegun"), CC(9,"flashbanggrenademode"), CC(14,"stop"),
            },
            ["Colonel Burton"] = new() {
                CC(1,"knifeattack"), CC(6,"attackmove"), CC(7,"guard"),
                CC(8,"timeddemocharge"), CC(9,"remotedemocharge"), CC(10,"detonatecharges"), CC(14,"stop"),
            },
            ["Pathfinder"] = new() {
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Humvee"] = new() {
                CC(1,"constructamericavehiclebattledrone"), CC(2,"constructamericavehiclehellfiredrone"),
                CC(3,"transportexit"), CC(4,"transportexit"), CC(5,"evacuate"),
                CC(6,"attackmove"), CC(7,"guard"),
                CC(8,"constructamericavehiclescoutdrone"),
                CC(9,"transportexit"), CC(10,"transportexit"), CC(14,"stop"),
            },
            ["Avenger"] = new() {
                CC(1,"constructamericavehiclebattledrone"), CC(2,"constructamericavehiclehellfiredrone"),
                CC(6,"attackmove"), CC(7,"guard"),
                CC(8,"constructamericavehiclescoutdrone"), CC(14,"stop"),
            },
            ["Tomahawk"] = new() {
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Medic"] = new() {
                CC(1,"constructamericavehiclebattledrone"), CC(2,"constructamericavehiclehellfiredrone"),
                CC(3,"transportexit"), CC(6,"attackmove"), CC(7,"guard"),
                CC(8,"constructamericavehiclescoutdrone"),
                CC(9,"transportexit"), CC(10,"transportexit"),
                CC(11,"evacuate"), CC(12,"ambulancecleanuparea"), CC(14,"stop"),
            },
            ["Sentry Drone"] = new() {
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Microwave Tank"] = new() {
                CC(1,"constructamericavehiclebattledrone"), CC(2,"constructamericavehiclehellfiredrone"),
                CC(6,"attackmove"), CC(7,"guard"),
                CC(8,"constructamericavehiclescoutdrone"), CC(14,"stop"),
            },
            ["Comanche"] = new() {
                CC(1,"firerocketpods"), CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Stealth Fighter"] = new() {
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Aurora"] = new() {
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Raptor"] = new() {
                CC(6,"attackmove"), CC(7,"guard"), CC(12,"guardflyingunitsonly"), CC(14,"stop"),
            },
            ["Chinook"] = new() {
                CC(1,"transportexit"), CC(2,"transportexit"), CC(3,"transportexit"), CC(4,"transportexit"),
                CC(5,"evacuate"),
                CC(8,"transportexit"), CC(9,"transportexit"), CC(10,"transportexit"), CC(11,"transportexit"),
                CC(12,"combatdrop"), CC(14,"stop"),
            },
            ["King Raptor"] = new() {
                CC(6,"attackmove"), CC(7,"guard"), CC(12,"guardflyingunitsonly"), CC(14,"stop"),
            },
            ["Chinook (Air Force)"] = new() {
                CC(1,"transportexit"), CC(2,"transportexit"), CC(3,"transportexit"), CC(4,"transportexit"),
                CC(5,"evacuate"),
                CC(8,"transportexit"), CC(9,"transportexit"), CC(10,"transportexit"), CC(11,"transportexit"),
                CC(12,"combatdrop"), CC(14,"stop"),
            },
            ["Fuel Air Aurora"] = new() {
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Crusader"] = new() {
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Paladin"] = new() {
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Laser Crusader"] = new() {
                CC(1,"constructamericavehiclebattledrone"), CC(2,"constructamericavehiclehellfiredrone"),
                CC(6,"attackmove"), CC(7,"guard"),
                CC(8,"constructamericavehiclescoutdrone"), CC(14,"stop"),
            },
            // ─── China Unit Cards ──────────────────────────────────────────────────────
            ["Red Guard"] = new() {
                CC(1,"capturebuilding"), CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Tank Hunter"] = new() {
                CC(1,"tntattack"), CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Hacker"] = new() {
                CC(1,"disablebuildinghack"), CC(2,"internethack"),
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Black Lotus"] = new() {
                CC(1,"capturebuilding"), CC(2,"disablevehiclehack"), CC(3,"stealcashhack"),
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Helix"] = new() {
                CC(1,"transportexit"), CC(2,"transportexit"), CC(3,"transportexit"),
                CC(4,"upgradehelixnapalmbomb"), CCI(5,"dropnapalmbomb","upgradehelixnapalmbomb"),
                CC(6,"attackmove"), CC(7,"guard"),
                CC(8,"transportexit"), CC(9,"transportexit"),
                CC(10,"upgradechinahelixbattlebunker"),
                CC(11,"upgradechinahelixpropagandatower"),
                CC(12,"upgradechinahelixgattlingcannon"),
                CC(13,"evacuate"), CC(14,"stop"),
            },
            ["MIG"] = new() {
                CC(6,"attackmove"), CC(7,"guard"), CC(13,"guardflyingunitsonly"), CC(14,"stop"),
            },
            ["Dragon Tank"] = new() {
                CC(1,"firewall"), CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Troop Crawler"] = new() {
                CC(1,"transportexit"), CC(2,"transportexit"), CC(3,"transportexit"), CC(4,"transportexit"),
                CC(5,"evacuate"),
                CC(8,"transportexit"), CC(9,"transportexit"), CC(10,"transportexit"), CC(11,"transportexit"),
                CC(14,"stop"),
            },
            ["Listening Outpost"] = new() {
                CC(1,"transportexit"), CC(6,"attackmove"), CC(7,"guard"),
                CC(8,"transportexit"), CC(13,"evacuate"), CC(14,"stop"),
            },
            ["ECM Tank"] = new() {
                CC(1,"ecmdisablevehicle"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Supply Truck"] = new() {
                CC(14,"stop"),
            },
            ["Overlord"] = new() {
                CC(1,"upgradechinaoverlordbattlebunker"),
                CC(2,"upgradechinaoverlordgattlingcannon"),
                CC(3,"upgradechinaoverlordpropagandatower"),
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Nuke Launcher"] = new() {
                CC(1,"nukewarhead"), CC(2,"neutronwarhead"),
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            // ─── GLA Unit Cards ────────────────────────────────────────────────────────
            ["Rebel"] = new() {
                CC(1,"capturebuilding"), CC(6,"attackmove"), CC(7,"guard"),
                CC(8,"upgradeglaboobytrap"), CC(14,"stop"),
            },
            ["Terrorist"] = new() {
                CC(1,"carbomb"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Jarmen Kell"] = new() {
                CC(1,"sniperattack"), CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Radar Van"] = new() {
                CC(1,"radarvanscan"), CC(14,"stop"),
            },
            ["Toxin Truck"] = new() {
                CC(1,"contaminate"), CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Combat Bike"] = new() {
                CC(1,"evacuate"), CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Battle Bus"] = new() {
                CC(1,"transportexit"), CC(2,"transportexit"), CC(3,"transportexit"), CC(4,"transportexit"),
                CC(8,"transportexit"), CC(9,"transportexit"), CC(10,"transportexit"), CC(11,"transportexit"),
                CC(12,"evacuate"), CC(14,"stop"),
            },
            ["Technical"] = new() {
                CC(1,"transportexit"), CC(2,"transportexit"), CC(3,"transportexit"),
                CC(8,"transportexit"), CC(9,"transportexit"),
                CC(12,"evacuate"), CC(14,"stop"),
            },
            ["Scud Launcher"] = new() {
                CC(1,"explosivewarhead"), CC(2,"anthraxwarhead"),
                CC(6,"attackmove"), CC(7,"guard"), CC(14,"stop"),
            },
            ["Bomb Truck"] = new() {
                CC(1,"disguiseasvehicle"), CC(2,"detonatebombtruck"),
                CC(4,"upgradeglabombtruckbiobomb"),
                CC(11,"upgradeglabombtruckhighexplosivebomb"),
                CC(14,"stop"),
            },
        };

        // ─── Star Power Slots (General's Powers) ──────────────────────────────────────
        // LabelCsfId = CSF key to edit; ImageCsfId = TGA icon lookup (may differ for star slots).
        // Slot → grid: row = (slot-1) / 7, col = (slot-1) % 7
        private static readonly StarSlotRef STAR_ER1     = new("CONTROLBAR:EmergencyRepair",             "SCIENCE:EmergencyRepair1");
        private static readonly StarSlotRef STAR_ER2     = new("CONTROLBAR:EmergencyRepair",             "SCIENCE:EmergencyRepair2");
        private static readonly StarSlotRef STAR_ER3     = new("CONTROLBAR:EmergencyRepair",             "SCIENCE:EmergencyRepair3");
        private static readonly StarSlotRef STAR_PD1     = new("CONTROLBAR:Paradrop",                    "SCIENCE:USAParaDrop1");
        private static readonly StarSlotRef STAR_AB1     = new("CONTROLBAR:ArtilleryBarrage",            "SCIENCE:ChinaArtilleryBarrage");
        private static readonly StarSlotRef STAR_FRENZY1 = new("CONTROLBAR:Frenzy",                      "SCIENCE:ChinaFrenzy");
        private static readonly StarSlotRef STAR_AMB1    = new("CONTROLBAR:Ambush",                      "SCIENCE:GLARebelAmbush1");
        private static readonly StarSlotRef STAR_ANTHRAX = new("CONTROLBAR:AnthraxBomb",                 "SCIENCE:GLAAnthraxBomb");
        private static readonly StarSlotRef STAR_CLUSTER = new("CONTROLBAR:ClusterMines",                "SCIENCE:ChinaClusterMines");
        private static readonly StarSlotRef STAR_SPY     = new("CONTROLBAR:SpyDrone",                    "SCIENCE:USASpyDrone");
        private static readonly StarSlotRef STAR_PATHFINDER = new("SCIENCE:USAPathfinder");
        private static readonly StarSlotRef STAR_DAISY   = new("CONTROLBAR:DaisyCutter",                 "SCIENCE:USADaisyCutter");
        private static readonly StarSlotRef STAR_A101    = new("CONTROLBAR:A10ThunderboltMissileStrike", "SCIENCE:USAA10Strike1");
        private static readonly StarSlotRef STAR_A102    = new("CONTROLBAR:A10ThunderboltMissileStrike", "SCIENCE:USAA10Strike2");
        private static readonly StarSlotRef STAR_A103    = new("CONTROLBAR:A10ThunderboltMissileStrike", "SCIENCE:USAA10Strike3");
        private static readonly StarSlotRef STAR_PD2     = new("CONTROLBAR:Paradrop", "SCIENCE:USAParaDrop2");
        private static readonly StarSlotRef STAR_PD3     = new("CONTROLBAR:Paradrop", "SCIENCE:USAParaDrop3");
        private static readonly StarSlotRef STAR_LEAFLET = new("CONTROLBAR:LeafletDrop", "SCIENCE:USALeafletDrop");
        private static readonly StarSlotRef STAR_SG1     = new("CONTROLBAR:SpectreGunshipFromShortcut", "SCIENCE:USASpectreGunship1");
        private static readonly StarSlotRef STAR_SG2     = new("CONTROLBAR:SpectreGunshipFromShortcut", "SCIENCE:USASpectreGunship2");
        private static readonly StarSlotRef STAR_SG3     = new("CONTROLBAR:SpectreGunshipFromShortcut", "SCIENCE:USASpectreGunship3");

        private static readonly StarSlotRef STAR_AB2     = new("CONTROLBAR:ArtilleryBarrage", "SCIENCE:ChinaArtilleryBarrage2");
        private static readonly StarSlotRef STAR_AB3     = new("CONTROLBAR:ArtilleryBarrage", "SCIENCE:ChinaArtilleryBarrage3");
        private static readonly StarSlotRef STAR_CASH_HACK1 = new("CONTROLBAR:CashHack", "SCIENCE:ChinaCashHack1");
        private static readonly StarSlotRef STAR_CASH_HACK2 = new("CONTROLBAR:CashHack", "SCIENCE:ChinaCashHack2");
        private static readonly StarSlotRef STAR_CASH_HACK3 = new("CONTROLBAR:CashHack", "SCIENCE:ChinaCashHack3");
        private static readonly StarSlotRef STAR_FRENZY2 = new("CONTROLBAR:Frenzy", "SCIENCE:ChinaFrenzy2");
        private static readonly StarSlotRef STAR_FRENZY3 = new("CONTROLBAR:Frenzy", "SCIENCE:ChinaFrenzy3");
        private static readonly StarSlotRef STAR_EMP     = new("CONTROLBAR:EMPPulse", "SCIENCE:ChinaEMPPulse");
        private static readonly StarSlotRef STAR_TANK_PD1 = new("CONTROLBAR:TankParadrop", "SCIENCE:ChinaTankParadrop1");
        private static readonly StarSlotRef STAR_TANK_PD2 = new("CONTROLBAR:TankParadrop", "SCIENCE:ChinaTankParadrop2");
        private static readonly StarSlotRef STAR_TANK_PD3 = new("CONTROLBAR:TankParadrop", "SCIENCE:ChinaTankParadrop3");
        private static readonly StarSlotRef STAR_CARPET  = new("CONTROLBAR:CarpetBomb", "SCIENCE:ChinaCarpetBomb");
        private static readonly StarSlotRef STAR_CARPET_NUKE = new("CONTROLBAR:CarpetBomb", "SCIENCE:Nuke_ChinaCarpetBomb");

        private static readonly StarSlotRef STAR_AMB2    = new("CONTROLBAR:Ambush", "SCIENCE:GLARebelAmbush2");
        private static readonly StarSlotRef STAR_AMB3    = new("CONTROLBAR:Ambush", "SCIENCE:GLARebelAmbush3");
        private static readonly StarSlotRef STAR_SNEAK   = new("CONTROLBAR:SneakAttack", "SCIENCE:GLASneakAttack");
        private static readonly StarSlotRef STAR_GPS     = new("CONTROLBAR:GPSScrambler", "SCIENCE:GPSScrambler");

        private static readonly Dictionary<string, Dictionary<int, StarSlotRef>> STAR_SLOT_MAP = new()
        {
            ["USA General"] = new() {
                [1]  = new StarSlotRef("SCIENCE:USAPaladin"),
                [2]  = new StarSlotRef("SCIENCE:USAStealthFighter"),
                [3]  = STAR_SPY,
                [5]  = STAR_PATHFINDER,
                [6]  = STAR_PD1,
                [7]  = STAR_A101,
                [8]  = STAR_ER1,
                [11] = STAR_PD2,
                [12] = STAR_A102,
                [13] = STAR_ER2,
                [16] = STAR_PD3,
                [17] = STAR_A103,
                [18] = STAR_ER3,
                [20] = STAR_DAISY,
                [21] = STAR_LEAFLET,
            },
            ["USA Laser (Townes)"] = new() {
                [1]  = STAR_SPY,
                [2]  = new StarSlotRef("SCIENCE:USAStealthFighter"),
                [5]  = STAR_PATHFINDER,
                [6]  = STAR_PD1,
                [7]  = STAR_A101,
                [8]  = STAR_ER1,
                [11] = STAR_PD2,
                [12] = STAR_A102,
                [13] = STAR_ER2,
                [16] = STAR_PD3,
                [17] = STAR_A103,
                [18] = STAR_ER3,
                [20] = STAR_DAISY,
                [21] = STAR_LEAFLET,
                [22] = STAR_SG1,
            },
            ["USA Air Force (Granger)"] = new() {
                [1]  = STAR_SPY,
                [2]  = new StarSlotRef("SCIENCE:USAStealthFighter"),
                [5]  = STAR_PATHFINDER,
                [6]  = STAR_PD1,
                [7]  = STAR_A101,
                [8]  = STAR_ER1,
                [11] = STAR_PD2,
                [12] = STAR_A102,
                [13] = STAR_ER2,
                [16] = STAR_PD3,
                [17] = STAR_A103,
                [18] = STAR_ER3,
                [20] = STAR_DAISY,
                [21] = STAR_LEAFLET,
                [22] = STAR_SG1,
            },
            ["USA Superweapon (Alexander)"] = new() {
                [1]  = STAR_SPY,
                [2]  = new StarSlotRef("SCIENCE:USAStealthFighter"),
                [5]  = STAR_PATHFINDER,
                [6]  = STAR_PD1,
                [7]  = STAR_A101,
                [8]  = STAR_ER1,
                [11] = STAR_PD2,
                [12] = STAR_A102,
                [13] = STAR_ER2,
                [14] = STAR_SG1,
                [16] = STAR_PD3,
                [17] = STAR_A103,
                [18] = STAR_ER3,
                [19] = STAR_SG2,
                [20] = STAR_DAISY,
                [21] = STAR_SG3,
            },
            ["China General"] = new() {
                [1]  = new StarSlotRef("SCIENCE:ChinaRedGuardTraining"),
                [2]  = new StarSlotRef("SCIENCE:ChinaArtilleryTraining"),
                [3]  = new StarSlotRef("SCIENCE:ChinaNukeLauncher"),
                [5]  = STAR_CLUSTER,
                [6]  = STAR_AB1,
                [7]  = STAR_CASH_HACK1,
                [8]  = STAR_ER1,
                [9]  = STAR_FRENZY1,
                [11] = STAR_AB2,
                [12] = STAR_CASH_HACK2,
                [13] = STAR_ER2,
                [14] = STAR_FRENZY2,
                [15] = STAR_CARPET,
                [16] = STAR_AB3,
                [17] = STAR_CASH_HACK3,
                [18] = STAR_ER3,
                [19] = STAR_FRENZY3,
                [20] = STAR_EMP,
            },
            ["China Infantry (Shin Fai)"] = new() {
                [1]  = new StarSlotRef("SCIENCE:INFA_ChinaRedGuardTraining"),
                [2]  = new StarSlotRef("SCIENCE:ChinaArtilleryTraining"),
                [3]  = new StarSlotRef("SCIENCE:ChinaNukeLauncher"),
                [4]  = STAR_FRENZY1,
                [5]  = STAR_CLUSTER,
                [6]  = STAR_AB1,
                [7]  = STAR_ER1,
                [8]  = STAR_FRENZY2,
                [9]  = STAR_PD1,
                [11] = STAR_AB2,
                [12] = STAR_ER2,
                [13] = STAR_FRENZY3,
                [14] = STAR_PD2,
                [15] = STAR_CARPET,
                [16] = STAR_AB3,
                [17] = STAR_ER3,
                [19] = STAR_PD3,
                [20] = STAR_EMP,
            },
            ["China Tank (Kwai)"] = new() {
                [1]  = new StarSlotRef("SCIENCE:ChinaRedGuardTraining"),
                [2]  = new StarSlotRef("SCIENCE:ChinaBattlemasterTraining"),
                [4]  = STAR_ER1,
                [5]  = STAR_CLUSTER,
                [6]  = STAR_AB1,
                [7]  = STAR_TANK_PD1,
                [8]  = STAR_ER2,
                [9]  = STAR_FRENZY1,
                [11] = STAR_AB2,
                [12] = STAR_TANK_PD2,
                [13] = STAR_ER3,
                [14] = STAR_FRENZY2,
                [16] = STAR_AB3,
                [17] = STAR_TANK_PD3,
                [19] = STAR_FRENZY3,
                [20] = STAR_EMP,
            },
            ["China Nuke (Tao)"] = new() {
                [1]  = new StarSlotRef("SCIENCE:ChinaRedGuardTraining"),
                [2]  = new StarSlotRef("SCIENCE:ChinaArtilleryTraining"),
                [4]  = STAR_ER1,
                [5]  = STAR_CLUSTER,
                [6]  = STAR_AB1,
                [7]  = STAR_CASH_HACK1,
                [8]  = STAR_ER2,
                [9]  = STAR_FRENZY1,
                [11] = STAR_AB2,
                [12] = STAR_CASH_HACK2,
                [13] = STAR_ER3,
                [14] = STAR_FRENZY2,
                [15] = STAR_CARPET_NUKE,
                [16] = STAR_AB3,
                [17] = STAR_CASH_HACK3,
                [19] = STAR_FRENZY3,
                [20] = STAR_EMP,
            },
            ["GLA General"] = new() {
                [1]  = new StarSlotRef("SCIENCE:GLAScudLauncher"),
                [2]  = new StarSlotRef("SCIENCE:GLAMaruaderTank"),
                [3]  = new StarSlotRef("SCIENCE:GLATechnicalTraining"),
                [5]  = new StarSlotRef("SCIENCE:GLAHijacker"),
                [6]  = STAR_AMB1,
                [7]  = new StarSlotRef("SCIENCE:GLACashBounty1"),
                [8]  = STAR_ER1,
                [11] = STAR_AMB2,
                [12] = new StarSlotRef("SCIENCE:GLACashBounty2"),
                [13] = STAR_ER2,
                [16] = STAR_AMB3,
                [17] = new StarSlotRef("SCIENCE:GLACashBounty3"),
                [18] = STAR_ER3,
                [20] = STAR_ANTHRAX,
                [21] = STAR_SNEAK,
                [22] = STAR_GPS,
            },
            ["GLA Demo (Juhziz)"] = new() {
                [1]  = new StarSlotRef("SCIENCE:GLAScudLauncher"),
                [2]  = new StarSlotRef("SCIENCE:GLAMaruaderTank"),
                [3]  = new StarSlotRef("SCIENCE:GLATechnicalTraining"),
                [6]  = STAR_AMB1,
                [7]  = new StarSlotRef("SCIENCE:GLACashBounty1"),
                [8]  = STAR_ER1,
                [11] = STAR_AMB2,
                [12] = new StarSlotRef("SCIENCE:GLACashBounty2"),
                [13] = STAR_ER2,
                [16] = STAR_AMB3,
                [17] = new StarSlotRef("SCIENCE:GLACashBounty3"),
                [18] = STAR_ER3,
                [20] = STAR_ANTHRAX,
                [21] = STAR_SNEAK,
                [22] = STAR_GPS,
            },
            ["GLA Toxin (Thrax)"] = new() {
                [1]  = new StarSlotRef("SCIENCE:GLAScudLauncher"),
                [2]  = new StarSlotRef("SCIENCE:GLAMaruaderTank"),
                [3]  = new StarSlotRef("SCIENCE:GLATechnicalTraining"),
                [6]  = STAR_AMB1,
                [7]  = new StarSlotRef("SCIENCE:GLACashBounty1"),
                [8]  = STAR_ER1,
                [11] = STAR_AMB2,
                [12] = new StarSlotRef("SCIENCE:GLACashBounty2"),
                [13] = STAR_ER2,
                [16] = STAR_AMB3,
                [17] = new StarSlotRef("SCIENCE:GLACashBounty3"),
                [18] = STAR_ER3,
                [20] = STAR_ANTHRAX,
                [21] = STAR_SNEAK,
                [22] = STAR_GPS,
            },
            ["GLA Stealth (Kassad)"] = new() {
                [1]  = new StarSlotRef("SCIENCE:GLATechnicalTraining"),
                [4]  = STAR_ER1,
                [5]  = STAR_GPS,
                [6]  = STAR_AMB1,
                [7]  = new StarSlotRef("SCIENCE:GLACashBounty1"),
                [8]  = STAR_ER2,
                [11] = STAR_AMB2,
                [12] = new StarSlotRef("SCIENCE:GLACashBounty2"),
                [13] = STAR_ER3,
                [16] = STAR_AMB3,
                [17] = new StarSlotRef("SCIENCE:GLACashBounty3"),
                [20] = STAR_ANTHRAX,
                [21] = STAR_SNEAK,
            },
        };

        // Unit list icon overrides — mirrors web's UNIT_LIST_IMAGE.
        // Maps army → (unitName → csfId of the construction/build button whose image to use).
        private static readonly Dictionary<string, Dictionary<string, string>> _unitListImageIds = new()
        {
            ["China General"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Power Plant"]              = "controlbar:constructchinapowerplant",
                ["Barracks (Advanced)"]      = "controlbar:constructchinabarracks",
                ["Supply Center"]            = "controlbar:constructchinasupplycenter",
                ["Bunker"]                   = "controlbar:constructchinabunker",
                ["Gattling Cannon"]          = "controlbar:constructchinagattlingcannon",
                ["War Factory"]              = "controlbar:constructchinawarfactory",
                ["Internet Center"]          = "controlbar:constructchinainternetcenter",
                ["Airfield"]                 = "controlbar:constructchinaairfield",
                ["Propaganda Center"]        = "controlbar:constructchinapropagandacenter",
                ["Speaker Tower"]            = "controlbar:constructchinaspeakertower",
                ["Nuclear Missile Launcher"] = "controlbar:constructchinanuclearmissilelauncher",
                ["Command Center"]           = "controlbar:constructchinacommandcenter",
                ["Dozer"]                    = "controlbar:constructchinadozer",
                ["Red Guard"]                = "controlbar:constructchinainfantryredguard",
                ["Tank Hunter"]              = "controlbar:constructchinainfantrytankhunter",
                ["Hacker"]                   = "controlbar:constructchinainfantryhacker",
                ["Black Lotus"]              = "controlbar:constructchinainfantryblacklotus",
                ["Helix"]                    = "controlbar:constructchinavehiclehelix",
                ["MIG"]                      = "controlbar:constructchinajetmig",
                ["Dragon Tank"]              = "controlbar:constructchinatankdragon",
                ["Troop Crawler"]            = "controlbar:constructchinavehicletroopcrawler",
                ["Listening Outpost"]        = "controlbar:constructchinavehiclelisteningoutpost",
                ["ECM Tank"]                 = "controlbar:constructchinatankecm",
                ["Supply Truck"]             = "controlbar:constructchinavehiclesupplytruck",
                ["Overlord"]                 = "controlbar:constructchinatankoverlord",
                ["Nuke Launcher"]            = "controlbar:constructchinavehiclenukelauncher",
            },
            ["China Tank (Kwai)"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Power Plant"]              = "controlbar:constructchinapowerplant",
                ["Barracks (Advanced)"]      = "controlbar:constructchinabarracks",
                ["Supply Center"]            = "controlbar:constructchinasupplycenter",
                ["Bunker"]                   = "controlbar:constructchinabunker",
                ["Gattling Cannon"]          = "controlbar:constructchinagattlingcannon",
                ["War Factory"]              = "controlbar:constructchinawarfactory",
                ["Internet Center"]          = "controlbar:constructchinainternetcenter",
                ["Airfield"]                 = "controlbar:constructchinaairfield",
                ["Propaganda Center"]        = "controlbar:constructchinapropagandacenter",
                ["Speaker Tower"]            = "controlbar:constructchinaspeakertower",
                ["Nuclear Missile Launcher"] = "controlbar:constructchinanuclearmissilelauncher",
                ["Command Center"]           = "controlbar:constructchinacommandcenter",
                ["Dozer"]                    = "controlbar:constructchinadozer",
                ["Red Guard"]                = "controlbar:constructchinainfantryredguard",
                ["Tank Hunter"]              = "controlbar:constructchinainfantrytankhunter",
                ["Hacker"]                   = "controlbar:constructchinainfantryhacker",
                ["Black Lotus"]              = "controlbar:constructchinainfantryblacklotus",
                ["Helix"]                    = "controlbar:constructchinavehiclehelix",
                ["MIG"]                      = "controlbar:constructchinajetmig",
                ["Dragon Tank"]              = "controlbar:constructchinatankdragon",
                ["Troop Crawler"]            = "controlbar:constructchinavehicletroopcrawler",
                ["Listening Outpost"]        = "controlbar:constructchinavehiclelisteningoutpost",
                ["ECM Tank"]                 = "controlbar:constructchinatankecm",
                ["Supply Truck"]             = "controlbar:constructchinavehiclesupplytruck",
                ["Overlord"]                 = "controlbar:constructchinatankoverlord",
                ["Nuke Launcher"]            = "controlbar:constructchinavehiclenukelauncher",
            },
            ["China Infantry (Shin Fai)"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Power Plant"]              = "controlbar:constructchinapowerplant",
                ["Barracks (Advanced)"]      = "controlbar:constructchinabarracks",
                ["Supply Center"]            = "controlbar:constructchinasupplycenter",
                ["Bunker"]                   = "controlbar:infa_constructchinabunker",
                ["Gattling Cannon"]          = "controlbar:constructchinagattlingcannon",
                ["War Factory"]              = "controlbar:constructchinawarfactory",
                ["Internet Center"]          = "controlbar:constructchinainternetcenter",
                ["Airfield"]                 = "controlbar:constructchinaairfield",
                ["Propaganda Center"]        = "controlbar:constructchinapropagandacenter",
                ["Speaker Tower"]            = "controlbar:constructchinaspeakertower",
                ["Nuclear Missile Launcher"] = "controlbar:constructchinanuclearmissilelauncher",
                ["Command Center"]           = "controlbar:constructchinacommandcenter",
                ["Dozer"]                    = "controlbar:constructchinadozer",
                ["Mini-Gunner"]              = "controlbar:constructchinainfantryminigunner",
            },
            ["China Nuke (Tao)"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Power Plant"]              = "controlbar:nuke_constructchinapowerplant",
                ["Barracks (Advanced)"]      = "controlbar:constructchinabarracks",
                ["Supply Center"]            = "controlbar:constructchinasupplycenter",
                ["Bunker"]                   = "controlbar:constructchinabunker",
                ["Gattling Cannon"]          = "controlbar:constructchinagattlingcannon",
                ["War Factory"]              = "controlbar:constructchinawarfactory",
                ["Internet Center"]          = "controlbar:constructchinainternetcenter",
                ["Airfield"]                 = "controlbar:constructchinaairfield",
                ["Propaganda Center"]        = "controlbar:constructchinapropagandacenter",
                ["Speaker Tower"]            = "controlbar:constructchinaspeakertower",
                ["Nuclear Missile Launcher"] = "controlbar:constructchinanuclearmissilelauncher",
                ["Command Center"]           = "controlbar:constructchinacommandcenter",
                ["Dozer"]                    = "controlbar:constructchinadozer",
                ["Red Guard"]                = "controlbar:constructchinainfantryredguard",
                ["Tank Hunter"]              = "controlbar:constructchinainfantrytankhunter",
                ["Hacker"]                   = "controlbar:constructchinainfantryhacker",
                ["Black Lotus"]              = "controlbar:constructchinainfantryblacklotus",
                ["Helix"]                    = "controlbar:constructchinavehiclehelix",
                ["MIG"]                      = "controlbar:constructchinajetmig",
                ["Dragon Tank"]              = "controlbar:constructchinatankdragon",
                ["Troop Crawler"]            = "controlbar:constructchinavehicletroopcrawler",
                ["Listening Outpost"]        = "controlbar:constructchinavehiclelisteningoutpost",
                ["ECM Tank"]                 = "controlbar:constructchinatankecm",
                ["Supply Truck"]             = "controlbar:constructchinavehiclesupplytruck",
                ["Overlord"]                 = "controlbar:constructchinatankoverlord",
                ["Nuke Launcher"]            = "controlbar:constructchinavehiclenukelauncher",
            },
            ["USA General"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["USA Power Plant"]          = "controlbar:constructamericapowerplant",
                ["USA Barracks"]             = "controlbar:constructamericabarracks",
                ["USA Supply Center"]        = "controlbar:constructamericasupplycenter",
                ["Patriot Battery"]          = "controlbar:constructamericapatriotbattery",
                ["Firebase"]                 = "controlbar:constructamericafirebase",
                ["USA War Factory"]          = "controlbar:constructamericawarfactory",
                ["USA Airfield"]             = "controlbar:constructamericaairfield",
                ["Strategy Center"]          = "controlbar:constructamericastrategycenter",
                ["Supply Drop Zone"]         = "controlbar:constructamericasupplydropzone",
                ["Particle Cannon Uplink"]   = "controlbar:constructamericaparticlecannonuplink",
                ["USA Command Center"]       = "controlbar:constructamericacommandcenter",
                ["USA Dozer"]                = "controlbar:constructamericadozer",
                ["S/Missile Defender"]       = "controlbar:constructamericainfantrymissiledefender",
                ["Ranger"]                   = "controlbar:constructamericainfantryranger",
                ["Colonel Burton"]           = "controlbar:constructamericainfantrycolonelburton",
                ["Pathfinder"]               = "controlbar:constructamericainfantrypathfinder",
                ["Humvee"]                   = "controlbar:constructamericavehiclehumvee",
                ["Avenger"]                  = "controlbar:constructamericatankavenger",
                ["Tomahawk"]                 = "controlbar:constructamericavehicletomahawk",
                ["Medic"]                    = "controlbar:constructamericavehiclemedic",
                ["Sentry Drone"]             = "controlbar:constructamericavehiclesentrydrone",
                ["Microwave Tank"]           = "controlbar:constructamericatankmicrowave",
                ["Comanche"]                 = "controlbar:constructamericavehiclecomanche",
                ["Stealth Fighter"]          = "controlbar:constructamericajetstealthfighter",
                ["Aurora"]                   = "controlbar:constructamericajetaurora",
                ["Raptor"]                   = "controlbar:constructamericajetraptor",
                ["Chinook"]                  = "controlbar:constructamericavehiclechinook",
                ["Crusader"]                 = "controlbar:constructamericatankcrusader",
                ["Paladin"]                  = "controlbar:constructamericatankpaladin",
            },
            ["USA Air Force (Granger)"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["USA Power Plant"]          = "controlbar:constructamericapowerplant",
                ["USA Barracks"]             = "controlbar:constructamericabarracks",
                ["USA Supply Center"]        = "controlbar:constructamericasupplycenter",
                ["Patriot Battery"]          = "controlbar:constructamericapatriotbattery",
                ["Firebase"]                 = "controlbar:constructamericafirebase",
                ["USA War Factory"]          = "controlbar:constructamericawarfactory",
                ["USA Airfield"]             = "controlbar:constructamericaairfield",
                ["Strategy Center"]          = "controlbar:constructamericastrategycenter",
                ["Supply Drop Zone"]         = "controlbar:constructamericasupplydropzone",
                ["Particle Cannon Uplink"]   = "controlbar:constructamericaparticlecannonuplink",
                ["USA Command Center"]       = "controlbar:constructamericacommandcenter",
                ["USA Dozer"]                = "controlbar:constructamericadozer",
                ["S/Missile Defender"]       = "controlbar:constructamericainfantrymissiledefender",
                ["Ranger"]                   = "controlbar:constructamericainfantryranger",
                ["Colonel Burton"]           = "controlbar:constructamericainfantrycolonelburton",
                ["Pathfinder"]               = "controlbar:constructamericainfantrypathfinder",
                ["Humvee"]                   = "controlbar:constructamericavehiclehumvee",
                ["Avenger"]                  = "controlbar:constructamericatankavenger",
                ["Tomahawk"]                 = "controlbar:constructamericavehicletomahawk",
                ["Medic"]                    = "controlbar:constructamericavehiclemedic",
                ["Sentry Drone"]             = "controlbar:constructamericavehiclesentrydrone",
                ["Microwave Tank"]           = "controlbar:constructamericatankmicrowave",
                ["Comanche"]                 = "controlbar:constructamericavehiclecomanche",
                ["Stealth Fighter"]          = "controlbar:constructamericajetstealthfighter",
                ["Aurora"]                   = "controlbar:constructamericajetaurora",
                ["Raptor"]                   = "controlbar:constructamericajetraptor",
                ["Chinook"]                  = "controlbar:constructamericavehiclechinook",
                ["King Raptor"]              = "controlbar:constructamericajetkingraptor",
                ["Chinook (Air Force)"]      = "controlbar:airf_constructamericavehiclechinook",
            },
            ["USA Laser (Townes)"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["USA Power Plant"]          = "controlbar:constructamericapowerplant",
                ["USA Barracks"]             = "controlbar:constructamericabarracks",
                ["USA Supply Center"]        = "controlbar:constructamericasupplycenter",
                ["Laser Patriot"]            = "controlbar:lazr_constructamericapatriotbattery",
                ["Firebase"]                 = "controlbar:constructamericafirebase",
                ["USA War Factory"]          = "controlbar:constructamericawarfactory",
                ["USA Airfield"]             = "controlbar:constructamericaairfield",
                ["Strategy Center"]          = "controlbar:constructamericastrategycenter",
                ["Supply Drop Zone"]         = "controlbar:constructamericasupplydropzone",
                ["Particle Cannon Uplink"]   = "controlbar:constructamericaparticlecannonuplink",
                ["USA Command Center"]       = "controlbar:constructamericacommandcenter",
                ["USA Dozer"]                = "controlbar:constructamericadozer",
                ["S/Missile Defender"]       = "controlbar:constructamericainfantrymissiledefender",
                ["Ranger"]                   = "controlbar:constructamericainfantryranger",
                ["Colonel Burton"]           = "controlbar:constructamericainfantrycolonelburton",
                ["Pathfinder"]               = "controlbar:constructamericainfantrypathfinder",
                ["Humvee"]                   = "controlbar:constructamericavehiclehumvee",
                ["Avenger"]                  = "controlbar:constructamericatankavenger",
                ["Tomahawk"]                 = "controlbar:constructamericavehicletomahawk",
                ["Medic"]                    = "controlbar:constructamericavehiclemedic",
                ["Sentry Drone"]             = "controlbar:constructamericavehiclesentrydrone",
                ["Microwave Tank"]           = "controlbar:constructamericatankmicrowave",
                ["Comanche"]                 = "controlbar:constructamericavehiclecomanche",
                ["Stealth Fighter"]          = "controlbar:constructamericajetstealthfighter",
                ["Aurora"]                   = "controlbar:constructamericajetaurora",
                ["Raptor"]                   = "controlbar:constructamericajetraptor",
                ["Chinook"]                  = "controlbar:constructamericavehiclechinook",
                ["Laser Crusader"]           = "controlbar:lazr_constructamericatankcrusader",
            },
            ["USA Superweapon (Alexander)"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SW Power Plant"]           = "controlbar:supw_constructamericapowerplant",
                ["USA Barracks"]             = "controlbar:constructamericabarracks",
                ["USA Supply Center"]        = "controlbar:constructamericasupplycenter",
                ["EMP Patriot"]              = "controlbar:supw_constructamericapatriotbattery",
                ["Firebase"]                 = "controlbar:constructamericafirebase",
                ["USA War Factory"]          = "controlbar:constructamericawarfactory",
                ["USA Airfield"]             = "controlbar:constructamericaairfield",
                ["Strategy Center"]          = "controlbar:constructamericastrategycenter",
                ["Supply Drop Zone"]         = "controlbar:constructamericasupplydropzone",
                ["SW Particle Cannon"]       = "controlbar:supw_constructamericaparticlecannonuplink",
                ["USA Command Center"]       = "controlbar:constructamericacommandcenter",
                ["USA Dozer"]                = "controlbar:constructamericadozer",
                ["S/Missile Defender"]       = "controlbar:constructamericainfantrymissiledefender",
                ["Ranger"]                   = "controlbar:constructamericainfantryranger",
                ["Colonel Burton"]           = "controlbar:constructamericainfantrycolonelburton",
                ["Pathfinder"]               = "controlbar:constructamericainfantrypathfinder",
                ["Humvee"]                   = "controlbar:constructamericavehiclehumvee",
                ["Avenger"]                  = "controlbar:constructamericatankavenger",
                ["Tomahawk"]                 = "controlbar:constructamericavehicletomahawk",
                ["Medic"]                    = "controlbar:constructamericavehiclemedic",
                ["Sentry Drone"]             = "controlbar:constructamericavehiclesentrydrone",
                ["Microwave Tank"]           = "controlbar:constructamericatankmicrowave",
                ["Comanche"]                 = "controlbar:constructamericavehiclecomanche",
                ["Stealth Fighter"]          = "controlbar:constructamericajetstealthfighter",
                ["Aurora"]                   = "controlbar:constructamericajetaurora",
                ["Raptor"]                   = "controlbar:constructamericajetraptor",
                ["Chinook"]                  = "controlbar:constructamericavehiclechinook",
                ["Fuel Air Aurora"]          = "controlbar:constructamericajetfuelairaurora",
            },
            ["GLA General"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Supply Stash"]             = "controlbar:constructglasupplystash",
                ["Barracks"]                 = "controlbar:constructglabarracks",
                ["Stinger Site"]             = "controlbar:constructglastingersite",
                ["Tunnel Network"]           = "controlbar:constructglatunnelnetwork",
                ["Arms Dealer"]              = "controlbar:constructglaarmsdealer",
                ["Demo Trap"]                = "controlbar:constructglademotrap",
                ["Palace"]                   = "controlbar:constructglapalace",
                ["Black Market"]             = "controlbar:constructglablackmarket",
                ["Scud Storm"]               = "controlbar:constructglascudstorm",
                ["GLA Command Center"]       = "controlbar:constructglacommandcenter",
                ["Fake Arms Dealer"]         = "controlbar:constructfakeglaarmsdealer",
                ["Fake Barracks"]            = "controlbar:constructfakeglabarracks",
                ["Fake Black Market"]        = "controlbar:constructfakeglablackmarket",
                ["Fake Command Center"]      = "controlbar:constructfakeglacommandcenter",
                ["Fake Supply Stash"]        = "controlbar:constructfakeglasupplystash",
                ["Worker"]                   = "controlbar:constructglaworker",
                ["Rebel"]                    = "controlbar:constructglainfantryrebel",
                ["Terrorist"]                = "controlbar:constructglainfantryterrorist",
                ["Jarmen Kell"]              = "controlbar:constructglainfantryjarmenkell",
                ["Radar Van"]                = "controlbar:constructglavehicleradarvan",
                ["Toxin Truck"]              = "controlbar:constructglavehicletoxintruck",
                ["Combat Bike"]              = "controlbar:constructglavehiclecombatbike",
                ["Battle Bus"]               = "controlbar:constructglavehiclebattlebus",
                ["Technical"]                = "controlbar:constructglavehicletechnical",
                ["Scud Launcher"]            = "controlbar:constructglavehiclescudlauncher",
                ["Bomb Truck"]               = "controlbar:constructglavehiclebombtruck",
            },
            ["GLA Toxin (Thrax)"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Supply Stash"]             = "controlbar:constructglasupplystash",
                ["Barracks"]                 = "controlbar:constructglabarracks",
                ["Stinger Site"]             = "controlbar:constructglastingersite",
                ["Tunnel Network"]           = "controlbar:chem_constructglatunnelnetwork",
                ["Arms Dealer"]              = "controlbar:constructglaarmsdealer",
                ["Demo Trap"]                = "controlbar:constructglademotrap",
                ["Palace"]                   = "controlbar:constructglapalace",
                ["Black Market"]             = "controlbar:constructglablackmarket",
                ["Scud Storm"]               = "controlbar:constructglascudstorm",
                ["GLA Command Center"]       = "controlbar:constructglacommandcenter",
                ["Fake Arms Dealer"]         = "controlbar:constructfakeglaarmsdealer",
                ["Fake Barracks"]            = "controlbar:constructfakeglabarracks",
                ["Fake Black Market"]        = "controlbar:constructfakeglablackmarket",
                ["Fake Command Center"]      = "controlbar:constructfakeglacommandcenter",
                ["Fake Supply Stash"]        = "controlbar:constructfakeglasupplystash",
                ["Worker (Toxin)"]           = "controlbar:constructglaworker",
                ["Rebel"]                    = "controlbar:constructglainfantryrebel",
                ["Terrorist"]                = "controlbar:constructglainfantryterrorist",
                ["Jarmen Kell"]              = "controlbar:constructglainfantryjarmenkell",
                ["Radar Van"]                = "controlbar:constructglavehicleradarvan",
                ["Toxin Truck"]              = "controlbar:constructglavehicletoxintruck",
                ["Combat Bike"]              = "controlbar:constructglavehiclecombatbike",
                ["Battle Bus"]               = "controlbar:constructglavehiclebattlebus",
                ["Technical"]                = "controlbar:constructglavehicletechnical",
                ["Scud Launcher"]            = "controlbar:constructglavehiclescudlauncher",
                ["Bomb Truck"]               = "controlbar:constructglavehiclebombtruck",
            },
            ["GLA Stealth (Kassad)"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Supply Stash"]             = "controlbar:constructglasupplystash",
                ["Barracks"]                 = "controlbar:constructglabarracks",
                ["Stinger Site"]             = "controlbar:constructglastingersite",
                ["Tunnel Network"]           = "controlbar:constructglatunnelnetwork",
                ["Arms Dealer"]              = "controlbar:constructglaarmsdealer",
                ["Demo Trap"]                = "controlbar:constructglademotrap",
                ["Palace"]                   = "controlbar:constructglapalace",
                ["Black Market"]             = "controlbar:constructglablackmarket",
                ["Scud Storm"]               = "controlbar:constructglascudstorm",
                ["GLA Command Center"]       = "controlbar:constructglacommandcenter",
                ["Fake Arms Dealer"]         = "controlbar:constructfakeglaarmsdealer",
                ["Fake Barracks"]            = "controlbar:constructfakeglabarracks",
                ["Fake Black Market"]        = "controlbar:constructfakeglablackmarket",
                ["Fake Command Center"]      = "controlbar:constructfakeglacommandcenter",
                ["Fake Supply Stash"]        = "controlbar:constructfakeglasupplystash",
                ["Worker"]                   = "controlbar:constructglaworker",
                ["Rebel"]                    = "controlbar:constructglainfantryrebel",
                ["Terrorist"]                = "controlbar:constructglainfantryterrorist",
                ["Jarmen Kell"]              = "controlbar:constructglainfantryjarmenkell",
                ["Radar Van"]                = "controlbar:constructglavehicleradarvan",
                ["Toxin Truck"]              = "controlbar:constructglavehicletoxintruck",
                ["Combat Bike"]              = "controlbar:constructglavehiclecombatbike",
                ["Battle Bus"]               = "controlbar:constructglavehiclebattlebus",
                ["Technical"]                = "controlbar:constructglavehicletechnical",
                ["Scud Launcher"]            = "controlbar:constructglavehiclescudlauncher",
                ["Bomb Truck"]               = "controlbar:constructglavehiclebombtruck",
            },
            ["GLA Demo (Juhziz)"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Supply Stash"]             = "controlbar:constructglasupplystash",
                ["Barracks"]                 = "controlbar:constructglabarracks",
                ["Stinger Site"]             = "controlbar:constructglastingersite",
                ["Tunnel Network"]           = "controlbar:constructglatunnelnetwork",
                ["Arms Dealer"]              = "controlbar:constructglaarmsdealer",
                ["Demo Trap"]                = "controlbar:demo_constructglademotrap",
                ["Palace"]                   = "controlbar:constructglapalace",
                ["Black Market"]             = "controlbar:constructglablackmarket",
                ["Scud Storm"]               = "controlbar:constructglascudstorm",
                ["GLA Command Center"]       = "controlbar:constructglacommandcenter",
                ["Fake Arms Dealer"]         = "controlbar:constructfakeglaarmsdealer",
                ["Fake Barracks"]            = "controlbar:constructfakeglabarracks",
                ["Fake Black Market"]        = "controlbar:constructfakeglablackmarket",
                ["Fake Command Center"]      = "controlbar:constructfakeglacommandcenter",
                ["Fake Supply Stash"]        = "controlbar:constructfakeglasupplystash",
                ["Worker (Demo)"]            = "controlbar:constructglaworker",
                ["Rebel"]                    = "controlbar:constructglainfantryrebel",
                ["Terrorist"]                = "controlbar:constructglainfantryterrorist",
                ["Jarmen Kell"]              = "controlbar:constructglainfantryjarmenkell",
                ["Radar Van"]                = "controlbar:constructglavehicleradarvan",
                ["Toxin Truck"]              = "controlbar:constructglavehicletoxintruck",
                ["Combat Bike"]              = "controlbar:constructglavehiclecombatbike",
                ["Battle Bus"]               = "controlbar:constructglavehiclebattlebus",
                ["Technical"]                = "controlbar:constructglavehicletechnical",
                ["Scud Launcher"]            = "controlbar:constructglavehiclescudlauncher",
                ["Bomb Truck"]               = "controlbar:constructglavehiclebombtruck",
            },
        };

        private static readonly Dictionary<string, string> _ccLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["sell"] = "Sell",
            ["setrallypoint"] = "Rally Point",
            ["stop"] = "Stop",
            ["evacuate"] = "Evacuate",
            ["structureexit"] = "Exit",
            ["capturebuilding"] = "Capture Bldg",
            ["attackmove"] = "Attack Move",
            ["guard"] = "Guard",
            ["disarmminesatposition"] = "Disarm Mines",
            ["overcharge"] = "Overcharge",
            ["neutronmissile"] = "Nuclear Missile",
            ["fireparticleuplinkcannon"] = "Fire Cannon",
            ["emppulse"] = "EMP Pulse",
            ["frenzy"] = "Frenzy",
            ["cashhack"] = "Cash Hack",
            ["emergencyrepair"] = "Emergency Repair",
            ["paradrop"] = "Paradrop",
            ["daisycutter"] = "Daisy Cutter",
            ["spydrone"] = "Spy Drone",
            ["spysatellite"] = "Spy Satellite",
            ["ciaintelligence"] = "CIA Intelligence",
            ["a10thunderboltmissilestrike"] = "A-10 Strike",
            ["leafletdrop"] = "Leaflet Drop",
            ["spectregunshipfromshortcut"] = "Spectre Gunship",
            ["lasermissileattack"] = "Laser Missiles",
            ["detonatefakebuilding"] = "Detonate",
            ["glascudstormlaunched"] = "Launch Scud",
            ["rebelambush"] = "Rebel Ambush",
            ["anthraxbomb"] = "Anthrax Bomb",
            ["gpscrambler"] = "GPS Scrambler",
            ["radarvanscansweep"] = "Radar Scan",
            ["upgradechinamines"] = "Land Mines",
            ["upgradechinaradar"] = "Radar Upgrade",
            ["chinacarpetbomb"] = "Carpet Bomb",
            ["chinaclustermines"] = "Cluster Mines",
            ["chinaartillerybarrage"] = "Artillery Barrage",
            ["upgradechinachainguns"] = "Chain Guns",
            ["upgradechinablacknapalm"] = "Black Napalm",
            ["upgradechinaaircraftarmor"] = "Aircraft Armor",
            ["upgradechinanationalism"] = "Nationalism",
            ["upgradechinasubliminalmessaging"] = "Subliminal Msg",
            ["upgradechinaisotopestability"] = "Isotope Stability",
            ["upgradechinauraniumshells"] = "Uranium Shells",
            ["upgradechinanucleartanks"] = "Nuclear Tanks",
            ["upgradechinaneutronshells"] = "Neutron Shells",
            ["upgradechinatacticalnukemig"] = "Tactical Nuke MiG",
            ["upgradechinasatellitehackone"] = "Satellite Hack",
            ["upgradechinafanaticism"] = "Fanaticism",
            ["upgradeamericaadvancedcontrolrods"] = "Control Rods",
            ["supw_upgradeamericaadvancedcontrolrods"] = "Cold Fusion Reactor",
            ["upgradeamericaflashbanggrenade"] = "Flashbangs",
            ["upgradeamericarangercapturebuilding"] = "Capture Training",
            ["upgradeamericamoab"] = "MOAB",
            ["upgradeamericaadvancedtraining"] = "Adv. Training",
            ["upgradeamericasupplylines"] = "Supply Lines",
            ["upgradeamericachemicalsuits"] = "Chemical Suits",
            ["upgradeamericacompositearmor"] = "Composite Armor",
            ["upgradeamericadronearmor"] = "Drone Armor",
            ["upgradeamericasentrydronegun"] = "Sentry Drone Gun",
            ["upgradeamericatowmissile"] = "TOW Missile",
            ["upgradecomancherocketpods"] = "Rocket Pods",
            ["upgradeamericacountermeasures"] = "Countermeasures",
            ["upgradeamericalasermissiles"] = "Laser Missiles",
            ["upgradeamericabunkerbusters"] = "Bunker Busters",
            ["initiatebattleplanbombardment"] = "Plan: Bombardment",
            ["initiatebattleplanholdtheline"] = "Plan: Hold Line",
            ["initiatebattleplansearchanddestroy"] = "Plan: S&D",
            ["upgradeglarebelcapturebuilding"] = "Capture Training",
            ["upgradeglaboobytrap"] = "Booby Trap",
            ["upgradeglascorpionrocket"] = "Scorpion Rocket",
            ["upgradeglacamonetting"] = "Camo Netting",
            ["upgradeglaworkerfakecommandset"] = "Fake Build Mode",
            ["becomerealglaarmsdealer"] = "Become Real",
            ["becomerealglabarracks"] = "Become Real",
            ["becomerealglablackmarket"] = "Become Real",
            ["becomerealglasupplystash"] = "Become Real",
            // ── Unit abilities ──────────────────────────────────────────────────────────
            ["transportexit"]                      = "Exit Transport",
            ["combatdrop"]                         = "Combat Drop",
            ["guardflyingunitsonly"]               = "Guard Aircraft",
            ["rangermachinegun"]                   = "Machine Gun",
            ["flashbanggrenademode"]               = "Flashbang Mode",
            ["knifeattack"]                        = "Knife Attack",
            ["timeddemocharge"]                    = "Timed Charge",
            ["remotedemocharge"]                   = "Remote Charge",
            ["detonatecharges"]                    = "Detonate",
            ["firerocketpods"]                     = "Rocket Pods",
            ["ambulancecleanuparea"]               = "Cleanup Area",
            ["constructamericavehiclebattledrone"] = "Battle Drone",
            ["constructamericavehiclehellfiredrone"]= "Hellfire Drone",
            ["constructamericavehiclescoutdrone"]  = "Scout Drone",
            // China unit abilities
            ["tntattack"]                          = "TNT Attack",
            ["disablebuildinghack"]                = "Disable Bldg",
            ["internethack"]                       = "Internet Hack",
            ["disablevehiclehack"]                 = "Disable Vehicle",
            ["stealcashhack"]                      = "Steal Cash",
            ["firewall"]                           = "Firewall",
            ["ecmdisablevehicle"]                  = "ECM Disable",
            ["dropnapalmbomb"]                     = "Drop Napalm",
            ["upgradehelixnapalmbomb"]             = "Napalm Bomb",
            ["upgradechinahelixbattlebunker"]      = "Battle Bunker",
            ["upgradechinahelixpropagandatower"]   = "Propaganda",
            ["upgradechinahelixgattlingcannon"]    = "Gattling Cannon",
            ["nukewarhead"]                        = "Nuke Warhead",
            ["neutronwarhead"]                     = "Neutron Warhead",
            ["upgradechinaoverlordbattlebunker"]   = "Battle Bunker",
            ["upgradechinaoverlordgattlingcannon"] = "Gattling Cannon",
            ["upgradechinaoverlordpropagandatower"]= "Propaganda",
            // GLA unit abilities
            ["carbomb"]                            = "Car Bomb",
            ["sniperattack"]                       = "Sniper Attack",
            ["radarvanscan"]                       = "Radar Scan",
            ["contaminate"]                        = "Contaminate",
            ["disguiseasvehicle"]                  = "Disguise",
            ["detonatebombtruck"]                  = "Detonate",
            ["upgradeglabombtruckbiobomb"]         = "Bio Bomb",
            ["upgradeglabombtruckhighexplosivebomb"]= "HE Bomb",
            ["explosivewarhead"]                   = "Explosive WH",
            ["anthraxwarhead"]                     = "Anthrax WH",
        };

        private bool _ccTabInitialized;
        private static Dictionary<string, string>? _csfLabels;
        private string _currentEditCsfId = "";
        private string _currentEditArmy    = "";
        private string _currentEditCsfKey  = "";
        private bool _suppressSlotEditor;
        private bool _commandCardBusy;
        private string _slotTextAtFocus = "";
        private readonly Stack<string> _slotLabelUndo = new();

        private static readonly string[] _generalPrefixes =
            { "lazr_", "airf_", "supw_", "nuke_", "infa_" };

        private const double UnitListIconSize = 44;
        private const double CommandCardSlotSize = 52;
        private const string CcArmyAll = "All";

        private enum CcNormalScope { All, Buildings, Units }

        private static readonly HashSet<string> _ccAllBuildingNames = BuildCcNameSet(d => d.Buildings);
        private static readonly HashSet<string> _ccAllUnitNames = BuildCcNameSet(d => d.Units);

        private static HashSet<string> BuildCcNameSet(Func<(string[] Buildings, string[] Units), string[]> pick)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var data in _ccArmyData.Values)
                foreach (string name in pick(data))
                    set.Add(name);
            return set;
        }

        private static bool MatchesCcNormalScope(string mapKey, CcNormalScope scope)
            => scope switch
            {
                CcNormalScope.Buildings => _ccAllBuildingNames.Contains(mapKey),
                CcNormalScope.Units => _ccAllUnitNames.Contains(mapKey),
                _ => true,
            };

        private readonly List<TextBox> _layoutKeyInputs = new();

        private sealed record LayoutSlotTag(int SlotNumber, bool IsStar);

        private bool IsGlobalLayoutMode()
            => string.Equals(cmbCommandCardArmy?.SelectedItem as string, CcArmyAll, StringComparison.Ordinal);

        private void InitCommandCardTab()
        {
            if (_ccTabInitialized) return;
            _ccTabInitialized = true;

            // Merge all .big in the game folder (alphabetical load order; ! mods win).
            if (_csfLabels == null)
            {
                string? gameDir = GameBigStack.DiscoverGameDirectory();
                _csfLabels = gameDir != null
                    ? GameBigStack.BuildMergedCsfLabels(gameDir)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                ButtonImageReader.Load(gameDir);
            }

            cmbCommandCardArmy.Items.Clear();
            cmbCommandCardArmy.Items.Add(CcArmyAll);
            foreach (var army in _ccArmyData.Keys)
                cmbCommandCardArmy.Items.Add(army);
            if (cmbCommandCardArmy.Items.Count > 0)
                cmbCommandCardArmy.SelectedIndex = 0;
        }

        /// <summary>Vanilla English CSF BIG used as rebuild base when saving !EnglishZH.big.</summary>
        private static string FindEnglishZhBig()
        {
            string? gameDir = GameBigStack.DiscoverGameDirectory();
            if (gameDir != null)
            {
                string? vanilla = GameBigStack.FindVanillaEnglishBig(gameDir);
                if (vanilla != null) return vanilla;
            }

            return Path.Combine(GameDirectory.Get(), "EnglishZH.big");
        }

        private void CmbCommandCardArmy_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool global = IsGlobalLayoutMode();
            if (PanelCcGlobalLayout != null)
                PanelCcGlobalLayout.Visibility = global ? Visibility.Visible : Visibility.Collapsed;
            if (PanelCcUnitEditor != null)
                PanelCcUnitEditor.Visibility = global ? Visibility.Collapsed : Visibility.Visible;

            bool showUnitFilters = !global;
            if (rbCcAll != null) rbCcAll.IsEnabled = showUnitFilters;
            if (rbCcBuildings != null) rbCcBuildings.IsEnabled = showUnitFilters;
            if (rbCcUnits != null) rbCcUnits.IsEnabled = showUnitFilters;

            if (global)
                BuildGlobalLayoutCards();
            else
                PopulateCommandCardUnits();
        }

        private void CcFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (lbCommandCardUnits == null) return;
            PopulateCommandCardUnits();
        }

        private void PopulateCommandCardUnits()
        {
            if (lbCommandCardUnits == null || cmbCommandCardArmy == null) return;
            lbCommandCardUnits.Items.Clear();
            if (IsGlobalLayoutMode()) return;
            if (cmbCommandCardArmy.SelectedItem is not string army) return;
            if (!_ccArmyData.TryGetValue(army, out var data)) return;

            bool showBuildings = rbCcAll?.IsChecked == true || rbCcBuildings?.IsChecked == true;
            bool showUnits     = rbCcAll?.IsChecked == true || rbCcUnits?.IsChecked == true;

            var names = new List<string>();
            // "General Powers" always first for armies that have star power slots
            if (STAR_SLOT_MAP.ContainsKey(army))
                names.Add("General Powers");
            if (showBuildings) names.AddRange(data.Buildings);
            if (showUnits)     names.AddRange(data.Units);

            _unitListImageIds.TryGetValue(army, out var armyImageMap);

            foreach (var name in names)
            {
                // 1. Prefer explicit construction-button override (UNIT_LIST_IMAGE port).
                // 2. Fall back to first slot in the unit's command card.
                System.Windows.Media.Imaging.BitmapSource? icon = null;
                string? iconCsfId = null;
                if (armyImageMap != null && armyImageMap.TryGetValue(name, out var overrideCsf))
                    iconCsfId = overrideCsf;
                else if (_ccSlotMap.TryGetValue(name, out var slots))
                {
                    var first = slots.OrderBy(s => s.Row * 10 + s.Col).FirstOrDefault();
                    iconCsfId = first?.ImageLookupId;
                }
                else if (name == "General Powers" && STAR_SLOT_MAP.TryGetValue(army, out var starMap) && starMap.Count > 0)
                    iconCsfId = starMap.OrderBy(kv => kv.Key).First().Value.ImageCsfId;

                if (iconCsfId != null)
                    icon = ButtonImageReader.GetSlotImage(iconCsfId, army);

                // ── Build row: Grid col0=icon, col1=text ─────────────────────
                var row = new Grid
                {
                    Tag = name,
                    Margin = new Thickness(0, 2, 0, 2),
                    MinHeight = UnitListIconSize + 6,
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                UIElement iconElem;
                if (icon != null)
                {
                    var img = new System.Windows.Controls.Image
                    {
                        Source = icon,
                        Width  = UnitListIconSize,
                        Height = UnitListIconSize,
                        Stretch = System.Windows.Media.Stretch.Uniform,
                        Margin = new Thickness(4, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        SnapsToDevicePixels = true,
                    };
                    RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
                    iconElem = img;
                }
                else
                {
                    iconElem = new Border
                    {
                        Width  = UnitListIconSize,
                        Height = UnitListIconSize,
                        Margin = new Thickness(4, 0, 8, 0),
                        Background = new SolidColorBrush(Color.FromRgb(0x08, 0x10, 0x20)),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                }
                Grid.SetColumn(iconElem, 0);
                row.Children.Add(iconElem);

                var tb = new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetColumn(tb, 1);
                row.Children.Add(tb);

                lbCommandCardUnits.Items.Add(row);
            }
        }

        private void LbCommandCardUnits_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;   // stop outer ScrollViewer from scrolling the whole page
            var sv = FindDescendant<ScrollViewer>(lbCommandCardUnits);
            sv?.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 120.0 * SystemParameters.WheelScrollLines);
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var found = FindDescendant<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void LbCommandCardUnits_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string? name = null;
            if (lbCommandCardUnits.SelectedItem is Grid g && g.Tag is string gtag) name = gtag;
            else if (lbCommandCardUnits.SelectedItem is StackPanel sp && sp.Tag is string stag) name = stag;
            if (name == null) return;
            BuildCommandCardGrid(name);
        }

        private void BuildCommandCardGrid(string name)
        {
            lblSelectedUnit.Text = name;
            SlotInfoPanel.Visibility = Visibility.Collapsed;

            string army = cmbCommandCardArmy.SelectedItem as string ?? "";

            // Star powers panel — uses per-slot separate label/image CSF ids
            if (name == "General Powers" && STAR_SLOT_MAP.TryGetValue(army, out var starSlots))
            {
                BuildStarPowerGrid(starSlots, army);
                return;
            }

            string lookupName = name;
            if (name == "USA Dozer" && army == "USA Laser (Townes)") lookupName = "USA Dozer (Laser)";
            else if (name == "USA Dozer" && army == "USA Superweapon (Alexander)") lookupName = "USA Dozer (SW)";
            else if (name == "Dozer" && army == "China Nuke (Tao)") lookupName = "Dozer (Nuke)";
            else if (name == "Dozer" && army == "China Infantry (Shin Fai)") lookupName = "Dozer (Infantry)";
            else if (name == "Worker" && army == "GLA Toxin (Thrax)") lookupName = "Worker (Toxin)";
            else if (name == "Worker" && army == "GLA Demo (Juhziz)") lookupName = "Worker (Demo)";

            if (!_ccSlotMap.TryGetValue(lookupName, out var slots) || slots.Count == 0)
            {
                CommandCardGridHost.Content = new TextBlock
                {
                    Text = "(No command card data available for this unit)",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x88)),
                    FontSize = 11,
                };
                return;
            }

            int maxRow = slots.Max(s => s.Row) + 1;
            maxRow = Math.Max(maxRow, 2);
            const int COLS = 7;
            double W = CommandCardSlotSize, H = CommandCardSlotSize;

            var lookup = slots.ToDictionary(s => (s.Row, s.Col));
            var grid = new System.Windows.Controls.Grid();

            for (int c = 0; c < COLS; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(W) });
            for (int r = 0; r < maxRow; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(H) });

            for (int r = 0; r < maxRow; r++)
            {
                for (int c = 0; c < COLS; c++)
                {
                    bool has = lookup.TryGetValue((r, c), out var slot);

                    // Try to load the button icon image
                    var bmp = has ? ButtonImageReader.GetSlotImage(slot!.ImageLookupId, army) : null;
                    string label = has ? FormatCcSlotId(slot!.CsfId) : "";

                    var btn = new System.Windows.Controls.Button
                    {
                        Width  = W - 4,
                        Height = H - 4,
                        Padding = new Thickness(0),
                        Margin  = new Thickness(2),
                        BorderThickness = new Thickness(2),
                        Background = new SolidColorBrush(has
                            ? Color.FromRgb(0x08, 0x14, 0x28)
                            : Color.FromRgb(0x04, 0x07, 0x14)),
                        BorderBrush = new SolidColorBrush(has
                            ? Color.FromRgb(0x00, 0x88, 0xA8)
                            : Color.FromRgb(0x0C, 0x14, 0x24)),
                        Cursor = has ? System.Windows.Input.Cursors.Hand : null,
                        ToolTip = has ? slot!.CsfId : null,
                        IsEnabled = has,
                        Tag = has ? slot!.CsfId : null,
                    };

                    // Style override — remove default button chrome
                    var ct = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
                    var factory = new FrameworkElementFactory(typeof(Border));
                    factory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
                        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                    factory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
                        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                    factory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
                        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                    factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
                    var content = new FrameworkElementFactory(typeof(ContentPresenter));
                    content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
                    content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
                    factory.AppendChild(content);
                    ct.VisualTree = factory;
                    btn.Template = ct;

                    if (has)
                    {
                        string labelId = slot!.CsfId;
                        string imageId = slot.ImageLookupId;
                        btn.Click += (_, _) => OnSlotClicked(labelId, imageId, army);

                        if (bmp != null)
                        {
                            var img = new System.Windows.Controls.Image
                            {
                                Source = bmp,
                                Stretch = System.Windows.Media.Stretch.Uniform,
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                                VerticalAlignment   = VerticalAlignment.Stretch,
                            };
                            RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
                            btn.Content = img;
                        }
                        else
                        {
                            var fallbackTb = new TextBlock
                            {
                                FontSize = 9,
                                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                                FontWeight = FontWeights.SemiBold,
                                TextWrapping = TextWrapping.Wrap,
                                TextAlignment = TextAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment   = VerticalAlignment.Center,
                                Margin = new Thickness(3),
                            };
                            ApplyHotkeyFormatting(fallbackTb, label);
                            btn.Content = fallbackTb;
                        }
                    }

                    Grid.SetRow(btn, r);
                    Grid.SetColumn(btn, c);
                    grid.Children.Add(btn);
                }
            }

            CommandCardGridHost.Content = grid;
        }

        private void BuildStarPowerGrid(Dictionary<int, StarSlotRef> starSlots, string army)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };
            
            var sections = new[]
            {
                new { Label = "⭐ 1 Star Required",         Color = Color.FromRgb(200, 176, 64), StartSlot = 1,  EndSlot = 4,  Rows = 1, Cols = 4 },
                new { Label = "⭐⭐⭐ 3 Stars Required",      Color = Color.FromRgb(208, 144, 48), StartSlot = 5,  EndSlot = 19, Rows = 3, Cols = 5 },
                new { Label = "⭐⭐⭐⭐⭐ 5 Stars Required", Color = Color.FromRgb(224, 104, 32), StartSlot = 20, EndSlot = 24, Rows = 1, Cols = 4 }
            };

            foreach (var sec in sections)
            {
                var header = new TextBlock
                {
                    Text = sec.Label,
                    Foreground = new SolidColorBrush(sec.Color),
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Margin = new Thickness(0, 10, 0, 4)
                };
                panel.Children.Add(header);

                var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 10) };
                for (int c = 0; c < sec.Cols; c++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CommandCardSlotSize) });
                for (int r = 0; r < sec.Rows; r++)
                    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CommandCardSlotSize) });

                for (int slotNum = sec.StartSlot; slotNum <= sec.EndSlot; slotNum++)
                {
                    int idx = slotNum - sec.StartSlot;
                    int r = idx / sec.Cols;
                    int c = idx % sec.Cols;

                    if (r >= sec.Rows) continue;

                    bool has = starSlots.TryGetValue(slotNum, out var starSlot);

                    var bmp   = has ? ButtonImageReader.GetSlotImage(starSlot!.ImageCsfId, army) : null;
                    string label = has ? FormatCcSlotId(starSlot!.LabelCsfId) : "";

                    var btn = new System.Windows.Controls.Button
                    {
                        Width  = CommandCardSlotSize - 4,
                        Height = CommandCardSlotSize - 4,
                        Padding = new Thickness(0),
                        Margin  = new Thickness(2),
                        BorderThickness = new Thickness(2),
                        Background = new SolidColorBrush(has
                            ? Color.FromRgb(0x08, 0x14, 0x28)
                            : Color.FromRgb(0x04, 0x07, 0x14)),
                        BorderBrush = new SolidColorBrush(has
                            ? Color.FromRgb(0x00, 0x88, 0xA8)
                            : Color.FromRgb(0x0C, 0x14, 0x24)),
                        Cursor    = has ? System.Windows.Input.Cursors.Hand : null,
                        ToolTip   = has ? $"{starSlot!.LabelCsfId} | img:{starSlot.ImageCsfId}" : null,
                        IsEnabled = has,
                    };

                    var ct = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
                    var factory = new FrameworkElementFactory(typeof(Border));
                    factory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
                        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                    factory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
                        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                    factory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
                        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                    factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
                    var cp = new FrameworkElementFactory(typeof(ContentPresenter));
                    cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
                    cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
                    factory.AppendChild(cp);
                    ct.VisualTree = factory;
                    btn.Template = ct;

                    if (has)
                    {
                        var labelId = starSlot!.LabelCsfId;
                        var imageId = starSlot.ImageCsfId;
                        btn.Click += (_, _) => OnSlotClicked(labelId, imageId, army);

                        if (bmp != null)
                        {
                            var img = new System.Windows.Controls.Image
                            {
                                Source = bmp,
                                Stretch = System.Windows.Media.Stretch.Uniform,
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                                VerticalAlignment   = VerticalAlignment.Stretch,
                            };
                            RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
                            btn.Content = img;
                        }
                        else
                        {
                            var fallbackTb = new TextBlock
                            {
                                FontSize = 9,
                                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                                FontWeight = FontWeights.SemiBold,
                                TextWrapping = TextWrapping.Wrap,
                                TextAlignment = TextAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment   = VerticalAlignment.Center,
                                Margin = new Thickness(3),
                            };
                            ApplyHotkeyFormatting(fallbackTb, label);
                            btn.Content = fallbackTb;
                        }
                    }

                    Grid.SetRow(btn, r);
                    Grid.SetColumn(btn, c);
                    grid.Children.Add(btn);
                }

                panel.Children.Add(grid);
            }

            CommandCardGridHost.Content = panel;
        }

        private void OnSlotClicked(string csfId, string army)
            => OnSlotClicked(csfId, csfId, army);

        /// <param name="labelCsfId">CSF key for text editing.</param>
        /// <param name="imageCsfId">CSF id for TGA icon lookup (may differ from labelCsfId for star power slots).</param>
        private void OnSlotClicked(string labelCsfId, string imageCsfId, string army)
        {
            _currentEditCsfId  = imageCsfId;
            _currentEditArmy   = army;
            _currentEditCsfKey = ResolveCsfLabelKey(labelCsfId);

            SlotInfoPanel.Visibility = Visibility.Visible;
            lblSlotId.Text = labelCsfId;

            var bmp = ButtonImageReader.GetSlotImage(imageCsfId, army);
            imgSlotPreview.Source = bmp;

            string raw = GetCsfLabelRawText(labelCsfId);
            _slotLabelUndo.Clear();
            SetSlotLabelText(raw, recordUndo: false);
            if (btnSlotUndo != null) btnSlotUndo.IsEnabled = false;
        }

        private string ResolveCsfLabelKey(string labelCsfId)
        {
            if (_csfLabels is { Count: > 0 })
            {
                if (_csfLabels.ContainsKey(labelCsfId))
                    return labelCsfId;

                int ci = labelCsfId.IndexOf(':');
                string bare = ci >= 0 ? labelCsfId[(ci + 1)..] : labelCsfId;
                if (_csfLabels.ContainsKey(bare))
                    return bare;

                foreach (var pfx in _generalPrefixes)
                {
                    if (!bare.StartsWith(pfx, StringComparison.OrdinalIgnoreCase)) continue;
                    string stripped = bare[pfx.Length..];
                    if (_csfLabels.ContainsKey(stripped))
                        return stripped;
                    string strippedFull = ci >= 0 ? labelCsfId[..(ci + 1)] + stripped : stripped;
                    if (_csfLabels.ContainsKey(strippedFull))
                        return strippedFull;
                }
            }
            return labelCsfId;
        }

        private string GetCsfLabelRawText(string labelCsfId)
        {
            string key = ResolveCsfLabelKey(labelCsfId);
            if (_csfLabels != null && _csfLabels.TryGetValue(key, out var text) && !string.IsNullOrEmpty(text))
                return CsfFirstLine(text);
            return FormatCcSlotId(labelCsfId);
        }

        private int SetCsfLabelAndShortcutAliases(string labelCsfId, string labelText)
        {
            _csfLabels ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string labelKey = ResolveCsfLabelKey(labelCsfId);
            _csfLabels[labelKey] = labelText;
            int updated = 1;

            char hotkey = HotkeyPainter.ExtractHotkeyChar(labelText);
            if (hotkey == '\0') return updated;

            if (!_shortcutLabelAliasesByBare.TryGetValue(BareCsfId(labelCsfId), out var aliases)
             && !_shortcutLabelAliasesByBare.TryGetValue(BareCsfId(labelKey), out aliases))
                return updated;

            foreach (string aliasId in aliases)
            {
                string aliasKey = ResolveCsfLabelKey(aliasId);
                if (string.Equals(aliasKey, labelKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                string aliasRaw = GetCsfLabelRawText(aliasId);
                _csfLabels[aliasKey] = CommandCardHotkeyService.SetHotkeyCharInLabel(aliasRaw, hotkey);
                updated++;
            }

            return updated;
        }

        private static char ParseSingleKey(TextBox? tb)
        {
            string s = (tb?.Text ?? "").Trim();
            return s.Length > 0 ? char.ToUpperInvariant(s[0]) : '\0';
        }

        private void ShowCommandCardStatus(string message, bool isError = false)
        {
            if (lblCommandCardStatus == null) return;
            lblCommandCardStatus.Text = message;
            lblCommandCardStatus.Foreground = new SolidColorBrush(
                isError ? Color.FromRgb(0xFF, 0x60, 0x60) : Color.FromRgb(0x55, 0xCC, 0x88));
            lblCommandCardStatus.Visibility = Visibility.Visible;
        }

        private void SetCommandCardBusy(bool busy, string message = "")
        {
            _commandCardBusy = busy;
            if (PanelCommandCardProgress != null)
                PanelCommandCardProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (lblCommandCardProgress != null && !string.IsNullOrWhiteSpace(message))
                lblCommandCardProgress.Text = message;

            if (btnApplyAllKeysFromLabels != null) btnApplyAllKeysFromLabels.IsEnabled = !busy;
            if (btnApplyNormalLabelsBuildings != null) btnApplyNormalLabelsBuildings.IsEnabled = !busy;
            if (btnApplyNormalImagesBuildings != null) btnApplyNormalImagesBuildings.IsEnabled = !busy;
            if (btnApplyNormalLabelsUnits != null) btnApplyNormalLabelsUnits.IsEnabled = !busy;
            if (btnApplyNormalImagesUnits != null) btnApplyNormalImagesUnits.IsEnabled = !busy;
            if (btnApplyStarLabels != null) btnApplyStarLabels.IsEnabled = !busy;
            if (btnApplyStarImages != null) btnApplyStarImages.IsEnabled = !busy;

            if (btnSlotApplyLabel != null) btnSlotApplyLabel.IsEnabled = !busy;
            if (btnSlotApplyImages != null) btnSlotApplyImages.IsEnabled = !busy;
            if (!busy)
                UpdateSlotApplyButtonsState();
        }

        private bool PersistCommandCardToBig(Dictionary<string, byte[]>? tgaPatches)
        {
            if (_csfLabels == null) return false;
            string bigPath = FindEnglishZhBig();
            if (!File.Exists(bigPath))
            {
                ShowCommandCardStatus("EnglishZH.big not found (EXE folder).", isError: true);
                return false;
            }

            string? outPath = BigCsfWriter.RebuildAll(bigPath, _csfLabels, tgaPatches);
            if (outPath == null)
            {
                ShowCommandCardStatus("Failed to save BIG file.", isError: true);
                return false;
            }

            ButtonImageReader.Reload(GameDirectory.Get());
            return true;
        }

        private static bool ConfirmCommandCardAction(string message)
            => MessageBox.Show(
                message,
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;

        private List<CommandCardHotkeyService.SlotBinding> CollectBindingsForArmy(
            string army,
            bool includeNormal = true,
            bool includeStar = true,
            CcNormalScope normalScope = CcNormalScope.All)
        {
            var list = new List<CommandCardHotkeyService.SlotBinding>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (includeNormal)
            {
                foreach (var (mapKey, slots) in _ccSlotMap)
                {
                    if (!MatchesCcNormalScope(mapKey, normalScope)) continue;
                    foreach (var slot in slots)
                    {
                        string dedupe = slot.ImageLookupId + "|" + army;
                        if (!seen.Add(dedupe)) continue;
                        string raw = GetCsfLabelRawText(slot.CsfId);
                        if (HotkeyPainter.ExtractHotkeyChar(raw) == '\0') continue;
                        list.Add(new CommandCardHotkeyService.SlotBinding(
                            slot.CsfId, slot.ImageLookupId, army, raw));
                    }
                }
            }

            if (includeStar && STAR_SLOT_MAP.TryGetValue(army, out var stars))
            {
                foreach (var star in stars.Values)
                {
                    string dedupe = star.ImageCsfId + "|" + army;
                    if (!seen.Add(dedupe)) continue;
                    string raw = GetCsfLabelRawText(star.LabelCsfId);
                    if (HotkeyPainter.ExtractHotkeyChar(raw) == '\0') continue;
                    list.Add(new CommandCardHotkeyService.SlotBinding(
                        star.LabelCsfId, star.ImageCsfId, army, raw));
                }
            }

            return list;
        }

        private void RefreshCommandCardViewAfterBulk()
        {
            string? unitName = null;
            if (lbCommandCardUnits?.SelectedItem is Grid g && g.Tag is string tag) unitName = tag;
            PopulateCommandCardUnits();
            if (unitName != null)
            {
                foreach (var item in lbCommandCardUnits!.Items)
                {
                    if (item is Grid ig && ig.Tag is string t && t == unitName)
                    {
                        lbCommandCardUnits.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void BuildGlobalLayoutCards()
        {
            _layoutKeyInputs.Clear();
            if (GlobalNormalCardHost == null || GlobalStarCardHost == null) return;

            const int cols = 7, rows = 2;
            double cell = 56;
            var normalGrid = new System.Windows.Controls.Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x06, 0x0A, 0x18)),
                Margin = new Thickness(0, 0, 0, 4),
            };
            for (int c = 0; c < cols; c++)
                normalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
            for (int r = 0; r < rows; r++)
                normalGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cell) });

            for (int slot = 1; slot <= 14; slot++)
            {
                CommandCardHotkeyService.SlotNumberToGrid(slot, out int row, out int col);
                var cellUi = CreateLayoutSlotCell(slot, isStar: false, GetRepresentativeGridSlotKey(slot));
                Grid.SetRow(cellUi, row);
                Grid.SetColumn(cellUi, col);
                normalGrid.Children.Add(cellUi);
            }
            GlobalNormalCardHost.Content = normalGrid;

            var starPanel = new StackPanel { Orientation = Orientation.Vertical };
            var sections = new[]
            {
                new { Label = "1 star required", Start = 1,  End = 4,  Rows = 1, Cols = 4 },
                new { Label = "3 stars required", Start = 5,  End = 19, Rows = 3, Cols = 5 },
                new { Label = "5 stars required", Start = 20, End = 24, Rows = 1, Cols = 4 },
            };
            foreach (var sec in sections)
            {
                starPanel.Children.Add(new TextBlock
                {
                    Text = sec.Label,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD6, 0xE5)),
                    FontSize = 11,
                    Margin = new Thickness(0, 6, 0, 4),
                });
                var sg = new System.Windows.Controls.Grid();
                for (int c = 0; c < sec.Cols; c++)
                    sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
                for (int r = 0; r < sec.Rows; r++)
                    sg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cell) });

                for (int n = sec.Start; n <= sec.End; n++)
                {
                    int idx = n - sec.Start;
                    int r = idx / sec.Cols, c = idx % sec.Cols;
                    if (r >= sec.Rows) continue;
                    bool anyArmy = STAR_SLOT_MAP.Values.Any(m => m.ContainsKey(n));
                    var cellUi = CreateLayoutSlotCell(n, isStar: true, GetRepresentativeStarSlotKey(n), dimmed: !anyArmy);
                    Grid.SetRow(cellUi, r);
                    Grid.SetColumn(cellUi, c);
                    sg.Children.Add(cellUi);
                }
                starPanel.Children.Add(sg);
            }
            GlobalStarCardHost.Content = starPanel;
        }

        private FrameworkElement CreateLayoutSlotCell(int slotNumber, bool isStar, char currentKey, bool dimmed = false)
        {
            var border = new Border
            {
                Margin = new Thickness(2),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x60)),
                Background = new SolidColorBrush(dimmed
                    ? Color.FromRgb(0x04, 0x06, 0x10)
                    : Color.FromRgb(0x0A, 0x12, 0x24)),
                CornerRadius = new CornerRadius(3),
            };
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = slotNumber.ToString(),
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x70, 0x90)),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            var tb = new TextBox
            {
                Width = 28,
                Height = 22,
                MaxLength = 1,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                CharacterCasing = CharacterCasing.Upper,
                Background = new SolidColorBrush(Color.FromRgb(0x12, 0x18, 0x30)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x50, 0x80)),
                Tag = new LayoutSlotTag(slotNumber, isStar),
                IsEnabled = !dimmed,
            };
            if (currentKey != '\0') tb.Text = char.ToUpperInvariant(currentKey).ToString();
            _layoutKeyInputs.Add(tb);
            sp.Children.Add(tb);
            border.Child = sp;
            return border;
        }

        private char GetRepresentativeGridSlotKey(int slotNumber)
        {
            CommandCardHotkeyService.SlotNumberToGrid(slotNumber, out int row, out int col);
            foreach (var slots in _ccSlotMap.Values)
            {
                var slot = slots.FirstOrDefault(s => s.Row == row && s.Col == col);
                if (slot == null) continue;
                char hk = HotkeyPainter.ExtractHotkeyChar(GetCsfLabelRawText(slot.CsfId));
                if (hk != '\0') return hk;
            }
            return '\0';
        }

        private char GetRepresentativeStarSlotKey(int starSlotNumber)
        {
            foreach (var stars in STAR_SLOT_MAP.Values)
            {
                if (!stars.TryGetValue(starSlotNumber, out var star)) continue;
                char hk = HotkeyPainter.ExtractHotkeyChar(GetCsfLabelRawText(star.LabelCsfId));
                if (hk != '\0') return hk;
            }
            return '\0';
        }

        private HashSet<string> GetConflictingNormalLabelKeys(CcNormalScope scope)
        {
            var byLabel = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (mapKey, slots) in _ccSlotMap)
            {
                if (!MatchesCcNormalScope(mapKey, scope)) continue;
                foreach (var slot in slots)
                {
                    string labelKey = ResolveCsfLabelKey(slot.CsfId);
                    int slotNumber = slot.Row * 7 + slot.Col + 1;
                    if (!byLabel.TryGetValue(labelKey, out var slotSet))
                    {
                        slotSet = new HashSet<int>();
                        byLabel[labelKey] = slotSet;
                    }
                    slotSet.Add(slotNumber);
                }
            }

            return byLabel
                .Where(kv => kv.Value.Count > 1)
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private int ApplyGridSlotHotkey(
            int slotNum,
            char key,
            CcNormalScope scope = CcNormalScope.All,
            ISet<string>? conflictingLabelKeys = null)
        {
            CommandCardHotkeyService.SlotNumberToGrid(slotNum, out int row, out int col);
            _csfLabels ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int updated = 0;
            var labelKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (mapKey, slots) in _ccSlotMap)
            {
                if (!MatchesCcNormalScope(mapKey, scope)) continue;
                foreach (var slot in slots.Where(s => s.Row == row && s.Col == col))
                {
                    string labelKey = ResolveCsfLabelKey(slot.CsfId);
                    if (conflictingLabelKeys?.Contains(labelKey) == true) continue;
                    if (!labelKeys.Add(labelKey)) continue;
                    string raw = GetCsfLabelRawText(slot.CsfId);
                    updated += SetCsfLabelAndShortcutAliases(
                        slot.CsfId,
                        CommandCardHotkeyService.ApplyHotkeyCharToLabel(raw, key));
                }
            }
            return updated;
        }

        private int ApplyStarSlotHotkey(int starNum, char key, bool allArmies)
        {
            _csfLabels ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int updated = 0;
            IEnumerable<string> armies = allArmies
                ? STAR_SLOT_MAP.Keys
                : cmbCommandCardArmy.SelectedItem is string single && single != CcArmyAll
                    ? new[] { single }
                    : Array.Empty<string>();

            foreach (var army in armies)
            {
                if (!STAR_SLOT_MAP.TryGetValue(army, out var stars) || !stars.TryGetValue(starNum, out var star))
                    continue;
                string raw = GetCsfLabelRawText(star.LabelCsfId);
                updated += SetCsfLabelAndShortcutAliases(
                    star.LabelCsfId,
                    CommandCardHotkeyService.ApplyHotkeyCharToLabel(raw, key));
            }
            return updated;
        }

        private bool EnsureGlobalLayoutMode()
        {
            if (IsGlobalLayoutMode()) return true;
            ShowCommandCardStatus("Select «All» in the Army dropdown for template cards.", isError: true);
            return false;
        }

        private int ApplyLayoutKeysFromInputs(
            bool normalOnly, bool starOnly, CcNormalScope normalScope = CcNormalScope.All)
        {
            int updated = 0;
            HashSet<string>? conflictingNormalLabels = !starOnly
                ? GetConflictingNormalLabelKeys(normalScope)
                : null;

            foreach (var tb in _layoutKeyInputs)
            {
                if (tb.Tag is not LayoutSlotTag tag) continue;
                if (normalOnly && tag.IsStar) continue;
                if (starOnly && !tag.IsStar) continue;
                char k = ParseSingleKey(tb);
                if (k == '\0') continue;
                if (tag.IsStar) updated += ApplyStarSlotHotkey(tag.SlotNumber, k, allArmies: true);
                else updated += ApplyGridSlotHotkey(
                    tag.SlotNumber, k, normalScope, conflictingNormalLabels);
            }
            return updated;
        }

        private static string NormalScopeLabel(CcNormalScope scope)
            => scope switch
            {
                CcNormalScope.Buildings => "buildings",
                CcNormalScope.Units => "units",
                _ => "all",
            };

        private void ApplyNormalLabels(CcNormalScope scope)
        {
            if (!EnsureGlobalLayoutMode()) return;
            string kind = NormalScopeLabel(scope);
            if (!ConfirmCommandCardAction(
                $"Apply hotkey labels to normal command card slots for {kind} only (all armies) at the matching grid positions?\n\nAre you sure?"))
                return;

            int n = ApplyLayoutKeysFromInputs(normalOnly: true, starOnly: false, normalScope: scope);
            if (n == 0)
            {
                ShowCommandCardStatus("Enter at least one key on the normal card.", isError: true);
                return;
            }
            ShowCommandCardStatus(
                $"Normal card ({kind}): {n} label(s) updated in memory. Use «Apply keys to images» to write BIG.");
        }

        private void BtnApplyNormalLabelsBuildings_Click(object sender, RoutedEventArgs e)
            => ApplyNormalLabels(CcNormalScope.Buildings);

        private void BtnApplyNormalLabelsUnits_Click(object sender, RoutedEventArgs e)
            => ApplyNormalLabels(CcNormalScope.Units);

        private void BtnApplyStarLabels_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureGlobalLayoutMode()) return;
            if (!ConfirmCommandCardAction(
                "Apply hotkey labels to every general's power slot (all armies) at the matching star positions?\n\nAre you sure?"))
                return;

            int n = ApplyLayoutKeysFromInputs(normalOnly: false, starOnly: true);
            if (n == 0)
            {
                ShowCommandCardStatus("Enter at least one key on the star card.", isError: true);
                return;
            }
            ShowCommandCardStatus($"Star powers: {n} label(s) updated in memory. Use «Apply keys to images» to write BIG.");
        }

        private List<CommandCardHotkeyService.SlotBinding> CollectBindingsAllArmies(
            bool includeNormal = true,
            bool includeStar = true,
            CcNormalScope normalScope = CcNormalScope.All)
        {
            var list = new List<CommandCardHotkeyService.SlotBinding>();
            foreach (var army in _ccArmyData.Keys)
                list.AddRange(CollectBindingsForArmy(army, includeNormal, includeStar, normalScope));
            return list;
        }

        private async Task<bool> ApplyHotkeyImagePatchesAsync(
            List<CommandCardHotkeyService.SlotBinding> bindings, string scopeDescription)
        {
            if (_commandCardBusy) return false;
            if (bindings.Count == 0)
            {
                ShowCommandCardStatus("No slots with & shortcuts found in CSF labels.", isError: true);
                return false;
            }

            if (_csfLabels == null)
            {
                ShowCommandCardStatus("CSF labels are not loaded.", isError: true);
                return false;
            }

            string bigPath = FindEnglishZhBig();
            if (!File.Exists(bigPath))
            {
                ShowCommandCardStatus("EnglishZH.big not found (EXE folder).", isError: true);
                return false;
            }

            var labelsSnapshot = new Dictionary<string, string>(_csfLabels, StringComparer.OrdinalIgnoreCase);
            SetCommandCardBusy(true, $"Working on {scopeDescription}... Building image patches and saving BIG.");

            try
            {
                var result = await Task.Run(() =>
                {
                    var patches = CommandCardHotkeyService.BuildTgaPatches(bindings);
                    if (patches.Count == 0)
                        return (Success: false, Error: "Could not resolve TGA atlas mappings.", PatchCount: 0);

                    string? outPath = BigCsfWriter.RebuildAll(bigPath, labelsSnapshot, patches);
                    if (outPath == null)
                        return (Success: false, Error: "Failed to save BIG file.", PatchCount: patches.Count);

                    return (Success: true, Error: "", PatchCount: patches.Count);
                });

                if (!result.Success)
                {
                    ShowCommandCardStatus(result.Error, isError: true);
                    return false;
                }

                ButtonImageReader.Reload(GameDirectory.Get());

                if (IsGlobalLayoutMode())
                    BuildGlobalLayoutCards();
                else
                    RefreshCommandCardViewAfterBulk();

                ShowCommandCardStatus(
                    $"{scopeDescription}: {bindings.Count} slot(s), {result.PatchCount} atlas file(s) written to BIG.");
                return true;
            }
            catch (Exception ex)
            {
                ShowCommandCardStatus($"Failed while applying image hotkeys: {ex.Message}", isError: true);
                return false;
            }
            finally
            {
                SetCommandCardBusy(false);
            }
        }

        private async void BtnApplyAllKeysFromLabels_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmCommandCardAction(
                "Paint hotkey letters on every command card button using the & shortcut already in each slot's CSF label?\n\n" +
                "Includes all armies — normal cards (buildings + units) and general's powers.\n\nAre you sure?"))
                return;

            var bindings = CollectBindingsAllArmies(includeNormal: true, includeStar: true);
            await ApplyHotkeyImagePatchesAsync(bindings, "All slots (from current labels)");
        }

        private async Task ApplyHotkeysToImagesAsync(
            bool normalOnly, bool starOnly, CcNormalScope normalScope = CcNormalScope.All)
        {
            if (!EnsureGlobalLayoutMode()) return;

            string scope = normalOnly
                ? $"normal command card ({NormalScopeLabel(normalScope)})"
                : "general's power";
            if (!ConfirmCommandCardAction(
                $"Paint hotkey letters onto {scope} button images for all armies and save to !EnglishZH.big?\n\nAre you sure?"))
                return;

            // Image painting must use the template letters the user sees, not stale CSF defaults.
            ApplyLayoutKeysFromInputs(
                normalOnly: normalOnly,
                starOnly: starOnly,
                normalScope: normalScope);

            var bindings = CollectBindingsAllArmies(
                includeNormal: !starOnly,
                includeStar: !normalOnly,
                normalScope: normalScope);
            await ApplyHotkeyImagePatchesAsync(bindings, scope);
        }

        private async void BtnApplyNormalImagesBuildings_Click(object sender, RoutedEventArgs e)
            => await ApplyHotkeysToImagesAsync(normalOnly: true, starOnly: false, normalScope: CcNormalScope.Buildings);

        private async void BtnApplyNormalImagesUnits_Click(object sender, RoutedEventArgs e)
            => await ApplyHotkeysToImagesAsync(normalOnly: true, starOnly: false, normalScope: CcNormalScope.Units);

        private async void BtnApplyStarImages_Click(object sender, RoutedEventArgs e)
            => await ApplyHotkeysToImagesAsync(normalOnly: false, starOnly: true);

        private void SetSlotLabelText(string text, bool recordUndo)
        {
            if (lblSlotText == null) return;
            if (recordUndo && lblSlotText.Text != text)
                PushSlotLabelUndo(lblSlotText.Text);

            _suppressSlotEditor = true;
            lblSlotText.Text = text;
            if (txtSlotHotkey != null)
            {
                char hk = HotkeyPainter.ExtractHotkeyChar(text);
                txtSlotHotkey.Text = hk != '\0' ? hk.ToString() : "";
            }
            if (lblSlotFormatted != null)
                ApplyHotkeyFormatting(lblSlotFormatted, text);
            UpdateSlotApplyButtonsState();
            _suppressSlotEditor = false;
        }

        private void PushSlotLabelUndo(string snapshot)
        {
            if (string.IsNullOrEmpty(snapshot)) return;
            if (_slotLabelUndo.Count > 0 && _slotLabelUndo.Peek() == snapshot) return;
            _slotLabelUndo.Push(snapshot);
            if (btnSlotUndo != null) btnSlotUndo.IsEnabled = true;
        }

        private void LblSlotText_GotFocus(object sender, RoutedEventArgs e)
            => _slotTextAtFocus = lblSlotText?.Text ?? "";

        private void LblSlotText_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_suppressSlotEditor || lblSlotText == null) return;
            if (lblSlotText.Text != _slotTextAtFocus)
                PushSlotLabelUndo(_slotTextAtFocus);
        }

        private string GetSlotLabelForSave() => lblSlotText?.Text ?? "";

        private void SyncSlotKeyFromLabel(string labelText)
        {
            if (txtSlotHotkey == null) return;
            char hk = HotkeyPainter.ExtractHotkeyChar(labelText);
            string keyStr = hk != '\0' ? char.ToUpperInvariant(hk).ToString() : "";
            if (txtSlotHotkey.Text == keyStr) return;
            _suppressSlotEditor = true;
            txtSlotHotkey.Text = keyStr;
            _suppressSlotEditor = false;
        }

        private void LblSlotText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSlotEditor || lblSlotText == null) return;

            string text = lblSlotText.Text;
            SyncSlotKeyFromLabel(text);
            if (lblSlotFormatted != null)
                ApplyHotkeyFormatting(lblSlotFormatted, text);
            UpdateSlotApplyButtonsState();
        }

        private void TxtSlotHotkey_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSlotEditor || txtSlotHotkey == null || lblSlotText == null) return;

            string s = txtSlotHotkey.Text.Trim();
            if (s.Length > 1)
            {
                _suppressSlotEditor = true;
                txtSlotHotkey.Text = s[..1];
                _suppressSlotEditor = false;
                s = txtSlotHotkey.Text;
            }

            char key = s.Length > 0 ? char.ToUpperInvariant(s[0]) : '\0';
            string merged = CommandCardHotkeyService.SetHotkeyCharInLabel(lblSlotText.Text, key);
            if (merged == lblSlotText.Text)
            {
                if (lblSlotFormatted != null)
                    ApplyHotkeyFormatting(lblSlotFormatted, merged);
                UpdateSlotApplyButtonsState();
                return;
            }

            SetSlotLabelText(merged, recordUndo: true);
        }

        private void BtnSlotUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_slotLabelUndo.Count == 0) return;
            string prev = _slotLabelUndo.Pop();
            SetSlotLabelText(prev, recordUndo: false);
            if (btnSlotUndo != null)
                btnSlotUndo.IsEnabled = _slotLabelUndo.Count > 0;
        }

        private void UpdateSlotApplyButtonsState()
        {
            if (lblSlotText == null) return;
            bool ok = CommandCardHotkeyService.TryValidateLabel(GetSlotLabelForSave(), out _);
            if (btnSlotApplyLabel != null) btnSlotApplyLabel.IsEnabled = ok && !_commandCardBusy;
            if (btnSlotApplyImages != null) btnSlotApplyImages.IsEnabled = ok && !_commandCardBusy;
        }

        private void BtnSlotApplyLabel_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentEditCsfKey)) return;

            string labelText = GetSlotLabelForSave();
            if (!CommandCardHotkeyService.TryValidateLabel(labelText, out string? err))
            {
                ShowCommandCardStatus(err ?? "Invalid label.", isError: true);
                return;
            }

            if (!ConfirmCommandCardAction(
                "Save this label text to !EnglishZH.big (CSF only, no button image change)?\n\nAre you sure?"))
                return;

            _csfLabels ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            SetCsfLabelAndShortcutAliases(_currentEditCsfKey, labelText);

            if (!PersistCommandCardToBig(tgaPatches: null))
            {
                MessageBox.Show("Could not save label to BIG file.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RefreshCommandCardViewAfterBulk();
            SlotInfoPanel.Visibility = Visibility.Visible;
            lblSlotId.Text = _currentEditCsfId;
            SetSlotLabelText(labelText, recordUndo: false);
            _slotLabelUndo.Clear();
            if (btnSlotUndo != null) btnSlotUndo.IsEnabled = false;
            ShowCommandCardStatus("Label saved to BIG.");
        }

        private async void BtnSlotApplyImages_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentEditCsfId)) return;

            string labelText = GetSlotLabelForSave();
            if (!CommandCardHotkeyService.TryValidateLabel(labelText, out string? err))
            {
                ShowCommandCardStatus(err ?? "Invalid label.", isError: true);
                return;
            }

            if (!ConfirmCommandCardAction(
                "Paint the hotkey letter on this button image and save to !EnglishZH.big (image only)?\n\nAre you sure?"))
                return;

            _csfLabels ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            SetCsfLabelAndShortcutAliases(_currentEditCsfKey, labelText);

            if (!await ApplyHotkeyImagePatchesAsync(new List<CommandCardHotkeyService.SlotBinding>
                {
                    new(_currentEditCsfKey, _currentEditCsfId, _currentEditArmy, labelText),
                }, "This slot"))
                return;

            SlotInfoPanel.Visibility = Visibility.Visible;
            lblSlotId.Text = _currentEditCsfId;
            imgSlotPreview.Source = ButtonImageReader.GetSlotImage(_currentEditCsfId, _currentEditArmy);
            ApplyHotkeyFormatting(lblSlotFormatted, labelText);
            _slotLabelUndo.Clear();
            if (btnSlotUndo != null) btnSlotUndo.IsEnabled = false;
        }

        /// <summary>
        /// Renders text into a TextBlock with the character following '&amp;' shown in yellow.
        /// The '&amp;' itself is hidden.
        /// </summary>
        private static void ApplyHotkeyFormatting(TextBlock tb, string raw)
        {
            tb.Inlines.Clear();
            if (string.IsNullOrEmpty(raw)) return;

            var normal = new SolidColorBrush(Color.FromRgb(0x88, 0xCC, 0xFF));
            var hotkey = new SolidColorBrush(Color.FromRgb(0xFF, 0xDD, 0x33));
            int i = 0;
            while (i < raw.Length)
            {
                if (raw[i] == '&')
                {
                    if (i + 1 < raw.Length && raw[i + 1] != '&')
                    {
                        tb.Inlines.Add(new System.Windows.Documents.Run("&")
                            { Foreground = normal, FontSize = tb.FontSize * 0.85 });
                        i++;
                        tb.Inlines.Add(new System.Windows.Documents.Run(raw[i].ToString())
                            { Foreground = hotkey });
                        i++;
                    }
                    else
                    {
                        tb.Inlines.Add(new System.Windows.Documents.Run("&") { Foreground = normal });
                        i++;
                    }
                    continue;
                }

                int start = i;
                while (i < raw.Length && raw[i] != '&') i++;
                if (i > start)
                    tb.Inlines.Add(new System.Windows.Documents.Run(raw[start..i]) { Foreground = normal });
            }
        }

        private static string FormatCcSlotId(string csfId)
        {
            // ── 1. Try game CSF (EnglishZH.big) ──────────────────────────────
            if (_csfLabels is { Count: > 0 })
            {
                // Direct match: e.g. "controlbar:sell"
                if (_csfLabels.TryGetValue(csfId, out var v))
                    return CsfFirstLine(v);

                // Strip general-specific prefix then retry
                // e.g. "controlbar:infa_constructchinainfantryhacker"
                //       → "controlbar:constructchinainfantryhacker"
                int ci = csfId.IndexOf(':');
                if (ci >= 0)
                {
                    string ns  = csfId[..ci];
                    string seg = csfId[(ci + 1)..];
                    foreach (var pfx in _generalPrefixes)
                    {
                        if (seg.StartsWith(pfx, StringComparison.OrdinalIgnoreCase))
                        {
                            if (_csfLabels.TryGetValue(ns + ":" + seg[pfx.Length..], out v))
                                return CsfFirstLine(v);
                            break;
                        }
                    }
                }
            }

            // ── 2. Built-in label table ───────────────────────────────────────
            int colon = csfId.LastIndexOf(':');
            string key = colon >= 0 ? csfId[(colon + 1)..] : csfId;

            if (_ccLabels.TryGetValue(key, out var lbl)) return lbl;

            foreach (var pfx in _generalPrefixes)
            {
                if (key.StartsWith(pfx, StringComparison.OrdinalIgnoreCase))
                { key = key[pfx.Length..]; break; }
            }

            if (_ccLabels.TryGetValue(key, out lbl)) return lbl;

            // ── 3. Heuristic string formatting ───────────────────────────────
            string verb = "";
            if (key.StartsWith("construct", StringComparison.OrdinalIgnoreCase))
            { key = key[9..]; verb = "Build "; }
            else if (key.StartsWith("upgrade", StringComparison.OrdinalIgnoreCase))
            { key = key[7..]; }
            else if (key.StartsWith("initiatebattleplan", StringComparison.OrdinalIgnoreCase))
            { key = key[18..]; verb = "Plan: "; }

            foreach (var f in new[] { "china", "america", "gla", "usa" })
            {
                if (key.StartsWith(f, StringComparison.OrdinalIgnoreCase))
                { key = key[f.Length..]; break; }
            }

            foreach (var cat in new[] { "vehicle", "infantry", "tank", "jet" })
            {
                if (key.StartsWith(cat, StringComparison.OrdinalIgnoreCase))
                { key = key[cat.Length..]; break; }
            }

            var sb2 = new StringBuilder();
            for (int i = 0; i < key.Length; i++)
            {
                if (i > 0 && char.IsUpper(key[i])) sb2.Append(' ');
                sb2.Append(key[i]);
            }
            key = sb2.ToString().Trim();

            var parts = key.Split(' ');
            var result = string.Join(" ", parts.Select(w =>
                w.Length == 0 ? w : char.ToUpper(w[0]) + w[1..].ToLower()));

            return verb + result;
        }

        /// <summary>Returns only the first line of a CSF value (trim whitespace).</summary>
        private static string CsfFirstLine(string s)
        {
            int nl = s.IndexOf('\n');
            return (nl > 0 ? s[..nl] : s).Trim();
        }

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

        // ── EXTRA SETTINGS tab ───────────────────────────────────────────────

        private static readonly string[] _extraKeyValues =
        {
            "KEY_A","KEY_B","KEY_C","KEY_D","KEY_E","KEY_F","KEY_G","KEY_H","KEY_I","KEY_J",
            "KEY_K","KEY_L","KEY_M","KEY_N","KEY_O","KEY_P","KEY_Q","KEY_R","KEY_S","KEY_T",
            "KEY_U","KEY_V","KEY_W","KEY_X","KEY_Y","KEY_Z",
            "KEY_0","KEY_1","KEY_2","KEY_3","KEY_4","KEY_5","KEY_6","KEY_7","KEY_8","KEY_9",
            "KEY_F1","KEY_F2","KEY_F3","KEY_F4","KEY_F5","KEY_F6",
            "KEY_F7","KEY_F8","KEY_F9","KEY_F10","KEY_F11","KEY_F12",
            "KEY_SPACE","KEY_TAB","KEY_COMMA","KEY_PERIOD","KEY_BACKSLASH","KEY_SLASH",
            "KEY_LBRACKET","KEY_RBRACKET","KEY_MINUS","KEY_EQUAL","KEY_SEMICOLON",
            "KEY_TICK","KEY_BACKSPACE","KEY_ENTER","KEY_INSERT","KEY_DELETE",
            "KEY_HOME","KEY_END","KEY_UP","KEY_DOWN","KEY_LEFT","KEY_RIGHT",
            "KEY_PGUP","KEY_PGDN",
            "KEY_NUMPAD0","KEY_NUMPAD1","KEY_NUMPAD2","KEY_NUMPAD3","KEY_NUMPAD4",
            "KEY_NUMPAD5","KEY_NUMPAD6","KEY_NUMPAD7","KEY_NUMPAD8","KEY_NUMPAD9",
        };

        private static readonly string[] _extraModValues =
            { "NONE","CTRL","SHIFT","ALT","CTRL_SHIFT","CTRL_ALT","SHIFT_ALT","SHIFT_ALT_CTRL" };

        private string? _idleWorkerBigPath;
        private string? _idleWorkerIniPath;
        private string? _beaconIniPath;
        private string? _gameCommandMapBigPath;
        private string? _gameCommandMapIniPath;
        private bool _extraTabInitialized;

        private sealed record ExtraGameHotkeyDef(
            string Command,
            string Label,
            string DefaultKey,
            string DefaultModifiers,
            ComboBox KeyCombo,
            ComboBox ModCombo,
            TextBlock ShortcutLabel);

        private sealed record ExtraShortcutRow(
            string Label,
            ComboBox KeyCombo,
            ComboBox ModCombo,
            bool Active);

        private ExtraGameHotkeyDef[] GetGameHotkeyDefs() => new[]
        {
            new ExtraGameHotkeyDef("SELECT_ALL", "Select all units", "KEY_Q", "NONE",
                cmbSelectAllKey, cmbSelectAllMod, lblSelectAllShortcut),
            new ExtraGameHotkeyDef("SELECT_ALL_AIRCRAFT", "Select aircraft", "KEY_W", "NONE",
                cmbSelectAircraftKey, cmbSelectAircraftMod, lblSelectAircraftShortcut),
            new ExtraGameHotkeyDef("SELECT_MATCHING_UNITS", "Select matching units", "KEY_E", "NONE",
                cmbSelectMatchingKey, cmbSelectMatchingMod, lblSelectMatchingShortcut),
            new ExtraGameHotkeyDef("SCATTER", "Scatter units", "KEY_X", "NONE",
                cmbScatterKey, cmbScatterMod, lblScatterShortcut),
            new ExtraGameHotkeyDef("STOP", "Stop", "KEY_S", "NONE",
                cmbStopKey, cmbStopMod, lblStopShortcut),
            new ExtraGameHotkeyDef("SELECT_HERO", "Select hero", "KEY_H", "CTRL",
                cmbSelectHeroKey, cmbSelectHeroMod, lblSelectHeroShortcut),
            new ExtraGameHotkeyDef("VIEW_COMMAND_CENTER", "View command center", "KEY_H", "NONE",
                cmbViewCommandCenterKey, cmbViewCommandCenterMod, lblViewCommandCenterShortcut),
            new ExtraGameHotkeyDef("CREATE_FORMATION", "Create formation", "KEY_F", "CTRL",
                cmbCreateFormationKey, cmbCreateFormationMod, lblCreateFormationShortcut),
        };

        private void InitExtraSettingsTab()
        {
            if (_extraTabInitialized) return;
            _extraTabInitialized = true;

            var gameHotkeys = GetGameHotkeyDefs();

            foreach (var cb in new[] { cmbIdleWorkerKey, cmbBeaconKey }.Concat(gameHotkeys.Select(d => d.KeyCombo)))
            {
                cb.Items.Clear();
                foreach (var k in _extraKeyValues) cb.Items.Add(k);
            }
            foreach (var cb in new[] { cmbIdleWorkerMod, cmbBeaconMod }.Concat(gameHotkeys.Select(d => d.ModCombo)))
            {
                cb.Items.Clear();
                foreach (var m in _extraModValues) cb.Items.Add(m);
            }

            foreach (var cb in new[] { cmbIdleWorkerKey, cmbIdleWorkerMod, cmbBeaconKey, cmbBeaconMod }
                .Concat(gameHotkeys.SelectMany(d => new[] { d.KeyCombo, d.ModCombo })))
            {
                cb.SelectionChanged += (_, _) => UpdateExtraHotkeyLabelsAndValidation();
            }

            string bigPath = FindInizHBig();
            _idleWorkerBigPath = bigPath;

            if (File.Exists(bigPath))
            {
                CommandMapLocator.Load(bigPath);
                var (iw, iwPath, bc, bcPath) = CommandMapLocator.FindAll();

                _idleWorkerIniPath = iwPath;
                _beaconIniPath     = bcPath;

                if (iw != null)
                {
                    SelectExtraCombo(cmbIdleWorkerKey, iw.Key);
                    SelectExtraCombo(cmbIdleWorkerMod, iw.Modifiers);
                    lblIdleWorkerPath.Text      = iwPath ?? "";
                    btnSaveIdleWorker.IsEnabled = true;
                }
                else
                {
                    SelectExtraCombo(cmbIdleWorkerKey, "KEY_I");
                    SelectExtraCombo(cmbIdleWorkerMod, "CTRL");
                    lblIdleWorkerPath.Text = "(bulunamadı)";
                }

                if (bc != null)
                {
                    SelectExtraCombo(cmbBeaconKey, bc.Key);
                    SelectExtraCombo(cmbBeaconMod, bc.Modifiers);
                    lblBeaconPath.Text      = bcPath ?? "";
                    btnSaveBeacon.IsEnabled = true;
                }
                else
                {
                    SelectExtraCombo(cmbBeaconKey, "KEY_B");
                    SelectExtraCombo(cmbBeaconMod, "NONE");
                    lblBeaconPath.Text      = "(BIG'de bulunamadı)";
                    btnSaveBeacon.IsEnabled = false;
                }
            }
            else
            {
                SelectExtraCombo(cmbIdleWorkerKey, "KEY_I"); SelectExtraCombo(cmbIdleWorkerMod, "CTRL");
                SelectExtraCombo(cmbBeaconKey,     "KEY_B"); SelectExtraCombo(cmbBeaconMod,     "NONE");
                lblIdleWorkerPath.Text = "(INIZH.big bulunamadı)";
                lblBeaconPath.Text     = "(INIZH.big bulunamadı)";
            }

            LoadGameCommandMapHotkeys(gameHotkeys);
            UpdateExtraHotkeyLabelsAndValidation();
        }

        private void LoadGameCommandMapHotkeys(IReadOnlyList<ExtraGameHotkeyDef> defs)
        {
            string bigPath = FindEnglishZhBig();
            _gameCommandMapBigPath = bigPath;
            _gameCommandMapIniPath = null;

            foreach (var def in defs)
            {
                SelectExtraCombo(def.KeyCombo, def.DefaultKey);
                SelectExtraCombo(def.ModCombo, def.DefaultModifiers);
            }

            if (!File.Exists(bigPath))
            {
                lblGameHotkeyPath.Text = "(EnglishZH.big bulunamadı)";
                btnSaveGameHotkeys.IsEnabled = false;
                return;
            }

            var idx = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
            CommandMapLocator.IndexBig(bigPath, idx);
            _gameCommandMapIniPath =
                idx.Keys.FirstOrDefault(k => string.Equals(
                    k.Replace('/', '\\'),
                    @"Data\English\CommandMap.ini",
                    StringComparison.OrdinalIgnoreCase))
                ?? GetFallbackCommandMapIni(bigPath);

            if (_gameCommandMapIniPath == null)
            {
                lblGameHotkeyPath.Text = "(CommandMap.ini bulunamadı)";
                btnSaveGameHotkeys.IsEnabled = false;
                return;
            }

            byte[]? raw = CommandMapLocator.ReadEntry(bigPath, idx, _gameCommandMapIniPath);
            string text = raw != null ? Encoding.UTF8.GetString(raw) : "";

            foreach (var def in defs)
            {
                var entry = CommandMapParser.ParseBlock(text, def.Command);
                if (entry == null) continue;

                SelectExtraCombo(def.KeyCombo, entry.Key);
                SelectExtraCombo(def.ModCombo, entry.Modifiers);
            }

            lblGameHotkeyPath.Text = _gameCommandMapIniPath;
            btnSaveGameHotkeys.IsEnabled = true;
        }

        private IEnumerable<ExtraShortcutRow> GetExtraShortcutRows()
        {
            bool idleActive = _idleWorkerBigPath != null
                           && _idleWorkerIniPath != null
                           && File.Exists(_idleWorkerBigPath);
            bool beaconActive = _idleWorkerBigPath != null
                             && _beaconIniPath != null
                             && File.Exists(_idleWorkerBigPath);
            bool gameActive = _gameCommandMapBigPath != null
                           && _gameCommandMapIniPath != null
                           && File.Exists(_gameCommandMapBigPath);

            yield return new ExtraShortcutRow("Select idle worker", cmbIdleWorkerKey, cmbIdleWorkerMod, idleActive);
            yield return new ExtraShortcutRow("Beacon", cmbBeaconKey, cmbBeaconMod, beaconActive);

            foreach (var def in GetGameHotkeyDefs())
                yield return new ExtraShortcutRow(def.Label, def.KeyCombo, def.ModCombo, gameActive);
        }

        private static string ShortcutToken(ComboBox keyCb, ComboBox modCb)
        {
            string key = keyCb.SelectedItem as string ?? "";
            string mod = modCb.SelectedItem as string ?? "NONE";
            return $"{mod}|{key}";
        }

        private static string ShortcutDisplay(ComboBox keyCb, ComboBox modCb)
        {
            string key = keyCb.SelectedItem as string ?? "";
            string mod = modCb.SelectedItem as string ?? "NONE";
            string keyDisplay = key.StartsWith("KEY_") ? key[4..] : key;
            string modDisplay = mod.Replace("_", " + ");
            return mod == "NONE" ? keyDisplay : $"{modDisplay} + {keyDisplay}";
        }

        private bool TryFindExtraShortcutDuplicate(out string message)
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in GetExtraShortcutRows().Where(r => r.Active))
            {
                string key = row.KeyCombo.SelectedItem as string ?? "";
                if (string.IsNullOrWhiteSpace(key)) continue;

                string token = ShortcutToken(row.KeyCombo, row.ModCombo);
                if (seen.TryGetValue(token, out var other))
                {
                    message = $"Duplicate shortcut blocked: {other} and {row.Label} both use {ShortcutDisplay(row.KeyCombo, row.ModCombo)}.";
                    return true;
                }
                seen[token] = row.Label;
            }

            message = "";
            return false;
        }

        private void UpdateExtraHotkeyLabelsAndValidation()
        {
            UpdateExtraShortcutLabel(lblIdleWorkerShortcut, cmbIdleWorkerKey, cmbIdleWorkerMod);
            UpdateExtraShortcutLabel(lblBeaconShortcut, cmbBeaconKey, cmbBeaconMod);

            foreach (var def in GetGameHotkeyDefs())
                UpdateExtraShortcutLabel(def.ShortcutLabel, def.KeyCombo, def.ModCombo);

            bool duplicate = TryFindExtraShortcutDuplicate(out string duplicateMessage);

            btnSaveIdleWorker.IsEnabled = _idleWorkerBigPath != null
                                        && _idleWorkerIniPath != null
                                        && File.Exists(_idleWorkerBigPath)
                                        && !duplicate;
            btnSaveBeacon.IsEnabled = _idleWorkerBigPath != null
                                    && _beaconIniPath != null
                                    && File.Exists(_idleWorkerBigPath)
                                    && !duplicate;
            btnSaveGameHotkeys.IsEnabled = _gameCommandMapBigPath != null
                                         && _gameCommandMapIniPath != null
                                         && File.Exists(_gameCommandMapBigPath)
                                         && !duplicate;

            if (duplicate)
            {
                ShowExtraStatus(lblGameHotkeysStatus, duplicateMessage, isError: true);
            }
            else if (lblGameHotkeysStatus.Text.StartsWith("Duplicate shortcut", StringComparison.OrdinalIgnoreCase))
            {
                lblGameHotkeysStatus.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnSaveIdleWorker_Click(object sender, RoutedEventArgs e)
        {
            string key       = cmbIdleWorkerKey.SelectedItem as string ?? "KEY_I";
            string mod       = cmbIdleWorkerMod.SelectedItem as string ?? "CTRL";
            string? targetIni = _idleWorkerIniPath ?? GetFallbackCommandMapIni(_idleWorkerBigPath);

            if (TryFindExtraShortcutDuplicate(out string duplicateMessage))
            {
                ShowExtraStatus(lblIdleWorkerStatus, duplicateMessage, isError: true);
                UpdateExtraHotkeyLabelsAndValidation();
                return;
            }

            if (targetIni == null || _idleWorkerBigPath == null || !File.Exists(_idleWorkerBigPath))
            {
                ShowExtraStatus(lblIdleWorkerStatus, "HATA: INIZH.big bulunamadı.", isError: true);
                return;
            }

            byte[]? newBig = CommandMapEditor.SaveEntry(
                _idleWorkerBigPath, targetIni,
                "SELECT_NEXT_IDLE_WORKER", key, mod);

            if (newBig != null)
            {
                File.WriteAllBytes(_idleWorkerBigPath, newBig);
                ShowExtraStatus(lblIdleWorkerStatus, $"Kaydedildi: {key} + {mod}", isError: false);
            }
            else
            {
                ShowExtraStatus(lblIdleWorkerStatus, "HATA: Blok kaydedilemedi.", isError: true);
            }
        }

        private void BtnSaveBeacon_Click(object sender, RoutedEventArgs e)
        {
            string key        = cmbBeaconKey.SelectedItem as string ?? "KEY_B";
            string mod        = cmbBeaconMod.SelectedItem as string ?? "NONE";
            string? targetIni = _beaconIniPath ?? _idleWorkerIniPath
                                ?? GetFallbackCommandMapIni(_idleWorkerBigPath);

            if (TryFindExtraShortcutDuplicate(out string duplicateMessage))
            {
                ShowExtraStatus(lblBeaconStatus, duplicateMessage, isError: true);
                UpdateExtraHotkeyLabelsAndValidation();
                return;
            }

            if (targetIni == null || _idleWorkerBigPath == null || !File.Exists(_idleWorkerBigPath))
            {
                ShowExtraStatus(lblBeaconStatus, "HATA: INIZH.big bulunamadı.", isError: true);
                return;
            }

            byte[]? newBig = CommandMapEditor.SaveEntry(
                _idleWorkerBigPath, targetIni,
                "PLACE_BEACON", key, mod);

            if (newBig != null)
            {
                File.WriteAllBytes(_idleWorkerBigPath, newBig);
                ShowExtraStatus(lblBeaconStatus, $"Kaydedildi: {key} + {mod}", isError: false);
            }
            else
            {
                ShowExtraStatus(lblBeaconStatus, "HATA: Blok kaydedilemedi.", isError: true);
            }
        }

        private void BtnSaveGameHotkeys_Click(object sender, RoutedEventArgs e)
        {
            if (TryFindExtraShortcutDuplicate(out string duplicateMessage))
            {
                ShowExtraStatus(lblGameHotkeysStatus, duplicateMessage, isError: true);
                UpdateExtraHotkeyLabelsAndValidation();
                return;
            }

            if (_gameCommandMapBigPath == null
             || _gameCommandMapIniPath == null
             || !File.Exists(_gameCommandMapBigPath))
            {
                ShowExtraStatus(lblGameHotkeysStatus, "HATA: EnglishZH.big / CommandMap.ini bulunamadı.", isError: true);
                return;
            }

            var updates = GetGameHotkeyDefs()
                .Select(def => new CommandMapEditor.CommandMapUpdate(
                    def.Command,
                    def.KeyCombo.SelectedItem as string ?? def.DefaultKey,
                    def.ModCombo.SelectedItem as string ?? def.DefaultModifiers))
                .ToList();

            byte[]? newBig = CommandMapEditor.SaveEntries(
                _gameCommandMapBigPath,
                _gameCommandMapIniPath,
                updates);

            if (newBig != null)
            {
                File.WriteAllBytes(_gameCommandMapBigPath, newBig);
                ShowExtraStatus(lblGameHotkeysStatus, "Gameplay hotkeys saved to EnglishZH.big.", isError: false);
                LoadGameCommandMapHotkeys(GetGameHotkeyDefs());
                UpdateExtraHotkeyLabelsAndValidation();
            }
            else
            {
                ShowExtraStatus(lblGameHotkeysStatus, "HATA: CommandMap blokları kaydedilemedi.", isError: true);
            }
        }

        private void BtnNukeBattlemasterFix_Click(object sender, RoutedEventArgs e)
        {
            string bigPath = FindInizHBig();
            if (!File.Exists(bigPath))
            {
                ShowExtraStatus(lblNukeBattlemasterStatus, "HATA: INIZH.big bulunamadı.", isError: true);
                return;
            }

            var (newBig, count) = CommandButtonPatcher.PatchButtonImage(
                bigPath,
                buttonNamePattern: "nukeconstructglatankbattlemaster",
                oldImage:          "SNBattlemaster",
                newImage:          "SNNukeBtleMstr_L");

            if (newBig != null)
            {
                File.WriteAllBytes(bigPath, newBig);
                ShowExtraStatus(lblNukeBattlemasterStatus,
                    $"Düzeltildi — {count} satır güncellendi.", isError: false);
            }
            else
            {
                ShowExtraStatus(lblNukeBattlemasterStatus,
                    "Zaten düzeltilmiş veya blok bulunamadı.", isError: false);
            }
        }

        private static string FindInizHBig()
            => Path.Combine(GameDirectory.Get(), "INIZH.big");

        private static string? GetFallbackCommandMapIni(string? bigPath)
        {
            if (bigPath == null || !File.Exists(bigPath)) return null;
            var idx = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
            CommandMapLocator.IndexBig(bigPath, idx);
            return idx.Keys.FirstOrDefault(k =>
                       k.ToLower().Contains("commandmap") &&
                       k.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                   ?? idx.Keys.FirstOrDefault(k =>
                       k.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        }

        private static void SelectExtraCombo(ComboBox cb, string value)
        {
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (string.Equals(cb.Items[i] as string, value, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedIndex = i;
                    return;
                }
            }
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }

        private static void UpdateExtraShortcutLabel(TextBlock lbl, ComboBox keyCb, ComboBox modCb)
        {
            string key        = keyCb.SelectedItem as string ?? "";
            string mod        = modCb.SelectedItem as string ?? "NONE";
            string keyDisplay = key.StartsWith("KEY_") ? key[4..] : key;
            string modDisplay = mod.Replace("_", " + ");
            lbl.Text = mod == "NONE" ? keyDisplay : $"{modDisplay} + {keyDisplay}";
        }

        private static void ShowExtraStatus(TextBlock lbl, string msg, bool isError)
        {
            lbl.Text       = msg;
            lbl.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x60, 0x60))
                : new SolidColorBrush(Color.FromRgb(0x55, 0xCC, 0x88));
            lbl.Visibility = Visibility.Visible;
        }
    }
}
