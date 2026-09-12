using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace WindowSnapper.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    public const string DefaultCaptureNow = "Ctrl+Shift+S";
    public const string DefaultToggleCapture = "Ctrl+Shift+P";

    private readonly Action _captureNow;
    private readonly Action _toggleCapture;
    private readonly HotkeyGesture _captureGesture;
    private readonly HotkeyGesture _toggleGesture;
    private Thread? _nativeThread;
    private readonly ManualResetEventSlim _nativeReady = new(false);
    private volatile bool _registered;
    private volatile bool _stopping;
    private Process? _portalHelper;
#if WINDOWS
    private uint _windowsThreadId;
#endif

    public string LastError { get; private set; } = string.Empty;
    public string BackendName { get; private set; } = string.Empty;

    public GlobalHotkeyService(
        string captureNowHotkey,
        string toggleCaptureHotkey,
        Action captureNow,
        Action toggleCapture)
    {
        if (!HotkeyGesture.TryParse(captureNowHotkey, out _captureGesture, out var captureError))
            throw new ArgumentException(captureError, nameof(captureNowHotkey));
        if (!HotkeyGesture.TryParse(toggleCaptureHotkey, out _toggleGesture, out var toggleError))
            throw new ArgumentException(toggleError, nameof(toggleCaptureHotkey));
        if (_captureGesture == _toggleGesture)
            throw new ArgumentException("Capture-now and start/stop hotkeys must be different.");

        _captureNow = captureNow;
        _toggleCapture = toggleCapture;
    }

    public async Task<bool> StartAsync()
    {
        if (_registered)
            return true;

        _stopping = false;
        LastError = string.Empty;

#if WINDOWS
        BackendName = "Windows RegisterHotKey";
        return StartWindows();
#else
        if (!OperatingSystem.IsLinux())
        {
            LastError = "Global hotkeys are currently supported on Windows and Linux.";
            return false;
        }

        if (PlatformInfo.IsWayland)
        {
            BackendName = "XDG Global Shortcuts portal";
            if (await StartWaylandPortalAsync())
                return true;

            // X11 saves our ass in XWayland. Native Wayland wants the portal because one hotkey system would apparently be too easy.
            if (!PlatformInfo.HasX11)
                return false;
        }

        BackendName = "X11 XGrabKey";
        return StartX11();
#endif
    }

    public void Dispose()
    {
        _stopping = true;
        _registered = false;

        var helper = Interlocked.Exchange(ref _portalHelper, null);
        if (helper is not null)
        {
            TryKill(helper);
            helper.Dispose();
        }

#if WINDOWS
        var windowsThreadId = Volatile.Read(ref _windowsThreadId);
        if (windowsThreadId != 0)
            PostThreadMessage(windowsThreadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
#endif

        var thread = Interlocked.Exchange(ref _nativeThread, null);
        if (thread is { IsAlive: true })
            thread.Join(TimeSpan.FromMilliseconds(750));
    }

#if WINDOWS
    private bool StartWindows()
    {
        if (_nativeThread is not null)
            return _registered;

        _nativeReady.Reset();
        _nativeThread = new Thread(WindowsHotkeyThread)
        {
            IsBackground = true,
            Name = "WindowSnapper Windows global hotkeys"
        };
        _nativeThread.Start();
        if (!_nativeReady.Wait(TimeSpan.FromSeconds(1)))
        {
            LastError = "Timed out while registering Windows global hotkeys.";
            _stopping = true;
            var threadId = Volatile.Read(ref _windowsThreadId);
            if (threadId != 0)
                PostThreadMessage(threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            return false;
        }
        return _registered;
    }

    private void WindowsHotkeyThread()
    {
        _windowsThreadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

        var first = RegisterHotKey(IntPtr.Zero, 1, WindowsModifiers(_captureGesture), _captureGesture.WindowsVirtualKey);
        var second = first && RegisterHotKey(IntPtr.Zero, 2, WindowsModifiers(_toggleGesture), _toggleGesture.WindowsVirtualKey);
        _registered = first && second && !_stopping;
        if (!_registered)
            LastError = "Windows could not register one or both shortcuts. Another application may already be using them.";
        _nativeReady.Set();

        if (!_registered)
        {
            if (first) UnregisterHotKey(IntPtr.Zero, 1);
            if (second) UnregisterHotKey(IntPtr.Zero, 2);
            _windowsThreadId = 0;
            return;
        }

        try
        {
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.message != WM_HOTKEY)
                    continue;

                if (message.wParam == (IntPtr)1)
                    Dispatcher.UIThread.Post(_captureNow);
                else if (message.wParam == (IntPtr)2)
                    Dispatcher.UIThread.Post(_toggleCapture);
            }
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, 1);
            UnregisterHotKey(IntPtr.Zero, 2);
            _windowsThreadId = 0;
            _registered = false;
        }
    }

    private static uint WindowsModifiers(HotkeyGesture gesture)
    {
        var result = MOD_NOREPEAT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control)) result |= MOD_CONTROL;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) result |= MOD_ALT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) result |= MOD_SHIFT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Meta)) result |= MOD_WIN;
        return result;
    }

    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max, uint removeMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
