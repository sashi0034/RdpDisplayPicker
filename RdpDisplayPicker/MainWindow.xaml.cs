using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
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

        public MainWindow()
        {
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

            foreach (var monitor in Monitors)
            {
                monitor.IsSelected = selectAll;
            }

            UpdateRdpText();
            DrawMonitorMap();
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
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"RdpDisplayPicker-{DateTime.Now:yyyyMMddHHmmss}.rdp");
                File.WriteAllText(path, RdpText, Encoding.Unicode);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "mstsc.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false,
                });

                StatusText = $"RDPを起動しました: {path}";
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
            var selectedDeviceNames = Monitors
                .Where(monitor => monitor.IsSelected)
                .Select(monitor => monitor.DeviceName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Monitors.Clear();

            var screens = Forms.Screen.AllScreens;
            for (var i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                var item = new MonitorItem(
                    rdpId: i,
                    deviceName: screen.DeviceName,
                    bounds: new Int32Rect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height),
                    isPrimary: screen.Primary)
                {
                    IsSelected = selectedDeviceNames.Count == 0 || selectedDeviceNames.Contains(screen.DeviceName),
                };

                item.PropertyChanged += MonitorItem_PropertyChanged;
                Monitors.Add(item);
            }

            UpdateRdpText();
            DrawMonitorMap();
            StatusText = $"{Monitors.Count}台のモニターを検出しました。RDP IDはこのアプリ内の列挙順です。必要なら `mstsc /l` の表示と照合してください。";
        }

        private void MonitorItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MonitorItem.IsSelected))
            {
                return;
            }

            UpdateRdpText();
            DrawMonitorMap();
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

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class MonitorItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public MonitorItem(int rdpId, string deviceName, Int32Rect bounds, bool isPrimary)
        {
            RdpId = rdpId;
            DeviceName = deviceName;
            Bounds = bounds;
            IsPrimary = isPrimary;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int RdpId { get; }

        public string DeviceName { get; }

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
}
