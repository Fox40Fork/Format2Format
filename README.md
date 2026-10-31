# Format2Format

A small, offline Windows app that converts images from one format to another: JFIF, WebP, HEIC and
the rest in, PNG, JPG and the rest out. No uploads, no online converters.

- Add files with **Add Files**, by dragging them onto the window, or by right-clicking them in Explorer
  (**Convert with Format2Format**).
- Pick a format under **Convert to**, then click **Convert**.
- Converted files go to your **Downloads** folder. When it's done, **Open folder** shows them there,
  already selected.

## Formats

| | Formats |
|---|---|
| **Reads** | JPG/JPEG, JFIF, PNG, WEBP, BMP, GIF, TIFF, ICO, HEIC/HEIF, AVIF |
| **Writes** | PNG, JPG, WEBP, BMP, GIF, TIFF, ICO |

How conversion behaves:

- **The file's content decides the format, not its extension.** A PNG saved as `.jpg`, or a
  `photo.jpg_large` from a browser, converts fine. Files that aren't images are listed as *Skipped*
  with the reason, and the other files still convert.
- **File names:** the original name with the new extension (`photo.jfif` → `photo.png`). If that name
  is taken, Format2Format adds ` (1)`, ` (2)`, and so on. It never overwrites a file.
- **Quality** (JPG and WEBP only): 1–100, default 90. For WEBP, 100 means lossless.
- **Transparency:** kept for PNG, WEBP, GIF, TIFF and ICO. JPG and BMP can't store it, so transparent
  areas are filled with white.
- **Animation:** animated GIF and WEBP stay animated when converted to GIF or WEBP. Converted to any
  other format, only the first frame is used, and the Status column says so. Multi-page TIFFs work
  the same way: only the first page is converted.
- **ICO:** writes a multi-size icon (16–256 px). Larger images are scaled down to 256×256, and
  non-square images are padded with transparency instead of stretched.
- **Photo rotation:** if a phone photo is marked as rotated (common in JPG/JFIF/HEIC), the rotation is
  applied, so the result isn't sideways.

## Install

Run `Format2Format-<version>-setup.exe`.

- It installs for your user only, into `%LOCALAPPDATA%\Programs\Format2Format`. No admin rights are
  needed.
- The installer has a checkbox (on by default) to add **Convert with Format2Format** to Explorer's
  right-click menu for image files.
- The app is self-contained: the .NET runtime is included, so nothing else needs to be installed and
  it works offline.

To uninstall, use **Settings → Apps → Installed apps → Format2Format** in Windows. Uninstalling also
removes the right-click entry, the log and the saved settings.

## Right-click menu

After installing (with the checkbox ticked), right-click one or more images in Explorer and choose
**Convert with Format2Format**. The files are added to the list. Pick a format and click **Convert**.

> **Windows 11:** the entry is in the classic menu. Right-click, choose **Show more options**
> (or press **Shift+F10**), then **Convert with Format2Format**.

If you select several files, Windows starts the app once per file. Format2Format detects this and
collects all of them into **one window**.

You can add or remove the entry at any time with **Settings → Add/Remove right-click menu entry**. It's
stored for your user under `HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\Format2Format`.
It's added next to whatever app opens those files and doesn't change which app that is. The supported
extensions are `.jpg .jpeg .jfif .png .webp .bmp .gif .tif .tiff .ico .heic .heif .avif`.

## Command line

```text
Format2Format.exe [file ...]       open the window with these files in the list
                                   (if a window is already open, they're added to it)
Format2Format.exe --register-shell     add the right-click menu entry and exit
Format2Format.exe --unregister-shell   remove the right-click menu entry and exit
```

## Logs and settings

| File | Contents |
|---|---|
| `%LOCALAPPDATA%\Format2Format\log.txt` | What was converted, and full error details when something fails. After 1 MB it's renamed to `log.old.txt` and a new log starts. |
| `%LOCALAPPDATA%\Format2Format\settings.json` | The last chosen format and quality. |

**Help → View Log File** opens the log.

---

## Building from source

### Requirements

- Windows 10 or 11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Inno Setup](https://jrsoftware.org/isdl.php) 6.3 or newer, only for building the installer
  (CI uses 7.1)

### Build, test, run

```powershell
dotnet build                                  # builds everything
dotnet test                                   # runs the unit tests
dotnet run --project src/Format2Format        # starts the app (Debug)
```

You can also open `Format2Format.sln` in Visual Studio 2022 and press F5. The window is built in code
(`MainForm.cs`), so there's no designer file.

### Release build and installer

```powershell
./build/build.ps1          # tests + publish + installer
```

Or step by step:

```powershell
dotnet publish src/Format2Format -p:PublishProfile=win-x64
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\Format2Format.iss   # or ...\Inno Setup 7\ISCC.exe
```

- The app is written to `publish\win-x64\`, the installer to `dist\Format2Format-<version>-setup.exe`.
- The version comes from `<Version>` in `Directory.Build.props`.

### Building on Linux/macOS

The core library and its tests are plain `net8.0` and run anywhere. The Windows Forms project also
*builds* on Linux and macOS (`EnableWindowsTargeting` is on), but it only runs on Windows.

Use Microsoft's .NET SDK for this. Some Linux distributions' own builds (e.g. Ubuntu's `dotnet-sdk-8.0`
package) leave out the Windows Desktop targets, and building the app project fails with
`Microsoft.NET.Sdk.WindowsDesktop.targets was not found`.

### Continuous integration

`.github/workflows/build.yml` runs on a Windows runner and:

1. Runs the unit tests and publishes the app.
2. Smoke-tests on real Windows (`build/smoke-tests.ps1`):
   - registering and removing the right-click entry
   - several processes merging into one window
   - a silent install and uninstall, including registry cleanup
3. Uploads the installer as a build artifact.

## Project structure

```text
src/Format2Format.Core/          net8.0 library, no UI (testable anywhere)
  ConversionService.cs           decode → orient/flatten/resize → encode; never throws for bad input
  FormatDetector.cs              detects the format from the file's first bytes
  ImageFormats.cs                supported formats, extensions, display names
  OutputPathResolver.cs          "name.ext", "name (1).ext", ...; atomic, never overwrites
  SingleInstance.cs              named mutex + named pipe so all files land in one window
  Log.cs                         thread-safe file log in %LOCALAPPDATA%\Format2Format
src/Format2Format/               Windows Forms app (net8.0-windows)
  Program.cs                     startup, single-instance hand-off, --register-shell/--unregister-shell
  MainForm*.cs                   window, conversion on a background thread, drag & drop, Settings
  ShellIntegration.cs            Explorer right-click entry (HKCU registry)
  NativeMethods.cs               Downloads folder via SHGetKnownFolderPath; Explorer "open and select"
tests/Format2Format.Core.Tests/  xUnit tests: conversions, transparency, animation, ICO, naming, errors
installer/Format2Format.iss      Inno Setup script (per-user install)
build/                           build and smoke-test scripts
```

Image decoding and encoding use [Magick.NET](https://github.com/dlemstra/Magick.NET) (ImageMagick),
package `Magick.NET-Q8-AnyCPU`. HEIC and AVIF support is included in it.
