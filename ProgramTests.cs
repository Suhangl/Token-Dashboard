using System;

static class ProgramTests
{
    static int failures;

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
        Expect(CodexAppServerQuota.ParseJsonRpcId("not json") == null, "JSON-RPC ignores non-JSON");
        Expect(CodexAppServerQuota.ParseJsonRpcId("{\"id\":1}") == 1, "JSON-RPC id:1 (not target)");

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

        // --- Legacy field tolerance: parse old-format JSON directly via JavaScriptSerializer ---
        string legacyJson = "{\"windowTopmost\":true,\"windowLeft\":100,\"windowTop\":200,\"windowWidth\":400,\"windowHeight\":300,\"glass\":{\"cornerRadius\":10},\"codex\":{\"enabled\":true}}";
        DashboardSettings loaded = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<DashboardSettings>(legacyJson);
        Expect(loaded != null, "legacy settings.json deserializes without exception");
        Expect(loaded.popupDismissDelayMs == 300, "legacy load falls back to default popupDismissDelayMs");
        Expect(!loaded.popupStickyOnLaunch, "legacy load falls back to default popupStickyOnLaunch");

        // --- Settings round-trip preserves popupLeft/Top + sticky + timings (Task 3) ---
        DashboardSettings popupSettings = new DashboardSettings();
        popupSettings.popupLeft = 123.5; popupSettings.popupTop = 678.9; popupSettings.popupStickyOnLaunch = true;
        popupSettings.popupDismissDelayMs = 250; popupSettings.popupHoverDelayMs = 350;
        string popupJson = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(popupSettings);
        DashboardSettings popupBack = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<DashboardSettings>(popupJson);
        Expect(popupBack.popupLeft == 123.5 && popupBack.popupTop == 678.9, "popupLeft/Top round-trip");
        Expect(popupBack.popupStickyOnLaunch, "popupStickyOnLaunch round-trip");
        Expect(popupBack.popupDismissDelayMs == 250 && popupBack.popupHoverDelayMs == 350, "popup timings round-trip");

        // --- BuildUiFactory builds without throwing for any provider mix ---
        DashboardSettings[] configs = new DashboardSettings[]
        {
            new DashboardSettings { codex = new CodexSettings { enabled = true }, minimax = new MiniMaxSettings { enabled = false }, deepseek = new DeepSeekSettings { enabled = false } },
            new DashboardSettings { codex = new CodexSettings { enabled = false }, minimax = new MiniMaxSettings { enabled = true }, deepseek = new DeepSeekSettings { enabled = false } },
            new DashboardSettings { codex = new CodexSettings { enabled = false }, minimax = new MiniMaxSettings { enabled = false }, deepseek = new DeepSeekSettings { enabled = true } },
            new DashboardSettings { codex = new CodexSettings { enabled = true }, minimax = new MiniMaxSettings { enabled = true }, deepseek = new DeepSeekSettings { enabled = true } }
        };
        string[] names = new string[] { "codex only", "minimax only", "deepseek only", "all three" };
        for (int i = 0; i < configs.Length; i++)
        {
            try
            {
                BuildResult result = BuildUiFactory.Build(configs[i]);
                Expect(result.Root != null, "BuildUiFactory " + names[i] + " returns non-null root");
                Expect(result.Bindings != null, "BuildUiFactory " + names[i] + " returns non-null bindings");
                Expect(result.Bindings.stickyPinButton != null, "BuildUiFactory " + names[i] + " populates stickyPinButton");
            }
            catch (Exception ex)
            {
                Expect(false, "BuildUiFactory " + names[i] + " threw: " + ex.Message);
            }
        }

        DashboardSettings pinSettings = new DashboardSettings();
        PopupWindow popup = new PopupWindow(pinSettings);
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
            Expect((string)System.Windows.Controls.ToolTipService.GetToolTip(pinGrid) == "已钉住（点击其他窗口取消）", "sticky pin click sets active tooltip");
            WaitForDispatcher(250);
            Expect(pinFill != null && pinFill.Color == System.Windows.Media.Color.FromRgb(230, 230, 230), "sticky pin animates to filled");
            popup.LeaveSticky();
            Expect(!popup.IsSticky && !popup.Topmost, "LeaveSticky exits sticky mode");
            Expect((string)System.Windows.Controls.ToolTipService.GetToolTip(pinGrid) == "钉住 popup", "LeaveSticky restores inactive tooltip");
            WaitForDispatcher(250);
            Expect(pinFill != null && pinFill.Color == System.Windows.Media.Colors.Transparent, "LeaveSticky animates pin to transparent");
        }
        popup.Close();

        // --- BitmapFactory (Task 5) ---
        System.Drawing.Icon trayIcon = BitmapFactory.CreateTrayIcon(
            System.Drawing.ColorTranslator.FromHtml("#74DE80"),
            System.Drawing.ColorTranslator.FromHtml("#EC5353"));
        Expect(trayIcon != null, "BitmapFactory.CreateTrayIcon returns non-null icon");
        Expect(trayIcon.Width == 16 && trayIcon.Height == 16, "tray icon is 16x16");

        // --- TrayController + PopupWindow integration (Task 6) ---
        // TrayController's timer state machine (hover, dismiss, cursor-poll) drives on
        // the WPF Dispatcher; the brief acknowledges that full coverage needs an
        // interactive smoke run. We CAN still verify the constructor wires
        // backend.SetIcon + backend.Show synchronously with a fake backend.
        FakeBackend trayFake = new FakeBackend();
        DashboardSettings traySettings = new DashboardSettings { codex = new CodexSettings { enabled = true } };
        PopupWindow trayPopup = new PopupWindow(traySettings);
        trayPopup.Show();
        WaitForDispatcher(50);
        int showCountBefore = trayFake.ShowCount;
        int setIconCountBefore = trayFake.SetIconCount;
        TrayController trayController = new TrayController(traySettings, trayFake, trayPopup);
        WaitForDispatcher(20);
        Expect(trayFake.ShowCount == showCountBefore + 1, "TrayController construction calls backend.Show exactly once");
        Expect(trayFake.SetIconCount == setIconCountBefore + 1, "TrayController construction calls backend.SetIcon exactly once");
        Expect(trayController.Popup == trayPopup, "TrayController exposes the Popup instance it was constructed with");
        trayController.Dispose();

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
    public int ShowCount, HideCount, SetIconCount;
    public void Show() { ShowCount++; }
    public void Hide() { HideCount++; }
    public void SetIcon(System.Drawing.Icon icon) { SetIconCount++; }
    public void SetContextMenu(System.Windows.Forms.ContextMenu menu) { }
    public void Dispose() { }
    public void RaiseMouseMove() { var h = MouseMove; if (h != null) h(this, null); }
    public void RaiseMouseLeave() { var h = MouseLeave; if (h != null) h(this, EventArgs.Empty); }
    public void RaiseClick() { var h = Click; if (h != null) h(this, EventArgs.Empty); }
    public void RaiseRightClick() { var h = RightClick; if (h != null) h(this, EventArgs.Empty); }
}
