using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PreySense.Frontend.Services;
using PreySense.Overlay;

namespace PreySense.Frontend.Views
{
    public partial class MetricsWindow : Window
    {
        private const string RegistryKeyPath = @"SOFTWARE\PreySense";
        private static readonly string[] MetricOptions =
        {
            "None", "Usage", "Temperature", "Clock", "Power", "Memory", "Fan Speed", "Voltage", "Memory Speed"
        };
        private static readonly string[] HorizontalPositions = { "Left", "Center", "Right" };
        private static readonly string[] VerticalPositions = { "Top", "Center", "Bottom" };

        private bool _isInitializing = true;
        private readonly UiDebouncer _hudScaleDebouncer = new(TimeSpan.FromMilliseconds(100));
        private readonly UiDebouncer _opacityDebouncer = new(TimeSpan.FromMilliseconds(100));
        private readonly UiDebouncer _intervalDebouncer = new(TimeSpan.FromMilliseconds(100));

        public MetricsWindow()
        {
            InitializeComponent();
            LoadSettings();
            BuildColumnSlots();
            _isInitializing = false;
            OverlayBridge.EnabledChanged += OnOverlayEnabledChanged;
            Closed += (_, _) =>
            {
                OverlayBridge.EnabledChanged -= OnOverlayEnabledChanged;
                _hudScaleDebouncer.Dispose();
                _opacityDebouncer.Dispose();
                _intervalDebouncer.Dispose();
            };
        }

        private void OnOverlayEnabledChanged(bool enabled)
        {
            void Apply()
            {
                if (CheckGameOnly.IsChecked == true)
                    return;

                if (CheckEnableOverlay.IsChecked == enabled)
                    return;

                _isInitializing = true;
                CheckEnableOverlay.IsChecked = enabled;
                _isInitializing = false;
            }

            if (Dispatcher.CheckAccess())
                Apply();
            else
                _ = Dispatcher.BeginInvoke(Apply);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static int GetRegInt(string keyName, int defaultValue)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                var val = key?.GetValue(keyName);
                if (val != null && int.TryParse(val.ToString(), out int res))
                    return res;
            }
            catch { }
            return defaultValue;
        }

