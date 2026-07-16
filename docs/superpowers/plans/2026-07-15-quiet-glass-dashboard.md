# Quiet Glass Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved Quiet Glass popup, corrected provider hierarchy, persistent tray-icon modes, and a matching transparent application icon without regressing tray behavior or quota handling.

**Architecture:** Keep the current single-file WPF application structure and isolate new behavior behind small testable helpers in `Program.cs`: presentation formatting, icon-state selection, procedural icon rendering, and icon lifecycle management. Add a vector master application icon plus a reproducible local generator for the Windows ICO, then embed that ICO through the existing `csc.exe` build.

**Tech Stack:** C# / .NET Framework 4.0, WPF, Windows Forms `NotifyIcon`, System.Drawing/GDI, DWM backdrop APIs, PowerShell build and test scripts.

## Global Constraints

- Preserve the repaired tray hover, dismissal, multi-monitor, DPI, and top/bottom taskbar behavior.
- Available allowance meters use restrained pale grey; only unavailable meters are solid black.
- Codex contains token usage; MiniMax contains both 5-hour and weekly allowance rows.
- Five-hour reset copy is t-minus (`−HH:mm`); weekly reset time is hidden.
- Tray percentage mode falls back to the default open gauge ring whenever Codex 5-hour data is unavailable.
- The exact value `100` must have a purpose-built compact tray treatment, not `99+`.
- Do not implement recent 15-minute consumption in this release.
- Do not copy Apple, OpenAI, Codex, or SF Symbols artwork.
- Do not add external runtime dependencies.

---

## File Map

- Modify `Program.cs`: settings, presentation helpers, Quiet Glass WPF construction, icon rendering/cache/lifecycle, tray menu, and dynamic updates.
- Modify `ProgramTests.cs`: deterministic formatting, settings, meter-state, icon-state, icon-rendering, and lifecycle tests.
- Modify `build.ps1`: embed the generated application ICO.
- Modify `test.ps1`: compile any source required by tests without changing test entry behavior.
- Create `assets/app-icon.svg`: transparent vector master for the open gauge-ring identity.
- Create `assets/app-icon.ico`: generated multi-resolution Windows application icon.
- Create `tools/GenerateAppIcon.ps1`: reproducibly render and package the ICO from the approved geometry.
- Modify `README.md`: document the two tray-icon modes and unavailable fallback.

---

### Task 1: Presentation rules and persistent tray mode

**Files:**
- Modify: `Program.cs:213-275, 1135-1198, 1330-1355`
- Test: `ProgramTests.cs:45-65, 165-230`

**Interfaces:**
- Produces: `TrayIconMode` enum; `DashboardSettings.trayIconMode`; `DashboardSettings.NormalizeTrayIconMode(string)`; `PresentationText.TMinus(DateTime?, DateTime)`.
- Consumes: existing `DashboardSettings.Load/Save`, `QuotaSnapshot`, and `MiniMaxSnapshot`.

- [ ] **Step 1: Add failing settings and presentation tests**

```csharp
Expect(DashboardSettings.NormalizeTrayIconMode("percentage") == TrayIconMode.Percentage,
    "tray mode accepts percentage");
Expect(DashboardSettings.NormalizeTrayIconMode("unknown") == TrayIconMode.Default,
    "unknown tray mode falls back to default");

DashboardSettings iconSettings = new DashboardSettings();
iconSettings.trayIconMode = "percentage";
string iconJson = new JavaScriptSerializer().Serialize(iconSettings);
DashboardSettings iconRoundTrip = new JavaScriptSerializer().Deserialize<DashboardSettings>(iconJson);
Expect(DashboardSettings.NormalizeTrayIconMode(iconRoundTrip.trayIconMode) == TrayIconMode.Percentage,
    "tray mode survives settings round trip");

DateTime now = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Local);
Expect(PresentationText.TMinus(now.AddHours(2).AddMinutes(18), now) == "−02:18",
    "5h reset uses compact t-minus copy");
Expect(PresentationText.TMinus(null, now) == "", "unknown reset time is omitted");
```

- [ ] **Step 2: Run tests and verify the new assertions fail**

Run: `powershell -ExecutionPolicy Bypass -File .\test.ps1`

Expected: compilation fails because `TrayIconMode`, `NormalizeTrayIconMode`, and `PresentationText` do not exist.

- [ ] **Step 3: Add the minimal enum, normalization, and t-minus formatter**

