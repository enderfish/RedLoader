using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace RedLoader.Utils;

/// <summary>
///     Small loading window shown while RedLoader starts: a title, the latest log line and a progress bar.
///     Built only from Windows' own user32/comctl32/gdi32 controls (it replaces the native Splash.dll, which had no
///     published source). It runs on its own background thread; if anything about it fails, loading carries on
///     without it. Set <c>hide_status_window</c> in <c>UserData/_Redloader.cfg</c> to turn it off.
/// </summary>
public class SplashWindow
{
    public static int TotalProgressSteps = 100;

    private const int Width = 480;
    private const int Height = 112;

    private static Thread _thread;
    private static IntPtr _window;
    private static IntPtr _status;
    private static IntPtr _progress;
    private static WndProc _wndProc; // must stay referenced while the window exists

    private static volatile string _pendingStatus = "Starting RedLoader... (after a game update this can take about a minute)";
    private static volatile int _pendingProgress; // 0..1000
    private static volatile bool _closeRequested;
    private static string _shownStatus;

    public static void CreateWindow()
    {
        try
        {
            if (_thread != null || !OperatingSystem.IsWindows() || CorePreferences.HideStatusWindow.Value)
                return;

            _thread = new Thread(WindowThread) { IsBackground = true, Name = "RedLoader loading window" };
            _thread.Start();
        }
        catch (Exception e)
        {
            RLog.Warning($"Loading window unavailable: {e.Message}");
        }
    }

    public static void CloseWindow() => _closeRequested = true;

    public static void PrintToConsole(string str) => _pendingStatus = str;

    public static void SetProgress(float progress) => _pendingProgress = (int)(Math.Clamp(progress, 0f, 1f) * 1000);

    public static void SetProgressSteps(int step) => SetProgress(step / (float)TotalProgressSteps);

    public static void HookLog()
    {
        RLog.MsgDrawingCallbackHandler -= LogCallback;
        RLog.MsgDrawingCallbackHandler += LogCallback;
    }

    public static void UnhookLog()
    {
        RLog.MsgDrawingCallbackHandler -= LogCallback;
    }

    private static void LogCallback(Color namesectionColor, Color textColor, string namesection, string text)
    {
        var line = text?.Split('\n')[0] ?? "";
        _pendingStatus = string.IsNullOrEmpty(namesection) ? line : $"[{namesection}] {line}";
    }

    private static void WindowThread()
    {
        try
        {
            RunWindow();
        }
        catch (Exception e)
        {
            RLog.Warning($"Loading window unavailable: {e.Message}");
        }
    }

    private static void RunWindow()
    {
        var icc = new INITCOMMONCONTROLSEX { dwSize = Marshal.SizeOf<INITCOMMONCONTROLSEX>(), dwICC = ICC_PROGRESS_CLASS };
        InitCommonControlsEx(ref icc);

        var hInstance = GetModuleHandleW(null);
        _wndProc = WindowProc;
        var wc = new WNDCLASSEXW
        {
            cbSize = Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            hCursor = LoadCursorW(IntPtr.Zero, IDC_APPSTARTING),
            hbrBackground = (IntPtr)(COLOR_BTNFACE + 1),
            lpszClassName = ClassName,
        };
        if (RegisterClassExW(ref wc) == 0)
            throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastWin32Error()})");

        var x = (GetSystemMetrics(SM_CXSCREEN) - Width) / 2;
        var y = (GetSystemMetrics(SM_CYSCREEN) - Height) / 2;
        _window = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW, ClassName, "RedLoader", WS_POPUP | WS_BORDER,
                                  x, y, Width, Height, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_window == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()})");

        var font = GetStockObject(DEFAULT_GUI_FONT);
        var title = CreateChild("STATIC", "RedLoader: loading mods...", SS_LEFTNOWORDWRAP, 16, 14, Width - 32, 20, font, hInstance);
        _status = CreateChild("STATIC", "", SS_LEFTNOWORDWRAP | SS_ENDELLIPSIS, 16, 40, Width - 32, 20, font, hInstance);
        _progress = CreateChild("msctls_progress32", "", 0, 16, 70, Width - 32, 20, font, hInstance);
        if (title == IntPtr.Zero || _status == IntPtr.Zero || _progress == IntPtr.Zero)
            throw new InvalidOperationException("Creating the loading window controls failed");
        SendMessageW(_progress, PBM_SETRANGE32, IntPtr.Zero, (IntPtr)1000);

        Refresh();
        ShowWindow(_window, SW_SHOWNOACTIVATE);
        SetTimer(_window, (IntPtr)1, 100, IntPtr.Zero);

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    private static IntPtr CreateChild(string cls, string text, uint style, int x, int y, int w, int h, IntPtr font, IntPtr hInstance)
    {
        var child = CreateWindowExW(0, cls, text, WS_CHILD | WS_VISIBLE | style, x, y, w, h, _window, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (child != IntPtr.Zero)
            SendMessageW(child, WM_SETFONT, font, (IntPtr)1);
        return child;
    }

    // Runs on the window thread only; the loader threads just write the volatile fields above.
    private static void Refresh()
    {
        var status = _pendingStatus;
        if (!ReferenceEquals(status, _shownStatus))
        {
            SetWindowTextW(_status, status);
            _shownStatus = status;
        }

        SendMessageW(_progress, PBM_SETPOS, (IntPtr)_pendingProgress, IntPtr.Zero);
    }

    private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_TIMER:
                if (_closeRequested)
                    DestroyWindow(hWnd);
                else
                    Refresh();
                return IntPtr.Zero;
            case WM_DESTROY:
                KillTimer(hWnd, (IntPtr)1);
                PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    #region Win32

    private const string ClassName = "RedLoaderLoadingWindow";

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_BORDER = 0x00800000;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SS_LEFTNOWORDWRAP = 0x0000000C;
    private const uint SS_ENDELLIPSIS = 0x00004000;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_SETFONT = 0x0030;
    private const uint WM_TIMER = 0x0113;
    private const uint PBM_SETPOS = 0x0402;
    private const uint PBM_SETRANGE32 = 0x0406;
    private const uint ICC_PROGRESS_CLASS = 0x00000020;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int COLOR_BTNFACE = 15;
    private const int DEFAULT_GUI_FONT = 17;
    private static readonly IntPtr IDC_APPSTARTING = (IntPtr)32650;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INITCOMMONCONTROLSEX
    {
        public int dwSize;
        public uint dwICC;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y,
                                                 int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowTextW(IntPtr hWnd, string text);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr id, uint elapseMs, IntPtr timerProc);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hWnd, IntPtr id);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursorW(IntPtr instance, IntPtr cursorName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string moduleName);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int obj);

    [DllImport("comctl32.dll")]
    private static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    #endregion
}