#else
    private async Task<bool> StartWaylandPortalAsync()
    {
        if (!PlatformInfo.CommandExists("python3"))
        {
            LastError = "Wayland global shortcuts need Python 3 plus python3-dbus/python3-gi for the XDG portal helper.";
            return false;
        }

        var helperPath = Path.Combine(AppContext.BaseDirectory, "Platforms", "Linux", "global_hotkey_helper.py");
        if (!File.Exists(helperPath))
        {
            LastError = $"Linux global-hotkey helper was not published: {helperPath}";
            return false;
        }

        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startInfo = new ProcessStartInfo
        {
            FileName = "python3",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add(_captureGesture.XdgTrigger);
        startInfo.ArgumentList.Add(_toggleGesture.XdgTrigger);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                LastError = e.Data.Trim();
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                LastError = "Could not start the Wayland global-hotkey helper.";
                return false;
            }
        }
        catch (Exception ex)
        {
            process.Dispose();
            LastError = $"Could not start the Wayland global-hotkey helper: {ex.Message}";
            return false;
        }

        process.BeginErrorReadLine();
        _portalHelper = process;
        _ = ReadPortalOutputAsync(process, ready);

        try
        {
            var registered = await ready.Task.WaitAsync(TimeSpan.FromSeconds(90));
            _registered = registered && !_stopping;
            if (!_registered && ReferenceEquals(_portalHelper, process))
            {
                _portalHelper = null;
                TryKill(process);
                process.Dispose();
            }
            return _registered;
        }
        catch (TimeoutException)
        {
            LastError = "Timed out while waiting for the desktop portal to bind global shortcuts.";
            if (ReferenceEquals(_portalHelper, process))
                _portalHelper = null;
            TryKill(process);
            process.Dispose();
            return false;
        }
    }

    private async Task ReadPortalOutputAsync(Process process, TaskCompletionSource<bool> ready)
    {
        try
        {
            while (!_stopping && !process.HasExited)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line is null)
                    break;

                if (line == "READY")
                {
                    ready.TrySetResult(true);
                }
                else if (line == "CAPTURE")
                {
                    Dispatcher.UIThread.Post(_captureNow);
                }
                else if (line == "TOGGLE")
                {
                    Dispatcher.UIThread.Post(_toggleCapture);
                }
                else if (line.StartsWith("ERR\t", StringComparison.Ordinal))
                {
                    LastError = line[4..];
                    ready.TrySetResult(false);
                }
            }
        }
        catch (Exception ex) when (!_stopping)
        {
            LastError = ex.Message;
            ready.TrySetResult(false);
        }
        finally
        {
            ready.TrySetResult(false);
            if (ReferenceEquals(_portalHelper, process))
                _registered = false;
        }
    }

    private bool StartX11()
    {
        if (!PlatformInfo.HasX11)
        {
            LastError = "No X11 display is available for global hotkeys.";
            return false;
        }

        _nativeReady.Reset();
        _nativeThread = new Thread(X11HotkeyThread)
        {
            IsBackground = true,
            Name = "WindowSnapper X11 global hotkeys"
        };
        _nativeThread.Start();
        if (!_nativeReady.Wait(TimeSpan.FromSeconds(2)))
        {
            LastError = "Timed out while registering X11 global hotkeys.";
            _stopping = true;
            return false;
        }
        return _registered;
    }

    private void X11HotkeyThread()
    {
        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                LastError = "Could not open the X11 display.";
                return;
            }

            var root = XDefaultRootWindow(display);
            var captureKeysym = XStringToKeysym(_captureGesture.X11KeysymName);
            var toggleKeysym = XStringToKeysym(_toggleGesture.X11KeysymName);
            var captureKeycode = XKeysymToKeycode(display, captureKeysym);
            var toggleKeycode = XKeysymToKeycode(display, toggleKeysym);
            if (captureKeycode == 0 || toggleKeycode == 0)
            {
                LastError = "X11 could not resolve one of the configured shortcut keys.";
                return;
            }

            var captureMask = X11Modifiers(_captureGesture);
            var toggleMask = X11Modifiers(_toggleGesture);
            _x11GrabError = false;
            _x11ErrorHandler = (_, errorPtr) =>
            {
                if (errorPtr != IntPtr.Zero)
                {
                    var error = Marshal.PtrToStructure<XErrorEvent>(errorPtr);
                    _x11GrabError = true;
                }
                return 0;
            };
            var handlerPtr = Marshal.GetFunctionPointerForDelegate(_x11ErrorHandler);
            var oldHandler = XSetErrorHandler(handlerPtr);

            GrabX11WithLockVariants(display, root, captureKeycode, captureMask);
            GrabX11WithLockVariants(display, root, toggleKeycode, toggleMask);
            XSync(display, false);
            XSetErrorHandler(oldHandler);

            if (_x11GrabError)
            {
                UngrabX11WithLockVariants(display, root, captureKeycode, captureMask);
                UngrabX11WithLockVariants(display, root, toggleKeycode, toggleMask);
                XSync(display, false);
                LastError = "X11 could not grab one or both shortcuts. Another application may already be using them.";
                return;
            }

            _registered = !_stopping;
            _nativeReady.Set();
            if (!_registered)
                return;

            while (!_stopping)
            {
                while (XPending(display) > 0)
                {
                    XNextEvent(display, out var xevent);
                    if (xevent.type != KeyPress)
                        continue;

                    var cleanState = xevent.xkey.state & ~(LockMask | Mod2Mask);
                    if (xevent.xkey.keycode == captureKeycode && cleanState == captureMask)
                        Dispatcher.UIThread.Post(_captureNow);
                    else if (xevent.xkey.keycode == toggleKeycode && cleanState == toggleMask)
                        Dispatcher.UIThread.Post(_toggleCapture);
                }

                Thread.Sleep(15);
            }

            UngrabX11WithLockVariants(display, root, captureKeycode, captureMask);
            UngrabX11WithLockVariants(display, root, toggleKeycode, toggleMask);
            XSync(display, false);
        }
        catch (DllNotFoundException)
        {
            LastError = "libX11 is not installed, so X11 global hotkeys are unavailable.";
        }
        catch (Exception ex)
        {
            LastError = $"X11 global-hotkey error: {ex.Message}";
        }
        finally
        {
            _registered = false;
            _nativeReady.Set();
            if (display != IntPtr.Zero)
                XCloseDisplay(display);
        }
    }

    private static uint X11Modifiers(HotkeyGesture gesture)
    {
        uint result = 0;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) result |= ShiftMask;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control)) result |= ControlMask;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) result |= Mod1Mask;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Meta)) result |= Mod4Mask;
        return result;
    }

    private static void GrabX11WithLockVariants(IntPtr display, IntPtr root, int keycode, uint mask)
    {
        foreach (var locks in LockVariants)
            XGrabKey(display, keycode, mask | locks, root, false, GrabModeAsync, GrabModeAsync);
    }

    private static void UngrabX11WithLockVariants(IntPtr display, IntPtr root, int keycode, uint mask)
    {
        foreach (var locks in LockVariants)
            XUngrabKey(display, keycode, mask | locks, root);
    }

    private static readonly uint[] LockVariants = { 0, LockMask, Mod2Mask, LockMask | Mod2Mask };
    private const int KeyPress = 2;
    private const int GrabModeAsync = 1;
    private const uint ShiftMask = 1u << 0;
    private const uint LockMask = 1u << 1;
    private const uint ControlMask = 1u << 2;
    private const uint Mod1Mask = 1u << 3;
    private const uint Mod2Mask = 1u << 4;
    private const uint Mod4Mask = 1u << 6;

    private static volatile bool _x11GrabError;
    private static XErrorHandler? _x11ErrorHandler;

    [StructLayout(LayoutKind.Sequential)]
    private struct XKeyEvent
    {
        public int type;
        public UIntPtr serial;
        public int send_event;
        public IntPtr display;
        public IntPtr window;
        public IntPtr root;
        public IntPtr subwindow;
        public UIntPtr time;
        public int x;
        public int y;
        public int x_root;
        public int y_root;
        public uint state;
        public uint keycode;
        public int same_screen;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(0)] public XKeyEvent xkey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XErrorEvent
    {
        public int type;
        public IntPtr display;
        public UIntPtr resourceid;
        public UIntPtr serial;
        public byte error_code;
        public byte request_code;
        public byte minor_code;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XStringToKeysym([MarshalAs(UnmanagedType.LPStr)] string value);

    [DllImport("libX11.so.6")]
    private static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);

    [DllImport("libX11.so.6")]
    private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, bool ownerEvents, int pointerMode, int keyboardMode);

    [DllImport("libX11.so.6")]
    private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);

    [DllImport("libX11.so.6")]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XNextEvent(IntPtr display, out XEvent xevent);

    [DllImport("libX11.so.6")]
    private static extern int XSync(IntPtr display, bool discard);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XSetErrorHandler(IntPtr handler);
#endif

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}
