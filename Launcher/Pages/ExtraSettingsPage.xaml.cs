using LauncherWinUI.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LauncherWinUI.Pages
{
    public partial class ExtraSettingsPage : Page
    {
        private static readonly string[] _keyValues =
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

        private static readonly string[] _modValues =
            { "NONE","CTRL","SHIFT","ALT","CTRL_SHIFT","CTRL_ALT","SHIFT_ALT","SHIFT_ALT_CTRL" };

        private string? _idleWorkerBigPath;
        private string? _idleWorkerIniPath;
        private string? _beaconIniPath;

        public ExtraSettingsPage()
        {
            InitializeComponent();
            Loaded += ExtraSettingsPage_Loaded;
        }

        private void ExtraSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            InitTab();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.GoBack();
        }

        // ── Init ─────────────────────────────────────────────────────────────

        private void InitTab()
        {
            // Populate combo boxes
            foreach (var cb in new[] { cmbIdleWorkerKey, cmbBeaconKey })
            {
                cb.Items.Clear();
                foreach (var k in _keyValues) cb.Items.Add(k);
            }
            foreach (var cb in new[] { cmbIdleWorkerMod, cmbBeaconMod })
            {
                cb.Items.Clear();
                foreach (var m in _modValues) cb.Items.Add(m);
            }

            // Wire shortcut preview
            cmbIdleWorkerKey.SelectionChanged += (_, _) =>
                UpdateShortcutLabel(lblIdleWorkerShortcut, cmbIdleWorkerKey, cmbIdleWorkerMod);
            cmbIdleWorkerMod.SelectionChanged += (_, _) =>
                UpdateShortcutLabel(lblIdleWorkerShortcut, cmbIdleWorkerKey, cmbIdleWorkerMod);
            cmbBeaconKey.SelectionChanged += (_, _) =>
                UpdateShortcutLabel(lblBeaconShortcut, cmbBeaconKey, cmbBeaconMod);
            cmbBeaconMod.SelectionChanged += (_, _) =>
                UpdateShortcutLabel(lblBeaconShortcut, cmbBeaconKey, cmbBeaconMod);

            // Load current values from INIZH.big
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
                    SelectCombo(cmbIdleWorkerKey, iw.Key);
                    SelectCombo(cmbIdleWorkerMod, iw.Modifiers);
                    lblIdleWorkerPath.Text      = iwPath ?? "";
                    btnSaveIdleWorker.IsEnabled = true;
                }
                else
                {
                    SelectCombo(cmbIdleWorkerKey, "KEY_I");
                    SelectCombo(cmbIdleWorkerMod, "CTRL");
                    lblIdleWorkerPath.Text = "(bulunamadı)";
                }

                if (bc != null)
                {
                    SelectCombo(cmbBeaconKey, bc.Key);
                    SelectCombo(cmbBeaconMod, bc.Modifiers);
                    lblBeaconPath.Text      = bcPath ?? "";
                    btnSaveBeacon.IsEnabled = true;
                }
                else
                {
                    SelectCombo(cmbBeaconKey, "KEY_B");
                    SelectCombo(cmbBeaconMod, "NONE");
                    lblBeaconPath.Text      = "(BIG'de bulunamadı)";
                    btnSaveBeacon.IsEnabled = false;
                }
            }
            else
            {
                SelectCombo(cmbIdleWorkerKey, "KEY_I"); SelectCombo(cmbIdleWorkerMod, "CTRL");
                SelectCombo(cmbBeaconKey,     "KEY_B"); SelectCombo(cmbBeaconMod,     "NONE");
                lblIdleWorkerPath.Text = "(INIZH.big bulunamadı)";
                lblBeaconPath.Text     = "(INIZH.big bulunamadı)";
            }
        }

        // ── Button handlers ──────────────────────────────────────────────────

        private void BtnSaveIdleWorker_Click(object sender, RoutedEventArgs e)
        {
            string key       = cmbIdleWorkerKey.SelectedItem as string ?? "KEY_I";
            string mod       = cmbIdleWorkerMod.SelectedItem as string ?? "CTRL";
            string? targetIni = _idleWorkerIniPath ?? GetFallbackCommandMapIni(_idleWorkerBigPath);

            if (targetIni == null || _idleWorkerBigPath == null || !File.Exists(_idleWorkerBigPath))
            {
                ShowStatus(lblIdleWorkerStatus, "HATA: INIZH.big bulunamadı.", isError: true);
                return;
            }

            byte[]? newBig = CommandMapEditor.SaveEntry(
                _idleWorkerBigPath, targetIni,
                "SELECT_NEXT_IDLE_WORKER", key, mod);

            if (newBig != null)
            {
                File.WriteAllBytes(_idleWorkerBigPath, newBig);
                ShowStatus(lblIdleWorkerStatus, $"Kaydedildi: {key} + {mod}", isError: false);
            }
            else
            {
                ShowStatus(lblIdleWorkerStatus, "HATA: Blok kaydedilemedi.", isError: true);
            }
        }

        private void BtnSaveBeacon_Click(object sender, RoutedEventArgs e)
        {
            string key        = cmbBeaconKey.SelectedItem as string ?? "KEY_B";
            string mod        = cmbBeaconMod.SelectedItem as string ?? "NONE";
            string? targetIni = _beaconIniPath ?? _idleWorkerIniPath
                                ?? GetFallbackCommandMapIni(_idleWorkerBigPath);

            if (targetIni == null || _idleWorkerBigPath == null || !File.Exists(_idleWorkerBigPath))
            {
                ShowStatus(lblBeaconStatus, "HATA: INIZH.big bulunamadı.", isError: true);
                return;
            }

            byte[]? newBig = CommandMapEditor.SaveEntry(
                _idleWorkerBigPath, targetIni,
                "PLACE_BEACON", key, mod);

            if (newBig != null)
            {
                File.WriteAllBytes(_idleWorkerBigPath, newBig);
                ShowStatus(lblBeaconStatus, $"Kaydedildi: {key} + {mod}", isError: false);
            }
            else
            {
                ShowStatus(lblBeaconStatus, "HATA: Blok kaydedilemedi.", isError: true);
            }
        }

        private void BtnNukeBattlemasterFix_Click(object sender, RoutedEventArgs e)
        {
            string bigPath = FindInizHBig();
            if (!File.Exists(bigPath))
            {
                ShowStatus(lblNukeBattlemasterStatus, "HATA: INIZH.big bulunamadı.", isError: true);
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
                ShowStatus(lblNukeBattlemasterStatus,
                    $"Düzeltildi — {count} satır güncellendi.", isError: false);
            }
            else
            {
                ShowStatus(lblNukeBattlemasterStatus,
                    "Zaten düzeltilmiş veya blok bulunamadı.", isError: false);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

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

        private static void SelectCombo(ComboBox cb, string value)
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

        private static void UpdateShortcutLabel(TextBlock lbl, ComboBox keyCb, ComboBox modCb)
        {
            string key        = keyCb.SelectedItem as string ?? "";
            string mod        = modCb.SelectedItem as string ?? "NONE";
            string keyDisplay = key.StartsWith("KEY_") ? key[4..] : key;
            string modDisplay = mod.Replace("_", " + ");
            lbl.Text = mod == "NONE" ? keyDisplay : $"{modDisplay} + {keyDisplay}";
        }

        private static void ShowStatus(TextBlock lbl, string msg, bool isError)
        {
            lbl.Text       = msg;
            lbl.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x60, 0x60))
                : new SolidColorBrush(Color.FromRgb(0x55, 0xCC, 0x88));
            lbl.Visibility = Visibility.Visible;
        }
    }
}
