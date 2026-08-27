namespace WindowSnapper.Models;

public sealed record WindowInfo(
    nint Handle,
    string Title,
    string ProcessName,
    int ProcessId,
    string NativeId = "")
{
    public bool HasNativeId => !string.IsNullOrWhiteSpace(NativeId);

    public string Display
    {
        get
        {
            var app = string.IsNullOrWhiteSpace(ProcessName) ? "window" : ProcessName;
            var title = string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title;
            return $"{app} — {title}";
        }
    }

    public override string ToString() => Display;
}
