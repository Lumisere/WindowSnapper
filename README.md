# WindowSnapper

WindowSnapper is a small desktop screenshot utility for capturing a specific window manually or on a schedule. It started as something I wanted for unattended screenshots without having to keep messing with capture tools every few minutes.

It's built with **.NET 8** and **Avalonia**, with support for Windows and Linux.

## Features

- Capture a selected window once or on a timer
- Pick windows by process, title, window ID, or from the open-window list
- Windows Graphics Capture is preferred automatically on Windows
- PNG, JPEG, WebP, and AVIF output
- Configurable global hotkeys
- Optional clipboard copying and notification toasts
- Optional screenshot watermark and verification system
- Adjustable interface and notification settings
- Experimental exclusive-fullscreen compatibility mode for games
- Portable/self-contained release builds

## Capture methods

On Windows, **Auto** prefers Windows Graphics Capture and falls back when needed. Other capture methods are available if a particular app doesn't behave well with Auto.

There is also an optional **Exclusive fullscreen compatibility** mode. It's disabled by default for now and is mainly intended for games that don't play nicely with normal desktop capture. Some protected, DRM, or anti-cheat controlled content may still refuse to capture.

## Screenshot verification

WindowSnapper can optionally add a small verification watermark to screenshots. Watermarked images can later be checked from the settings menu to help tell whether the image has been changed after capture.

This is completely optional and can be disabled if you just want normal screenshots.

## Building

You'll need the **.NET 8 SDK**.

To build the project normally:

```powershell
dotnet build WindowSnapper.sln -c Release
```

To create the self-contained release builds and the `publish` folder:

```powershell
dotnet msbuild build-release.proj
```

The release script creates Windows and Linux x64 builds under `publish/`, along with ZIP files for distribution.

## Notes

If something breaks, feel free to open an issue with what app you were capturing, your OS, and which capture method you were using.

## License / third-party software

See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for third-party components used by the project.
