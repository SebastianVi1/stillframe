# Stillframe development build

Stillframe 0.1.9 is an unsigned development application for Windows 11 x64.

The application reads selected local image files and stores its own settings and a bounded diagnostic log under the current user's LocalApplicationData folder. It includes no network client, account system or analytics. It does not modify source photographs.

Desktop attachment uses internal Windows Explorer behavior; compatibility is not guaranteed across Windows updates. This build has been tested on Windows 11 build 26200.0. See RELEASING.md for compatibility checks and limitations.

The installer includes the Microsoft .NET runtime, distributed under its accompanying license notices. Development test dependencies are not included in the runtime application. Inno Setup builds the installer; see https://jrsoftware.org/ for its license and distribution terms.

Installation is per-user. Startup is off by default. Uninstallation removes the application and its startup entry, while preserving photo files and preferences.
