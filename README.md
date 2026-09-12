# WindowSnapper

WindowSnapper is a cross-platform Avalonia utility for taking screenshots of a selected window once or on a schedule.

## What it does

- Select a window by process name, title, native window ID, or from the open-window list.
- Take one screenshot or keep capturing on a fixed interval.
- Use configurable global screenshot hotkeys. Defaults are **Ctrl+Shift+S** for capture once and **Ctrl+Shift+P** for start/stop. Windows uses `RegisterHotKey`; Linux uses native X11 grabs or the XDG Global Shortcuts portal on Wayland.
- Save PNG, JPEG, WebP, or AVIF with an editable filename prefix.
- Screenshots are always sorted into subfolders named after the filename prefix; this is automatic and has no Settings toggle/section.
- Optionally embed a bottom-left verification stamp that mirrors the live WindowSnapper toast: the same 344×82 sizing model, 5 px red rail, 11 px rounding, spacing, colors, toast-scale setting, and platform font family. The header is simply `WindowSnapper`; the second line is kept short as `ID … • V …`, so it fits the same single message row as a normal toast. Nothing is shown on the desktop.
- New watermarked captures use a compact 10-character Base32 ID (displayed 5-5) plus a short 10-character manual verification code. The larger whole-image proof is encoded as a tiny machine-readable dark pattern inside the toast's existing bottom padding instead of being printed as a long text string. It is part of the image pixels, not a sidecar, metadata field, or trailing file payload.
- Verification is either/or. **Choose image and verify…** reads the proof directly from the watermark pixels, so a Discord-downloaded image can be checked without typing an ID or code. The separate manual ID/code controls are only a fallback. Upload verification checks the complete decoded screenshot and an authenticated 384-bit spatial/color fingerprint spanning a 12×8 grid across the whole frame. The current proof format calibrates lossy captures against the selected codec itself and remains intentionally tight: at most 2 descriptor-bit differences are accepted, with at most 4 carrier-cell errors. v11-v13 WSVB captures and older v9/v10 captures retain an 8-bit compatibility tolerance. The Settings result is kept deliberately short: `Verified — bit difference: N` or `Altered / Not Verified — bit difference: N`.
- Optionally copy the latest screenshot to the clipboard. Standard outputs are decoded directly for speed, with an 8-bit sRGB PNG normalization fallback for codecs/layouts the platform decoder rejects.
- Show a topmost, click-through notification toast after successful screenshots.
- Adjust the main UI and Settings UI together with the interface-scale slider.
- Adjust toast size/duration and preview notifications from Settings.
- Use the bundled `notif.mp3` or select a custom audio file.
- Remember settings in the platform-local WindowSnapper settings file.

## Global hotkeys on Linux

On X11, WindowSnapper registers shortcuts directly with X11, so they work regardless of which application is focused. On Wayland, global shortcuts are registered through `org.freedesktop.portal.GlobalShortcuts`; the desktop may show a one-time permission/configuration dialog. Wayland support therefore requires a desktop portal backend that implements Global Shortcuts plus `python3`, `python3-dbus`, and `python3-gi`.

Hotkeys are configurable in Settings. Supported shortcuts use one or more of `Ctrl`, `Alt`, `Shift`, or `Super` plus a letter, number, F1-F24, arrow/navigation key, Space, Enter, Escape, or PrintScreen.

## Performance changes

- Windows Graphics Capture waits less time for an unusable/stalled frame before reporting/falling back.
- HDR/scRGB conversion uses lookup tables instead of expensive per-pixel power functions.
- Large HDR conversion buffers are reused through `ArrayPool` to reduce repeated allocations and GC pressure during long capture sessions.
- Windows bitmap serialization and ImageMagick encoding run off the UI thread.
- Scheduled captures use `PeriodicTimer`, so encoding/clipboard time does not get added to the configured screenshot interval. Starting a schedule waits one full configured interval before taking the first screenshot.
- AVIF uses the ImageMagick HEIC/AVIF encoder with a faster encoder speed and 4:4:4 chroma to keep UI/text screenshots crisp.

## Capture methods

**Auto** tries Windows Graphics Capture first on Windows, then falls back to PrintWindow, Portable window capture, and Screen Copy as needed. Linux/Wayland options depend on the current desktop/session and installed helpers.

**Windows Graphics Capture** is the preferred option for normal, hardware-accelerated, and borderless targets. Auto tries it first. True exclusive fullscreen is a different beast: WGC depends on desktop composition and is not guaranteed to see every exclusive swap chain.

**Exclusive fullscreen compatibility** is an experimental Windows setting and is off by default. When enabled, WindowSnapper never tries to guess whether a window is truly exclusive. Instead it treats the selected target as focus-sensitive: DXGI Desktop Duplication is tried first, WGC remains a passive fallback, Portable capture is skipped, taskbars are never shown/hidden for the frame, and visual capture/reminder toasts are suppressed. Normal Auto mode remains WGC-first when this option is off. Desktop Duplication is specifically designed to duplicate visible fullscreen DirectX output without activating the game. Protected/display-only content and some anti-cheat configurations can still block public capture APIs.


**PrintWindow** uses the Win32 `PrintWindow` API. It can work for windows that are partially covered, but some GPU-rendered content may return a blank frame.

**Screen Copy** copies visible pixels from the desktop, so the target must be visible.

Minimized windows, protected video, DRM content, and some anti-cheat protected surfaces may not be capturable.

## Audio

The default notification sound is `notif.mp3` in the application directory. Custom MP3, WAV, WMA, M4A/AAC, AIFF, and FLAC files can be selected from the UI. NAudio handles playback on Windows; supported formats can depend on installed codec support.

## Build

1. Open `WindowSnapper.sln` in Visual Studio 2022 or newer, or use a .NET 8 SDK from the command line.
2. Restore NuGet packages.
3. Build the solution.

The project targets .NET 8 and .NET 8 for Windows. The UI uses Avalonia; the Windows target also uses SharpDX for Windows Graphics Capture Direct3D interop and NAudio for notification audio.

For both self-contained x64 release builds, run one command from Windows or Linux:

```text
dotnet msbuild build-release.proj
```

One run publishes both targets, even when the build machine is Windows:

- `publish/WindowSnapper-win-x64.zip`
- `publish/WindowSnapper-linux-x64.zip`

The unpacked builds are also left in `publish/win-x64` and `publish/linux-x64`. If a ZIP created on Windows loses the Linux executable bit when extracted, run `chmod +x WindowSnapper` once on the Linux machine.
