using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace WindowSnapper;

public sealed partial class ToastWindow : Window
{
    private const double BaseWidth = 344;
    private const double BaseHeight = 82;
    private readonly DispatcherTimer _timer;

    public ToastWindow()
        : this("Notification", string.Empty, 1.0, 6.0)
    {
    }

    public ToastWindow(string title, string message, double scale, double durationSeconds)
    {
        InitializeComponent();

        scale = Math.Clamp(double.IsFinite(scale) && scale > 0 ? scale : 1, 0.8, 2.0);
        durationSeconds = Math.Clamp(double.IsFinite(durationSeconds) && durationSeconds > 0 ? durationSeconds : 6.0, 2.0, 15.0);

        Width = BaseWidth * scale;
        Height = BaseHeight * scale;
        TitleText.FontSize = 12.5 * scale;
        MessageText.FontSize = 10.5 * scale;
        TitleText.Text = title;
        MessageText.Text = message;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(durationSeconds) };
        _timer.Tick += (_, _) => Close();
        Opened += (_, _) => PositionToast();
        Closed += (_, _) => _timer.Stop();
    }

    public void StartAutoClose() => _timer.Start();

    private void PositionToast()
    {
        var screen = Screens.Primary;
        if (screen is null)
            return;

        var area = screen.WorkingArea;
        const int margin = 18;
        var physicalWidth = Width * screen.Scaling;
        Position = new PixelPoint(
            area.Right - (int)Math.Ceiling(physicalWidth) - margin,
            area.Y + margin);
    }
}
