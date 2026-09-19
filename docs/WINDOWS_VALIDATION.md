# The remaining Windows validation

**Status: NOT PERFORMED.** This procedure has not been carried out, and nothing in this repository
should be read as saying that it has.

---

## Why it has not been done

The development environment for this project is Linux. The .NET SDK installed there does not ship
the Windows Desktop targets, so `Tawaka.Ui.Desktop` — the WPF host — cannot be compiled at all:

```
$ dotnet build Tawaka.Payroll.sln -c Release
...
/usr/lib/dotnet/sdk/8.0.131/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.targets(1355,3):
  error MSB4019: The imported project
  ".../Microsoft.NET.Sdk.WindowsDesktop/targets/Microsoft.NET.Sdk.WindowsDesktop.targets"
  was not found.
  [src/Tawaka.Ui.Desktop/Tawaka.Ui.Desktop.csproj]

$ ls /usr/lib/dotnet/sdk/8.0.131/Sdks/ | grep -i windows
(nothing)
```

Every other project in `Tawaka.Payroll.sln` builds in Release with zero warnings; the single error
is this one.

Three separate ways round it were tried, and all three are closed:

| Attempt | Result |
|---|---|
| `dotnet publish -r win-x64 --self-contained` | Same `MSB4019`. The missing piece is the targets file, not the runtime |
| `-p:EnableWindowsTargeting=true` | Same `MSB4019`. That switch supplies *targeting packs* from NuGet; it cannot supply an SDK folder the installation does not have |
| Fetching the Windows SDK to borrow its `Microsoft.NET.Sdk.WindowsDesktop` folder | `builds.dotnet.microsoft.com` is blocked by this environment's proxy (`403` at CONNECT). NuGet carries `Microsoft.NET.Sdk.WindowsDesktop` only at 3.0.0, from the .NET Core 3.0 era, which cannot be grafted onto an 8.0.131 SDK |

**What this means:** the WPF host, the `BlazorWebView`, the WebView2 control, the startup sequence
and the printing path have never executed. They are compiled nowhere and run nowhere.

## What *has* been established without Windows

So that the list below is a list of the genuinely unknown, not of everything:

- Every screen renders. `tests/Tawaka.Ui.Tests` renders the real Razor components headlessly with
  bUnit, against the real composition root, the real migrations and a real SQLite database — every
  route, the sign-in gate, forms, validation, saving, permission trimming, currency display and the
  whole payroll lifecycle driven from the buttons. This found a defect that took out every screen in
  the application, so it is not a formality; but it exercises the components, not WPF, not WebView2
  and not Windows.
- The database, migrations, seeding, calculation, reporting, payslips, obligations, backup and
  restore all run on Linux, and are the same code on Windows.
- The host's own wiring is checked against its files by `WindowsHostConfigurationTests`, because a
  project that cannot be compiled cannot be checked by a compiler: the solution parses and carries
  the desktop project with valid configuration rows; the project targets `net8.0-windows` with WPF
  and references everything it hosts; `App.xaml` has no `StartupUri`, and startup assigns
  `App.Services`, migrates and seeds **before** the window is created; the root component selector
  matches an element on the host page; the host page loads the framework script and a stylesheet
  that exists; and the publish profile is self-contained win-x64 and untrimmed.

The gap is exactly: **the host process, the browser control it embeds, the operating system around
them, and printing.**

---

## The procedure

