using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UmamusumeDarkMode
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// Transparent, click-through darkmode overlay that tracks Umamusume's window.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Win32 Structures and Constants

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00000800;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        // Hotkey Ctrl+Alt+D: alterna "solo laterales"
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_D = 0x44;
        private const int HOTKEY_ID_SIDES = 0xB001;

        private const uint GW_HWNDPREV = 3;
        private const long WS_EX_TOPMOST = 0x00000008;
        private static readonly IntPtr HWND_TOP_Z = IntPtr.Zero;

        // Excluye nuestras ventanas de las capturas de pantalla (para poder "ver" el juego sin el tinte)
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        private const int SRCCOPY = 0x00CC0020;
        private const int HALFTONE = 4;

        // Hotkeys extra
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_UP = 0x26;
        private const uint VK_DOWN = 0x28;
        private const uint VK_H = 0x48;
        private const int HOTKEY_SIDES_UP = 0xB002;
        private const int HOTKEY_SIDES_DOWN = 0xB003;
        private const int HOTKEY_CENTER_UP = 0xB004;
        private const int HOTKEY_CENTER_DOWN = 0xB005;
        private const int HOTKEY_PAUSE = 0xB006;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 101;

        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;

        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        #endregion

        #region Win32 P/Invoke Declarations

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, int nIcons);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lpTPMParams);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string lpString);

        private static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr ho);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int x, int y);

        [DllImport("gdi32.dll")]
        private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);

        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int mode);

        #endregion

        private readonly DispatcherTimer _detectionTimer;
        private readonly DispatcherTimer _trackingTimer;
        private IntPtr _hwnd = IntPtr.Zero;
        private Process? _targetProcess;
        private IntPtr _targetHwnd = IntPtr.Zero;
        private float _savedVolume = 1.0f;
        private bool _isMutedByFocusLoss = false;
        private bool _isUpdatingVolumeFromCode = false;

        private readonly AppSettings _settings;
        private ControlBarWindow? _controlBar;
        private IntPtr _controlBarHwnd = IntPtr.Zero;

        // Estado de la geometria del tinte (para no recalcular a 60 fps)
        private double _lastTintW = -1;
        private double _lastTintH = -1;
        private bool _tintDirty = true;

        // Deteccion de "pantalla completa" (conciertos, carreras): sin laterales
        private bool _fullWidthDetected = false;
        private int _pendingCount = 0;
        private bool _wasFocused = false;
        private readonly Stopwatch _focusGrace = Stopwatch.StartNew();
        private readonly Stopwatch _detectWatch = Stopwatch.StartNew();

        // Tinte: pinceles animados, niveles actuales y extra por destellos
        private enum TintMode { All, Split, GameOnly }
        private TintMode _mode = TintMode.All;
        private readonly SolidColorBrush _sidesBrush = new SolidColorBrush(Colors.Transparent);
        private readonly SolidColorBrush _centerBrush = new SolidColorBrush(Colors.Transparent);
        private double _sidesCur = 0, _centerCur = 0;
        private double _sidesExtra = 0, _centerExtra = 0;
        private bool _paused = false;
        private readonly Stopwatch _frameWatch = Stopwatch.StartNew();
        private readonly Stopwatch _sampleWatch = Stopwatch.StartNew();

        private static readonly string[] TintNames = { "Negro", "Gris c\u00E1lido", "\u00C1mbar", "Azul noche" };

        public MainWindow()
        {
            InitializeComponent();

            _settings = AppSettings.Load();
            _savedVolume = (_settings.Volume > 0 && _settings.Volume <= 100) ? (_settings.Volume / 100.0f) : 1.0f;

            // Revalidate startup shortcut if present in shell:startup
            StartupManager.RevalidateShortcut();

            // Force creation of MainWindow's HWND right now on app startup!
            // This triggers OnSourceInitialized, registers WndProc, and initializes
            // the System Tray Icon immediately on boot/autostart (even though Visibility is Hidden).
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            InitializeTrayIcon();

            // Restore volume if Windows shuts down or the process terminates
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                if (_targetProcess != null && !_targetProcess.HasExited)
                {
                    AudioManager.SetApplicationVolume(_targetProcess.Id, _savedVolume);
                }
            };

            _controlBar = new ControlBarWindow();
            _controlBar.Left = 0;
            _controlBar.Top = 0;
            _controlBar.OpacitySlider.Value = _settings.Opacity;
            _controlBar.OpacityText.Text = $"{_settings.Opacity}%";
            TintPath.Fill = _sidesBrush;
            CenterPath.Fill = _centerBrush;
            _sidesCur = _settings.Opacity / 100.0;
            _centerCur = _settings.CenterOpacity / 100.0;
            if (_settings.TintColor < 0 || _settings.TintColor >= TintNames.Length) _settings.TintColor = 0;
            ApplyBrushes();

            _controlBar.VolumeSlider.Value = (int)Math.Round(_savedVolume * 100);
            _controlBar.VolumeText.Text = $"{(int)_controlBar.VolumeSlider.Value}%";

            _controlBar.MuteCheckbox.IsChecked = _settings.MuteOnFocusLoss;
            _controlBar.AutostartCheckbox.IsChecked = StartupManager.IsAutostartEnabled();

            _controlBar.OpacitySlider.ValueChanged += OpacitySlider_ValueChanged;
            _controlBar.VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
            _controlBar.MuteCheckbox.Checked += (s, e) => OnMuteSettingChanged(true);
            _controlBar.MuteCheckbox.Unchecked += (s, e) => OnMuteSettingChanged(false);
            _controlBar.AutostartCheckbox.Checked += (s, e) => OnAutostartSettingChanged(true);
            _controlBar.AutostartCheckbox.Unchecked += (s, e) => OnAutostartSettingChanged(false);

            _controlBar.SidesOnlyCheckbox.IsChecked = _settings.SidesOnly;
            _controlBar.HideRequested += () => SetControlBarVisible(false);

            // (3) Nivel del centro
            _controlBar.CenterSlider.Value = _settings.CenterOpacity;
            _controlBar.CenterText.Text = $"{_settings.CenterOpacity}%";
            _controlBar.CenterSlider.ValueChanged += (s, e) =>
            {
                int p = Math.Min(90, (int)Math.Round(e.NewValue));
                _controlBar!.CenterText.Text = $"{p}%";
                _settings.CenterOpacity = p;
                _settings.Save();
                if (!_settings.AdaptiveFlash) { _centerCur = p / 100.0; ApplyBrushes(); }
            };

            // (1) Suavizar destellos
            _controlBar.AdaptiveCheckbox.IsChecked = _settings.AdaptiveFlash;
            _controlBar.AdaptiveCheckbox.Checked += (s, e) => { _settings.AdaptiveFlash = true; _settings.Save(); };
            _controlBar.AdaptiveCheckbox.Unchecked += (s, e) =>
            {
                _settings.AdaptiveFlash = false;
                _settings.Save();
                _sidesExtra = 0; _centerExtra = 0;
                _sidesCur = _settings.Opacity / 100.0;
                _centerCur = _settings.CenterOpacity / 100.0;
                ApplyBrushes();
            };
            _controlBar.StrengthSlider.Value = _settings.AdaptiveStrength;
            _controlBar.StrengthText.Text = $"{_settings.AdaptiveStrength}%";
            _controlBar.StrengthSlider.ValueChanged += (s, e) =>
            {
                int p = (int)Math.Round(e.NewValue);
                _controlBar!.StrengthText.Text = $"{p}%";
                _settings.AdaptiveStrength = p;
                _settings.Save();
            };

            // (2) Color del tinte
            _controlBar.SetColorName(TintNames[_settings.TintColor]);
            _controlBar.ColorCycleRequested += () =>
            {
                _settings.TintColor = (_settings.TintColor + 1) % TintNames.Length;
                _settings.Save();
                _controlBar!.SetColorName(TintNames[_settings.TintColor]);
                ApplyBrushes();
            };
            _controlBar.SidesOnlyCheckbox.Checked += (s, e) => SetSidesOnly(true);
            _controlBar.SidesOnlyCheckbox.Unchecked += (s, e) => SetSidesOnly(false);

            _controlBarHwnd = new WindowInteropHelper(_controlBar).EnsureHandle();
            SetWindowDisplayAffinity(_controlBarHwnd, WDA_EXCLUDEFROMCAPTURE);

            // Loop 1: Checks for the game process every 15 seconds when not running
            _detectionTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _detectionTimer.Tick += (s, e) => TryAttachToGame();

            // Loop 2: Frequently tracks the window position & size while the game is running (~60 fps)
            _trackingTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _trackingTimer.Tick += (s, e) => UpdateTracking();

            // Check immediately on app startup
            if (!TryAttachToGame())
            {
                _detectionTimer.Start();
            }
        }

        private NOTIFYICONDATA _trayIconData;
        private IntPtr _hTrayIcon = IntPtr.Zero;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _hwnd = new WindowInteropHelper(this).Handle;

            // 100% Click-through overlay style
            long exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE));

            HwndSource? source = HwndSource.FromHwnd(_hwnd);
            source?.AddHook(WndProc);

            RegisterHotKey(_hwnd, HOTKEY_ID_SIDES, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_D);
            SetWindowDisplayAffinity(_hwnd, WDA_EXCLUDEFROMCAPTURE);
            RegisterHotKey(_hwnd, HOTKEY_SIDES_UP, MOD_CONTROL | MOD_ALT, VK_UP);
            RegisterHotKey(_hwnd, HOTKEY_SIDES_DOWN, MOD_CONTROL | MOD_ALT, VK_DOWN);
            RegisterHotKey(_hwnd, HOTKEY_CENTER_UP, MOD_CONTROL | MOD_ALT | MOD_SHIFT, VK_UP);
            RegisterHotKey(_hwnd, HOTKEY_CENTER_DOWN, MOD_CONTROL | MOD_ALT | MOD_SHIFT, VK_DOWN);
            RegisterHotKey(_hwnd, HOTKEY_PAUSE, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_H);

            InitializeTrayIcon();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() != HOTKEY_ID_SIDES)
            {
                HandleHotkey(wParam.ToInt32());
                handled = true;
            }
            else if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID_SIDES)
            {
                if (_controlBar != null)
                {
                    // Dispara Checked/Unchecked -> SetSidesOnly
                    _controlBar.SidesOnlyCheckbox.IsChecked = !_settings.SidesOnly;
                }
                handled = true;
            }
            else if (msg == WM_TASKBARCREATED)
            {
                // Explorer restarted: re-create tray icon
                if (_trayIconData.hWnd != IntPtr.Zero)
                {
                    Shell_NotifyIcon(NIM_ADD, ref _trayIconData);
                }
                handled = true;
            }
            else if (msg == WM_TRAYICON)
            {
                int mouseMsg = (int)lParam;
                if (mouseMsg == WM_RBUTTONUP)
                {
                    ShowTrayContextMenu();
                    handled = true;
                }
                else if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK)
                {
                    ToggleControlBarFromTray();
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        private void InitializeTrayIcon()
        {
            _hTrayIcon = LoadAppIconHandle();

            _trayIconData = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hTrayIcon,
                szTip = "Uma Night"
            };

            Shell_NotifyIcon(NIM_ADD, ref _trayIconData);
        }

        private static IntPtr LoadAppIconHandle()
        {
            try
            {
                string icoPath = Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
                if (File.Exists(icoPath))
                {
                    ExtractIconEx(icoPath, 0, out _, out IntPtr hSmall, 1);
                    if (hSmall != IntPtr.Zero) return hSmall;
                }

                string exePath = StartupManager.GetCurrentExecutablePath();
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    ExtractIconEx(exePath, 0, out _, out IntPtr hSmall, 1);
                    if (hSmall != IntPtr.Zero) return hSmall;
                }
            }
            catch { }

            return IntPtr.Zero;
        }

        private void RemoveTrayIcon()
        {
            if (_trayIconData.hWnd != IntPtr.Zero)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _trayIconData);
                _trayIconData.hWnd = IntPtr.Zero;
            }

            if (_hTrayIcon != IntPtr.Zero)
            {
                DestroyIcon(_hTrayIcon);
                _hTrayIcon = IntPtr.Zero;
            }
        }

        private void ShowTrayContextMenu()
        {
            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hwnd);

            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            try
            {
                AppendMenu(hMenu, MF_STRING, 1003, _settings.ShowControlBar ? "Ocultar barra" : "Mostrar barra");
                AppendMenu(hMenu, MF_STRING | (_settings.SidesOnly ? 0x0008u : 0u), 1004, "Solo oscurecer laterales");
                AppendMenu(hMenu, MF_STRING | (_paused ? 0x0008u : 0u), 1005, "Pausar oscurecido (Ctrl+Alt+H)");
                AppendMenu(hMenu, MF_STRING, 1001, "Configuraci\u00F3n");
                AppendMenu(hMenu, MF_SEPARATOR, 0, string.Empty);
                AppendMenu(hMenu, MF_STRING, 1002, "Salir");

                uint cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, _hwnd, IntPtr.Zero);

                if (cmd == 1005)
                {
                    TogglePause();
                }
                else if (cmd == 1003)
                {
                    SetControlBarVisible(!_settings.ShowControlBar);
                }
                else if (cmd == 1004)
                {
                    if (_controlBar != null) _controlBar.SidesOnlyCheckbox.IsChecked = !_settings.SidesOnly;
                }
                else if (cmd == 1001)
                {
                    _settings.ShowControlBar = true;
                    _settings.Save();
                    if (_controlBar != null)
                    {
                        _controlBar.ToggleExpanded();
                        FocusGame();
                    }
                }
                else if (cmd == 1002)
                {
                    Close();
                }
            }
            finally
            {
                DestroyMenu(hMenu);
            }
        }

        private void SetControlBarVisible(bool visible)
        {
            _settings.ShowControlBar = visible;
            _settings.Save();

            if (_controlBar == null) return;
            if (!visible)
            {
                _controlBar.Visibility = Visibility.Hidden;
            }
            else if (_targetProcess != null && !_targetProcess.HasExited)
            {
                FocusGame();
            }
        }

        private void FocusGame()
        {
            if (_targetHwnd != IntPtr.Zero && IsWindow(_targetHwnd))
            {
                SetForegroundWindow(_hwnd);
                SetForegroundWindow(_targetHwnd);
            }
        }

        private void ToggleControlBarFromTray()
        {
            SetControlBarVisible(!_settings.ShowControlBar);
        }

        protected override void OnClosed(EventArgs e)
        {
            RemoveTrayIcon();

            if (_hwnd != IntPtr.Zero)
            {
                foreach (int id in new[] { HOTKEY_ID_SIDES, HOTKEY_SIDES_UP, HOTKEY_SIDES_DOWN, HOTKEY_CENTER_UP, HOTKEY_CENTER_DOWN, HOTKEY_PAUSE })
                {
                    UnregisterHotKey(_hwnd, id);
                }
            }
            _settings.SaveNow();

            // Always restore game volume before closing the app so it is never left at 0%
            if (_targetProcess != null && !_targetProcess.HasExited)
            {
                AudioManager.SetApplicationVolume(_targetProcess.Id, _savedVolume);
            }

            try
            {
                _controlBar?.Close();
            }
            catch { }

            base.OnClosed(e);
        }

        private bool TryAttachToGame()
        {
            var process = FindTargetProcess();
            if (process == null || process.HasExited)
            {
                return false;
            }

            IntPtr hwnd = GetTargetWindowHandle(process);
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                return false;
            }

            _targetProcess = process;
            _targetHwnd = hwnd;

            // Sync volume slider:
            // Only adopt the game's volume if it is currently playing audio (> 1%).
            // If the game is starting up at 0% or was left muted by focus loss / previous session,
            // DO NOT overwrite _savedVolume with 0! Keep/restore the volume from before focus loss.
            float? currentVol = AudioManager.GetApplicationVolume(process.Id);
            if (currentVol.HasValue && currentVol.Value > 0.01f)
            {
                _savedVolume = currentVol.Value;
                _settings.Volume = (int)Math.Round(_savedVolume * 100);
                _settings.Save();
            }
            else
            {
                if (_savedVolume < 0.01f)
                {
                    _savedVolume = (_settings.Volume > 0 && _settings.Volume <= 100) ? (_settings.Volume / 100.0f) : 1.0f;
                }
            }

            if (_controlBar != null)
            {
                _isUpdatingVolumeFromCode = true;
                _controlBar.VolumeSlider.Value = Math.Round(_savedVolume * 100);
                _controlBar.VolumeText.Text = $"{(int)_controlBar.VolumeSlider.Value}%";
                _isUpdatingVolumeFromCode = false;
            }

            // Stop 15s polling and switch to fast window tracking loop
            _detectionTimer.Stop();
            _trackingTimer.Start();

            UpdateTracking();
            return true;
        }

        private void UpdateTracking()
        {
            if (_targetProcess == null || _targetProcess.HasExited || !IsWindow(_targetHwnd))
            {
                DetachFromGame();
                return;
            }

            IntPtr fgHwnd = GetForegroundWindow();
            GetWindowThreadProcessId(fgHwnd, out uint fgPid);
            bool isGameInFocus = (fgPid == _targetProcess.Id || fgHwnd == _hwnd || (_controlBarHwnd != IntPtr.Zero && fgHwnd == _controlBarHwnd));
            bool isMinimized = IsIconic(_targetHwnd);

            // Audio: mute al perder foco (si esta habilitado) / restaurar al volver
            if (!isGameInFocus || isMinimized)
            {
                if (_settings.MuteOnFocusLoss && !_isMutedByFocusLoss)
                {
                    float? currentVol = AudioManager.GetApplicationVolume(_targetProcess.Id);
                    if (currentVol.HasValue && currentVol.Value > 0.01f)
                    {
                        _savedVolume = currentVol.Value;
                        _settings.Volume = (int)Math.Round(_savedVolume * 100);
                        _settings.Save();
                    }
                    AudioManager.SetApplicationVolume(_targetProcess.Id, 0.0f);
                    _isMutedByFocusLoss = true;
                }
            }
            else if (_isMutedByFocusLoss)
            {
                AudioManager.SetApplicationVolume(_targetProcess.Id, _savedVolume);
                _isMutedByFocusLoss = false;
            }

            // Minimizado o sin bounds: ocultar todo
            if (isMinimized || !TryGetWindowBounds(_targetHwnd, out RECT rect))
            {
                if (Visibility != Visibility.Hidden) Visibility = Visibility.Hidden;
                if (_controlBar != null && _controlBar.Visibility != Visibility.Hidden) _controlBar.Visibility = Visibility.Hidden;
                return;
            }

            // Convert physical screen pixels to WPF DIPs according to current DPI scaling
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            double targetLeft = rect.Left / scaleX;
            double targetTop = rect.Top / scaleY;
            double targetWidth = rect.Width / scaleX;
            double targetHeight = rect.Height / scaleY;

            if (Math.Abs(Left - targetLeft) > 0.5) Left = targetLeft;
            if (Math.Abs(Top - targetTop) > 0.5) Top = targetTop;
            if (Math.Abs(Width - targetWidth) > 0.5) Width = targetWidth;
            if (Math.Abs(Height - targetHeight) > 0.5) Height = targetHeight;

            // Solo medir con el juego en foco: si hay otra ventana encima, la captura la veria a ella
            // Al volver al juego (Alt+Tab, clic) esperar 1 s: los primeros cuadros suelen ser transiciones
            if (isGameInFocus && !_wasFocused) { _focusGrace.Restart(); _pendingCount = 0; }
            _wasFocused = isGameInFocus;
            if (isGameInFocus && _focusGrace.ElapsedMilliseconds > 1000) DetectLayout(rect);
            UpdateTintGeometry(targetWidth, targetHeight);
            UpdateAdaptive(rect, isGameInFocus);

            if (Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
            }

            // Juego visible pero sin foco (ej: clic en la otra pantalla):
            // se mantiene el tinte, sin topmost y justo encima del juego,
            // asi no tapa ventanas que pongas delante del juego.
            if (!isGameInFocus)
            {
                if (_controlBar != null && _controlBar.Visibility != Visibility.Hidden)
                {
                    _controlBar.Visibility = Visibility.Hidden;
                }
                KeepOverlayJustAboveGame();
                return;
            }

            if (!Topmost) Topmost = true;

            if (_controlBar != null && !_settings.ShowControlBar && _controlBar.Visibility != Visibility.Hidden)
            {
                _controlBar.Visibility = Visibility.Hidden;
            }

            // Position and show the compact ControlBar (solo si el usuario la tiene visible)
            if (_controlBar != null && _settings.ShowControlBar)
            {
                double barWidth = _controlBar.ActualWidth > 0 ? _controlBar.ActualWidth : 235;
                double barLeft = targetLeft + (targetWidth - barWidth) / 2.0;
                double barTop = targetTop + 8.0;

                if (Math.Abs(_controlBar.Left - barLeft) > 0.5) _controlBar.Left = barLeft;
                if (Math.Abs(_controlBar.Top - barTop) > 0.5) _controlBar.Top = barTop;

                if (_controlBar.Visibility != Visibility.Visible)
                {
                    _controlBar.SizeToContent = SizeToContent.WidthAndHeight;
                    _controlBar.UpdateLayout();
                    _controlBar.Visibility = Visibility.Visible;
                }

                if (_controlBarHwnd != IntPtr.Zero)
                {
                    SetWindowPos(_controlBarHwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }

            if (_hwnd != IntPtr.Zero)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        /// <summary>
        /// Deja el overlay (no topmost) inmediatamente por encima del juego en el orden Z.
        /// </summary>
        private void KeepOverlayJustAboveGame()
        {
            if (_hwnd == IntPtr.Zero) return;

            if (Topmost) Topmost = false;

            IntPtr above = GetWindow(_targetHwnd, GW_HWNDPREV);
            if (above == _hwnd) return;

            // SetWindowPos coloca NUESTRA ventana debajo de 'insertAfter'.
            // Si la ventana de arriba es topmost, usamos el tope de la banda normal.
            IntPtr insertAfter = above;
            if (above == IntPtr.Zero || (GetWindowLongPtr(above, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0)
            {
                insertAfter = HWND_TOP_Z;
            }

            SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// Dibuja el tinte. Tres modos:
        ///  All      = todo con el nivel principal (modo clasico o ventana vertical)
        ///  Split    = laterales con el nivel principal + centro con su propio nivel
        ///  GameOnly = juego a pantalla completa (conciertos/carreras): todo con el nivel del centro
        /// </summary>
        private void UpdateTintGeometry(double w, double h)
        {
            if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0) return;

            bool sides = _settings.SidesOnly && w > h;
            TintMode mode = !sides ? TintMode.All : (_fullWidthDetected ? TintMode.GameOnly : TintMode.Split);

            if (!_tintDirty && mode == _mode &&
                Math.Abs(w - _lastTintW) < 0.5 && Math.Abs(h - _lastTintH) < 0.5)
            {
                return;
            }

            _lastTintW = w;
            _lastTintH = h;
            _mode = mode;
            _tintDirty = false;

            var full = new RectangleGeometry(new Rect(0, 0, w, h));
            full.Freeze();

            switch (mode)
            {
                case TintMode.All:
                    TintPath.Data = full;
                    CenterPath.Data = null;
                    break;
                case TintMode.GameOnly:
                    TintPath.Data = null;
                    CenterPath.Data = full;
                    break;
                default:
                    var hole = new RectangleGeometry(new Rect(
                        w * _settings.HoleLeftPct / 100.0,
                        h * _settings.HoleTopPct / 100.0,
                        w * _settings.HoleWidthPct / 100.0,
                        h * _settings.HoleHeightPct / 100.0));
                    hole.Freeze();
                    var sidesGeo = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
                    sidesGeo.Freeze();
                    TintPath.Data = sidesGeo;
                    CenterPath.Data = hole;
                    break;
            }
        }

        // ================= (1) Suavizar destellos =================

        /// <summary>
        /// Cada frame: mide el brillo (cada 100 ms) y acerca el nivel actual al objetivo.
        /// Sube rapido (0.12 s) y baja lento (0.9 s): los flashes blancos quedan amortiguados.
        /// </summary>
        private void UpdateAdaptive(RECT rect, bool focused)
        {
            double dt = Math.Min(0.5, _frameWatch.Elapsed.TotalSeconds);
            _frameWatch.Restart();

            if (_settings.AdaptiveFlash && focused && _sampleWatch.ElapsedMilliseconds >= 100)
            {
                _sampleWatch.Restart();
                if (MeasureLuminance(rect, out double lumSides, out double lumCenter, out double lumAll))
                {
                    double k = _settings.AdaptiveStrength / 100.0;
                    switch (_mode)
                    {
                        case TintMode.All:
                            _sidesExtra = FlashExtra(lumAll) * k; _centerExtra = 0; break;
                        case TintMode.GameOnly:
                            _sidesExtra = 0; _centerExtra = FlashExtra(lumAll) * k; break;
                        default:
                            _sidesExtra = FlashExtra(lumSides) * k; _centerExtra = FlashExtra(lumCenter) * k; break;
                    }
                }
            }

            if (!_settings.AdaptiveFlash) { _sidesExtra = 0; _centerExtra = 0; }

            double sBase = _settings.Opacity / 100.0;
            double cBase = _settings.CenterOpacity / 100.0;
            // El extra solo se aplica a zonas que tienen tinte activado (nivel > 0)
            double sTarget = sBase > 0 ? Math.Min(0.9, sBase + _sidesExtra) : 0;
            double cTarget = cBase > 0 ? Math.Min(0.9, cBase + _centerExtra) : 0;

            _sidesCur = Approach(_sidesCur, sTarget, dt);
            _centerCur = Approach(_centerCur, cTarget, dt);
            ApplyBrushes();
        }

        // 0 hasta 55% de brillo, sube lineal hasta 1 en 90% de brillo
        private static double FlashExtra(double lum)
        {
            return Math.Clamp((lum - 0.55) / 0.35, 0.0, 1.0);
        }

        private double Approach(double cur, double target, double dt)
        {
            if (!_settings.AdaptiveFlash) return target;
            double tau = target > cur ? 0.12 : 0.9;
            return cur + (target - cur) * (1.0 - Math.Exp(-dt / tau));
        }

        /// <summary>
        /// Captura el juego reducido a 64x36 (el overlay esta excluido de la captura)
        /// y calcula el brillo medio de laterales, centro y total.
        /// </summary>
        private bool MeasureLuminance(RECT rect, out double lumSides, out double lumCenter, out double lumAll)
        {
            lumSides = lumCenter = lumAll = 0;
            if (rect.Width <= 0 || rect.Height <= 0) return false;

            const int W = 64, H = 36;
            double cl = _settings.HoleLeftPct / 100.0;
            double cr = (_settings.HoleLeftPct + _settings.HoleWidthPct) / 100.0;

            IntPtr sdc = GetDC(IntPtr.Zero);
            IntPtr mdc = CreateCompatibleDC(sdc);
            IntPtr bmp = CreateCompatibleBitmap(sdc, W, H);
            IntPtr old = SelectObject(mdc, bmp);
            try
            {
                SetStretchBltMode(mdc, HALFTONE);
                if (!StretchBlt(mdc, 0, 0, W, H, sdc, rect.Left, rect.Top, rect.Width, rect.Height, SRCCOPY)) return false;

                double sumS = 0, sumC = 0; int nS = 0, nC = 0;
                for (int x = 0; x < W; x++)
                {
                    double fx = (x + 0.5) / W;
                    bool isCenter = fx >= cl && fx < cr;
                    for (int y = 0; y < H; y++)
                    {
                        uint p = GetPixel(mdc, x, y);
                        double l = (0.2126 * (p & 0xFF) + 0.7152 * ((p >> 8) & 0xFF) + 0.0722 * ((p >> 16) & 0xFF)) / 255.0;
                        if (isCenter) { sumC += l; nC++; } else { sumS += l; nS++; }
                    }
                }
                lumSides = nS > 0 ? sumS / nS : 0;
                lumCenter = nC > 0 ? sumC / nC : 0;
                lumAll = (sumS + sumC) / Math.Max(1, nS + nC);
                return true;
            }
            finally
            {
                SelectObject(mdc, old);
                DeleteObject(bmp);
                DeleteDC(mdc);
                ReleaseDC(IntPtr.Zero, sdc);
            }
        }

        // ================= (2) Color del tinte =================

        private (byte r, byte g, byte b) TintRgb()
        {
            switch (_settings.TintColor)
            {
                case 1: return (40, 32, 24);   // Gris calido
                case 2: return (70, 40, 0);    // Ambar
                case 3: return (8, 14, 42);    // Azul noche
                default: return (0, 0, 0);     // Negro
            }
        }

        private void ApplyBrushes()
        {
            var (r, g, b) = TintRgb();
            byte sa = _paused ? (byte)0 : (byte)Math.Round(255 * Math.Clamp(_sidesCur, 0, 0.9));
            byte ca = _paused ? (byte)0 : (byte)Math.Round(255 * Math.Clamp(_centerCur, 0, 0.9));
            var sc = Color.FromArgb(sa, r, g, b);
            var cc = Color.FromArgb(ca, r, g, b);
            if (_sidesBrush.Color != sc) _sidesBrush.Color = sc;
            if (_centerBrush.Color != cc) _centerBrush.Color = cc;
        }

        // ================= (4) Atajos =================

        private void HandleHotkey(int id)
        {
            if (_controlBar == null) return;
            switch (id)
            {
                case HOTKEY_SIDES_UP: Nudge(_controlBar.OpacitySlider, +5); break;
                case HOTKEY_SIDES_DOWN: Nudge(_controlBar.OpacitySlider, -5); break;
                case HOTKEY_CENTER_UP: Nudge(_controlBar.CenterSlider, +5); break;
                case HOTKEY_CENTER_DOWN: Nudge(_controlBar.CenterSlider, -5); break;
                case HOTKEY_PAUSE: TogglePause(); break;
            }
        }

        private static void Nudge(Slider slider, double delta)
        {
            slider.Value = Math.Clamp(slider.Value + delta, slider.Minimum, slider.Maximum);
        }

        private void TogglePause()
        {
            _paused = !_paused;
            ApplyBrushes();
        }


        /// <summary>
        /// Cada ~400 ms mira los dos bordes de la vista central del juego.
        /// En el layout con menu lateral hay un "corte" vertical marcado en esos bordes;
        /// en pantalla completa (conciertos, carreras) la imagen sigue de largo.
        /// </summary>
        private void DetectLayout(RECT rect)
        {
            if (!_settings.SidesOnly || rect.Width <= rect.Height) return;
            if (_detectWatch.ElapsedMilliseconds < 400) return;
            _detectWatch.Restart();

            int xl = rect.Left + (int)Math.Round(rect.Width * _settings.HoleLeftPct / 100.0);
            int xr = rect.Left + (int)Math.Round(rect.Width * (_settings.HoleLeftPct + _settings.HoleWidthPct) / 100.0);

            double rl = EdgeHitRatio(xl, rect.Top, rect.Height);
            double rr = EdgeHitRatio(xr, rect.Top, rect.Height);
            bool full = Math.Max(rl, rr) < _settings.SplitEdgeThreshold;

            // Pantallas de carga / transiciones: laterales casi lisos (todo blanco o todo negro).
            // Ahi no se puede saber el layout por los bordes.
            SideStats(rect, out double sideMean, out double sideStd, out double centerMean, out double centerStd);
            // Carga ("Connecting") o transicion: el centro queda liso (todo blanco) aunque el menu siga a la derecha
            bool uniform = sideStd < _settings.UniformStdThreshold || centerStd < _settings.UniformStdThreshold;

            if (_settings.DebugDetect)
            {
                try
                {
                    string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmaNight", "detect.log");
                    File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss.fff} L={rl:F2} R={rr:F2} std={sideStd:F3} mean={sideMean:F2} cstd={centerStd:F3} cmean={centerMean:F2} uniform={uniform} full={full} actual={_fullWidthDetected}\n");
                }
                catch { }
            }

            if (uniform)
            {
                // Pantalla lisa y clara (carga en blanco): mantener oscurecidos los laterales.
                // Pantalla lisa y oscura: dejar el estado como esta.
                if (_fullWidthDetected && (sideMean > 0.6 || centerMean > 0.6))
                {
                    _fullWidthDetected = false;
                    _tintDirty = true;
                }
                _pendingCount = 0;
                return;
            }

            if (full == _fullWidthDetected)
            {
                _pendingCount = 0;
                return;
            }

            // Histeresis: 2 lecturas seguidas iguales antes de cambiar (evita parpadeos)
            if (++_pendingCount >= (full ? 3 : 2))
            {
                _fullWidthDetected = full;
                _pendingCount = 0;
                _tintDirty = true;
            }
        }

        /// <summary>
        /// Brillo medio y desvio estandar de las zonas laterales (captura reducida 64x36).
        /// </summary>
        private void SideStats(RECT rect, out double mean, out double std, out double cMean, out double cStd)
        {
            mean = 0; std = 1; cMean = 0; cStd = 1;
            const int W = 64, H = 36;
            double cl = _settings.HoleLeftPct / 100.0;
            double cr = (_settings.HoleLeftPct + _settings.HoleWidthPct) / 100.0;

            IntPtr sdc = GetDC(IntPtr.Zero);
            IntPtr mdc = CreateCompatibleDC(sdc);
            IntPtr bmp = CreateCompatibleBitmap(sdc, W, H);
            IntPtr old = SelectObject(mdc, bmp);
            try
            {
                SetStretchBltMode(mdc, HALFTONE);
                if (!StretchBlt(mdc, 0, 0, W, H, sdc, rect.Left, rect.Top, rect.Width, rect.Height, SRCCOPY)) return;

                double sum = 0, sum2 = 0; int n = 0;
                double csum = 0, csum2 = 0; int cn = 0;
                for (int x = 0; x < W; x++)
                {
                    double fx = (x + 0.5) / W;
                    bool isCenter = fx >= cl && fx < cr;
                    for (int y = 0; y < H; y++)
                    {
                        uint p = GetPixel(mdc, x, y);
                        double l = (0.2126 * (p & 0xFF) + 0.7152 * ((p >> 8) & 0xFF) + 0.0722 * ((p >> 16) & 0xFF)) / 255.0;
                        if (isCenter) { csum += l; csum2 += l * l; cn++; }
                        else { sum += l; sum2 += l * l; n++; }
                    }
                }
                if (n > 0)
                {
                    mean = sum / n;
                    std = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
                }
                if (cn > 0)
                {
                    cMean = csum / cn;
                    cStd = Math.Sqrt(Math.Max(0, csum2 / cn - cMean * cMean));
                }
            }
            finally
            {
                SelectObject(mdc, old);
                DeleteObject(bmp);
                DeleteDC(mdc);
                ReleaseDC(IntPtr.Zero, sdc);
            }
        }

        private static int ColorDiff(uint a, uint b)
        {
            return Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF))
                 + Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF))
                 + Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF));
        }

        /// <summary>
        /// Porcentaje de filas donde hay un corte vertical fuerte en la columna x
        /// (comparado con la variacion normal a cada lado).
        /// </summary>
        private static double EdgeHitRatio(int x, int top, int height)
        {
            const int W = 24;
            IntPtr sdc = GetDC(IntPtr.Zero);
            IntPtr mdc = CreateCompatibleDC(sdc);
            IntPtr bmp = CreateCompatibleBitmap(sdc, W, height);
            IntPtr old = SelectObject(mdc, bmp);
            try
            {
                BitBlt(mdc, 0, 0, W, height, sdc, x - W / 2, top, SRCCOPY);
                int rows = 0, hits = 0;
                for (int y = 4; y < height - 4; y += 6)
                {
                    rows++;
                    uint l2 = GetPixel(mdc, 3, y);
                    uint l1 = GetPixel(mdc, 9, y);
                    uint r1 = GetPixel(mdc, 15, y);
                    uint r2 = GetPixel(mdc, 21, y);
                    int e = ColorDiff(l1, r1);
                    int rf = Math.Max(ColorDiff(l2, l1), ColorDiff(r1, r2));
                    if (e > 30 && e > 2 * rf + 10) hits++;
                }
                return rows > 0 ? (double)hits / rows : 0;
            }
            finally
            {
                SelectObject(mdc, old);
                DeleteObject(bmp);
                DeleteDC(mdc);
                ReleaseDC(IntPtr.Zero, sdc);
            }
        }

        private void SetSidesOnly(bool enable)
        {
            _settings.SidesOnly = enable;
            _settings.Save();
            _tintDirty = true;
            UpdateTintGeometry(Width, Height);
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int percent = (int)Math.Round(e.NewValue);
            if (percent > 90) percent = 90;
            if (_controlBar?.OpacityText != null)
            {
                _controlBar.OpacityText.Text = $"{percent}%";
            }

            if (!_settings.AdaptiveFlash) { _sidesCur = percent / 100.0; ApplyBrushes(); }

            _settings.Opacity = percent;
            _settings.Save();
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingVolumeFromCode) return;

            int percent = (int)Math.Round(e.NewValue);
            if (_controlBar?.VolumeText != null)
            {
                _controlBar.VolumeText.Text = $"{percent}%";
            }

            _savedVolume = (float)(percent / 100.0);
            _settings.Volume = percent;
            _settings.Save();

            // If game is currently in focus (not muted by focus loss), update volume immediately
            if (!_isMutedByFocusLoss && _targetProcess != null && !_targetProcess.HasExited)
            {
                AudioManager.SetApplicationVolume(_targetProcess.Id, _savedVolume);
            }
        }

        private void OnMuteSettingChanged(bool enable)
        {
            _settings.MuteOnFocusLoss = enable;
            _settings.Save();

            // If user unchecks mute while the game was muted by focus loss, restore volume immediately
            if (!enable && _isMutedByFocusLoss && _targetProcess != null && !_targetProcess.HasExited)
            {
                AudioManager.SetApplicationVolume(_targetProcess.Id, _savedVolume);
                _isMutedByFocusLoss = false;
            }
        }

        private void OnAutostartSettingChanged(bool enable)
        {
            StartupManager.SetAutostart(enable);
        }

        private void DetachFromGame()
        {
            // Always restore volume before detaching so the game is not left muted at 0%
            if (_targetProcess != null && !_targetProcess.HasExited)
            {
                AudioManager.SetApplicationVolume(_targetProcess.Id, _savedVolume);
            }
            _isMutedByFocusLoss = false;

            if (Visibility != Visibility.Hidden)
            {
                Visibility = Visibility.Hidden;
            }
            if (_controlBar != null && _controlBar.Visibility != Visibility.Hidden)
            {
                _controlBar.Visibility = Visibility.Hidden;
            }

            _targetProcess?.Dispose();
            _targetProcess = null;
            _targetHwnd = IntPtr.Zero;

            // Stop tracking loop and resume 15s detection loop
            _trackingTimer.Stop();
            _detectionTimer.Start();
        }

        private static Process? FindTargetProcess()
        {
            var processes = Process.GetProcessesByName("UmamusumePrettyDerby");
            if (processes.Length > 0)
            {
                return processes[0];
            }

            return Process.GetProcesses().FirstOrDefault(p =>
                p.ProcessName.Equals("UmamusumePrettyDerby", StringComparison.OrdinalIgnoreCase) ||
                p.ProcessName.Contains("Umamusume", StringComparison.OrdinalIgnoreCase));
        }

        private static IntPtr GetTargetWindowHandle(Process process)
        {
            try
            {
                process.Refresh();
                if (process.HasExited) return IntPtr.Zero;

                IntPtr handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero && IsWindow(handle))
                {
                    return handle;
                }

                // Fallback: search for top-level visible window belonging to process
                IntPtr found = IntPtr.Zero;
                EnumWindows((hWnd, lParam) =>
                {
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid == process.Id && IsWindowVisible(hWnd))
                    {
                        GetWindowRect(hWnd, out RECT r);
                        if (r.Width > 100 && r.Height > 100)
                        {
                            found = hWnd;
                            return false; // Stop searching
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                return found;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static bool TryGetWindowBounds(IntPtr hWnd, out RECT rect)
        {
            rect = default;
            if (!IsWindow(hWnd) || !IsWindowVisible(hWnd) || IsIconic(hWnd))
            {
                return false;
            }

            // Prefer DWM extended frame bounds (excludes invisible borders in Windows 10/11)
            int hr = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>());
            if (hr != 0)
            {
                if (!GetWindowRect(hWnd, out rect))
                {
                    return false;
                }
            }

            // Verify window is on screen and has valid dimensions
            return rect.Left > -30000 && rect.Top > -30000 && rect.Width > 0 && rect.Height > 0;
        }
    }

    #region Audio COM Declarations and AudioManager

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject
    {
    }

    internal enum EDataFlow
    {
        eRender,
        eCapture,
        eAll
    }

    internal enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int pcDevices);
        [PreserveSig] int Item(int nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(ref Guid AudioSessionGuid, int StreamFlags, out IntPtr AudioSessionControl);
        [PreserveSig] int GetSimpleAudioVolume(ref Guid AudioSessionGuid, int StreamFlags, out IntPtr AudioSessionControl);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
        [PreserveSig] int RegisterSessionNotification(IntPtr NewNotifications);
        [PreserveSig] int UnregisterSessionNotification(IntPtr NewNotifications);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionID, IntPtr duckNotification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int SessionCount);
        [PreserveSig] int GetSession(int SessionIndex, out IntPtr Session);
    }

    [ComImport]
    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        // IAudioSessionControl methods (0 - 8)
        [PreserveSig] int GetState(out int pRetVal);
        [PreserveSig] int GetDisplayName(out IntPtr pRetVal);
        [PreserveSig] int SetDisplayName(IntPtr Value, ref Guid EventContext);
        [PreserveSig] int GetIconPath(out IntPtr pRetVal);
        [PreserveSig] int SetIconPath(IntPtr Value, ref Guid EventContext);
        [PreserveSig] int GetGroupingParam(out Guid pRetVal);
        [PreserveSig] int SetGroupingParam(ref Guid Override, ref Guid EventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr NewNotifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr NewNotifications);

        // IAudioSessionControl2 methods (9 - 13)
        [PreserveSig] int GetSessionIdentifier(out IntPtr pRetVal);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr pRetVal);
        [PreserveSig] int GetProcessId(out uint pRetVal);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float fLevel, ref Guid EventContext);
        [PreserveSig] int GetMasterVolume(out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid EventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    internal static class AudioManager
    {
        private const int CLSCTX_ALL = 0x17;

        public static float? GetApplicationVolume(int processId)
        {
            var volumes = GetAudioVolumesForProcess((uint)processId);
            if (volumes.Count > 0)
            {
                if (volumes[0].GetMasterVolume(out float vol) == 0)
                {
                    return vol;
                }
            }
            return null;
        }

        public static void SetApplicationVolume(int processId, float volume)
        {
            var volumes = GetAudioVolumesForProcess((uint)processId);
            Guid empty = Guid.Empty;
            foreach (var vol in volumes)
            {
                vol.SetMasterVolume(volume, ref empty);
                vol.SetMute(volume <= 0.001f, ref empty);
            }
        }

        private static List<ISimpleAudioVolume> GetAudioVolumesForProcess(uint processId)
        {
            var result = new List<ISimpleAudioVolume>();
            try
            {
                var enumerator = new MMDeviceEnumeratorComObject() as IMMDeviceEnumerator;
                if (enumerator == null) return result;

                var devices = new List<IMMDevice>();

                // 1. Check default audio device
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice defaultDev) == 0 && defaultDev != null)
                {
                    devices.Add(defaultDev);
                }

                // 2. Also check all active audio render endpoints (e.g. headsets, virtual cables)
                if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1 /* DEVICE_STATE_ACTIVE */, out IMMDeviceCollection collection) == 0 && collection != null)
                {
                    if (collection.GetCount(out int devCount) == 0)
                    {
                        for (int d = 0; d < devCount; d++)
                        {
                            if (collection.Item(d, out IMMDevice dev) == 0 && dev != null)
                            {
                                devices.Add(dev);
                            }
                        }
                    }
                }

                Guid iidSessionManager2 = typeof(IAudioSessionManager2).GUID;

                foreach (var device in devices)
                {
                    if (device.Activate(ref iidSessionManager2, CLSCTX_ALL, IntPtr.Zero, out object sessionManagerObj) != 0 || sessionManagerObj == null)
                    {
                        continue;
                    }

                    var sessionManager = sessionManagerObj as IAudioSessionManager2;
                    if (sessionManager == null) continue;

                    if (sessionManager.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator) != 0 || sessionEnumerator == null)
                    {
                        continue;
                    }

                    if (sessionEnumerator.GetCount(out int count) != 0)
                    {
                        continue;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        if (sessionEnumerator.GetSession(i, out IntPtr pSession) == 0 && pSession != IntPtr.Zero)
                        {
                            try
                            {
                                object sessionObj = Marshal.GetObjectForIUnknown(pSession);
                                if (sessionObj is IAudioSessionControl2 control2)
                                {
                                    if (control2.GetProcessId(out uint pid) == 0 && pid == processId)
                                    {
                                        if (sessionObj is ISimpleAudioVolume volume)
                                        {
                                            result.Add(volume);
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                Marshal.Release(pSession);
                            }
                        }
                    }

                    if (result.Count > 0)
                    {
                        break;
                    }
                }
            }
            catch
            {
                // Audio session or endpoint unavailable
            }
            return result;
        }
    }

    #endregion

    #region ControlBarWindow

    public class ControlBarWindow : Window
    {
        public Slider OpacitySlider { get; }
        public TextBlock OpacityText { get; }
        public Slider VolumeSlider { get; }
        public TextBlock VolumeText { get; }
        public CheckBox MuteCheckbox { get; }
        public CheckBox AutostartCheckbox { get; }
        public CheckBox SidesOnlyCheckbox { get; }
        public event Action? HideRequested;
        public event Action? ColorCycleRequested;
        public Slider CenterSlider { get; }
        public TextBlock CenterText { get; }
        public CheckBox AdaptiveCheckbox { get; }
        public Slider StrengthSlider { get; }
        public TextBlock StrengthText { get; }
        private readonly TextBlock _colorText;

        public void SetColorName(string name)
        {
            _colorText.Text = "Color: " + name + "  \u25B8";
        }

        private readonly Border _mainBorder;
        private readonly StackPanel _rootStack;
        private readonly StackPanel _optionsPanel;
        private bool _isExpanded = false;

        public ControlBarWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Focusable = false;

            try
            {
                Icon = BitmapFrame.Create(new Uri("pack://application:,,,/assets/app.ico"));
            }
            catch { }

            Height = 28;

            _mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xEA, 0x1E, 0x1E, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 4),
                Height = 28,
                SnapsToDevicePixels = true,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 2,
                    Opacity = 0.5,
                    Color = Colors.Black
                }
            };

            _rootStack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            // Top Row: Fixed height so it is consistently compact from the very start
            var topRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Height = 22
            };

            // Opacity Section
            var moonIcon = new TextBlock
            {
                Text = "\U0001F319",
                FontSize = 11,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            OpacitySlider = new Slider
            {
                Width = 55,
                Height = 18,
                Minimum = 0,
                Maximum = 90,
                Value = 40,
                SmallChange = 1,
                LargeChange = 5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            OpacityText = new TextBlock
            {
                Text = "40%",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Width = 28,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            // Separator
            var separator = new System.Windows.Shapes.Rectangle
            {
                Width = 1,
                Height = 12,
                Fill = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            // Volume Section
            var speakerIcon = new TextBlock
            {
                Text = "\U0001F50A",
                FontSize = 11,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            VolumeSlider = new Slider
            {
                Width = 55,
                Height = 18,
                Minimum = 0,
                Maximum = 100,
                Value = 100,
                SmallChange = 1,
                LargeChange = 5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            VolumeText = new TextBlock
            {
                Text = "100%",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Width = 30,
                VerticalAlignment = VerticalAlignment.Center
            };

            topRow.Children.Add(moonIcon);
            topRow.Children.Add(OpacitySlider);
            topRow.Children.Add(OpacityText);
            topRow.Children.Add(separator);
            topRow.Children.Add(speakerIcon);
            topRow.Children.Add(VolumeSlider);
            topRow.Children.Add(VolumeText);

            var hideButton = new TextBlock
            {
                Text = "\u2014",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Ocultar en la bandeja"
            };
            hideButton.MouseLeftButtonUp += (s, e) =>
            {
                HideRequested?.Invoke();
                e.Handled = true;
            };
            topRow.Children.Add(hideButton);

            // Expandable Options Panel (attached dynamically on expansion)
            _optionsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(2, 4, 2, 2)
            };

            var divider = new System.Windows.Shapes.Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(0, 3, 0, 5)
            };

            MuteCheckbox = new CheckBox
            {
                Content = "Silenciar al perder el foco",
                Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                FontSize = 11,
                Margin = new Thickness(2, 2, 2, 3),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            AutostartCheckbox = new CheckBox
            {
                Content = "Iniciar con Windows",
                Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                FontSize = 11,
                Margin = new Thickness(2, 2, 2, 2),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            _optionsPanel.Children.Add(divider);
            _optionsPanel.Children.Add(MuteCheckbox);
            _optionsPanel.Children.Add(AutostartCheckbox);

            SidesOnlyCheckbox = new CheckBox
            {
                Content = "Solo oscurecer laterales (Ctrl+Alt+D)",
                Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                FontSize = 11,
                Margin = new Thickness(2, 2, 2, 2),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _optionsPanel.Children.Add(SidesOnlyCheckbox);

            var optFg = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

            // Nivel del centro
            var centerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 5, 2, 2) };
            centerRow.Children.Add(new TextBlock { Text = "Centro", FontSize = 11, Foreground = optFg, Width = 70, VerticalAlignment = VerticalAlignment.Center });
            CenterSlider = new Slider { Width = 90, Height = 18, Minimum = 0, Maximum = 90, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            CenterText = new TextBlock { Text = "0%", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Width = 30, VerticalAlignment = VerticalAlignment.Center };
            centerRow.Children.Add(CenterSlider);
            centerRow.Children.Add(CenterText);
            _optionsPanel.Children.Add(centerRow);

            // Suavizar destellos
            AdaptiveCheckbox = new CheckBox
            {
                Content = "Suavizar destellos",
                Foreground = optFg,
                FontSize = 11,
                Margin = new Thickness(2, 5, 2, 2),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _optionsPanel.Children.Add(AdaptiveCheckbox);

            var strengthRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 2, 2) };
            strengthRow.Children.Add(new TextBlock { Text = "Intensidad", FontSize = 11, Foreground = optFg, Width = 70, VerticalAlignment = VerticalAlignment.Center });
            StrengthSlider = new Slider { Width = 90, Height = 18, Minimum = 0, Maximum = 50, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            StrengthText = new TextBlock { Text = "25%", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Width = 30, VerticalAlignment = VerticalAlignment.Center };
            strengthRow.Children.Add(StrengthSlider);
            strengthRow.Children.Add(StrengthText);
            _optionsPanel.Children.Add(strengthRow);

            // Color (clic para cambiar)
            _colorText = new TextBlock
            {
                Text = "Color: Negro  \u25B8",
                FontSize = 11,
                Foreground = optFg,
                Margin = new Thickness(2, 5, 2, 2),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Clic para cambiar el color del tinte"
            };
            _colorText.MouseLeftButtonUp += (s, e) =>
            {
                ColorCycleRequested?.Invoke();
                e.Handled = true;
            };
            _optionsPanel.Children.Add(_colorText);

            _optionsPanel.Children.Add(new TextBlock
            {
                Text = "Ctrl+Alt+\u2191/\u2193 laterales \u00B7 +Shift centro \u00B7 Ctrl+Alt+H pausa",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                Margin = new Thickness(2, 6, 2, 1)
            });


            // ONLY topRow is added initially! _optionsPanel is added dynamically when expanded
            _rootStack.Children.Add(topRow);

            _mainBorder.Child = _rootStack;
            Content = _mainBorder;

            // Right-click expands / collapses the settings panel
            MouseRightButtonUp += (s, e) =>
            {
                ToggleExpanded();
                e.Handled = true;
            };
        }

        public void ToggleExpanded()
        {
            _isExpanded = !_isExpanded;
            if (_isExpanded)
            {
                if (!_rootStack.Children.Contains(_optionsPanel))
                {
                    _rootStack.Children.Add(_optionsPanel);
                }
                _mainBorder.Padding = new Thickness(8, 3, 8, 4);
                _mainBorder.Height = double.NaN;
                Height = double.NaN;
            }
            else
            {
                _rootStack.Children.Remove(_optionsPanel);
                _mainBorder.Padding = new Thickness(8, 2, 8, 4);
                _mainBorder.Height = 28;
                Height = 28;
            }
            SizeToContent = SizeToContent.WidthAndHeight;
            UpdateLayout();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong32(hwnd, -20);
            // Add WS_EX_TOOLWINDOW (0x00000080) so it does not appear in Alt-Tab or taskbar
            SetWindowLong32(hwnd, -20, exStyle | 0x00000080);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
    }

    #endregion

    #region AppSettings

    public class AppSettings
    {
        public bool MuteOnFocusLoss { get; set; } = true;
        public int Opacity { get; set; } = 40;
        public int Volume { get; set; } = 100;

        // Modo "solo laterales": agujero sin oscurecer, en % del tamano de la ventana del juego.
        // Valores medidos en 2560x1440 (vista del juego: x 197..1277, alto completo).
        public bool SidesOnly { get; set; } = false;
        public bool ShowControlBar { get; set; } = false;
        public double HoleLeftPct { get; set; } = 7.7;
        public double HoleTopPct { get; set; } = 0.0;
        public double HoleWidthPct { get; set; } = 42.2;
        public double HoleHeightPct { get; set; } = 100.0;

        // Deteccion automatica de pantalla completa
        public double SplitEdgeThreshold { get; set; } = 0.12;
        public double UniformStdThreshold { get; set; } = 0.05;
        public bool DebugDetect { get; set; } = false;

        // Mejoras
        public int CenterOpacity { get; set; } = 0;
        public bool AdaptiveFlash { get; set; } = true;
        public int AdaptiveStrength { get; set; } = 25;
        public int TintColor { get; set; } = 0;

        private static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UmaNight",
            "settings.json"
        );

        public static AppSettings Load()
        {
            try
            {
                string path = SettingsFilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch { }
            return new AppSettings();
        }

        private readonly object _saveLock = new object();
        private System.Threading.Timer? _saveTimer;

        /// <summary>Programa un guardado en 400 ms (agrupa cambios seguidos).</summary>
        public void Save()
        {
            lock (_saveLock)
            {
                _saveTimer ??= new System.Threading.Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
                _saveTimer.Change(400, Timeout.Infinite);
            }
        }

        public void SaveNow()
        {
            try
            {
                string path = SettingsFilePath;
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                lock (_saveLock)
                {
                    File.WriteAllText(path, json);
                }
            }
            catch { }
        }
    }

    #endregion

    #region StartupManager

    public static class StartupManager
    {
        private static string GetShortcutPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "UmaNight.lnk"
            );
        }

        public static string GetCurrentExecutablePath()
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) &&
                processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                return processPath;
            }

            string candidate = Path.Combine(AppContext.BaseDirectory, "UmaNight.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            return processPath ?? "";
        }

        public static bool IsAutostartEnabled()
        {
            return File.Exists(GetShortcutPath());
        }

        public static void SetAutostart(bool enable)
        {
            string shortcutPath = GetShortcutPath();
            if (enable)
            {
                string exePath = GetCurrentExecutablePath();
                if (!string.IsNullOrEmpty(exePath))
                {
                    CreateOrUpdateShortcut(shortcutPath, exePath);
                }
            }
            else
            {
                if (File.Exists(shortcutPath))
                {
                    try
                    {
                        File.Delete(shortcutPath);
                    }
                    catch { }
                }
            }
        }

        public static void RevalidateShortcut()
        {
            string shortcutPath = GetShortcutPath();
            if (!File.Exists(shortcutPath)) return;

            string exePath = GetCurrentExecutablePath();
            if (string.IsNullOrEmpty(exePath)) return;

            try
            {
                CreateOrUpdateShortcut(shortcutPath, exePath);
            }
            catch { }
        }

        private static void CreateOrUpdateShortcut(string shortcutPath, string exePath)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return;

                dynamic? shortcut = shell.CreateShortcut(shortcutPath);
                if (shortcut == null) return;

                string target = (string)shortcut.TargetPath;
                string dir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

                if (!string.Equals(target, exePath, StringComparison.OrdinalIgnoreCase))
                {
                    shortcut.TargetPath = exePath;
                    shortcut.WorkingDirectory = dir;
                    shortcut.Description = "Uma Night";
                    shortcut.Save();
                }
                else
                {
                    shortcut.WorkingDirectory = dir;
                    shortcut.Save();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupManager] Failed to create/update shortcut: {ex.Message}");
            }
        }
    }

    #endregion
}