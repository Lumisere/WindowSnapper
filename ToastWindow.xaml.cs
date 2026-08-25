using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WindowSnapper;

public partial class ToastWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    private readonly DispatcherTimer _dismissTimer;
    private bool _closing;
    private double _restingLeft;

    public ToastWindow(string title, string message, double scale)
    {
        InitializeComponent();

        var normalizedScale = Math.Clamp(double.IsFinite(scale) && scale > 0 ? scale : 1.0, 0.8, 2.0);
        Width = 344 * normalizedScale;
        Height = 82 * normalizedScale;
        TitleText.Text = title;
        FileNameText.Text = message;

        Opacity = 0;

        SourceInitialized += ToastWindow_SourceInitialized;
        Loaded += ToastWindow_Loaded;

        _dismissTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1900)
        };
        _dismissTimer.Tick += (_, _) => BeginDismiss();
    }

    private void ToastWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var currentStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        var newStyle = new IntPtr(currentStyle | WsExNoActivate | WsExToolWindow | WsExTransparent);
        SetWindowLongPtr(hwnd, GwlExStyle, newStyle);
    }

    private void ToastWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtTopRight();

        Left = _restingLeft + 18;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        BeginAnimation(LeftProperty, new DoubleAnimation(_restingLeft + 18, _restingLeft, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        _dismissTimer.Start();
    }

    private void PositionAtTopRight()
    {
        var workArea = SystemParameters.WorkArea;
        _restingLeft = Math.Max(workArea.Left + 12, workArea.Right - Width - 18);
        Left = _restingLeft;
        Top = workArea.Top + 18;
    }

    public void DismissImmediately()
    {
        _dismissTimer.Stop();
        _closing = true;
        Close();
    }

    private void BeginDismiss()
    {
        if (_closing) return;
        _closing = true;
        _dismissTimer.Stop();

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) => Close();

        BeginAnimation(OpacityProperty, fade);
        BeginAnimation(LeftProperty, new DoubleAnimation(_restingLeft, _restingLeft + 12, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _dismissTimer.Stop();
        base.OnClosed(e);
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, index)
            : new IntPtr(GetWindowLong32(hWnd, index));
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, index, newLong)
            : new IntPtr(SetWindowLong32(hWnd, index, newLong.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
