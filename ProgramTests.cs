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
        MiniMaxSnapshot quota = MiniMaxQuota.Parse("[{\"model_name\":\"MiniMax-M2\",\"current_interval_remaining_percent\":120,\"current_weekly_remaining_percent\":-4}]", "MiniMax-M*");
        Expect(quota.Available && quota.FiveHourRemainingPercent == 100 && quota.WeeklyRemainingPercent == 0, "MiniMax parses and clamps time-plan percentages");

        DeepSeekSnapshot official = DeepSeekBalance.ParseAndSelect("{\"is_available\":true,\"balance_infos\":[{\"currency\":\"USD\",\"total_balance\":\"2.50\"},{\"currency\":\"CNY\",\"total_balance\":\"37.28\",\"granted_balance\":\"7.28\",\"topped_up_balance\":\"30.00\"}]}", "CNY");
        Expect(official.Available && official.TotalBalance == 37.28m && official.GrantedBalance == 7.28m, "DeepSeek selects requested currency");

        Expect(ProviderMath.RemainingPercent(25m, 0m) == null, "zero reference budget has no percentage");
        Expect(ProviderMath.RemainingPercent(75m, 50m) == 100, "balance percentage clamps high");
        decimal estimate = ProviderMath.EstimatedCostPerRequest(30000, 8000, 1.5m, 0.025m, 3m, 6m);
        Expect(estimate > 0m, "request estimate clamps cache hit ratio");

        MiniMaxCommand cmd = MiniMaxQuota.BuildCommand("C:\\Users\\tf890\\AppData\\Roaming\\npm\\mmx.cmd", "quota show --output json");
        Expect(cmd.FileName == "cmd.exe" && cmd.Arguments.IndexOf("/d /s /c") >= 0 && cmd.Arguments.IndexOf("mmx.cmd") >= 0, "MiniMax cmd shim uses cmd.exe wrapper");

        if (failures != 0) Environment.Exit(1);
        Console.WriteLine("Provider self-tests passed");
    }
}