```csharp
enum TrayIconMode { Default, Percentage }

class DashboardSettings
{
    public string trayIconMode = "default";

    public static TrayIconMode NormalizeTrayIconMode(string raw)
    {
        return string.Equals(raw, "percentage", StringComparison.OrdinalIgnoreCase)
            ? TrayIconMode.Percentage : TrayIconMode.Default;
    }
}

static class PresentationText
{
    public static string TMinus(DateTime? resetAt, DateTime now)
    {
        if (!resetAt.HasValue) return "";
        TimeSpan left = resetAt.Value - now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        int hours = (int)Math.Floor(left.TotalHours);
        return "−" + hours.ToString("00") + ":" + left.Minutes.ToString("00");
    }
}
```

Update rendering so Codex five-hour reset uses `PresentationText.TMinus(...)`, Codex weekly copy contains no reset time, MiniMax five-hour remaining time is prefixed with `−` after normalization, and MiniMax weekly copy contains no reset time.

- [ ] **Step 4: Run the full test suite**

Run: `powershell -ExecutionPolicy Bypass -File .\test.ps1`

Expected: `All provider tests passed.`

- [ ] **Step 5: Commit the presentation contract**

```powershell
git add Program.cs ProgramTests.cs
git commit -m "feat: add quiet glass presentation settings"
```

---

### Task 2: Quiet Glass layout and meter hierarchy

**Files:**
- Modify: `Program.cs:702-927, 991-1015, 1135-1242, 1364-1405`
- Test: `ProgramTests.cs:230-270`

**Interfaces:**
- Consumes: `PresentationText.TMinus`, existing `Bindings`, and existing DWM `EnableSystemGlass` path.
- Produces: `QuietGlassPalette.AvailableMeter`, `QuietGlassPalette.UnavailableMeter`, `BuildUiFactory.Build` with Codex-owned token summary.

- [ ] **Step 1: Add failing palette and hierarchy tests**

```csharp
Expect(QuietGlassPalette.AvailableMeter != System.Windows.Media.Colors.Black,
    "available meter is pale grey, not black");
Expect(QuietGlassPalette.UnavailableMeter == System.Windows.Media.Colors.Black,
    "unavailable meter is black");

DashboardSettings allProviders = new DashboardSettings();
BuildResult quietUi = BuildUiFactory.Build(allProviders);
Expect(quietUi.Bindings.codexTokenUsage != null, "token usage belongs to Codex bindings");
Expect(quietUi.Bindings.footerLeft == null, "global footer no longer owns Codex token usage");
Expect(quietUi.Bindings.miniFiveTrack != null && quietUi.Bindings.miniWeekTrack != null,
    "MiniMax retains 5h and weekly meters");
```

- [ ] **Step 2: Run tests and verify failure**

Run: `powershell -ExecutionPolicy Bypass -File .\test.ps1`

Expected: compilation fails for the new palette and binding names.

- [ ] **Step 3: Implement Quiet Glass structure and palette**

Add a centralized palette:

```csharp
static class QuietGlassPalette
{
    public static readonly Color ShellTop = Color.FromArgb(190, 43, 50, 61);
    public static readonly Color ShellBottom = Color.FromArgb(220, 19, 23, 30);
    public static readonly Color AvailableMeter = Color.FromRgb(184, 190, 198);
    public static readonly Color AvailableTrack = Color.FromArgb(52, 204, 210, 218);
    public static readonly Color UnavailableMeter = Colors.Black;
    public static readonly Color Descriptor = Color.FromRgb(137, 149, 165);
}
```

Apply one shell gradient, a one-pixel translucent border, restrained shadow/highlight, and the existing system backdrop. Do not create per-provider blur surfaces. Change `5H`, `W`, and `Balance` to small muted descriptors. Move token usage into a new `Bindings.codexTokenUsage` element directly beneath the Codex composite meter. Keep MiniMax's existing two tracks. Keep the global footer limited to refresh state/provider count.

Change `UpdateMeter` so available fill is pale grey and unavailable fill remains full-width black:

```csharp
Color fillColor = available
    ? (overrideColor ?? QuietGlassPalette.AvailableMeter)
    : QuietGlassPalette.UnavailableMeter;
```

Do not add a 15-minute delta overlay.

- [ ] **Step 4: Run tests and build**

Run: `powershell -ExecutionPolicy Bypass -File .\test.ps1`

Expected: `All provider tests passed.`

Run: `powershell -ExecutionPolicy Bypass -File .\build.ps1`

Expected: `Built ...\dist\CodexDashboard.exe`