Work through it in order on a clean Windows 10 (1809+) or Windows 11 x64 machine. Record the result
of every step. A step that fails should be reported with the screen it happened on and the contents
of `%LOCALAPPDATA%\Tawaka Payroll\logs\`.

### A. Build

| # | Step | Expected |
|---|---|---|
| A1 | `dotnet --list-sdks` | An entry beginning `8.0.` |
| A2 | `dotnet restore Tawaka.Payroll.sln` | Restores, including `Microsoft.AspNetCore.Components.WebView.Wpf`. **This package version has never been restored on Windows**; if it fails, take the current 8.0.x from nuget.org and update `src/Tawaka.Ui.Desktop/Tawaka.Ui.Desktop.csproj` |
| A3 | `dotnet build Tawaka.Payroll.sln -c Release` | Build succeeded, 0 errors. Record any warnings |
| A4 | `dotnet test Tawaka.Payroll.sln -c Release` | All tests pass on Windows as they do on Linux |
| A5 | `dotnet publish src/Tawaka.Ui.Desktop -c Release -p:PublishProfile=win-x64` | One `Tawaka.Payroll.exe` under `src\Tawaka.Ui.Desktop\bin\Release\publish\win-x64\`. The profile carries the runtime identifier, self-contained, single-file and no-trimming settings |
| A6 | Copy that exe alone to a **second** machine with no .NET SDK | It runs — proving self-contained really is self-contained |

### B. Start-up

| # | Step | Expected |
|---|---|---|
| B1 | Run the exe on a machine with no `%LOCALAPPDATA%\Tawaka Payroll\` | The folder and `tawaka-payroll.db` are created |
| B2 | — | Every migration applies. Check `__EFMigrationsHistory` holds all seven |
| B3 | — | Seeding runs: statutory rules, currencies, the company shell, permissions, four roles |
| B4 | — | The **generated administrator password** dialog appears, once |
| B5 | The WPF window | Opens, is resizable, and the title bar reads Tawaka Payroll |
| B6 | The WebView2 control | Initialises; no blank white window, no "WebView2 runtime not found" |
| B7 | The Razor root component | The sign-in screen renders, styled — `app.css` reached the page from `_content/Tawaka.Ui.Shared/` |
| B8 | Dependency injection | The screen works, which means the whole graph resolved inside the host |
| B9 | Run it on a machine **without** the WebView2 runtime | A comprehensible message, not a silent failure or a crash |

### C. Authentication

| # | Step | Expected |
|---|---|---|
| C1 | Sign in as `admin` with the generated password | Accepted, and the forced password-change screen appears |
| C2 | Try to navigate away without changing it | Not possible |
| C3 | Change the password | Accepted; signed in; the top bar shows the user and the release stage |
| C4 | Sign out and back in with the new password | Accepted |
| C5 | Sign in with the old password | Refused |
| C6 | Five wrong passwords | The account locks; the message does not say whether the username exists |

### D. Every module renders and works

For each of the ten modules — Dashboard, Employees, Payroll, Time & Leave, Loans & Advances,
Projects & Sites, Statutory, Reports, Settings, Administration — and each of their tabs:

| # | Check |
|---|---|
| D1 | The navigation entry is there, and clicking it renders the page |
| D2 | Tables, tabs, badges, empty states and warning panels render — nothing clipped, nothing overlapping, no unstyled content |
| D3 | Forms render; every field accepts input; dropdowns open and select |
| D4 | Date pickers work as Windows/WebView2 renders them, and the dates they produce are the dates that get saved |
| D5 | Validation errors appear where they should and read correctly |
| D6 | Saving persists; cancelling does not |
| D7 | Amounts show two decimals; ZiG shows as "ZiG" and not "ZWG"; dates are `dd MMM yyyy` |
| D8 | An unresolved figure shows as a dash, never `0.00` |
| D9 | Scrolling works in wide tables, and the window can be resized without breaking the layout |

### E. The full journey

Set up a company, then a second and third user (an officer and a manager) in **Administration →
Users**, then: an employee, a contract, a recurring earning, a project, a site, a timesheet with
overtime, a leave request, a loan. Approve every input as the manager. Create a period and a run.
Calculate as the officer. Open **Explain** on an employee and check every figure against the
preview. Approve as the manager. Finalise. Check the obligations appeared, unpaid. Mark net wages
paid, and check the obligations are still unpaid. Record a statutory payment. Generate a payslip.
Print it. Run every report and export one to CSV. Lock the run.

Then, with the run locked, try to change: the payroll header, a result figure, an earning line, a
deduction line, a statutory obligation, the snapshot, a consumed timesheet. **Every one must be
refused.** Close the application, reopen it, and check the locked payroll is exactly as it was.

### F. Both currencies, separately

Run E again for a USD employee and a ZiG employee, and check at each step that the two are reported
separately and never combined: contract, earnings, deductions, PAYE, NSSA, net pay, payslip,
reports, obligations, payments, project cost, journal.

### G. Printing

| # | Check |
|---|---|
| G1 | A payslip prints to A4 through the WebView2 print dialogue, on one page, nothing clipped |
| G2 | "Save as PDF" from the same dialogue produces a readable PDF |
| G3 | The development watermark appears on a Development payslip and not on a live one |
| G4 | A report prints legibly, and a wide report is not cut off at the right margin |

### H. Persistence, restart and shutdown

| # | Check |
|---|---|
| H1 | Close the window; the process exits, leaving no orphan |
| H2 | Reopen; the data is exactly as it was |
| H3 | Take a backup, change something, restore the backup, reopen — the change is gone and the earlier state is back |
| H4 | Kill the process mid-edit with Task Manager, then reopen — the database opens and no committed payroll is damaged |

### I. Windows-specific behaviour

| # | Check |
|---|---|
| I1 | High-DPI: 100%, 150% and 200% scaling, and a monitor change while running. WPF on .NET 8 is per-monitor DPI aware by default through the manifest the Windows Desktop SDK generates; nothing in this project overrides it, and nothing has confirmed it |
| I2 | Keyboard: Tab order through forms, Enter on the sign-in screen, Escape closing a modal |
| I3 | Clipboard: copy and paste into fields |
| I4 | Windows dark mode does not make any text unreadable |
| I5 | An unhandled error shows the dialog and writes `logs\error-yyyyMMdd.log`, and the application stays usable |
| I6 | Memory after a 100-employee run and a few reports is not unreasonable, and does not climb while idle |
| I7 | A second copy started while one is running does not corrupt the database |

### J. Performance on the target machine

Measured on Linux, a hundred-employee payroll calculates in 1.6s, finalises in 0.4s, reports in
0.2s and produces a payslip in 0.1s. Repeat on the Windows machine at 10, 50 and 100 employees and
record the figures. Watch for the user interface freezing during a calculation, which would not show
up in a headless test.

---

## Reporting the result

Record, for each step: pass, fail, or not applicable, with the version of Windows, the WebView2
runtime version and the .NET SDK version. Until sections A–I are complete and passing, the release
stage of this software is **release candidate**, not released.
