using System;

static class ProgramTests
{
    static int failures;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetGuiResources(IntPtr process, int flags);

    static void Expect(bool value, string name)
    {
        if (value) return;
        failures++;
        Console.Error.WriteLine("FAIL " + name);
    }

    static void WaitForDispatcher(int milliseconds)
    {
        System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background);
        timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        timer.Tick += delegate
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    static bool IsDisposedIcon(System.Drawing.Icon icon)
    {
        if (icon == null) return true;
        try
        {
            if (icon.Handle == IntPtr.Zero) return true;
            using (System.Drawing.Bitmap bitmap = icon.ToBitmap()) { }
            return false;
        }
        catch (ObjectDisposedException) { return true; }
        catch (ArgumentException) { return true; }
        catch (System.Runtime.InteropServices.ExternalException) { return true; }
    }

    static System.Windows.Forms.MenuItem FindMenuItem(
        System.Windows.Forms.Menu menu, string text)
    {
        if (menu == null) return null;
        foreach (System.Windows.Forms.MenuItem item in menu.MenuItems)
        {
            if (item.Text == text) return item;
            System.Windows.Forms.MenuItem nested = FindMenuItem(item, text);
            if (nested != null) return nested;
        }
        return null;
    }

    static void RaiseContextMenuPopup(System.Windows.Forms.ContextMenu menu)
    {
        if (menu == null) return;
        System.Reflection.MethodInfo onPopup = typeof(System.Windows.Forms.ContextMenu).GetMethod(
            "OnPopup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        onPopup.Invoke(menu, new object[] { EventArgs.Empty });
    }

    static bool DashboardStateChangedTargets(object target)
    {
        System.Reflection.FieldInfo changed = typeof(DashboardState).GetField(
            "Changed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Action handlers = changed == null ? null : changed.GetValue(null) as Action;
        if (handlers == null) return false;
        foreach (Delegate handler in handlers.GetInvocationList())
            if (object.ReferenceEquals(handler.Target, target)) return true;
        return false;
    }

    static int DashboardStateTrayControllerTargetCount()
    {
        System.Reflection.FieldInfo changed = typeof(DashboardState).GetField(
            "Changed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Action handlers = changed == null ? null : changed.GetValue(null) as Action;
        if (handlers == null) return 0;
        int count = 0;
        foreach (Delegate handler in handlers.GetInvocationList())
            if (handler.Target is TrayController) count++;
        return count;
    }

    static void RaiseDashboardStateChangedFor(object target)
    {
        System.Reflection.FieldInfo changed = typeof(DashboardState).GetField(
            "Changed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Action handlers = changed == null ? null : changed.GetValue(null) as Action;
        if (handlers == null) return;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            if (!object.ReferenceEquals(handler.Target, target)) continue;
            handler.DynamicInvoke();
        }
    }

    static int CurrentGdiResourceCount()
    {
        using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess())
            return GetGuiResources(process.Handle, 0);
    }

    static System.Drawing.Rectangle NonTransparentBounds(System.Drawing.Bitmap bitmap, int alphaThreshold)
    {
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, y).A < alphaThreshold) continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
        return maxX < minX ? System.Drawing.Rectangle.Empty
            : System.Drawing.Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    static int CountOpaqueHighContrastPixels(
        System.Drawing.Bitmap bitmap, int backgroundLuminance, int alphaThreshold, double requiredContrast)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            System.Drawing.Color pixel = bitmap.GetPixel(x, y);
            if (pixel.A < alphaThreshold) continue;
            double foregroundLuminance = (pixel.R + pixel.G + pixel.B) / 3.0;
            double composited = (foregroundLuminance * pixel.A
                + backgroundLuminance * (255 - pixel.A)) / 255.0;
            if (Math.Abs(composited - backgroundLuminance) >= requiredContrast) count++;
        }
        return count;
    }

    static int MaximumOpaqueSaturation(System.Drawing.Bitmap bitmap, int alphaThreshold)
    {
        int maximum = 0;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            System.Drawing.Color pixel = bitmap.GetPixel(x, y);
            if (pixel.A < alphaThreshold) continue;
            int high = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
            int low = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
            maximum = Math.Max(maximum, high - low);
        }
        return maximum;
    }

    static int CountEnclosedTransparentRegions(System.Drawing.Bitmap bitmap, int alphaThreshold)
    {
        bool[,] visited = new bool[bitmap.Width, bitmap.Height];
        int enclosed = 0;
        int[] dx = new[] { -1, 1, 0, 0 };
        int[] dy = new[] { 0, 0, -1, 1 };
        for (int startY = 0; startY < bitmap.Height; startY++)
        for (int startX = 0; startX < bitmap.Width; startX++)
        {
            if (visited[startX, startY] || bitmap.GetPixel(startX, startY).A >= alphaThreshold) continue;
            System.Collections.Generic.Queue<System.Drawing.Point> queue =
                new System.Collections.Generic.Queue<System.Drawing.Point>();
            queue.Enqueue(new System.Drawing.Point(startX, startY));
            visited[startX, startY] = true;
            bool touchesEdge = false;
            while (queue.Count > 0)
            {
                System.Drawing.Point point = queue.Dequeue();
                if (point.X == 0 || point.Y == 0
                    || point.X == bitmap.Width - 1 || point.Y == bitmap.Height - 1)
                    touchesEdge = true;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nextX = point.X + dx[direction];
                    int nextY = point.Y + dy[direction];
                    if (nextX < 0 || nextY < 0 || nextX >= bitmap.Width || nextY >= bitmap.Height) continue;
                    if (visited[nextX, nextY] || bitmap.GetPixel(nextX, nextY).A >= alphaThreshold) continue;
                    visited[nextX, nextY] = true;
                    queue.Enqueue(new System.Drawing.Point(nextX, nextY));
                }
            }
            if (!touchesEdge) enclosed++;
        }
        return enclosed;
    }

    [STAThread]
    static void Main()
    {
        // --- MiniMax ---
        MiniMaxSnapshot quota = MiniMaxQuota.Parse("[{\"model_name\":\"MiniMax-M2\",\"current_interval_remaining_percent\":120,\"current_weekly_remaining_percent\":-4}]", "MiniMax-M*");
        Expect(quota.Available && quota.FiveHourRemainingPercent == 100 && quota.WeeklyRemainingPercent == 0, "MiniMax parses and clamps time-plan percentages");
        Expect(quota.Source == ProviderSource.Cli, "MiniMax CLI source is Cli");

        // MiniMax time: string passes through, raw numeric suppressed
        Expect(MiniMaxQuota.MiniMaxTime.Format("2h 34m", "CLI") == "2h 34m", "MiniMax time string pass-through");
        Expect(MiniMaxQuota.MiniMaxTime.Format("13317783", "API") == "3h 41m", "MiniMax converts ms to readable");
        Expect(MiniMaxQuota.MiniMaxTime.Format(null, "CLI") == "", "MiniMax null time returns empty");

        // Test .cmd wrapper — path does not need to exist on disk
        string testCmd = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CodexDashboard_test", "mmx.cmd");
        MiniMaxCommand cmd = MiniMaxQuota.BuildCommand(testCmd, "quota show --output json");
        Expect(cmd.FileName == "cmd.exe" && cmd.Arguments.IndexOf("/d /s /c") >= 0 && cmd.Arguments.IndexOf("mmx.cmd") >= 0, "MiniMax cmd shim uses cmd.exe wrapper");

        // --- DeepSeek ---
        DeepSeekSnapshot official = DeepSeekBalance.ParseAndSelect("{\"is_available\":true,\"balance_infos\":[{\"currency\":\"USD\",\"total_balance\":\"2.50\"},{\"currency\":\"CNY\",\"total_balance\":\"37.28\",\"granted_balance\":\"7.28\",\"topped_up_balance\":\"30.00\"}]}", "CNY");
        Expect(official.Available && official.TotalBalance == 37.28m && official.GrantedBalance == 7.28m, "DeepSeek selects requested currency");
        Expect(official.Source == ProviderSource.OfficialApi, "DeepSeek API source is OfficialApi");

        // DeepSeek balanceMode normalization
        Expect(DashboardSettings.NormalizeBalanceMode("autoThenManual") == "autoThenManual", "normalize: autoThenManual");
        Expect(DashboardSettings.NormalizeBalanceMode("officialOnly") == "officialOnly", "normalize: officialOnly");
        Expect(DashboardSettings.NormalizeBalanceMode("manualOnly") == "manualOnly", "normalize: manualOnly");
        Expect(DashboardSettings.NormalizeBalanceMode("autoThenManual（优先 API）") == "autoThenManual", "normalize: legacy Chinese description");
        Expect(DashboardSettings.NormalizeBalanceMode("manualOnly（仅手动）") == "manualOnly", "normalize: legacy manualOnly with description");
        Expect(DashboardSettings.NormalizeBalanceMode("garbage") == "autoThenManual", "normalize: unknown falls back");
        Expect(DashboardSettings.NormalizeBalanceMode(null) == "autoThenManual", "normalize: null falls back");
        Expect(DashboardSettings.NormalizeBalanceMode("") == "autoThenManual", "normalize: empty falls back");

        // --- ProviderMath ---
        Expect(ProviderMath.RemainingPercent(25m, 0m) == null, "zero reference budget has no percentage");
        Expect(ProviderMath.RemainingPercent(75m, 50m) == 100, "balance percentage clamps high");
        decimal estimate = ProviderMath.EstimatedCostPerRequest(30000, 8000, 1.5m, 0.025m, 3m, 6m);
        Expect(estimate > 0m, "request estimate clamps cache hit ratio");

        // --- Codex JSON-RPC id parsing ---
        Expect(CodexAppServerQuota.ParseJsonRpcId("{\"id\":2}") == 2, "JSON-RPC id:2");
        Expect(CodexAppServerQuota.ParseJsonRpcId("{\"id\": 2}") == 2, "JSON-RPC id: 2 (spaces)");
        Expect(CodexAppServerQuota.ParseJsonRpcId("{\"result\":{},\"id\":2}") == 2, "JSON-RPC id after result");

        // --- Codex quota Parse: each rate-limit window has independent availability ---
        QuotaSnapshot weeklyOnly = CodexAppServerQuota.ParseForTest("{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":10080,\"resetsAt\":1784557641},\"secondary\":null,\"credits\":{\"hasCredits\":false,\"unlimited\":false,\"balance\":\"0\"},\"planType\":\"plus\"}}}");
        Expect(weeklyOnly != null, "Codex Parse handles weekly-only response");
        Expect(weeklyOnly.Available && weeklyOnly.WeeklyAvailable, "Codex weekly-only keeps the weekly meter available");
        Expect(!weeklyOnly.FiveHourAvailable, "Codex weekly-only marks the missing 5h meter unavailable");
        Expect(weeklyOnly.WeeklyRemainingPercent == 100, "Codex weekly-only: week from primary 0% used");
        Expect(!weeklyOnly.FiveHourResetsAt.HasValue, "Codex weekly-only does not invent a 5h reset time");

        QuotaSnapshot dualWindow = CodexAppServerQuota.ParseForTest("{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300,\"resetsAt\":1784550000},\"secondary\":{\"usedPercent\":10,\"windowDurationMins\":10080,\"resetsAt\":1785550000}}}}");
        Expect(dualWindow != null && dualWindow.FiveHourAvailable && dualWindow.WeeklyAvailable,
            "Codex Parse exposes both standard windows when the backend returns them");
        Expect(dualWindow.FiveHourRemainingPercent == 75 && dualWindow.WeeklyRemainingPercent == 90,
            "Codex Parse computes remaining percentages by duration rather than field order");
        Expect(dualWindow.FiveHourResetsAt.HasValue && dualWindow.WeeklyResetsAt.HasValue,
            "Codex Parse preserves both backend reset times");

        QuotaSnapshot byLimitId = CodexAppServerQuota.ParseForTest("{\"id\":2,\"result\":{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":40,\"windowDurationMins\":300,\"resetsAt\":1784550000},\"secondary\":{\"usedPercent\":20,\"windowDurationMins\":10080,\"resetsAt\":1785550000}}},\"rateLimits\":{\"primary\":{\"usedPercent\":99,\"windowDurationMins\":10080}}}}");
        Expect(byLimitId != null && byLimitId.FiveHourRemainingPercent == 60 && byLimitId.WeeklyRemainingPercent == 80,
            "Codex Parse prioritizes the codex rateLimitsByLimitId bucket over the legacy fallback");

        Expect(PopupWindow.MeterFillWidthForTest(false, 50, 300) == 300,
            "unavailable meter uses a full-width indicator");
        Expect(PopupWindow.MeterFillColorForTest(false, QuietGlassPalette.FiveHourAllowance) == System.Windows.Media.Colors.Black,
            "unavailable meter uses a black indicator");
        Expect(PopupWindow.MeterFillColorForTest(true, QuietGlassPalette.FiveHourAllowance) == System.Windows.Media.Color.FromRgb(67, 214, 164),
            "5H allowance uses the approved mint accent");
        Expect(PopupWindow.MeterFillColorForTest(true, QuietGlassPalette.WeeklyAllowance) == System.Windows.Media.Color.FromRgb(88, 168, 255),
            "weekly allowance uses the approved mist-blue accent");
        Expect(PopupWindow.MeterFillColorForTest(true, QuietGlassPalette.DeepSeekAllowance) == System.Windows.Media.Color.FromRgb(157, 124, 255),
            "DeepSeek balance uses the approved soft-violet accent");
        Expect(QuietGlassPalette.UnavailableAllowance == System.Windows.Media.Colors.Black,
            "quiet glass unavailable allowance fill is black");
        Expect(PopupWindow.ShouldBeTopmostForTest(true, false),
            "visible hover popup is topmost above a maximized app");
        Expect(!PopupWindow.ShouldBeTopmostForTest(false, false),
            "hidden non-sticky popup restores normal z-order");
        Expect(PopupWindow.ShouldBeTopmostForTest(true, true),
            "visible sticky popup remains topmost");
        Expect(CodexAppServerQuota.ParseJsonRpcId("not json") == null, "JSON-RPC ignores non-JSON");
        Expect(CodexAppServerQuota.ParseJsonRpcId("{\"id\":1}") == 1, "JSON-RPC id:1 (not target)");

        // --- Tray popup placement stays in one process-coordinate space ---
        System.Drawing.Rectangle primaryBounds = new System.Drawing.Rectangle(0, 0, 1920, 1080);
        Expect(TrayController.InferTrayEdgeForTest(new System.Drawing.Point(1870, 1068), primaryBounds) == TrayEdge.Bottom,
            "tray edge inference recognizes bottom taskbar");
        Expect(TrayController.InferTrayEdgeForTest(new System.Drawing.Point(1870, 12), primaryBounds) == TrayEdge.Top,
            "tray edge inference recognizes top taskbar");

        System.Drawing.Rectangle bottomWork = new System.Drawing.Rectangle(0, 0, 1920, 1040);
        System.Drawing.Point bottomOrigin = TrayController.CalculatePopupOriginForTest(
            new System.Drawing.Point(1870, 1068), primaryBounds, bottomWork, new System.Drawing.Size(340, 220));
        Expect(bottomOrigin == new System.Drawing.Point(1522, 788),
            "bottom tray places popup above reserved working-area edge in process coordinates");

        System.Drawing.Rectangle topWork = new System.Drawing.Rectangle(0, 40, 1920, 1040);
        System.Drawing.Point topOrigin = TrayController.CalculatePopupOriginForTest(
            new System.Drawing.Point(1870, 12), primaryBounds, topWork, new System.Drawing.Size(340, 220));
        Expect(topOrigin == new System.Drawing.Point(1522, 72),
            "top tray places popup below reserved working-area edge in process coordinates");

        System.Drawing.Rectangle scaledBounds = new System.Drawing.Rectangle(0, 0, 2560, 1440);
        System.Drawing.Point scaledBottom = TrayController.CalculatePopupOriginForTest(
            new System.Drawing.Point(2500, 1428), scaledBounds, scaledBounds, new System.Drawing.Size(510, 330));
        Expect(scaledBottom == new System.Drawing.Point(1982, 1038),
            "150 percent dimensions reserve 64 process-coordinate units for auto-hidden bottom taskbar");
        System.Drawing.Point scaledTop = TrayController.CalculatePopupOriginForTest(
            new System.Drawing.Point(2500, 12), scaledBounds, scaledBounds, new System.Drawing.Size(510, 330));
        Expect(scaledTop == new System.Drawing.Point(1982, 72),
            "150 percent dimensions reserve 64 process-coordinate units for auto-hidden top taskbar");

        System.Drawing.Rectangle slightTopInsetWork = new System.Drawing.Rectangle(0, 1, 1920, 1079);
        System.Drawing.Point slightTopInset = TrayController.CalculatePopupOriginForTest(
            new System.Drawing.Point(1870, 0), primaryBounds, slightTopInsetWork, new System.Drawing.Size(340, 220));
        Expect(slightTopInset.Y == 72,
            "one-unit top inset still keeps the conservative 64-unit reserve");
        System.Drawing.Rectangle slightBottomInsetWork = new System.Drawing.Rectangle(0, 0, 1920, 1078);
        System.Drawing.Point slightBottomInset = TrayController.CalculatePopupOriginForTest(
            new System.Drawing.Point(1870, 1079), primaryBounds, slightBottomInsetWork, new System.Drawing.Size(340, 220));
        Expect(slightBottomInset.Y == 788,
            "two-unit bottom inset still keeps the conservative 64-unit reserve");

        System.Drawing.Rectangle leftBounds = new System.Drawing.Rectangle(-2560, 0, 2560, 1440);
        System.Drawing.Rectangle leftWork = new System.Drawing.Rectangle(-2560, 40, 2560, 1400);
        System.Drawing.Point negativeOrigin = TrayController.CalculatePopupOriginForTest(
            new System.Drawing.Point(-40, 10), leftBounds, leftWork, new System.Drawing.Size(510, 330));
        Expect(negativeOrigin == new System.Drawing.Point(-558, 72),
            "negative-coordinate monitor keeps top popup on the cursor monitor");
        Expect(leftWork.Contains(new System.Drawing.Rectangle(negativeOrigin, new System.Drawing.Size(510, 330))),
            "popup rectangle remains contained by negative-coordinate working area");
        Expect(TrayController.CalculateProbeOriginForTest(leftBounds, leftWork) == new System.Drawing.Point(-2552, 72),
            "first positioning stage moves the HWND into the target monitor before remeasurement");

        System.Windows.Rect virtualDipBounds = new System.Windows.Rect(-1280, 0, 3200, 1080);
        Expect(TrayController.IsSavedDipPositionOnVirtualScreenForTest(-1200, 100, 360, 240, virtualDipBounds),
            "sticky saved DIP position is accepted when visible in the WPF virtual screen");
        Expect(!TrayController.IsSavedDipPositionOnVirtualScreenForTest(2200, 100, 360, 240, virtualDipBounds),
            "sticky saved DIP position is rejected when outside the WPF virtual screen");
        Expect(!TrayController.IsSavedDipPositionOnVirtualScreenForTest(double.NaN, 100, 360, 240, virtualDipBounds),
            "sticky saved DIP position rejects NaN");

        PendingPopupPosition pendingPosition = new PendingPopupPosition();
        Expect(pendingPosition.CaptureIfEligible(true, 120, 240),
            "sticky location change captures a pending position snapshot");
        Expect(!pendingPosition.CaptureIfEligible(false, 900, 700),
            "automatic hover location change is not eligible for persistence");
        double capturedLeft, capturedTop;
        Expect(pendingPosition.TryTake(out capturedLeft, out capturedTop)
            && capturedLeft == 120 && capturedTop == 240,
            "debounce consumes the captured sticky snapshot instead of a later automatic position");
        Expect(!pendingPosition.TryTake(out capturedLeft, out capturedTop),
            "pending sticky snapshot is consumed only once");

        // --- Window persistence ---
        DashboardSettings ds = new DashboardSettings();
        Expect(double.IsNaN(ds.windowLeft) && double.IsNaN(ds.windowTop), "new settings have NaN position (use default)");
        Expect(ds.windowTopmost, "new settings default to topmost");
        Expect(ds.windowWidth == 0 && ds.windowHeight == 0, "new settings have zero stored size");

        // --- Tray + Popup settings defaults ---
        DashboardSettings fresh = new DashboardSettings();
        Expect(fresh.popupDismissDelayMs == 300, "popupDismissDelayMs default 300");
        Expect(fresh.popupHoverDelayMs == 400, "popupHoverDelayMs default 400");
        Expect(double.IsNaN(fresh.popupLeft) && double.IsNaN(fresh.popupTop), "popupLeft/Top default NaN");
        Expect(!fresh.popupStickyOnLaunch, "popupStickyOnLaunch default false");

        // --- Quiet glass presentation settings ---
        Expect(fresh.trayIconMode == "default", "trayIconMode defaults to default");
        Expect(DashboardSettings.NormalizeTrayIconMode("percentage") == TrayIconMode.Percentage,
            "trayIconMode recognizes percentage");
        Expect(DashboardSettings.NormalizeTrayIconMode("PeRcEnTaGe") == TrayIconMode.Percentage,
            "trayIconMode recognition is case-insensitive");
        Expect(DashboardSettings.NormalizeTrayIconMode(null) == TrayIconMode.Default,
            "trayIconMode null normalizes to default");
        Expect(DashboardSettings.NormalizeTrayIconMode("") == TrayIconMode.Default,
            "trayIconMode empty normalizes to default");
        Expect(DashboardSettings.NormalizeTrayIconMode("unknown") == TrayIconMode.Default,
            "trayIconMode unknown normalizes to default");

        // --- Legacy field tolerance: parse old-format JSON directly via JavaScriptSerializer ---
        string legacyJson = "{\"windowTopmost\":true,\"windowLeft\":100,\"windowTop\":200,\"windowWidth\":400,\"windowHeight\":300,\"glass\":{\"cornerRadius\":10},\"codex\":{\"enabled\":true}}";
        DashboardSettings loaded = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<DashboardSettings>(legacyJson);
        Expect(loaded != null, "legacy settings.json deserializes without exception");
        Expect(loaded.popupDismissDelayMs == 300, "legacy load falls back to default popupDismissDelayMs");
        Expect(!loaded.popupStickyOnLaunch, "legacy load falls back to default popupStickyOnLaunch");
        Expect(loaded.trayIconMode == "default", "legacy load falls back to default trayIconMode");

        DashboardSettings trayModeSettings = new DashboardSettings();
        trayModeSettings.trayIconMode = "percentage";
        string trayModeJson = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(trayModeSettings);
        DashboardSettings trayModeBack = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<DashboardSettings>(trayModeJson);
        Expect(trayModeBack.trayIconMode == "percentage", "trayIconMode round-trip");

        DateTime presentationNow = new DateTime(2026, 7, 15, 10, 0, 0);
        Expect(PresentationText.TMinus(presentationNow.AddHours(2).AddMinutes(18), presentationNow) == "\u221202:18",
            "future reset uses compact zero-padded total hours and minutes");
        Expect(PresentationText.TMinus(null, presentationNow) == "", "null reset omits t-minus copy");
        Expect(PresentationText.TMinus(presentationNow.AddSeconds(-1), presentationNow) == "\u221200:00",
            "elapsed reset clamps t-minus to zero");
        Expect(PresentationText.MiniMaxTMinus("2h 34m") == "\u221202:34",
            "MiniMax remaining time converts to compact t-minus copy");
        Expect(PresentationText.MiniMaxTMinus("30s") == "\u221200:00",
            "MiniMax seconds-only remaining time truncates to zero minutes");
        Expect(PresentationText.MiniMaxTMinus("45m 30s") == "\u221200:45",
            "MiniMax seconds do not round the compact minute value");
        Expect(PresentationText.MiniMaxTMinus("") == "", "unknown MiniMax remaining time is omitted");

        // --- Settings round-trip preserves popupLeft/Top + sticky + timings (Task 3) ---
        DashboardSettings popupSettings = new DashboardSettings();
        popupSettings.popupLeft = 123.5; popupSettings.popupTop = 678.9; popupSettings.popupStickyOnLaunch = true;
        popupSettings.popupDismissDelayMs = 250; popupSettings.popupHoverDelayMs = 350;
        string popupJson = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(popupSettings);
        DashboardSettings popupBack = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<DashboardSettings>(popupJson);
        Expect(popupBack.popupLeft == 123.5 && popupBack.popupTop == 678.9, "popupLeft/Top round-trip");
        Expect(popupBack.popupStickyOnLaunch, "popupStickyOnLaunch round-trip");
        Expect(popupBack.popupDismissDelayMs == 250 && popupBack.popupHoverDelayMs == 350, "popup timings round-trip");

        // --- BuildUiFactory exact layout contract for every provider mix ---
        DashboardSettings[] configs = new DashboardSettings[]
        {
            new DashboardSettings { codex = new CodexSettings { enabled = false }, minimax = new MiniMaxSettings { enabled = false }, deepseek = new DeepSeekSettings { enabled = false } },
            new DashboardSettings { codex = new CodexSettings { enabled = true }, minimax = new MiniMaxSettings { enabled = false }, deepseek = new DeepSeekSettings { enabled = false } },
            new DashboardSettings { codex = new CodexSettings { enabled = false }, minimax = new MiniMaxSettings { enabled = true }, deepseek = new DeepSeekSettings { enabled = false } },
            new DashboardSettings { codex = new CodexSettings { enabled = false }, minimax = new MiniMaxSettings { enabled = false }, deepseek = new DeepSeekSettings { enabled = true } },
            new DashboardSettings { codex = new CodexSettings { enabled = true }, minimax = new MiniMaxSettings { enabled = true }, deepseek = new DeepSeekSettings { enabled = false } },
            new DashboardSettings { codex = new CodexSettings { enabled = true }, minimax = new MiniMaxSettings { enabled = false }, deepseek = new DeepSeekSettings { enabled = true } },
            new DashboardSettings { codex = new CodexSettings { enabled = false }, minimax = new MiniMaxSettings { enabled = true }, deepseek = new DeepSeekSettings { enabled = true } },
            new DashboardSettings { codex = new CodexSettings { enabled = true }, minimax = new MiniMaxSettings { enabled = true }, deepseek = new DeepSeekSettings { enabled = true } }
        };
        string[] names = new string[] { "none", "codex only", "minimax only", "deepseek only", "codex + minimax", "codex + deepseek", "minimax + deepseek", "all three" };
        double[] expectedHeights = new double[] { 92, 167, 171, 158, 252, 239, 243, 324 };
        for (int i = 0; i < configs.Length; i++)
        {
            try
            {
                BuildResult result = BuildUiFactory.Build(configs[i]);
                Expect(result.Root != null, "BuildUiFactory " + names[i] + " returns non-null root");
                Expect(result.Bindings != null, "BuildUiFactory " + names[i] + " returns non-null bindings");
                Expect(result.Bindings.stickyPinButton != null, "BuildUiFactory " + names[i] + " populates stickyPinButton");
                Expect(result.Bindings.refreshButton != null
                    && Convert.ToString(result.Bindings.refreshButton.Content) == "Refresh"
                    && Math.Abs(result.Bindings.refreshButton.Width - 96) < 0.01
                    && result.Bindings.refreshButton.Template != null,
                    "footer exposes a fixed-width rounded Refresh button");
                System.Windows.Controls.Border quietShell = result.Root as System.Windows.Controls.Border;
                Expect(quietShell != null && quietShell.Margin.Left == 8 && quietShell.Margin.Top == 8
                    && quietShell.Margin.Right == 8 && quietShell.Margin.Bottom == 8,
                    "Quiet Glass shell reserves an 8px outer shadow gutter on all sides");
                Expect(quietShell != null && quietShell.Effect is System.Windows.Media.Effects.DropShadowEffect,
                    "Quiet Glass shell has a drop shadow effect");
                System.Windows.Controls.Grid layoutGrid = quietShell == null ? null : quietShell.Child as System.Windows.Controls.Grid;
                double rowHeight = 0;
                if (layoutGrid != null) foreach (System.Windows.Controls.RowDefinition definition in layoutGrid.RowDefinitions) rowHeight += definition.Height.Value;
                double actualLayoutHeight = quietShell == null || layoutGrid == null ? -1
                    : quietShell.Margin.Top + quietShell.Margin.Bottom + quietShell.BorderThickness.Top + quietShell.BorderThickness.Bottom
                        + layoutGrid.Margin.Top + layoutGrid.Margin.Bottom + rowHeight;
                Expect(Math.Abs(PopupWindow.DesiredHeight(configs[i]) - expectedHeights[i]) < 0.01,
                    "DesiredHeight is exact for " + names[i]);
                Expect(Math.Abs(actualLayoutHeight - expectedHeights[i]) < 0.01,
                    "built row layout matches DesiredHeight for " + names[i]);
                if (configs[i].codex.enabled)
                {
                    Expect(result.Bindings.codexTokenUsage != null, "Codex section exposes its token usage binding");
                    System.Windows.Controls.Grid codexFiveTrackRow = result.Bindings.fiveTrack.Parent as System.Windows.Controls.Grid;
                    System.Windows.Controls.Grid codexComposite = codexFiveTrackRow == null ? null : codexFiveTrackRow.Parent as System.Windows.Controls.Grid;
                    double codexCompositeHeight = 0;
                    if (codexComposite != null) foreach (System.Windows.Controls.RowDefinition definition in codexComposite.RowDefinitions) codexCompositeHeight += definition.Height.Value;
                    Expect(codexComposite != null && codexComposite.RowDefinitions.Count == 5 && Math.Abs(codexCompositeHeight - 55) < 0.01,
                        "Codex composite has five readable rows totaling 55px");
                    Expect(codexComposite != null && result.Bindings.fiveTrack.Height <= codexComposite.RowDefinitions[1].Height.Value
                        && result.Bindings.weekTrack.Height <= codexComposite.RowDefinitions[2].Height.Value,
                        "Codex tracks do not overlap adjacent composite rows");
                }
                if (configs[i].minimax.enabled)
                {
                    Expect(result.Bindings.miniFiveTrack != null && result.Bindings.miniWeekTrack != null
                        && result.Bindings.miniFivePercent != null && result.Bindings.miniWeekPercent != null,
                        "MiniMax retains both 5H and W tracks and percentages");
                    System.Windows.Controls.Grid miniFiveTrackRow = result.Bindings.miniFiveTrack.Parent as System.Windows.Controls.Grid;
                    System.Windows.Controls.Grid miniComposite = miniFiveTrackRow == null ? null : miniFiveTrackRow.Parent as System.Windows.Controls.Grid;
                    double miniCompositeHeight = 0;
                    if (miniComposite != null) foreach (System.Windows.Controls.RowDefinition definition in miniComposite.RowDefinitions) miniCompositeHeight += definition.Height.Value;
                    Expect(miniComposite != null && miniComposite.RowDefinitions.Count == 4 && Math.Abs(miniCompositeHeight - 55) < 0.01,
                        "MiniMax composite has four readable rows totaling 55px");
                    Expect(miniComposite != null && result.Bindings.miniFiveTrack.Height <= miniComposite.RowDefinitions[1].Height.Value
                        && result.Bindings.miniWeekTrack.Height <= miniComposite.RowDefinitions[2].Height.Value,
                        "MiniMax tracks do not overlap adjacent composite rows");
                }
            }
            catch (Exception ex)
            {
                Expect(false, "BuildUiFactory " + names[i] + " threw: " + ex.Message);
            }
        }

        DashboardSettings pinSettings = new DashboardSettings();
        PopupWindow popup = new PopupWindow(pinSettings);
        Expect(!popup.ShowActivated, "tray popup does not activate or steal focus from the foreground app");
        popup.Show();
        WaitForDispatcher(50);
        System.Windows.Controls.Grid pinGrid = popup.Bindings.stickyPinButton as System.Windows.Controls.Grid;
        System.Windows.Shapes.Ellipse pinEllipse = pinGrid == null ? null : pinGrid.Children[0] as System.Windows.Shapes.Ellipse;
        System.Windows.Media.SolidColorBrush pinFill = pinEllipse == null ? null : pinEllipse.Fill as System.Windows.Media.SolidColorBrush;
        Expect(pinGrid != null && pinEllipse != null && pinFill != null, "sticky pin initializes animatable ellipse fill");
        Expect(pinFill != null && pinFill.Color == System.Windows.Media.Colors.Transparent, "sticky pin starts transparent");
        Expect(pinGrid != null && (string)System.Windows.Controls.ToolTipService.GetToolTip(pinGrid) == "钉住 popup", "sticky pin starts with inactive tooltip");
        if (pinGrid != null)
        {
            System.Windows.Input.MouseButtonEventArgs click = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left);
            click.RoutedEvent = System.Windows.UIElement.MouseLeftButtonDownEvent;
            pinGrid.RaiseEvent(click);
            Expect(click.Handled, "sticky pin handles click");
            Expect(popup.IsSticky && popup.Topmost, "sticky pin click enters sticky mode");
            popup.HandleDeactivated();
            Expect(popup.IsSticky && popup.Topmost, "sticky popup remains pinned and topmost after focus changes");
            Expect((string)System.Windows.Controls.ToolTipService.GetToolTip(pinGrid) == "已钉住（再次点击 📌 取消）", "sticky pin click sets active tooltip");
            WaitForDispatcher(250);
            Expect(pinFill != null && pinFill.Color == System.Windows.Media.Color.FromRgb(230, 230, 230), "sticky pin animates to filled");
            // Click again to unsticky (toggle behavior)
            System.Windows.Input.MouseButtonEventArgs click2 = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left);
            click2.RoutedEvent = System.Windows.UIElement.MouseLeftButtonDownEvent;
            pinGrid.RaiseEvent(click2);
            Expect(click2.Handled, "sticky pin click-again handles event");
            Expect(!popup.IsSticky && !popup.Topmost, "sticky pin second click leaves sticky mode");
            Expect((string)System.Windows.Controls.ToolTipService.GetToolTip(pinGrid) == "钉住 popup", "sticky pin second click restores inactive tooltip");
            WaitForDispatcher(250);
            Expect(pinFill != null && pinFill.Color == System.Windows.Media.Colors.Transparent, "sticky pin second click animates back to transparent");
            popup.LeaveSticky();
            Expect(!popup.IsSticky && !popup.Topmost, "LeaveSticky exits sticky mode");
            Expect((string)System.Windows.Controls.ToolTipService.GetToolTip(pinGrid) == "钉住 popup", "LeaveSticky restores inactive tooltip");
            WaitForDispatcher(250);
            Expect(pinFill != null && pinFill.Color == System.Windows.Media.Colors.Transparent, "LeaveSticky animates pin to transparent");
        }
        popup.Close();

        // --- Quiet tray icon state + rendering ---
        QuotaSnapshot fiveHour72 = new QuotaSnapshot(true, true, 72, 91, "ok", null, null);
        QuotaSnapshot weeklyOnlyQuota = new QuotaSnapshot(false, true, 88, 91, "weekly only", null, null);
        TrayIconVisual defaultVisual = TrayIconState.Select(TrayIconMode.Default, fiveHour72);
        Expect(defaultVisual.Kind == TrayIconVisualKind.Default,
            "default tray mode always selects the default visual");
        TrayIconVisual percentage72 = TrayIconState.Select(TrayIconMode.Percentage, fiveHour72);
        Expect(percentage72.Kind == TrayIconVisualKind.Percentage && percentage72.Percentage == 72,
            "percentage tray mode selects the available Codex 5-hour percentage");
        Expect(TrayIconState.Select(TrayIconMode.Percentage, weeklyOnlyQuota).Kind == TrayIconVisualKind.Default,
            "weekly-only quota falls back immediately to the default tray visual");
        Expect(new TrayIconVisual(TrayIconVisualKind.Percentage, -5).Percentage == 0,
            "tray visual clamps negative percentages to zero");
        Expect(new TrayIconVisual(TrayIconVisualKind.Percentage, 105).Percentage == 100,
            "tray visual clamps percentages above 100");
        Expect(new TrayIconVisual(TrayIconVisualKind.Default, 88).Equals(
            new TrayIconVisual(TrayIconVisualKind.Default, 0)),
            "default tray visuals normalize percentage out of their cache identity");
        TrayIconVisual percentage100 = TrayIconState.Select(
            TrayIconMode.Percentage, new QuotaSnapshot(true, true, 100, 50, "ok", null, null));
        Expect(percentage100.Kind == TrayIconVisualKind.Percentage && percentage100.Percentage == 100,
            "tray visual preserves exact 100");
        TrayIconVisual[] reviewVisuals = new[]
        {
            defaultVisual,
            new TrayIconVisual(TrayIconVisualKind.Percentage, 0),
            new TrayIconVisual(TrayIconVisualKind.Percentage, 68),
            new TrayIconVisual(TrayIconVisualKind.Percentage, 99),
            percentage100
        };
        string[] reviewLabels = new[] { "default", "0", "68", "99", "100" };
        for (int reviewIndex = 0; reviewIndex < reviewVisuals.Length; reviewIndex++)
        {
            using (System.Drawing.Icon reviewIcon = BitmapFactory.CreateTrayIcon(reviewVisuals[reviewIndex], 16))
            using (System.Drawing.Bitmap reviewBitmap = reviewIcon.ToBitmap())
            {
                System.Drawing.Rectangle bounds = NonTransparentBounds(reviewBitmap, 200);
                Expect(!bounds.IsEmpty && bounds.Left > 0 && bounds.Top > 0
                    && bounds.Right < reviewBitmap.Width && bounds.Bottom < reviewBitmap.Height,
                    reviewLabels[reviewIndex] + " tray icon remains inside its 16px canvas");
                int darkContrastPixels = CountOpaqueHighContrastPixels(reviewBitmap, 31, 200, 80);
                int lightContrastPixels = CountOpaqueHighContrastPixels(reviewBitmap, 235, 200, 80);
                Expect(darkContrastPixels >= 16,
                    reviewLabels[reviewIndex] + " tray icon has a discernible light contour on a dark taskbar ("
                    + darkContrastPixels + ")");
                Expect(lightContrastPixels >= 16,
                    reviewLabels[reviewIndex] + " tray icon has a discernible dark contour on a light taskbar ("
                    + lightContrastPixels + ")");
                Expect(MaximumOpaqueSaturation(reviewBitmap, 200) <= 30,
                    reviewLabels[reviewIndex] + " tray icon stays low-saturation");
                if (reviewVisuals[reviewIndex].Percentage == 100)
                    Expect(CountEnclosedTransparentRegions(reviewBitmap, 96) == 2,
                        "exact 100 at 16px contains two enclosed transparent counters");
            }
        }
        Expect(percentage72.Equals(new TrayIconVisual(TrayIconVisualKind.Percentage, 72))
            && percentage72.GetHashCode() == new TrayIconVisual(TrayIconVisualKind.Percentage, 72).GetHashCode(),
            "tray visuals are value-equatable");

        int[] traySizes = new[] { 16, 20, 24, 32 };
        for (int sizeIndex = 0; sizeIndex < traySizes.Length; sizeIndex++)
        {
            using (System.Drawing.Icon sizedIcon = BitmapFactory.CreateTrayIcon(percentage72, traySizes[sizeIndex]))
            {
                Expect(sizedIcon != null && sizedIcon.Width == traySizes[sizeIndex] && sizedIcon.Height == traySizes[sizeIndex],
                    "tray renderer returns the requested deterministic " + traySizes[sizeIndex] + "px icon");
            }
        }
        int invalidTraySizesRejected = 0;
        foreach (int invalidSize in new[] { 15, 64 })
        {
            try
            {
                using (System.Drawing.Icon invalidIcon = BitmapFactory.CreateTrayIcon(percentage72, invalidSize)) { }
            }
            catch (ArgumentOutOfRangeException) { invalidTraySizesRejected++; }
        }
        Expect(invalidTraySizesRejected == 2,
            "tray renderer rejects sizes outside the bounded 16/20/24/32 set");

        Expect(TrayIconSizing.ForScalePercent(100) == 16
            && TrayIconSizing.ForScalePercent(125) == 20
            && TrayIconSizing.ForScalePercent(150) == 24
            && TrayIconSizing.ForScalePercent(200) == 32,
            "tray monitor scale mapping selects 16/20/24/32 at 100/125/150/200 percent");
        Expect(TrayIconSizing.ForScalePercent(112) == 16
            && TrayIconSizing.ForScalePercent(113) == 20
            && TrayIconSizing.ForScalePercent(137) == 20
            && TrayIconSizing.ForScalePercent(138) == 24
            && TrayIconSizing.ForScalePercent(175) == 24
            && TrayIconSizing.ForScalePercent(176) == 32
            && TrayIconSizing.ForScalePercent(50) == 16
            && TrayIconSizing.ForScalePercent(400) == 32,
            "tray monitor scale mapping clamps intermediate and out-of-range percentages to the nearest supported size");
        Expect(TrayIconSizing.ResolvePixelSize(delegate { return 0; }) == 16,
            "invalid monitor scale safely falls back to 100 percent / 16px");
        Expect(TrayIconSizing.ResolvePixelSize(delegate { throw new InvalidOperationException("scale unavailable"); }) == 16,
            "failed monitor scale lookup safely falls back to 100 percent / 16px");

        DashboardSettings failureSettings = new DashboardSettings
        {
            codex = new CodexSettings { enabled = false },
            minimax = new MiniMaxSettings { enabled = false },
            deepseek = new DeepSeekSettings { enabled = false },
            trayIconMode = "default"
        };
        DashboardState.CodexQuota = fiveHour72;
        PopupWindow constructorFailurePopup = new PopupWindow(failureSettings);
        bool constructorFailurePopupClosed = false;
        constructorFailurePopup.Closed += delegate { constructorFailurePopupClosed = true; };
        FakeBackend constructorFailureBackend = new FakeBackend { ThrowOnSetContextMenu = true };
        int traySubscriptionsBeforeFailure = DashboardStateTrayControllerTargetCount();
        bool constructorFailureObserved = false;
        try
        {
            new TrayController(failureSettings, constructorFailureBackend, constructorFailurePopup,
                delegate(DashboardSettings saved) { });
        }
        catch (InvalidOperationException) { constructorFailureObserved = true; }
        Expect(constructorFailureObserved, "SetContextMenu failure escapes TrayController construction");
        Expect(DashboardStateTrayControllerTargetCount() == traySubscriptionsBeforeFailure,
            "failed TrayController construction leaves no static DashboardState subscription");
        Expect(constructorFailurePopupClosed && constructorFailureBackend.DisposeCount == 1,
            "failed TrayController construction rolls back popup and backend resources");
        Expect(constructorFailureBackend.IconHandleWasValidAtDispose
            && IsDisposedIcon(constructorFailureBackend.CurrentIcon),
            "failed TrayController construction releases backend borrowing before cached icons");

        PopupWindow backendDisposePopup = new PopupWindow(failureSettings);
        FakeBackend backendDisposeFailure = new FakeBackend { ThrowOnDispose = true };
        TrayController backendDisposeController = new TrayController(
            failureSettings, backendDisposeFailure, backendDisposePopup, delegate(DashboardSettings saved) { });
        System.Drawing.Icon backendDisposeIcon = backendDisposeFailure.CurrentIcon;
        bool backendDisposeExceptionObserved = false;
        try { backendDisposeController.Dispose(); }
        catch (InvalidOperationException) { backendDisposeExceptionObserved = true; }
        Expect(backendDisposeExceptionObserved && backendDisposeFailure.DisposeCount == 1,
            "backend disposal exception escapes after the backend release attempt");
        Expect(backendDisposeFailure.IconHandleWasValidAtDispose && IsDisposedIcon(backendDisposeIcon),
            "backend disposal exception cannot prevent cached icon release");
        Expect(!DashboardStateChangedTargets(backendDisposeController),
            "backend disposal exception cannot leave a static DashboardState subscription");

        PopupWindow popupCloseFailure = new PopupWindow(failureSettings);
        FakeBackend popupCloseFailureBackend = new FakeBackend();
        TrayController popupCloseFailureController = new TrayController(
            failureSettings, popupCloseFailureBackend, popupCloseFailure, delegate(DashboardSettings saved) { });
        System.Drawing.Icon popupCloseFailureIcon = popupCloseFailureBackend.CurrentIcon;
        System.ComponentModel.CancelEventHandler throwingClosing = delegate { throw new InvalidOperationException("close failed"); };
        popupCloseFailure.Closing += throwingClosing;
        bool popupCloseExceptionObserved = false;
        try { popupCloseFailureController.Dispose(); }
        catch (InvalidOperationException) { popupCloseExceptionObserved = true; }
        Expect(popupCloseExceptionObserved && popupCloseFailureBackend.DisposeCount == 1,
            "popup close exception still reaches backend disposal");
        Expect(popupCloseFailureBackend.IconHandleWasValidAtDispose && IsDisposedIcon(popupCloseFailureIcon),
            "popup close exception cannot prevent cached icon release");
        Expect(!DashboardStateChangedTargets(popupCloseFailureController),
            "popup close exception cannot leave a static DashboardState subscription");
        popupCloseFailure.Closing -= throwingClosing;
        popupCloseFailure.Close();

        // --- TrayController + PopupWindow integration ---
        // TrayController's timer state machine (hover, dismiss, cursor-poll) drives on
        // the WPF Dispatcher; the brief acknowledges that full coverage needs an
        // interactive smoke run. We CAN still verify the constructor wires
        // backend.SetIcon + backend.Show synchronously with a fake backend.
        FakeBackend trayFake = new FakeBackend();
        DashboardSettings traySettings = new DashboardSettings
        {
            codex = new CodexSettings { enabled = false },
            minimax = new MiniMaxSettings { enabled = false },
            deepseek = new DeepSeekSettings { enabled = false },
            trayIconMode = "percentage"
        };
        int traySettingsSaveCount = 0;
        PopupWindow trayPopup = new PopupWindow(traySettings);
        trayPopup.Show();
        WaitForDispatcher(50);
        DashboardState.CodexQuota = fiveHour72;
        Expect(traySettings.trayIconMode == "percentage"
            && DashboardState.CodexQuota.FiveHourAvailable
            && DashboardState.CodexQuota.FiveHourRemainingPercent == 72,
            "tray integration precondition retains percentage mode and 72 percent quota");
        int showCountBefore = trayFake.ShowCount;
        int setIconCountBefore = trayFake.SetIconCount;
        int simulatedTrayScalePercent = 100;
        TrayController trayController = new TrayController(
            traySettings, trayFake, trayPopup, delegate(DashboardSettings saved) { traySettingsSaveCount++; },
            delegate(System.Drawing.Point point) { return simulatedTrayScalePercent; });
        WaitForDispatcher(20);
        Expect(trayFake.ShowCount == showCountBefore + 1, "TrayController construction calls backend.Show exactly once");
        Expect(trayFake.SetIconCount == setIconCountBefore + 1, "TrayController construction calls backend.SetIcon exactly once");
        Expect(trayController.Popup == trayPopup, "TrayController exposes the Popup instance it was constructed with");
        Expect(DashboardStateChangedTargets(trayController),
            "TrayController subscribes to DashboardState.Changed");
        System.Reflection.FieldInfo currentVisualField = typeof(TrayController).GetField(
            "_currentVisual", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        TrayIconVisual constructedVisual = currentVisualField == null
            ? null
            : currentVisualField.GetValue(trayController) as TrayIconVisual;
        Expect(constructedVisual != null && constructedVisual.Kind == TrayIconVisualKind.Percentage
            && constructedVisual.Percentage == 72,
            "TrayController construction derives the configured initial percentage visual");
        System.Drawing.Icon initialBorrowedIcon = trayFake.CurrentIcon;
        DashboardState.CodexQuota = new QuotaSnapshot(true, true, 72, 12, "same displayed 5h", null, null);
        trayController.RefreshIcon();
        Expect(trayFake.SetIconCount == setIconCountBefore + 1,
            "same derived tray visual does not replace the backend icon");
        Expect(object.ReferenceEquals(initialBorrowedIcon, trayFake.CurrentIcon),
            "same derived tray visual reuses the currently borrowed icon");
        Expect(initialBorrowedIcon != null && initialBorrowedIcon.Width == 16,
            "initial 100-percent tray monitor borrows the 16px icon");

        int replacementsBeforeScaleChange = trayFake.SetIconCount;
        simulatedTrayScalePercent = 125;
        trayFake.RaiseMouseMove();
        Expect(trayFake.SetIconCount == replacementsBeforeScaleChange + 1
            && trayFake.CurrentIcon != null && trayFake.CurrentIcon.Width == 20,
            "same visual on a 125-percent tray monitor replaces the backend icon with 20px");
        System.Drawing.Icon scale20Icon = trayFake.CurrentIcon;
        trayFake.RaiseMouseMove();
        Expect(trayFake.SetIconCount == replacementsBeforeScaleChange + 1
            && object.ReferenceEquals(scale20Icon, trayFake.CurrentIcon),
            "repeated mouse movement at the same tray monitor scale stabilizes without replacement");
        setIconCountBefore++;

        DashboardState.CodexQuota = new QuotaSnapshot(true, true, 71, 12, "changed 5h", null, null);
        RaiseDashboardStateChangedFor(trayController);
        Expect(trayFake.SetIconCount == setIconCountBefore + 2,
            "displayed percentage change replaces the backend icon");
        System.Drawing.Icon percentage71Icon = trayFake.CurrentIcon;

        DashboardState.CodexQuota = new QuotaSnapshot(false, true, 71, 99, "weekly only", null, null);
        trayController.RefreshIcon();
        Expect(trayFake.SetIconCount == setIconCountBefore + 3,
            "loss of Codex 5-hour availability replaces percentage with default fallback");
        DashboardState.CodexQuota = new QuotaSnapshot(false, true, 10, 40, "still weekly only", null, null);
        trayController.RefreshIcon();
        Expect(trayFake.SetIconCount == setIconCountBefore + 3,
            "weekly-only changes do not replace an unchanged default fallback");

        System.Windows.Forms.MenuItem defaultModeItem = FindMenuItem(trayFake.ContextMenu, "默认图标");
        System.Windows.Forms.MenuItem percentageModeItem = FindMenuItem(trayFake.ContextMenu, "Codex 5 小时百分比");
        Expect(defaultModeItem != null && percentageModeItem != null,
            "tray menu exposes both required icon mode choices");
        RaiseContextMenuPopup(trayFake.ContextMenu);
        Expect(percentageModeItem != null && percentageModeItem.Checked
            && defaultModeItem != null && !defaultModeItem.Checked,
            "tray menu checks the current normalized percentage mode when opened");
        if (defaultModeItem != null) defaultModeItem.PerformClick();
        Expect(traySettings.trayIconMode == "default" && traySettingsSaveCount == 1,
            "selecting default tray mode updates and persists settings");
        Expect(trayFake.SetIconCount == setIconCountBefore + 3,
            "selecting default while fallback is already visible avoids redundant replacement");
        RaiseContextMenuPopup(trayFake.ContextMenu);
        Expect(defaultModeItem != null && defaultModeItem.Checked
            && percentageModeItem != null && !percentageModeItem.Checked,
            "tray menu refreshes mutually exclusive checks after mode selection");
        if (percentageModeItem != null) percentageModeItem.PerformClick();
        Expect(traySettings.trayIconMode == "percentage" && traySettingsSaveCount == 2,
            "selecting percentage mode keeps and persists the setting while 5-hour data is unavailable");
        DashboardState.CodexQuota = new QuotaSnapshot(true, true, 71, 40, "5h restored", null, null);
        trayController.RefreshIcon();
        Expect(trayFake.SetIconCount == setIconCountBefore + 4
            && object.ReferenceEquals(percentage71Icon, trayFake.CurrentIcon),
            "restored 5-hour data automatically switches to the cached percentage icon");

        int gdiBeforeVariantLoop = CurrentGdiResourceCount();
        int replacementsBeforeVariantLoop = trayFake.SetIconCount;
        for (int percentage = 0; percentage <= 100; percentage++)
        {
            DashboardState.CodexQuota = new QuotaSnapshot(
                true, true, percentage, 40, "diagnostic first pass", null, null);
            trayController.RefreshIcon();
        }
        int gdiAfterFirstVariantLoop = CurrentGdiResourceCount();
        int replacementsAfterFirstVariantLoop = trayFake.SetIconCount;
        for (int percentage = 0; percentage <= 100; percentage++)
        {
            DashboardState.CodexQuota = new QuotaSnapshot(
                true, true, percentage, 40, "diagnostic cached pass", null, null);
            trayController.RefreshIcon();
        }
        int gdiAfterCachedVariantLoop = CurrentGdiResourceCount();
        Expect(replacementsAfterFirstVariantLoop == replacementsBeforeVariantLoop + 101
            && trayFake.SetIconCount == replacementsAfterFirstVariantLoop + 101,
            "changing-state diagnostic performs one replacement per displayed percentage transition");
        Expect(gdiBeforeVariantLoop > 0 && gdiAfterFirstVariantLoop >= gdiBeforeVariantLoop
            && gdiAfterCachedVariantLoop <= gdiAfterFirstVariantLoop + 2,
            "cached percentage pass does not cause unbounded GDI resource growth");

        System.Reflection.FieldInfo dismissField = typeof(TrayController).GetField(
            "_dismissTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        System.Windows.Threading.DispatcherTimer dismissTimer = dismissField == null
            ? null
            : dismissField.GetValue(trayController) as System.Windows.Threading.DispatcherTimer;
        Expect(dismissTimer != null, "TrayController owns a dismiss timer");
        if (dismissTimer != null)
        {
            dismissTimer.Start();
            trayFake.RaiseMouseMove();
            Expect(!dismissTimer.IsEnabled, "NotifyIcon MouseMove cancels a pending dismiss");
        }

        System.Drawing.Point trayAnchor = new System.Drawing.Point(1600, 20);
        Expect(TrayController.IsWithinTrayAnchorForTest(
            new System.Drawing.Point(1606, 24), trayAnchor, 32),
            "small cursor movement remains inside the observed tray icon anchor");
        Expect(!TrayController.IsWithinTrayAnchorForTest(
            new System.Drawing.Point(1660, 80), trayAnchor, 32),
            "cursor movement away from the observed tray icon leaves its anchor");
        System.Drawing.Icon lastBorrowedIcon = trayFake.CurrentIcon;
        int replacementsBeforeDispose = trayFake.SetIconCount;
        trayController.Dispose();
        trayController.Dispose();
        Expect(trayFake.DisposeCount == 1, "TrayController disposal is idempotent for the backend");
        Expect(trayFake.IconHandleWasValidAtDispose,
            "backend is disposed while its borrowed icon is still valid");
        Expect(IsDisposedIcon(lastBorrowedIcon),
            "controller disposes its cached icon resources after backend release");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        int gdiAfterControllerDispose = CurrentGdiResourceCount();
        Expect(gdiAfterControllerDispose <= gdiBeforeVariantLoop + 2,
            "controller disposal releases cached percentage icon GDI resources back to baseline");
        Console.WriteLine("Tray icon diagnostic: replacements="
            + replacementsBeforeVariantLoop + "->" + replacementsAfterFirstVariantLoop + "->"
            + trayFake.SetIconCount + ", GDI=" + gdiBeforeVariantLoop + "->"
            + gdiAfterFirstVariantLoop + "->" + gdiAfterCachedVariantLoop + "->"
            + gdiAfterControllerDispose);
        Expect(!DashboardStateChangedTargets(trayController),
            "disposed controller unsubscribes from dashboard state changes");
        Expect(trayFake.SetIconCount == replacementsBeforeDispose,
            "disposing the controller does not replace the backend icon");

        if (failures != 0) Environment.Exit(1);
        Console.WriteLine("Provider self-tests passed");
    }
}