- [ ] **Step 5: Commit the Quiet Glass popup**

```powershell
git add Program.cs ProgramTests.cs
git commit -m "feat: apply quiet glass dashboard styling"
```

---

### Task 3: Default and percentage tray icons with safe lifecycle

**Files:**
- Modify: `Program.cs:290-310, 1470-1655`
- Test: `ProgramTests.cs:261-330`

**Interfaces:**
- Consumes: `TrayIconMode`, `DashboardState.Changed`, `QuotaSnapshot.FiveHourAvailable`, and `QuotaSnapshot.FiveHourRemainingPercent`.
- Produces: `TrayIconState.Select(TrayIconMode, QuotaSnapshot)`; `BitmapFactory.CreateDefaultTrayIcon(int)`; `BitmapFactory.CreatePercentageTrayIcon(int, int)`; cached/disposable icon ownership in `TrayController`.

- [ ] **Step 1: Add failing state-selection and rendering tests**

```csharp
TrayIconVisual fallback = TrayIconState.Select(TrayIconMode.Percentage,
    new QuotaSnapshot(false, 55, 70, "missing", null, null));
Expect(fallback.Kind == TrayIconVisualKind.Default, "unavailable 5h falls back to default icon");

TrayIconVisual value100 = TrayIconState.Select(TrayIconMode.Percentage,
    new QuotaSnapshot(true, true, 100, 70, "ok", null, null));
Expect(value100.Kind == TrayIconVisualKind.Percentage && value100.Percent == 100,
    "percentage mode preserves exact 100");

using (System.Drawing.Icon defaultIcon = BitmapFactory.CreateDefaultTrayIcon(16))
using (System.Drawing.Icon percentIcon = BitmapFactory.CreatePercentageTrayIcon(16, 68))
using (System.Drawing.Icon fullIcon = BitmapFactory.CreatePercentageTrayIcon(16, 100))
{
    Expect(defaultIcon.Width == 16 && percentIcon.Width == 16 && fullIcon.Width == 16,
        "tray icon renderers return 16px icons");
}
```

Extend the fake backend to retain the current icon reference and count replacements. Assert repeated identical dashboard state does not call `SetIcon` again, while a percentage change does.

- [ ] **Step 2: Run tests and verify failure**

Run: `powershell -ExecutionPolicy Bypass -File .\test.ps1`

Expected: compilation fails for tray visual types and new bitmap methods.

- [ ] **Step 3: Implement the open gauge ring and state selector**

```csharp
enum TrayIconVisualKind { Default, Percentage }

struct TrayIconVisual
{
    public TrayIconVisualKind Kind;
    public int Percent;
}

static class TrayIconState
{
    public static TrayIconVisual Select(TrayIconMode mode, QuotaSnapshot quota)
    {
        if (mode == TrayIconMode.Percentage && quota != null && quota.FiveHourAvailable)
            return new TrayIconVisual { Kind = TrayIconVisualKind.Percentage,
                Percent = Math.Max(0, Math.Min(100, quota.FiveHourRemainingPercent)) };
        return new TrayIconVisual { Kind = TrayIconVisualKind.Default, Percent = -1 };
    }
}
```

Render the default icon as a low-saturation open circular gauge with a centered dot or short indicator. Render percentage icons with custom compact text metrics; use a dedicated smaller `100` layout. Keep the palette near neutral slate/white so it works on light and dark taskbars. Avoid gradients at 16px.

- [ ] **Step 4: Implement cache and disposal behavior**

Have `TrayController` subscribe to `DashboardState.Changed`, derive the desired `TrayIconVisual`, and update only if it differs from the last visual. Cache immutable render results per visual state. Ensure the real backend disposes the previously assigned owned icon after replacement and disposes the final icon on shutdown. Do not change hover or popup-position code.

Add context-menu radio choices `默认图标` and `Codex 5 小时百分比`; persist the selected `trayIconMode`; refresh immediately after selection.

- [ ] **Step 5: Run tests and inspect GDI stability**

Run: `powershell -ExecutionPolicy Bypass -File .\test.ps1`

Expected: `All provider tests passed.`

Run the built dashboard, trigger at least 100 state refreshes in a diagnostic loop, and compare the process GDI object count before and after. Expected: no unbounded increase; count returns to a stable plateau after icon variants have been cached.

- [ ] **Step 6: Commit tray icon modes**

```powershell
git add Program.cs ProgramTests.cs
git commit -m "feat: add quiet tray icon modes"
```

---

### Task 4: Transparent application icon and build embedding

