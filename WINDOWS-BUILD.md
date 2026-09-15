# Tawaka Payroll — Windows build and run

The application is a WPF host with a `BlazorWebView`. All screens live in
`Tawaka.Ui.Shared`, a Razor class library targeting `net8.0`, so the entire user interface
compiles and is type-checked on any platform. Only the host targets `net8.0-windows`, and only it
requires Windows (ADR-019).

> **The desktop host has never been compiled or run.** The development environment for this
> project is Linux, which cannot build `net8.0-windows`. Everything below is verified by
> inspection — project references resolve, the host page matches the root component selector,
> startup applies migrations and seeds, failures are handled — and nothing here is a claim that the
> window has been seen on screen. The first Windows build is the first real test of this project.

---

## 1. Prerequisites

| | |
|---|---|
| Windows | 10 version 1809 or later, or Windows 11 (x64) |
| .NET SDK | **8.0** ([dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)) |
| WebView2 Runtime | Pre-installed on Windows 11 and on up-to-date Windows 10. Otherwise install the **Evergreen Standalone Installer** from Microsoft |
| Visual Studio *(optional)* | 2022 17.8+, with **.NET desktop development** |

Check the SDK:

```powershell
dotnet --list-sdks
```

An entry beginning `8.0.` must be present.

---

## 2. Build

```powershell
git clone <repository-url> tawaka-payroll
cd tawaka-payroll

# The full solution, including the Windows host.
dotnet build Tawaka.Payroll.sln -c Release
```

`Tawaka.Payroll.sln` includes `Tawaka.Ui.Desktop`. `Tawaka.Payroll.Core.sln` deliberately
excludes it so the rest of the solution builds and tests on Linux and macOS.

**If `Microsoft.AspNetCore.Components.WebView.Wpf` 8.0.100 fails to restore**, check the current
8.0.x version on nuget.org and update the `PackageReference` in
`src/Tawaka.Ui.Desktop/Tawaka.Ui.Desktop.csproj`. The version is pinned and has never been
restored on Windows from this repository.

---

## 3. Run

```powershell
dotnet run --project src/Tawaka.Ui.Desktop -c Release
```

On first run the application:

1. creates `%LOCALAPPDATA%\Tawaka Payroll\`;
2. creates `tawaka-payroll.db` there and applies every migration;
3. seeds the statutory baseline, the company shell, the permissions and the four roles;
4. creates a **first administrator** with a randomly generated password, shown once in a dialog.

Write that password down. It is not stored anywhere, cannot be shown again, and must be changed at
first sign-in. There is no default password in this software.

---

## 4. Publish a self-contained application

```powershell
dotnet publish src/Tawaka.Ui.Desktop -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `src\Tawaka.Ui.Desktop\bin\Release\net8.0-windows\win-x64\publish\Tawaka.Payroll.exe`.

Self-contained means the target machine needs no .NET runtime. It still needs the WebView2
runtime.

---

## 5. Where things live

| | |
|---|---|
| Database | `%LOCALAPPDATA%\Tawaka Payroll\tawaka-payroll.db` |
| Error logs | `%LOCALAPPDATA%\Tawaka Payroll\logs\error-yyyyMMdd.log` |
| Report exports | `%USERPROFILE%\Documents\Tawaka Payroll\Exports\` |
| Backups | wherever you choose in **Settings → Backup & restore** |

Per-user application data, so the application needs no administrator rights and the database is
included in an ordinary Windows profile backup.

---

## 6. What to check on the first Windows run

Nothing in this list has been verified. It is what a first run should establish:

- [ ] The solution builds, including the WebView2 package restore.
- [ ] The window opens and the sign-in screen renders.
- [ ] The first-administrator dialog appears and the generated password signs in.
- [ ] The forced password change works.
- [ ] Navigation renders all ten modules and is permission-trimmed per role.
- [ ] Forms round-trip: creating an employee, a contract, a timesheet, a leave request, a loan.
- [ ] Tables, tabs, modals and validation panels render and behave.
- [ ] The payslip prints correctly to A4 through the browser print dialogue.
- [ ] A report exports to CSV and the file opens in Excel.
- [ ] Backup writes a folder; restore reads it back.
- [ ] High-DPI scaling is correct (the manifest requests PerMonitorV2).

Report anything that fails against the screen it happened on. The screens are type-checked but
have never been exercised.

---

## 7. Troubleshooting

**"WebView2 runtime not found"** — install the Evergreen Standalone Installer from Microsoft.

**The window opens blank** — the host page is `wwwroot/index.html` in the desktop project, and the
stylesheet comes from `_content/Tawaka.Ui.Shared/app.css`. A blank window usually means the Razor
class library's static assets were not copied; a clean rebuild resolves it.

**"could not prepare its database"** — the message names the database path. The usual cause is a
database restored from a backup taken on a newer version; install that version first. The
application writes the full exception to `logs\`.

**Startup fails after a restore** — Settings → Backup & restore keeps the displaced database next
to the original as `tawaka-payroll.db.replaced-<timestamp>`. Renaming it back recovers the previous
state.
