# Validation

Executed on 2026-09-17 using .NET SDK 10.0.100 on Linux:

- Core compositor checks: **15 passed**.
- Windows app and Windows check project: **compiled successfully, 0 warnings, 0 errors**.
- Self-contained Windows x64 single-file publish: **succeeded**.
- Project XML, project-reference paths, SDK JSON, and GitHub Actions YAML: **validated**.
- The Windows executable's PE header was checked for the x64 machine type.

The supplied executable was cross-compiled, not run on Windows in this environment.
The included Windows-specific checks have been compiled but **not executed here**.
No GitHub workflow was remotely triggered because no destination repository was supplied.

The GitHub workflow runs the core checks and Windows-specific checks before publish.
Windows checks cover WPF layer rendering/cropping, exact PNG dimensions, replacement
of an imported file, transparent PNG export, white-filled JPEG transparency, and
window construction/rendering. The workflow also uploads a UI smoke-test image.

Manual Windows verification still needed:

1. Open two portrait/landscape images and a transparent PNG; check orientation.
2. Drag and rotate the seam; zoom/move both images and verify undo/redo.
3. Try all blend modes, portrait/landscape canvases, and direct corner cropping.
4. Export PNG/JPEG and compare the result with the preview (without editing handles).
5. Check the interface at your Windows display scaling and with large photographs.

The included executable uses the runtime bundled by SDK 10.0.100. The GitHub
workflow installs the available 10.0.x SDK to create subsequent builds.