class FakeBackend : INotifyIconBackend
{
    public event System.Windows.Forms.MouseEventHandler MouseMove;
    public event EventHandler MouseLeave;
    public event EventHandler Click;
    public event EventHandler RightClick;
    public int ShowCount, HideCount, SetIconCount, DisposeCount;
    public bool IconHandleWasValidAtDispose;
    public bool ThrowOnSetContextMenu, ThrowOnDispose;
    public System.Drawing.Icon CurrentIcon;
    public System.Windows.Forms.ContextMenu ContextMenu;
    public void Show() { ShowCount++; }
    public void Hide() { HideCount++; }
    public void SetIcon(System.Drawing.Icon icon) { SetIconCount++; CurrentIcon = icon; }
    public void SetContextMenu(System.Windows.Forms.ContextMenu menu)
    {
        ContextMenu = menu;
        if (ThrowOnSetContextMenu) throw new InvalidOperationException("context menu failed");
    }
    public void Dispose()
    {
        DisposeCount++;
        IconHandleWasValidAtDispose = CurrentIcon != null && CurrentIcon.Handle != IntPtr.Zero;
        if (ThrowOnDispose) throw new InvalidOperationException("backend dispose failed");
    }
    public void RaiseMouseMove() { var h = MouseMove; if (h != null) h(this, null); }
    public void RaiseMouseLeave() { var h = MouseLeave; if (h != null) h(this, EventArgs.Empty); }
    public void RaiseClick() { var h = Click; if (h != null) h(this, EventArgs.Empty); }
    public void RaiseRightClick() { var h = RightClick; if (h != null) h(this, EventArgs.Empty); }
}
