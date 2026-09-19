# Tawaka Payroll — deployment

Installing it, building it, and what the machine needs. For using it, see `USER_GUIDE.md`; for
running it day to day, see `OPERATIONS.md`.

> **The Windows application has never been run.** It is built and tested on Linux, which cannot
> compile WPF, so the window has never been seen on a screen. `docs/WINDOWS_VALIDATION.md` is the
> procedure that closes that gap, and until somebody works through it this is a release candidate,
> not a release.

---

## 1. What the machine needs

| | |
|---|---|
| Windows | 10 version 1809 or later, or Windows 11 (x64) |
| WebView2 Runtime | Pre-installed on Windows 11 and up-to-date Windows 10. Otherwise install Microsoft's **Evergreen Standalone Installer** |
| Disk | 200 MB, plus the database — a few MB per year for a hundred employees |
| Rights | None special. Everything lives in the user's own profile |
| .NET runtime | Not needed for the self-contained build. Needed (8.0) only to run from source |

Tawaka Payroll is a single-user desktop application with a SQLite database in the signed-in user's
profile. It is not a server, it does not listen on a port, and two people cannot use one database
at the same time.

---

## 2. Installing the built application

1. Copy `Tawaka.Payroll.exe` to the machine — anywhere the user can write, e.g.
   `C:\Tawaka Payroll\`.
2. Install the WebView2 Evergreen runtime if the machine does not already have it.
3. Double-click it.

There is no installer, no registry entry and no service. Uninstalling is deleting the folder and
`%LOCALAPPDATA%\Tawaka Payroll\`.

---

## 3. Building and publishing

Needs the .NET 8.0 SDK ([dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)).
Check with `dotnet --list-sdks` that an entry beginning `8.0.` is present.

```powershell
git clone <repository-url> tawaka-payroll
cd tawaka-payroll

# Everything, including the Windows host.
dotnet build Tawaka.Payroll.sln -c Release

# The tests.
dotnet test Tawaka.Payroll.sln -c Release

# Run it from source.
dotnet run --project src/Tawaka.Ui.Desktop -c Release
```

### The release build

```powershell
dotnet publish src/Tawaka.Ui.Desktop -c Release -p:PublishProfile=win-x64
```

Output: `src\Tawaka.Ui.Desktop\bin\Release\publish\win-x64\Tawaka.Payroll.exe`

The profile (`src/Tawaka.Ui.Desktop/Properties/PublishProfiles/win-x64.pubxml`) carries the
settings so the command does not have to:

| Setting | Why |
|---|---|
| `RuntimeIdentifier` win-x64, `SelfContained` | The target machine needs no .NET runtime |
| `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract` | One file to copy |
| `PublishReadyToRun` | Starts faster |
| `PublishTrimmed` **false** | Entity Framework builds its model by reflection. A trimmer that removes an entity the model needs fails on the user's machine rather than in the build, and the extra size is not worth that in a payroll application |

They are in the profile rather than the project file so that an ordinary `dotnet build` and the
tests are unaffected: a project that is always self-contained and always win-x64 cannot be built
on anything else.

### Two solution files

| | |
|---|---|
| `Tawaka.Payroll.sln` | Everything, including `Tawaka.Ui.Desktop`. Builds on Windows |
| `Tawaka.Payroll.Core.sln` | Everything except the Windows host, so the rest builds and tests on Linux and macOS. This is what `build.sh` and `test.sh` use |

> If `Microsoft.AspNetCore.Components.WebView.Wpf` 8.0.100 fails to restore, take the current 8.0.x
> from nuget.org and update the `PackageReference` in
> `src/Tawaka.Ui.Desktop/Tawaka.Ui.Desktop.csproj`. That version is pinned and has never been
> restored on Windows from this repository.

---

## 4. First start

On first run the application creates `%LOCALAPPDATA%\Tawaka Payroll\`, creates its database there,
applies every migration, seeds the statutory baseline and the four roles, and shows a **generated
administrator password once**.

Write it down before clicking OK. It is stored only as a PBKDF2-HMAC-SHA256 hash with a per-user
salt and cannot be recovered by anybody. There is no default password in this software.

Then `USER_GUIDE.md` §1 for what to set up.

---

## 5. Where files live

| | |
|---|---|
| Database | `%LOCALAPPDATA%\Tawaka Payroll\tawaka-payroll.db` |
| Error logs | `%LOCALAPPDATA%\Tawaka Payroll\logs\error-yyyyMMdd.log` |
| Report exports | `%USERPROFILE%\Documents\Tawaka Payroll\Exports\` |
| Backups | wherever you choose in Settings → Backup & restore |

Per-user application data: no administrator rights, and the database is included in an ordinary
Windows profile backup.

---

## 6. Upgrading

1. Take a backup (Settings → Backup & restore).
2. Replace `Tawaka.Payroll.exe`.
3. Start it. Migrations apply automatically.

A database restored from a backup taken on a **newer** version is refused. Install that version
first.

---

## 7. When it will not start

**"WebView2 runtime not found"** — install Microsoft's Evergreen Standalone Installer.

**The window opens blank** — the host page is `wwwroot/index.html` in the desktop project and the
stylesheet comes from `_content/Tawaka.Ui.Shared/app.css`. A blank window usually means the Razor
class library's static assets were not copied; a clean rebuild resolves it.

**"Tawaka Payroll could not prepare its database"** — the message names the database path. The
usual cause is a database restored from a backup taken on a newer version. The full exception is in
`logs\`.

**It fails after a restore** — the displaced database is beside the original as
`tawaka-payroll.db.replaced-<timestamp>`. Close the application, delete the restored file, rename
that one back.
