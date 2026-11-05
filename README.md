# SnowApp

Lightweight Festive Desktop Snow & Window Physics for Windows 10 & 11.

![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?logo=windows)
![Framework](https://img.shields.io/badge/.NET-6.0-512BD4?logo=dotnet)
![Memory Footprint](https://img.shields.io/badge/RAM-~17.5%20MB-brightgreen)
![CPU Usage](https://img.shields.io/badge/CPU-%3C%201%25-brightgreen)
![License](https://img.shields.io/badge/License-MIT-blue)

A lightweight Windows desktop overlay that brings falling snowflakes, procedural snowdrifts, icicles, and festive decorations directly onto application windows and the taskbar.

---

## Features

- **Real-time Snow Accumulation:** Falling snowflakes naturally settle onto the top borders of open application windows and the Windows taskbar, forming smooth snowdrifts.
- **Procedural Window Decorations:** Windows randomly spawn frosted pine trees with golden stars and cheerful snowmen nestled in the snowbanks.
- **Crystal Icicles and Frost Glazing:** Delicate icicles grow beneath the snow layer along with a subtle rime glaze under window titlebars.
- **Physical Avalanche Cascades:** Moving windows, dragging them around, or selecting "Shake off snow" causes accumulated snow to cascade down the screen with realistic drag and gravity.
- **Smart Z-Order Occlusion:** Snowbanks and decorations submerge naturally behind foreground windows and automatically hide during fullscreen videos (YouTube, media players) and gaming.
- **Resource Efficient:** Built using native Win32 `UpdateLayeredWindow` with a raw 32-bit ARGB DIB section. Zero GPU load, consuming only ~17.5 MB of RAM and < 1% CPU.
- **Single-Instance and Clean Tray Integration:** Named mutex prevents duplicate instances, accompanied by automatic system tray sweeping to eliminate ghost icons.

---

## Preview

| Window Snowdrifts & Decorations | Shake-off Particle Cascade |
| :---: | :---: |
| ![Snow Window Decorations](assets/snow_window_decorations.png) | ![Snow Shake Cascade](assets/snow_shake_cascade.png) |

---

## Getting Started

### Pre-built Binary
1. Download `snow_app_v1.0.0.zip` from the [Releases](https://github.com/directive32/snow-app/releases) section.
2. Extract the archive and run `snow_app.exe`. No installation required.

### Building from Source
Prerequisites: [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0).

```bash
git clone https://github.com/directive32/snow-app.git
cd snow-app
dotnet publish -c Release
```
The compiled binary will be generated in `bin/Release/net6.0-windows/publish/snow_app.exe`.

---

## Tray Menu Controls

Right-click the snowflake icon in the system notification area:

- **Pause:** Pause or resume falling snowflakes.
- **Intensity:** Choose between Light (150 flakes), Moderate (300 flakes), or Blizzard (600 flakes).
- **Shake off all snow:** Cascades current snowdrifts into falling particles and clears window borders.
- **Restore snowbanks:** Instantly repopulates fresh snowdrifts, icicles, trees, and snowmen across all windows.
- **Exit:** Closes the overlay and removes the tray icon.

---

## Architecture

- **Runtime:** .NET 6 Windows Forms host with Win32 Interop.
- **Renderer:** Software rasterizer writing directly to a 32-bit ARGB DIB section with premultiplied alpha, presented via Windows Desktop Window Manager (`UpdateLayeredWindow`).
- **Obstacle Manager:** Low-priority background thread polling window bounds with `DwmGetWindowAttribute` (`DWMWA_EXTENDED_FRAME_BOUNDS`) and maintaining an occluded Z-order stack.
- **Memory Management:** Zero per-frame heap allocations; periodic trim keeps working set strictly under 18 MB.

---

## License

This project is licensed under the [MIT License](LICENSE).
