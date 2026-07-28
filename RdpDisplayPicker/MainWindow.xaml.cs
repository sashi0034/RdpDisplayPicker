using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace RdpDisplayPicker
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _connectionHost = string.Empty;
        private string _rdpText = string.Empty;
        private string _statusText = string.Empty;
        private readonly AppSettings _settings;
        private string _currentDisplayKey = string.Empty;
        private string _currentDisplaySignature = string.Empty;
        private bool _suppressSettingsSave;

        public MainWindow()
        {
            _settings = LoadSettings();
            InitializeComponent();
            DataContext = this;
            RefreshMonitors();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<MonitorItem> Monitors { get; } = [];

        public string ConnectionHost
        {
            get => _connectionHost;
            set
            {
                if (_connectionHost == value)
                {
                    return;
                }

                _connectionHost = value;
                OnPropertyChanged(nameof(ConnectionHost));
                UpdateRdpText();
                SaveCurrentSettings();
            }
        }

        public string RdpText
        {
            get => _rdpText;
            private set
            {
                if (_rdpText == value)
                {
                    return;
                }

                _rdpText = value;
                OnPropertyChanged(nameof(RdpText));
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value)
                {
                    return;
                }

                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshMonitors();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            var selectAll = Monitors.Any(monitor => !monitor.IsSelected);

            _suppressSettingsSave = true;
            try
            {
                foreach (var monitor in Monitors)
                {
                    monitor.IsSelected = selectAll;
                }
            }
            finally
            {
                _suppressSettingsSave = false;
            }

            UpdateRdpText();
            DrawMonitorMap();
            SaveCurrentSettings();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RdpText))
            {
                StatusText = "コピーできるRDP設定がありません。";
                return;
            }

            System.Windows.Clipboard.SetText(RdpText);
            StatusText = "RDP設定をクリップボードにコピーしました。";
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Monitors.Any(monitor => monitor.IsSelected))
            {
                StatusText = "RDPで使うモニターを1つ以上選択してください。";
                return;
            }

            try
            {
                var (path, fileResult) = PrepareRdpFile(RdpText);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "mstsc.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false,
                });

                var fileStatus = fileResult switch
                {
                    RdpFileResult.Reused => "既存のRDP構成ファイルを再利用しました",
                    RdpFileResult.Repaired => "RDP構成ファイルを修復しました",
                    _ => "RDP構成ファイルを作成しました",
                };
                StatusText = $"{fileStatus}: {path}";
            }
            catch (Exception ex)
            {
                StatusText = $"RDP起動に失敗しました: {ex.Message}";
            }
        }

        private void MonitorCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawMonitorMap();
        }

        private void RefreshMonitors()
        {
            var hadExistingMonitors = Monitors.Count > 0;
            var selectedDeviceNames = Monitors
                .Where(monitor => monitor.IsSelected)
                .Select(monitor => monitor.DeviceName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedRdpIds = Monitors
                .Where(monitor => monitor.IsSelected)
                .Select(monitor => monitor.RdpId)
                .ToHashSet();

            var screens = Forms.Screen.AllScreens;
            _currentDisplaySignature = CreateDisplaySignature(screens);
            _currentDisplayKey = CreateDisplayKey(_currentDisplaySignature);
            _settings.Profiles.TryGetValue(_currentDisplayKey, out var savedSettings);

            _suppressSettingsSave = true;
            Monitors.Clear();

            for (var i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                var monitorKey = CreateMonitorKey(screen);
                var item = new MonitorItem(
                    rdpId: i,
                    deviceName: screen.DeviceName,
                    monitorKey: monitorKey,
                    bounds: new Int32Rect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height),
                    isPrimary: screen.Primary)
                {
                    IsSelected = ShouldSelectMonitor(savedSettings, monitorKey, screen.DeviceName, i, hadExistingMonitors, selectedDeviceNames, selectedRdpIds),
                };

                item.PropertyChanged += MonitorItem_PropertyChanged;
                Monitors.Add(item);
            }

            if (savedSettings is not null)
            {
                _connectionHost = savedSettings.ConnectionHost ?? string.Empty;
                OnPropertyChanged(nameof(ConnectionHost));
            }

            _suppressSettingsSave = false;

            UpdateRdpText();
            DrawMonitorMap();
            SaveCurrentSettings();
            StatusText = $"{Monitors.Count}台のモニターを検出しました。このディスプレイ構成の設定を自動保存します。";
        }

        private void MonitorItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MonitorItem.IsSelected))
            {
                return;
            }

            UpdateRdpText();
            DrawMonitorMap();
            SaveCurrentSettings();
        }

        private void UpdateRdpText()
        {
            var selectedIds = Monitors
                .Where(monitor => monitor.IsSelected)
                .Select(monitor => monitor.RdpId.ToString())
                .ToArray();

            if (selectedIds.Length == 0)
            {
                RdpText = string.Empty;
                return;
            }

            var host = ConnectionHost.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(host))
            {
                builder.AppendLine($"full address:s:{host}");
            }

            builder.AppendLine("screen mode id:i:2");
            builder.AppendLine("use multimon:i:1");
            builder.AppendLine($"selectedmonitors:s:{string.Join(',', selectedIds)}");

            RdpText = builder.ToString();
        }

        private void DrawMonitorMap()
        {
            if (MonitorCanvas is null)
            {
                return;
            }

            MonitorCanvas.Children.Clear();

            if (Monitors.Count == 0 || MonitorCanvas.ActualWidth <= 0 || MonitorCanvas.ActualHeight <= 0)
            {
                return;
            }

            var minX = Monitors.Min(monitor => monitor.Bounds.X);
            var minY = Monitors.Min(monitor => monitor.Bounds.Y);
            var maxX = Monitors.Max(monitor => monitor.Bounds.X + monitor.Bounds.Width);
            var maxY = Monitors.Max(monitor => monitor.Bounds.Y + monitor.Bounds.Height);
            var virtualWidth = Math.Max(1, maxX - minX);
            var virtualHeight = Math.Max(1, maxY - minY);
            const double padding = 18;

            var availableWidth = Math.Max(1, MonitorCanvas.ActualWidth - padding * 2);
            var availableHeight = Math.Max(1, MonitorCanvas.ActualHeight - padding * 2);
            var scale = Math.Min(availableWidth / virtualWidth, availableHeight / virtualHeight);
            var offsetX = (MonitorCanvas.ActualWidth - virtualWidth * scale) / 2;
            var offsetY = (MonitorCanvas.ActualHeight - virtualHeight * scale) / 2;

            var dpi = VisualTreeHelper.GetDpi(MonitorCanvas);
            var dpiScale = Math.Max(1, Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
            var minMonitorWidth = 118 * dpiScale;
            var minMonitorHeight = 62 * dpiScale;
            var layouts = Monitors
                .Select(monitor => new
                {
                    Monitor = monitor,
                    Width = Math.Max(minMonitorWidth, monitor.Bounds.Width * scale),
                    Height = Math.Max(minMonitorHeight, monitor.Bounds.Height * scale),
                    Left = offsetX + (monitor.Bounds.X - minX) * scale,
                    Top = offsetY + (monitor.Bounds.Y - minY) * scale,
                })
                .ToArray();

            var drawnMinX = layouts.Min(layout => layout.Left);
            var drawnMinY = layouts.Min(layout => layout.Top);
            var drawnMaxX = layouts.Max(layout => layout.Left + layout.Width);
            var drawnMaxY = layouts.Max(layout => layout.Top + layout.Height);
            var recenterX = (MonitorCanvas.ActualWidth - (drawnMaxX - drawnMinX)) / 2 - drawnMinX;
            var recenterY = (MonitorCanvas.ActualHeight - (drawnMaxY - drawnMinY)) / 2 - drawnMinY;

            foreach (var layout in layouts)
            {
                var monitor = layout.Monitor;
                var left = layout.Left + recenterX;
                var top = layout.Top + recenterY;

                var fill = monitor.IsSelected
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 82, 90));

                var border = new Border
                {
                    Width = layout.Width,
                    Height = layout.Height,
                    CornerRadius = new CornerRadius(10),
                    Background = fill,
                    BorderBrush = monitor.IsPrimary ? System.Windows.Media.Brushes.White : new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 255, 255, 255)),
                    BorderThickness = monitor.IsPrimary ? new Thickness(3) : new Thickness(1),
                    Tag = monitor,
                    ToolTip = monitor.Details,
                };

                border.MouseLeftButtonUp += MonitorShape_MouseLeftButtonUp;

                var label = new StackPanel
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                label.Children.Add(new TextBlock
                {
                    Text = $"#{monitor.RdpId}",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                });

                label.Children.Add(new TextBlock
                {
                    Text = $"{monitor.Bounds.Width}x{monitor.Bounds.Height}",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                });

                border.Child = label;
                Canvas.SetLeft(border, left);
                Canvas.SetTop(border, top);
                MonitorCanvas.Children.Add(border);
            }
        }

        private void MonitorShape_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: MonitorItem monitor })
            {
                monitor.IsSelected = !monitor.IsSelected;
            }
        }

        private static bool ShouldSelectMonitor(
            SavedDisplaySettings? savedSettings,
            string monitorKey,
            string deviceName,
            int rdpId,
            bool hadExistingMonitors,
            HashSet<string> selectedDeviceNames,
            HashSet<int> selectedRdpIds)
        {
            if (savedSettings is not null)
            {
                if (savedSettings.SelectedMonitorKeys.Count > 0)
                {
                    return savedSettings.SelectedMonitorKeys.Contains(monitorKey, StringComparer.Ordinal);
                }

                if (savedSettings.SelectedDeviceNames.Count > 0)
                {
                    return savedSettings.SelectedDeviceNames.Contains(deviceName, StringComparer.OrdinalIgnoreCase);
                }

                return savedSettings.SelectedRdpIds.Contains(rdpId);
            }

            if (hadExistingMonitors)
            {
                return selectedDeviceNames.Contains(deviceName) || selectedRdpIds.Contains(rdpId);
            }

            return true;
        }

        private void SaveCurrentSettings()
        {
            if (_suppressSettingsSave || string.IsNullOrEmpty(_currentDisplayKey))
            {
                return;
            }

            _settings.LastDisplayKey = _currentDisplayKey;
            _settings.Profiles[_currentDisplayKey] = new SavedDisplaySettings
            {
                DisplaySignature = _currentDisplaySignature,
                ConnectionHost = ConnectionHost,
                SelectedMonitorKeys = Monitors.Where(monitor => monitor.IsSelected).Select(monitor => monitor.MonitorKey).ToList(),
                SelectedDeviceNames = Monitors.Where(monitor => monitor.IsSelected).Select(monitor => monitor.DeviceName).ToList(),
                SelectedRdpIds = Monitors.Where(monitor => monitor.IsSelected).Select(monitor => monitor.RdpId).ToList(),
                UpdatedAt = DateTimeOffset.Now,
            };

            SaveSettings(_settings);
        }

        private static AppSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new AppSettings();
                }

                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8)) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        private static void SaveSettings(AppSettings settings)
        {
            var directory = System.IO.Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json, Encoding.UTF8);
        }

        private static string SettingsPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RdpDisplayPicker",
            "settings.json");

        private static string CreateDisplaySignature(IEnumerable<Forms.Screen> screens)
        {
            return string.Join("\n", screens.Select(CreateMonitorKey).Order(StringComparer.Ordinal));
        }

        private static string CreateMonitorKey(Forms.Screen screen)
        {
            return $"{screen.Bounds.Left},{screen.Bounds.Top},{screen.Bounds.Width},{screen.Bounds.Height},{screen.Primary}";
        }

        private static string CreateDisplayKey(string displaySignature)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(displaySignature)))[..16];
        }

        private static (string Path, RdpFileResult Result) PrepareRdpFile(string rdpText)
        {
            var directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RdpDisplayPicker",
                "Connections");
            Directory.CreateDirectory(directory);

            var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rdpText)));
            var path = System.IO.Path.Combine(directory, $"RdpDisplayPicker-{contentHash}.rdp");
            var fileExisted = File.Exists(path);

            if (fileExisted)
            {
                try
                {
                    if (File.ReadAllText(path, Encoding.Unicode) == rdpText)
                    {
                        return (path, RdpFileResult.Reused);
                    }
                }
                catch (IOException)
                {
                    // Try to repair the file by overwriting it below.
                }
                catch (UnauthorizedAccessException)
                {
                    // Try to repair the file by overwriting it below.
                }
            }

            File.WriteAllText(path, rdpText, Encoding.Unicode);
            return (path, fileExisted ? RdpFileResult.Repaired : RdpFileResult.Created);
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private enum RdpFileResult
        {
            Created,
            Reused,
            Repaired,
        }
    }

    public sealed class MonitorItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public MonitorItem(int rdpId, string deviceName, string monitorKey, Int32Rect bounds, bool isPrimary)
        {
            RdpId = rdpId;
            DeviceName = deviceName;
            MonitorKey = monitorKey;
            Bounds = bounds;
            IsPrimary = isPrimary;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int RdpId { get; }

        public string DeviceName { get; }

        public string MonitorKey { get; }

        public Int32Rect Bounds { get; }

        public bool IsPrimary { get; }

        public string Title => $"#{RdpId}  {DeviceName}{(IsPrimary ? "  Primary" : string.Empty)}";

        public string Details => $"{Bounds.Width}x{Bounds.Height} / 位置 X:{Bounds.X}, Y:{Bounds.Y}";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public sealed class AppSettings
    {
        public string? LastDisplayKey { get; set; }

        public Dictionary<string, SavedDisplaySettings> Profiles { get; set; } = [];
    }

    public sealed class SavedDisplaySettings
    {
        public string? DisplaySignature { get; set; }

        public string? ConnectionHost { get; set; }

        public List<string> SelectedMonitorKeys { get; set; } = [];

        public List<string> SelectedDeviceNames { get; set; } = [];

        public List<int> SelectedRdpIds { get; set; } = [];

        public DateTimeOffset UpdatedAt { get; set; }
    }
}
