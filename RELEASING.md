# Publishing a release

Distribute **Stillframe-Setup-0.1.8.exe**, the self-contained installer. It includes the .NET 10 runtime, Windows desktop libraries and app dependencies. Users install it, open Stillframe and choose their own photo folder. They do not need the SDK or your development files.

Do not upload only the inner `Stillframe.exe`: the current build needs the other files in its publish folder. The setup installer packages these together.

## Build and check

1. For a new version, update `Version` in `src/Stillframe/Stillframe.csproj`, plus `AppVersion` and `OutputBaseFilename` in `installer/Stillframe.iss`. Update the README download filename too.
2. Install the .NET 10 SDK and Inno Setup. From the repository root, run:

```powershell
.\scripts\Build.ps1 -InnoCompiler "C:\path\to\ISCC.exe"
```

Replace the compiler path with your Inno Setup installation. The installer appears one folder above this repository; `artifacts/publish` contains its runtime payload.

3. Run the core and application checks, then test the installer on a clean Windows 11 x64 PC or VM without .NET installed. Check folder selection, slideshow, fade, desktop lock/unlock, restarting the app and uninstalling.
4. Sign the app and installer with a trusted code-signing certificate for public distribution. Generate a SHA-256 checksum after signing, because signing changes the file.

## Upload to GitHub

1. Commit and push the tested source to `main`.
2. Open the repository → **Releases** → **Draft a new release**.
3. Create tag `v0.1.8` targeting the tested commit; use the matching version for later releases.
4. Attach **Stillframe-Setup-0.1.8.exe** under release assets. Optionally attach its `.sha256` checksum.
5. Add notes stating Windows 11 x64 support, bundled .NET, changes and known limits. Use **pre-release** while compatibility coverage is limited.
6. Publish, then download the attached installer and verify its checksum and installation. GitHub's automatic source ZIP is source code, not the installer.

## What is supported today?

The app was tested on Windows 11 build 26200, with 14 core tests and 24 application checks passing. The user also confirmed the desktop frame was visible. This is not a guarantee for every PC: other Windows builds, HDR, multiple monitors, Explorer restart and sleep/resume need broader testing. Windows 10, macOS, Linux and native ARM64 builds are outside the current supported target.

The current installer is unsigned. SmartScreen can show reputation warnings, and Smart App Control or organization policies can block unsigned apps. Signing improves distribution but does not guarantee that a new release has established reputation. Do not ask users to disable Windows security to run it.

References: [GitHub release steps](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository), [.NET self-contained publishing](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [Windows app signing and reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).
