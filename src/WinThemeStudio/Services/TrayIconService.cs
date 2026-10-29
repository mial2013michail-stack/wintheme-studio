using System.Runtime.InteropServices;
using System.Windows.Interop;
using WinThemeStudio.Core;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;

namespace WinThemeStudio.Services;

public sealed class TrayIconService : IDisposable
{
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;
    private const uint CallbackMessage = 0x8000 + 41;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_NULL = 0x0000;
    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_CHECKED = 0x00000008;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly HwndSource _source;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly IReadOnlyList<ProfileInfo> _profiles;
    private NOTIFYICONDATA _data;
    private bool _undoAvailable = true;
    private bool _disposed;
    private string _activeProfile = "";

    public event EventHandler? ShowRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? UndoAllRequested;
    public event EventHandler<string>? ProfileRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(IReadOnlyList<ProfileInfo>? profiles = null)
    {
        _profiles = profiles ?? ProfileService.GetProfiles();
        _icon = NativeMethods.ExtractAppIconHandle();
        if (_icon == IntPtr.Zero)
        {
            _icon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
            _ownsIcon = false;
        }
        else
        {
            _ownsIcon = true;
        }

        // A regular hidden top-level window. Message-only windows (HWND_MESSAGE)
        // cannot own a tray context menu reliably, so do not use ParentWindow -3.
        var parameters = new HwndSourceParameters("WinThemeStudioTray")
        {
            Width = 1,
            Height = 1,
            WindowStyle = 0,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = "WinTheme Studio",
            szInfo = "",
            szInfoTitle = "",
            guidItem = Guid.Empty
        };

        AddIcon();
    }

    public void SetUndoAvailable(bool available)
    {
        _undoAvailable = available;
    }

    public void SetActiveProfile(string? key)
    {
        _activeProfile = key ?? "";
    }

    public void ShowBalloon(string title, string message)
    {
        if (_disposed)
        {
            return;
        }

        var data = _data;
        data.uFlags = NifInfo;
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = message.Length > 255 ? message[..255] : message;
        data.dwInfoFlags = NiifInfo;
        data.uTimeoutOrVersion = 8000;
        Shell_NotifyIcon(NimModify, ref data);
    }

    private void AddIcon()
    {
        if (_disposed)
        {
            return;
        }

        var data = _data;
        data.hWnd = _source.Handle;
        data.uFlags = NifMessage | NifIcon | NifTip;
        if (!Shell_NotifyIcon(NimAdd, ref data))
        {
            Logger.Warn("Не удалось добавить значок в системный трей.");
            return;
        }

        var version = data;
        version.uFlags = 0;
        version.uTimeoutOrVersion = 4;
        Shell_NotifyIcon(NimSetVersion, ref version);
        _data = data;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated)
        {
            // Explorer restarted and forgot every tray icon; add ours again.
            AddIcon();
            handled = true;
            return IntPtr.Zero;
        }

        if ((uint)msg != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        var mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
        if (mouseMessage == WM_LBUTTONDBLCLK)
        {
            ShowRequested?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (mouseMessage is WM_RBUTTONUP or WM_CONTEXTMENU)
        {
            ShowMenu();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MF_STRING, 1, "Открыть WinTheme Studio");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING | MF_GRAYED, 0, "Профиль");
            for (var i = 0; i < _profiles.Count; i++)
            {
                var profile = _profiles[i];
                var flags = MF_STRING;
                if (string.Equals(profile.Key, _activeProfile, StringComparison.OrdinalIgnoreCase))
                {
                    flags |= MF_CHECKED;
                }

                AppendMenu(menu, flags, (uint)(100 + i), profile.Name);
            }

            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING | (_undoAvailable ? 0 : MF_GRAYED), 2, "Отменить последнее изменение");
            AppendMenu(menu, MF_STRING | (_undoAvailable ? 0 : MF_GRAYED), 4, "Отменить всё");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, 3, "Выход");

            var (x, y) = NativeMethods.GetCursorScreenPosition();
            SetForegroundWindow(_source.Handle);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_LEFTALIGN, x, y,
                _source.Handle, IntPtr.Zero);
            PostMessage(_source.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case 1:
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case 2:
                    if (_undoAvailable)
                    {
                        UndoRequested?.Invoke(this, EventArgs.Empty);
                    }

                    break;
                case 3:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case 4:
                    if (_undoAvailable)
                    {
                        UndoAllRequested?.Invoke(this, EventArgs.Empty);
                    }

                    break;
                default:
                    if (command >= 100 && command < 100 + _profiles.Count)
                    {
                        ProfileRequested?.Invoke(this, _profiles[(int)command - 100].Key);
                    }

                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var data = _data;
        Shell_NotifyIcon(NimDelete, ref data);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        if (_ownsIcon)
        {
            NativeMethods.DestroyIconHandle(_icon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