        private static string GetRegString(string keyName, string defaultValue)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                var val = key?.GetValue(keyName);
                if (val != null) return val.ToString() ?? defaultValue;
            }
            catch { }
            return defaultValue;
        }

        private static void SetRegInt(string keyName, int value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(keyName, value, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static void SetRegString(string keyName, string value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(keyName, value, RegistryValueKind.String);
            }
            catch { }
        }

        private void LoadSettings()
        {
            bool isOverlayEnabled = AppConfig.IsOverlay();
            bool showInGamesOnly = GetRegInt("overlay_game_only", 0) == 1;
            bool showFps = GetRegInt("overlay_show_fps", 1) == 1;
            bool showFrameTime = showFps && GetRegInt("overlay_show_frame_time", 0) == 1;

            int rawScale = GetRegInt("overlay_scale_percent", 100);
            int scale = (int)Math.Round(rawScale / 25.0) * 25;
            scale = Math.Clamp(scale, 50, 200);

            int rawAlpha = GetRegInt("overlay_alpha", 50);
            int opacityPercent;
            if (rawAlpha > 100)
            {
                opacityPercent = (int)Math.Round((rawAlpha / 255.0) * 10.0) * 10;
            }
            else
            {
                opacityPercent = (int)Math.Round(rawAlpha / 10.0) * 10;
            }
            opacityPercent = Math.Clamp(opacityPercent, 0, 100);

            CheckEnableOverlay.IsChecked = isOverlayEnabled;
            CheckGameOnly.IsChecked = showInGamesOnly;
            CheckShowFps.IsChecked = showFps;
            CheckShowFrameTime.IsChecked = showFrameTime;
            CheckShowFrameTime.IsEnabled = showFps;
            ApplyEnableOverlayAvailability();
            SliderHudScale.Value = scale;
            SliderOpacity.Value = opacityPercent;

            LabelHudScale.Text = $"{scale}%";
            LabelOpacity.Text = $"{opacityPercent}%";
            double intervalSeconds = AppConfig.GetOverlayUpdateIntervalMs() / 1000.0;
            SliderUpdateInterval.Value = intervalSeconds;
            SliderUpdateInterval.ValueText = FormatInterval(intervalSeconds);

            DropdownHorizontalPos.ItemsSource = HorizontalPositions;
            DropdownVerticalPos.ItemsSource = VerticalPositions;

            string hPos = GetRegString("overlay_pos_horizontal", "Left");
            string vPos = GetRegString("overlay_pos_vertical", "Top");
            DropdownHorizontalPos.SelectedItem = hPos;
            DropdownVerticalPos.SelectedItem = vPos;
        }

        private void PositionDropdown_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            if (_isInitializing) return;
            string h = DropdownHorizontalPos.SelectedItem as string ?? "Left";
            string v = DropdownVerticalPos.SelectedItem as string ?? "Top";
            SetRegString("overlay_pos_horizontal", h);
            SetRegString("overlay_pos_vertical", v);
            HardwareOverlayManager.Reposition(h, v);
        }

        private void BuildColumnSlots()
        {
            AppConfig.EnsureDefaultOverlayColumns();
            PanelColumnsContainer.Children.Clear();
            double bodyFontSize = TryFindResource("FontSizeBody") as double? ?? 11d;

            for (int i = 0; i < 8; i++)
            {
                int colNum = i + 1;
                string configKey = $"overlay_col_{colNum}";
                string graphKey = $"overlay_col_{colNum}_graph";

                string defaultVal = AppConfig.DefaultOverlayColumn(colNum);
                int defaultGraph = (i == 4) ? 1 : 0;

                string currentVal = GetRegString(configKey, defaultVal);
                bool hasGraph = GetRegInt(graphKey, defaultGraph) == 1;

                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = $"Slot #{colNum}",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = bodyFontSize
                };

                var combo = new ComboBox
                {
                    ItemsSource = MetricOptions,
                    SelectedItem = currentVal,
                    Height = 28,
                    Margin = new Thickness(4, 0, 8, 0),
                    FontSize = bodyFontSize
                };
                combo.SelectionChanged += (s, e) =>
                {
                    if (!_isInitializing && combo.SelectedItem is string selected)
                    {
                        SetRegString(configKey, selected);
                        HardwareOverlayManager.RefreshDisplayFlags();
                    }
                };

                var checkGraph = new Controls.RectangularSwitch
                {
                    OnText = "│",
                    OffText = "○",
                    Width = 56,
                    Height = 28,
                    IsChecked = hasGraph,
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkGraph.Checked += (s, e) =>
                {
                    if (!_isInitializing)
                    {
                        SetRegInt(graphKey, 1);
                        HardwareOverlayManager.RefreshDisplayFlags();
                    }
                };
                checkGraph.Unchecked += (s, e) =>
                {
                    if (!_isInitializing)
                    {
                        SetRegInt(graphKey, 0);
                        HardwareOverlayManager.RefreshDisplayFlags();
                    }
                };

                Grid.SetColumn(label, 0);
                Grid.SetColumn(combo, 1);
                Grid.SetColumn(checkGraph, 2);

                row.Children.Add(label);
                row.Children.Add(combo);
                row.Children.Add(checkGraph);

                PanelColumnsContainer.Children.Add(row);
            }
        }

        private void OverlayToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (CheckGameOnly.IsChecked == true)
            {
                CheckEnableOverlay.IsChecked = false;
                return;
            }

            bool enabled = CheckEnableOverlay.IsChecked == true;
            if (enabled)
            {
                HardwareOverlayManager.Start();
            }
            else
            {
                HardwareOverlayManager.Stop();
            }
        }

        private void GameOnly_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SetRegInt("overlay_game_only", CheckGameOnly.IsChecked == true ? 1 : 0);
            ApplyEnableOverlayAvailability();
            HardwareOverlayManager.RefreshDisplayFlags();
        }

        private void ApplyEnableOverlayAvailability()
        {
            bool gameOnly = CheckGameOnly.IsChecked == true;
            CheckEnableOverlay.IsEnabled = !gameOnly;
            if (!gameOnly)
                return;

            if (CheckEnableOverlay.IsChecked == true)
            {
                _isInitializing = true;
                CheckEnableOverlay.IsChecked = false;
                _isInitializing = false;
            }

            HardwareOverlayManager.Stop();
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool showFps = CheckShowFps.IsChecked == true;
            SetRegInt("overlay_show_fps", showFps ? 1 : 0);
            CheckShowFrameTime.IsEnabled = showFps;
            if (!showFps)
            {
                _isInitializing = true;
                CheckShowFrameTime.IsChecked = false;
                _isInitializing = false;
                SetRegInt("overlay_show_frame_time", 0);
            }
            HardwareOverlayManager.RefreshDisplayFlags();
        }

        private void FrameTimeSettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool enabled = CheckShowFps.IsChecked == true && CheckShowFrameTime.IsChecked == true;
            SetRegInt("overlay_show_frame_time", enabled ? 1 : 0);
            HardwareOverlayManager.RefreshDisplayFlags();
        }

        private void SliderHudScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LabelHudScale != null)
            {
                int val = (int)Math.Round(SliderHudScale.Value / 25.0) * 25;
                val = Math.Clamp(val, 50, 200);
                if (Math.Abs(SliderHudScale.Value - val) > 0.01)
                    SliderHudScale.Value = val;

                LabelHudScale.Text = $"{val}%";
                if (!_isInitializing)
                {
                    _hudScaleDebouncer.Debounce(SaveHudScale);
                }
            }
        }

        private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LabelOpacity != null)
            {
                int val = (int)Math.Round(SliderOpacity.Value / 10.0) * 10;
                val = Math.Clamp(val, 0, 100);
                if (Math.Abs(SliderOpacity.Value - val) > 0.01)
                    SliderOpacity.Value = val;

                LabelOpacity.Text = $"{val}%";
                if (!_isInitializing)
                {
                    _opacityDebouncer.Debounce(SaveOpacity);
                }
            }
        }

        private void SaveHudScale()
        {
            int value = (int)Math.Round(SliderHudScale.Value / 25.0) * 25;
            SetRegInt("overlay_scale_percent", value);
            HardwareOverlayManager.RefreshDisplayFlags();
        }

        private void SaveOpacity()
        {
            int value = (int)Math.Round(SliderOpacity.Value / 10.0) * 10;
            SetRegInt("overlay_alpha", value);
            HardwareOverlayManager.RefreshDisplayFlags();
        }

        private void SliderUpdateInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SliderUpdateInterval == null)
                return;

            double seconds = SnapIntervalSeconds(SliderUpdateInterval.Value);
            if (Math.Abs(SliderUpdateInterval.Value - seconds) > 0.001)
                SliderUpdateInterval.Value = seconds;

            SliderUpdateInterval.ValueText = FormatInterval(seconds);
            if (!_isInitializing)
                _intervalDebouncer.Debounce(SaveUpdateInterval);
        }

        private void SaveUpdateInterval()
        {
            double seconds = SnapIntervalSeconds(SliderUpdateInterval.Value);
            int ms = AppConfig.SnapOverlayUpdateIntervalMs((int)Math.Round(seconds * 1000.0));
            SliderUpdateInterval.Value = ms / 1000.0;
            SliderUpdateInterval.ValueText = FormatInterval(ms / 1000.0);
            SetRegInt(AppConfig.OverlayUpdateIntervalMsKey, ms);
            HardwareOverlayManager.ApplyPollInterval();
        }

        private static double SnapIntervalSeconds(double seconds)
        {
            double step = AppConfig.OverlayUpdateIntervalStepMs / 1000.0;
            double snapped = Math.Round(seconds / step) * step;
            return Math.Clamp(snapped, AppConfig.OverlayUpdateIntervalMinSeconds, AppConfig.OverlayUpdateIntervalMaxSeconds);
        }

        private static string FormatInterval(double seconds)
        {
            return $"{seconds:0.00}s";
        }

    }
}