**Files:**
- Create: `assets/app-icon.svg`
- Create: `assets/app-icon.ico`
- Create: `tools/GenerateAppIcon.ps1`
- Modify: `build.ps1:20-24`
- Test: `ProgramTests.cs` or build verification script assertions

**Interfaces:**
- Consumes: approved open gauge-ring geometry and quiet slate/blue-grey palette.
- Produces: reproducible `assets/app-icon.ico` consumed by `build.ps1 /win32icon:`.

- [ ] **Step 1: Add a failing build precondition**

Add to `build.ps1` before compiler invocation:

```powershell
$appIcon = Join-Path $root "assets\app-icon.ico"
if (!(Test-Path $appIcon)) { throw "Application icon missing: $appIcon" }
```

Run: `powershell -ExecutionPolicy Bypass -File .\build.ps1`

Expected: FAIL because `assets\app-icon.ico` does not yet exist.

- [ ] **Step 2: Create the transparent vector master**

Create an SVG with a transparent canvas, an open 280-degree gauge ring, cool slate/blue-grey glass stroke, soft internal highlight, restrained healthy-green indicator accent, and a subtle filter shadow. Keep all geometry original and readable when simplified.

- [ ] **Step 3: Create a reproducible ICO generator**

Implement `tools/GenerateAppIcon.ps1` using `System.Drawing` to render optical variants at `16, 20, 24, 32, 48, 64, 128, 256`, encode each as PNG, and write a standard ICO directory plus image payloads. Small variants omit shadow and simplify the highlight; sizes 48 and above include the restrained depth treatment. The script writes only `assets/app-icon.ico`.

Run: `powershell -ExecutionPolicy Bypass -File .\tools\GenerateAppIcon.ps1`

Expected: `Generated ...\assets\app-icon.ico (8 sizes)`.

- [ ] **Step 4: Embed and verify the application icon**

Change the compiler invocation to include:

```powershell
/win32icon:"$appIcon"
```

Run: `powershell -ExecutionPolicy Bypass -File .\build.ps1`

Expected: build succeeds and Explorer displays the open gauge-ring icon for `dist\CodexDashboard.exe`.

- [ ] **Step 5: Commit application icon assets**

```powershell
git add assets/app-icon.svg assets/app-icon.ico tools/GenerateAppIcon.ps1 build.ps1
git commit -m "feat: add quiet glass application icon"
```

---

### Task 5: End-to-end regression and documentation

**Files:**
- Modify: `README.md`
- Modify only if a defect is found: `Program.cs`, `ProgramTests.cs`, `build.ps1`, `tools/GenerateAppIcon.ps1`

**Interfaces:**
- Consumes: all deliverables from Tasks 1-4.
- Produces: verified executable and user-facing mode documentation.

- [ ] **Step 1: Document tray icon behavior**

Add concise README guidance explaining the default open gauge icon, the optional Codex 5-hour percentage mode, exact `100` handling, and automatic fallback when five-hour data is unavailable.

- [ ] **Step 2: Run automated verification**

Run: `powershell -ExecutionPolicy Bypass -File .\test.ps1`

Expected: `All provider tests passed.`

Run: `powershell -ExecutionPolicy Bypass -File .\build.ps1`

Expected: `Built ...\dist\CodexDashboard.exe`

Run: `git diff --check`

Expected: no output.

- [ ] **Step 3: Run visual and behavior matrix**

Verify manually:

- Light, dark, and detailed desktop backgrounds.
- Top and bottom taskbars.
- Single-monitor to dual-monitor transition and back.
- 100%, 125%, 150%, and 200% scaling.
- Available pale-grey meters and unavailable solid-black meters.
- Codex token summary inside Codex; MiniMax 5H and W rows.
- T-minus on 5H only; no weekly reset time.
- Default tray icon, percentage values `0`, `68`, and exact `100`, plus unavailable fallback.
- Tray hover remains visible while the cursor moves within the tray target and popup.
- Application icon in Explorer small, medium, large, and shortcut views.

- [ ] **Step 4: Commit documentation or final fixes**

```powershell
git add README.md Program.cs ProgramTests.cs build.ps1 tools/GenerateAppIcon.ps1 assets/app-icon.svg assets/app-icon.ico
git commit -m "docs: describe quiet glass tray modes"
```

- [ ] **Step 5: Final evidence**

Record the exact test output, build output, commit list, remaining visual caveats, and whether the currently running executable was replaced and restarted. Do not claim completion without this evidence.
