# WindowSnapper

WindowSnapper is a small WPF utility for taking screenshots of a window on a timer.

## What it does

- Select a window by process name, title, HWND, or from the open-window list.
- Take one screenshot or keep capturing at a set interval.
- Run a separate notification-sound interval alongside screenshot capture.
- Save PNG or JPEG files with an editable filename prefix.
- Show a topmost, click-through toast after a successful screenshot.
- Adjust toast size and preview it from the main window.
- Use the bundled `notif.mp3` or select a custom audio file.
- Remember settings in `%LOCALAPPDATA%\WindowSnapper\settings.json`.

## Capture methods

**Auto** tries Windows Graphics Capture first, then PrintWindow, DXGI Desktop Duplication, and Screen Copy.

**Windows Graphics Capture** is the preferred option for most normal and hardware-accelerated windows.

**DXGI Desktop Duplication** captures the visible desktop output and crops it to the target window. It works well for many borderless/fullscreen applications, but anything covering the target can appear in the result.

**PrintWindow** uses the Win32 `PrintWindow` API. It can work for windows that are partially covered, but some GPU-rendered content may return a blank frame. (Will fit most uses)

**Screen Copy** copies the visible pixels from the desktop, so the target must be visible.

Minimized windows, protected video, DRM content, and some anti-cheat protected surfaces may not be capturable.

## Audio

The default notification sound is `notif.mp3` in the application directory. Custom MP3, WAV, WMA, M4A/AAC, AIFF, and FLAC files can be selected from the UI. NAudio handles playback; formats that depend on Media Foundation require the matching Windows codec support.

## Build

1. Open `WindowSnapper.sln` in Visual Studio 2022 or newer.
2. Make sure the **.NET desktop development** workload is installed.
3. Restore NuGet packages.
4. Build the solution.

The project targets .NET 8 for Windows and uses WPF, SharpDX 4.2 for Direct3D/DXGI interop, and NAudio for notification audio.

For a self-contained x64 publish, run:

```powershell
.\build-release.ps1
```

The output is written to `publish\win-x64`.
