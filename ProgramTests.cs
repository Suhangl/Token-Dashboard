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

    static void Main()
    {
        // --- MiniMax ---
        MiniMaxSnapshot quota = MiniMaxQuota.Parse("[{\"model_name\":\"MiniMax-M2\",\"current_interval_remaining_percent\":120,\"current_weekly_remaining_percent\":-4}]", "MiniMax-M*");
        Expect(quota.Available && quota.FiveHourRemainingPercent == 100 && quota.WeeklyRemainingPercent == 0, "MiniMax parses and clamps time-plan percentages");
        Expect(quota.Source == ProviderSource.Cli, "MiniMax CLI source is Cli");

        // MiniMax time: string passes through, raw numeric suppressed
        Expect(MiniMaxQuota.MiniMaxTime.Format("2h 34m", "CLI") == "2h 34m", "MiniMax time string pass-through");
        Expect(MiniMaxQuota.MiniMaxTime.Format("13317783", "API") == "", "MiniMax raw numeric suppressed for API");
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

        if (failures != 0) Environment.Exit(1);
        Console.WriteLine("Provider self-tests passed");
    }
}
