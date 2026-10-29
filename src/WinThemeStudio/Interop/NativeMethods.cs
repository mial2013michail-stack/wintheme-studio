using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WinThemeStudio.Interop;

public static class NativeMethods
{
    public const int WM_SETTINGCHANGE = 0x001A;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    public const uint SPI_SETDESKWALLPAPER = 0x0014;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;

    public const int SHCNE_ASSOCCHANGED = 0x08000000;
    public const int SHCNE_UPDATEDIR = 0x00001000;
    public const uint SHCNF_IDLIST = 0x0000;
    public const uint SHCNF_PATHW = 0x0005;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;

    private const uint SIIGBF_RESIZETOFIT = 0x00;
    private const uint SIIGBF_ICONONLY = 0x04;
    private const uint SIIGBF_BIGGERSIZEOK = 0x01;

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    private const int ACCENT_DISABLED = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, uint flags, out IntPtr phbm);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string? lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string? value, uint winIni);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref SHFILEINFO info,
        uint infoSize, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path,
        IntPtr bindingContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr largeIcon, out IntPtr smallIcon, uint count);

    public static void NotifySettingChange(string section)
    {
        try
        {
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, section,
                SMTO_ABORTIFHUNG, 1200, out _);
        }
        catch
        {
            // best effort
        }
    }

    public static void RefreshShellIcons()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrEmpty(desktop))
            {
                SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, desktop, IntPtr.Zero);
            }

            var ie4uinit = Path.Combine(Environment.SystemDirectory, "ie4uinit.exe");
            if (File.Exists(ie4uinit))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ie4uinit, "-show")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                });
            }
        }
        catch
        {
            // best effort
        }
    }

    public static void SetWallpaper(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
        else
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
    }

    public static void RefreshWallpaperFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", false);
            var path = key?.GetValue("Wallpaper") as string;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                SetWallpaper(path);
            }
            else
            {
                SetWallpaper(null);
            }
        }
        catch
        {
            // best effort
        }
    }

    public static void ApplyWindowChrome(Window window, bool darkTitleBar = true, bool rounded = true)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (darkTitleBar)
            {
                var dark = 1;
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }

            if (rounded)
            {
                var preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
        }
        catch
        {
            // best effort
        }
    }

    public static void MakeOverlayWindow(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(exStyle));
        }
        catch
        {
            // best effort
        }
    }

    public static void ApplyOverlayBackdrop(Window window, bool enabled)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var policy = new AccentPolicy
            {
                AccentState = enabled ? ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_DISABLED,
                AccentFlags = 2,
                GradientColor = unchecked((int)0xCC1A1512),
                AnimationId = 0
            };

            var size = Marshal.SizeOf<AccentPolicy>();
            var pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, pointer, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = pointer,
                    SizeOfData = size
                };
                var result = SetWindowCompositionAttribute(handle, ref data);
                if (result == 0 && enabled)
                {
                    policy.AccentState = ACCENT_ENABLE_BLURBEHIND;
                    Marshal.StructureToPtr(policy, pointer, false);
                    data.Data = pointer;
                    SetWindowCompositionAttribute(handle, ref data);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
        catch
        {
            // best effort
        }
    }

    public static BitmapSource? GetFileIcon(string path, int size = 96)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out var factory);
            try
            {
                var result = factory.GetImage(new SIZE { cx = size, cy = size },
                    SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK | SIIGBF_RESIZETOFIT, out var hBitmap);
                if (result == 0 && hBitmap != IntPtr.Zero)
                {
                    try
                    {
                        var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero,
                            Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        return source;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(factory);
            }
        }
        catch
        {
            // fall through to SHGetFileInfo
        }

        try
        {
            var info = new SHFILEINFO();
            var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    public static void AttachParentConsole()
    {
        try
        {
            AttachConsole(0xFFFFFFFF);
        }
        catch
        {
            // No parent console: the caller still writes to the log file.
        }
    }

    public static bool IsShellTaskbarPresent()
    {
        try
        {
            return FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    public static string GetForegroundWindowTitle()
    {
        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return "";
            }

            var builder = new StringBuilder(512);
            GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }
        catch
        {
            return "";
        }
    }

    public static bool IsForegroundFullscreen(IntPtr ownHandle)
    {
        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero || handle == ownHandle)
            {
                return false;
            }

            var classBuilder = new StringBuilder(128);
            GetClassName(handle, classBuilder, classBuilder.Capacity);
            var className = classBuilder.ToString();
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return false;
            }

            if (!GetWindowRect(handle, out var rect))
            {
                return false;
            }

            var monitor = MonitorFromWindow(handle, 2);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            var bounds = info.rcMonitor;
            // A borderless game is often WS_MAXIMIZE and still covers the whole
            // monitor, so the rectangle check is authoritative.
            return rect.Left <= bounds.Left + 1 &&
                   rect.Top <= bounds.Top + 1 &&
                   rect.Right >= bounds.Right - 1 &&
                   rect.Bottom >= bounds.Bottom - 1;
        }
        catch
        {
            return false;
        }
    }

    public static uint ShowWindowMessage { get; } = RegisterWindowMessage("WinThemeStudio.ShowWindow");

    public static bool ActivateExistingInstance()
    {
        try
        {
            var handle = FindWindow(null, "WinTheme Studio");
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            return PostMessage(handle, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            return false;
        }
    }

    public static Point GetCursorPosition()
    {
        try
        {
            return GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point(0, 0);
        }
        catch
        {
            return new Point(0, 0);
        }
    }

    public static (int X, int Y) GetCursorScreenPosition()
    {
        try
        {
            return GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    public static void ShowDesktop()
    {
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        keybd_event(0x44, 0, 0, UIntPtr.Zero);
        keybd_event(0x44, 0, 2, UIntPtr.Zero);
        keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }

    public static void OpenStartMenu()
    {
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }

    public static IntPtr ExtractAppIconHandle()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return IntPtr.Zero;
            }

            ExtractIconEx(exe, 0, out var large, out var small, 1);
            if (small != IntPtr.Zero)
            {
                DestroyIcon(small);
            }

            return large;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public static void DestroyIconHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            DestroyIcon(handle);
        }
    }
}
