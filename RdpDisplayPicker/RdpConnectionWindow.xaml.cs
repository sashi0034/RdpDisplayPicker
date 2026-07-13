using System.ComponentModel;
using System.Windows;
using Forms = System.Windows.Forms;

namespace RdpDisplayPicker
{
    public partial class RdpConnectionWindow : Window
    {
        private readonly string _server;
        private readonly int[] _selectedMonitorIds;
        private readonly AxMSTSCLib.AxMsRdpClient11NotSafeForScripting _rdpClient;
        private bool _connectStarted;
        private bool _isClosing;

        public RdpConnectionWindow(string server, IEnumerable<int> selectedMonitorIds)
        {
            _server = server;
            _selectedMonitorIds = selectedMonitorIds.ToArray();

            InitializeComponent();

            Title = $"{server} - RDP Display Picker";
            _rdpClient = new AxMSTSCLib.AxMsRdpClient11NotSafeForScripting
            {
                Dock = Forms.DockStyle.Fill,
            };

            ((ISupportInitialize)_rdpClient).BeginInit();
            RdpHost.Child = _rdpClient;
            ((ISupportInitialize)_rdpClient).EndInit();

            _rdpClient.OnConnecting += (_, _) => SetStatus($"{_server} に接続しています...");
            _rdpClient.OnConnected += (_, _) => SetStatus($"{_server} に接続しました。");
            _rdpClient.OnDisconnected += (_, _) =>
            {
                if (!_isClosing)
                {
                    SetStatus($"{_server} から切断されました。");
                }
            };
            _rdpClient.OnFatalError += (_, _) => SetStatus("RDP接続でエラーが発生しました。");

            ContentRendered += (_, _) => Connect();
        }

        private void Connect()
        {
            if (_connectStarted)
            {
                return;
            }

            _connectStarted = true;

            try
            {
                _rdpClient.Server = _server;
                _rdpClient.ColorDepth = 32;
                _rdpClient.FullScreenTitle = $"{_server} - RDP Display Picker";
                _rdpClient.ConnectingText = $"{_server} に接続しています...";
                _rdpClient.DisconnectedText = $"{_server} から切断されました。";

                var settings = _rdpClient.GetOcx() as MSTSCLib.IMsRdpClientNonScriptable6
                    ?? throw new InvalidOperationException("RDP ActiveXの複数モニター設定を初期化できませんでした。");
                settings.UseMultimon = true;
                settings.EnableCredSspSupport = true;
                settings.AllowPromptingForCredentials = true;
                settings.MarkRdpSettingsSecure = true;
                settings.ShowRedirectionWarningDialog = false;

                _rdpClient.MsRdpClientShell.SetRdpProperty(
                    "selectedmonitors",
                    string.Join(',', _selectedMonitorIds));

                _rdpClient.FullScreen = true;
                _rdpClient.Connect();
            }
            catch (Exception ex)
            {
                SetStatus($"RDP接続に失敗しました: {ex.Message}");
                _connectStarted = false;
            }
        }

        private void SetStatus(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                ConnectionStatusText.Text = message;
            }
            else
            {
                Dispatcher.BeginInvoke(() => ConnectionStatusText.Text = message);
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            _isClosing = true;

            try
            {
                if (_rdpClient.Connected != 0)
                {
                    _rdpClient.Disconnect();
                }
            }
            catch
            {
                // ActiveX側ですでに切断済みの場合は、ウィンドウをそのまま閉じます。
            }
        }
    }
}
