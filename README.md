# Fuse

A small Windows desktop app for combining two images through direct manipulation.
Built in C# with WPF and .NET 10. No third-party runtime packages, image uploads,
accounts, or network requests by the app.

## Get the Windows app through GitHub Actions

1. Create a GitHub repository and put **the contents of this folder** at its root.
   Include the hidden `.github` folder, `global.json`, and `Directory.Build.props`.
2. Push the files. The **Build Windows app** workflow runs automatically on pushes
   and pull requests. You can also run it from **Actions → Build Windows app → Run workflow**.
3. Open the completed successful run and download **Fuse-Windows-x64** from **Artifacts**.
4. Extract the ZIP and open **Fuse.exe**.

The download is self-contained: the target computer does not need a .NET runtime
or developer tools. It targets Windows x64 (Windows 10/11), and is unsigned.
GitHub Actions artifacts require a signed-in GitHub account to download and are
retained for 30 days. This workflow builds and packages the app; it does not
publish a GitHub Release or deploy anything.

If using Git from this folder:

```powershell
git init
git add .
git commit -m "Add Fuse image composer"
git branch -M main
git remote add origin https://github.com/YOUR-NAME/YOUR-REPO.git
git push -u origin main
```

## Use it

- Drop **two images** onto the canvas, or use the **First image / Second image** buttons.
  One dropped image replaces the image on that side of the divider. In crossfade
  mode, it replaces the selected A/B image. If more than two files are dropped,
  only the first two are used.
- Images initially cover the full canvas; the divider reveals one on each side.
  Drag each visible image to choose its crop. Scroll over it to zoom around the cursor.
- Drag the **green handle or the divider line** to move the seam.
- Drag the **white handle** to rotate the seam. Hold **Shift** to snap to 15°.
- Choose **Clean split**, **Soft seam**, or **Crossfade**. Softness controls the
  transition width; crossfade controls the contribution of image B. A 50% crossfade
  equally mixes both images; it does not automatically align subjects.
- Select A or B, then choose **Fit half** for a side-by-side layout, or **Fill canvas**
  for overlapping compositions. Double-clicking a photo also fits it to its half.
  In crossfade mode, the A/B selector determines which image you drag or zoom.
  **Alt + drag/scroll** forces the selected image in every mode.
- Drag the **bottom-right green corner** to resize the crop canvas, pick a ratio,
  or enter exact pixel dimensions and press **Set** or **Enter**.
- Choose white, dark, or transparent background. Hide editing handles for a clean preview.
- Export to **PNG** or **JPEG** at the exact canvas dimensions. Handles and the
  transparency checkerboard are never included. JPEG flattens transparency onto white.

The canvas clips both images automatically. Changing its dimensions recomputes
cover scaling while keeping each image's relative position and zoom. For angled
or soft seams, use Fill canvas or zoom out/in and reposition to get the desired
coverage. Fit half can intentionally expose the background where the seam crosses
beyond the image's bounds.

### Keyboard

| Action | Shortcut |
| --- | --- |
| Open A / B | Ctrl+O / Ctrl+Shift+O |
| Export | Ctrl+E or Ctrl+S |
| Undo / redo | Ctrl+Z / Ctrl+Y or Ctrl+Shift+Z |
| Move selected image, with canvas focused | Arrow keys |
| Move divider, with canvas focused | Ctrl+Arrow keys |
| Increase movement from 1 to 10 pixels | Hold Shift |
| Cancel the current drag | Escape |

Controls are keyboard focusable; sliders support arrow keys. Undo keeps the last
40 edits, including image replacements, crop changes, and whole drag gestures.

## Build locally

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet run --project src/Fuse.App
```

To build the solution and run the checks:

```powershell
dotnet build Fuse.slnx -c Release
dotnet run --project tests/Fuse.Checks -c Release
dotnet run --project tests/Fuse.WindowsChecks -c Release
```

To produce the same portable executable as CI:

```powershell
dotnet publish src/Fuse.App/Fuse.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/Fuse-win-x64
```

## Design and implementation

The slider interaction is inspired by [Knight Lab's Juxtapose](https://juxtapose.knightlab.com/).
Fuse adds free-angle seams, direct photo positioning, editable canvas dimensions,
and still-image export. No third-party app code or assets are included.
The platform is [Microsoft's .NET 10 / WPF](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview).

- `src/Fuse.Core`: canvas settings, seam geometry, premultiplied-alpha compositor.
- `src/Fuse.App`: native WPF UI, cached preview layers, pointer interaction, image I/O.
- `tests/Fuse.Checks`: dependency-free checks for masks, rotation, blending, alpha,
  background fill, buffer validation, and preview coordinate mapping.
- `tests/Fuse.WindowsChecks`: real WPF rasterization, PNG/JPEG round trips,
  file-handle release, transparency, and window-construction smoke checks.
- `.github/workflows/windows.yml`: Windows checks, self-contained publish, artifact upload.

Preview renders use a reduced resolution; exports render independently at full
canvas resolution on an STA worker thread. The compositor blends premultiplied
BGRA values with complementary weights. It does not implement AI stitching,
content-aware fusion, or linear-light/HDR color compositing.

Inputs: PNG, JPEG, BMP, TIFF, and GIF via Windows imaging codecs. GIF/TIFF imports
use the first frame. JPEG/TIFF EXIF orientation is applied, including mirrored
orientations. HEIC, WebP, SVG, and RAW are not guaranteed and are not listed in the
file picker. Source metadata is not copied to exports.

Canvas limits: 64–8192 pixels per side, at most 24 megapixels. Inputs over 60
megapixels are rejected after decoding. Large inputs/exports and image replacement
history can use significant memory. There is no project save/reopen format yet;
export saves the finished raster image, and edits exist only in the current session.

## Validation status

See `VALIDATION.md` for checks actually executed in the creation environment.
The Windows CI job must pass before treating a Windows build as verified.
