using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

static class NativeSqlite
{
    const int SQLITE_OK = 0, SQLITE_ROW = 100, SQLITE_DONE = 101;
    const int SQLITE_OPEN_READONLY = 0x00000001, SQLITE_OPEN_URI = 0x00000040;
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int numBytes, out IntPtr stmt, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern long sqlite3_column_int64(IntPtr stmt, int col);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_errmsg(IntPtr db);

    public static UsageSnapshot ReadState(string path)
    {
        string uri = "file:" + path.Replace("\\", "/") + "?mode=ro";
        IntPtr db;
        int rc = sqlite3_open_v2(Utf8(uri), out db, SQLITE_OPEN_READONLY | SQLITE_OPEN_URI, IntPtr.Zero);
        if (rc != SQLITE_OK) throw new Exception("sqlite open failed: " + PtrToString(sqlite3_errmsg(db)));
        try
        {
            IntPtr stmt;
            rc = sqlite3_prepare_v2(db, Utf8("select updated_at, coalesce(tokens_used,0) from threads"), -1, out stmt, IntPtr.Zero);
            if (rc != SQLITE_OK) throw new Exception("sqlite prepare failed: " + PtrToString(sqlite3_errmsg(db)));
            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                long total = 0, five = 0, week = 0;
                int rows = 0;
                while ((rc = sqlite3_step(stmt)) == SQLITE_ROW)
                {
                    long updated = sqlite3_column_int64(stmt, 0);
                    long tokens = sqlite3_column_int64(stmt, 1);
                    total += tokens;
                    rows++;
                    DateTimeOffset t = DateTimeOffset.FromUnixTimeSeconds(updated);
                    if (t >= now.AddHours(-5)) five += tokens;
                    if (t >= now.AddDays(-7)) week += tokens;
                }
                if (rc != SQLITE_DONE) throw new Exception("sqlite step failed: " + PtrToString(sqlite3_errmsg(db)));
                return new UsageSnapshot(true, five, week, total, rows, "local sqlite");
            }
            finally { sqlite3_finalize(stmt); }
        }
        finally { sqlite3_close(db); }
    }

    static byte[] Utf8(string s) { byte[] raw = Encoding.UTF8.GetBytes(s); byte[] nul = new byte[raw.Length + 1]; Array.Copy(raw, nul, raw.Length); return nul; }
    static string PtrToString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return "";
        int len = 0; while (Marshal.ReadByte(ptr, len) != 0) len++;
        byte[] bytes = new byte[len]; Marshal.Copy(ptr, bytes, 0, len);
        return Encoding.UTF8.GetString(bytes);
    }
}

class UsageSnapshot
{
    public readonly bool Available;
    public readonly long FiveHourTokens, WeekTokens, TotalTokens;
    public readonly int Threads;
    public readonly string Status;
    public UsageSnapshot(bool available, long fiveHourTokens, long weekTokens, long totalTokens, int threads, string status)
    { Available = available; FiveHourTokens = fiveHourTokens; WeekTokens = weekTokens; TotalTokens = totalTokens; Threads = threads; Status = status; }
}

class QuotaSnapshot
{
    public readonly bool Available;
    public readonly int FiveHourRemainingPercent, WeeklyRemainingPercent;
    public readonly DateTime? FiveHourResetsAt, WeeklyResetsAt;
    public readonly string Status;
    public QuotaSnapshot(bool available, int five, int week, string status, DateTime? fiveReset, DateTime? weekReset)
    {
        Available = available;
        FiveHourRemainingPercent = Clamp(five);
        WeeklyRemainingPercent = Clamp(week);
        Status = status;
        FiveHourResetsAt = fiveReset;
        WeeklyResetsAt = weekReset;
    }
    static int Clamp(int value) { return Math.Max(0, Math.Min(100, value)); }
}

enum ProviderSource { OfficialApi, Cli, Manual, LocalEstimate, Cached }

class MiniMaxSnapshot
{
    public readonly bool Available, IsStale;
    public readonly int FiveHourRemainingPercent, WeeklyRemainingPercent;
    public readonly DateTime LastSuccessAt;
    public readonly string Status, RemainsTime, WeeklyRemainsTime;
    public readonly ProviderSource Source;
    public MiniMaxSnapshot(bool available, int five, int week, bool stale, DateTime success, string status, ProviderSource source, string remainsTime = "", string weeklyRemainsTime = "")
    { Available = available; FiveHourRemainingPercent = ProviderMath.ClampPercent(five); WeeklyRemainingPercent = ProviderMath.ClampPercent(week); IsStale = stale; LastSuccessAt = success; Status = status; Source = source; RemainsTime = remainsTime ?? ""; WeeklyRemainsTime = weeklyRemainsTime ?? ""; }
}

class DeepSeekSnapshot
{
    public readonly bool Available, IsStale;
    public readonly decimal TotalBalance, GrantedBalance, ToppedUpBalance;
    public readonly string Currency, Status;
    public readonly DateTime LastSuccessAt;
    public readonly ProviderSource Source;
    public DeepSeekSnapshot(bool available, decimal total, decimal granted, decimal toppedUp, string currency, bool stale, DateTime success, string status, ProviderSource source)
    { Available = available; TotalBalance = total; GrantedBalance = granted; ToppedUpBalance = toppedUp; Currency = currency ?? "CNY"; IsStale = stale; LastSuccessAt = success; Status = status; Source = source; }
}

static class ProviderMath
{
    public static int ClampPercent(int value) { return Math.Max(0, Math.Min(100, value)); }
    public static int? RemainingPercent(decimal balance, decimal budget)
    {
        if (budget <= 0m) return null;
        return ClampPercent((int)Math.Round(balance / budget * 100m));
    }
    public static decimal EstimatedCostPerRequest(decimal input, decimal output, decimal cacheHitRatio, decimal hitPrice, decimal missPrice, decimal outputPrice)
    {
        if (input < 0m || output < 0m || hitPrice < 0m || missPrice < 0m || outputPrice < 0m) return 0m;
        cacheHitRatio = Math.Max(0m, Math.Min(1m, cacheHitRatio));
        decimal hit = input * cacheHitRatio;
        return hit / 1000000m * hitPrice + (input - hit) / 1000000m * missPrice + output / 1000000m * outputPrice;
    }
}

class HistoryPoint { public DateTime At; public int Percent; public HistoryPoint(DateTime at, int p) { At = at; Percent = p; } }

class HistoryRing
{
    HistoryPoint[] buf; int head, count; readonly int capacity;
    public HistoryRing(int cap) { capacity = Math.Max(1, cap); buf = new HistoryPoint[capacity]; }
    public void Add(DateTime at, int p)
    {
        if (count > 0 && p > buf[(head - 1 + capacity) % capacity].Percent * 2) { Clear(); }
        buf[head] = new HistoryPoint(at, ProviderMath.ClampPercent(p));
        head = (head + 1) % capacity;
        if (count < capacity) count++;
    }
    public void Clear() { head = count = 0; }
    public int? SampleAtSecondsAgo(int seconds)
    {
        if (count == 0) return null;
        DateTime target = DateTime.Now.AddSeconds(-seconds);
        int? best = null; long bestDelta = long.MaxValue;
        for (int i = 0; i < count; i++)
        {
            HistoryPoint pt = buf[(head - 1 - i + capacity) % capacity];
            long delta = Math.Abs((long)(pt.At - target).TotalSeconds);
            if (delta < bestDelta) { bestDelta = delta; best = pt.Percent; }
        }
        return best;
    }
    public int? Current { get { return count > 0 ? buf[(head - 1 + capacity) % capacity].Percent : (int?)null; } }
}

static class BarColors
{
    public static Color ForPercent(int p)
    {
        if (p >= 50) return Color.FromRgb(74, 222, 128);
        if (p >= 20) return Color.FromRgb(245, 180, 55);
        return Color.FromRgb(236, 83, 83);
    }
    public static Color BurnGhost(Color baseColor, int level)
    {
        double factor = level == 1 ? 0.45 : level == 2 ? 0.28 : 0.14;
        return Color.FromArgb((byte)(255 * factor), baseColor.R, baseColor.G, baseColor.B);
    }
}

class MiniMaxSettings { public bool enabled = false; public string mmxPath = ""; public string region = "cn"; public string quotaModelName = "MiniMax-M*"; }
class DeepSeekEstimateSettings { public bool enabled = true; public string model = "deepseek-v4-pro"; public decimal averageInputTokens = 30000m; public decimal averageOutputTokens = 8000m; public decimal cacheHitRatio = 0.3m; }
class TokenPrices { public decimal inputCacheHit; public decimal inputCacheMiss; public decimal output; }
class DeepSeekSettings
{
    public bool enabled = false; public string currency = "CNY"; public string balanceMode = "autoThenManual";
    public decimal manualBalance = 0m; public decimal referenceBudget = 0m; public string pricingLastVerifiedAt = "2026-07-11";
    public DeepSeekEstimateSettings estimate = new DeepSeekEstimateSettings();
    public Dictionary<string, TokenPrices> pricesPerMillionTokens = DefaultPrices();
    public static Dictionary<string, TokenPrices> DefaultPrices()
    {
        return new Dictionary<string, TokenPrices> {
            {"deepseek-v4-flash", new TokenPrices { inputCacheHit = 0.0028m, inputCacheMiss = 0.14m, output = 0.28m }},
            {"deepseek-v4-pro", new TokenPrices { inputCacheHit = 0.003625m, inputCacheMiss = 0.435m, output = 0.87m }}
        };
    }
}
class CodexSettings { public bool enabled = true; }
class GlassSettings { public int cornerRadius = 15; }
class DashboardSettings
{
    public int refreshSeconds = 60;
    public double windowLeft = double.NaN, windowTop = double.NaN, windowWidth, windowHeight;
    public bool windowTopmost = true;
    public CodexSettings codex = new CodexSettings();
    public MiniMaxSettings minimax = new MiniMaxSettings();
    public DeepSeekSettings deepseek = new DeepSeekSettings();
    public GlassSettings glass = new GlassSettings();
    static string FilePath { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexDashboard", "settings.json"); } }
    public static DashboardSettings Load()
    {
        DashboardSettings settings = new DashboardSettings();
        try
        {
            if (File.Exists(FilePath)) settings = new JavaScriptSerializer().Deserialize<DashboardSettings>(File.ReadAllText(FilePath, Encoding.UTF8));
        }
        catch { settings = new DashboardSettings(); }
        if (settings == null) settings = new DashboardSettings();
        if (settings.codex == null) settings.codex = new CodexSettings();
        if (settings.minimax == null) settings.minimax = new MiniMaxSettings();
        if (settings.deepseek == null) settings.deepseek = new DeepSeekSettings();
        if (settings.deepseek.estimate == null) settings.deepseek.estimate = new DeepSeekEstimateSettings();
        if (settings.deepseek.pricesPerMillionTokens == null) settings.deepseek.pricesPerMillionTokens = DeepSeekSettings.DefaultPrices();
        if (settings.glass == null) settings.glass = new GlassSettings();
        if (settings.refreshSeconds < 15) settings.refreshSeconds = 60;
        settings.deepseek.balanceMode = NormalizeBalanceMode(settings.deepseek.balanceMode);
        return settings;
    }

    public static string NormalizeBalanceMode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "autoThenManual";
        if (raw.StartsWith("autoThenManual", StringComparison.OrdinalIgnoreCase)) return "autoThenManual";
        if (raw.StartsWith("officialOnly", StringComparison.OrdinalIgnoreCase)) return "officialOnly";
        if (raw.StartsWith("manualOnly", StringComparison.OrdinalIgnoreCase)) return "manualOnly";
        return "autoThenManual";
    }
    public void Save()
    {
        string dir = System.IO.Path.GetDirectoryName(FilePath); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this), Encoding.UTF8);
    }
    public static string PathForDisplay { get { return FilePath; } }
}

class MiniMaxCommand { public readonly string FileName, Arguments; public MiniMaxCommand(string fileName, string arguments) { FileName = fileName; Arguments = arguments; } }

static class MiniMaxQuota
{
    public static MiniMaxSnapshot Fetch(MiniMaxSettings settings)
    {
        string model = settings == null ? "MiniMax-M*" : settings.quotaModelName;
        string subscriptionKey = WindowsCredentialStore.Read("CodexDashboard.MiniMaxTokenPlan");
        if (!string.IsNullOrWhiteSpace(subscriptionKey))
        {
            try { return MiniMaxRemainsApi.Fetch(subscriptionKey, model); } catch { }
        }
        string path = FindExecutable(settings == null ? "" : settings.mmxPath);
        if (string.IsNullOrEmpty(path)) throw new Exception("MiniMax CLI was not found in this process environment");
        ProcessResult result = ProcessRunner.Run(BuildCommand(path, "quota show --non-interactive --quiet --output json"), 15000, MiniMaxEnvironment());
        if (result.TimedOut) throw new Exception("MiniMax quota command timed out");
        if (result.ExitCode != 0) throw new Exception("MiniMax quota command failed");
        return Parse(result.StandardOutput, model);
    }
    public static MiniMaxCommand BuildCommand(string path, string arguments)
    {
        string extension = System.IO.Path.GetExtension(path ?? "").ToLowerInvariant();
        if (extension == ".cmd" || extension == ".bat") return new MiniMaxCommand("cmd.exe", "/d /s /c \"\"" + path + "\" " + arguments + "\"");
        return new MiniMaxCommand(path, arguments);
    }
    public static MiniMaxSnapshot Parse(string text, string requestedModel)
    {
        object root = new JavaScriptSerializer().DeserializeObject(text);
        List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>(); Walk(root, rows);
        Dictionary<string, object> fallback = null;
        foreach (Dictionary<string, object> row in rows)
        {
            object a, b, name; bool hasA = row.TryGetValue("current_interval_remaining_percent", out a), hasB = row.TryGetValue("current_weekly_remaining_percent", out b);
            if (!hasA || !hasB) continue;
            string model = row.TryGetValue("model_name", out name) ? Convert.ToString(name) : "";
            if (Matches(model, requestedModel)) return Snapshot(row, a, b, "MiniMax CLI", "CLI");
            if (fallback == null && IsLanguagePlan(model)) fallback = row;
        }
        if (fallback != null) return Snapshot(fallback, fallback["current_interval_remaining_percent"], fallback["current_weekly_remaining_percent"], "MiniMax CLI fallback plan", "CLI");
        throw new Exception("MiniMax language plan was not found");
    }
    static MiniMaxSnapshot Snapshot(Dictionary<string, object> row, object five, object week, string status, string timeSource)
    {
        object remains, weeklyRemains; row.TryGetValue("remains_time", out remains); row.TryGetValue("weekly_remains_time", out weeklyRemains);
        return new MiniMaxSnapshot(true, Convert.ToInt32(Math.Round(Convert.ToDecimal(five))), Convert.ToInt32(Math.Round(Convert.ToDecimal(week))), false, DateTime.Now, status, ProviderSource.Cli, MiniMaxTime.Format(remains, timeSource), MiniMaxTime.Format(weeklyRemains, timeSource));
    }

    // Called from MiniMaxRemainsApi (API source): value is raw numeric, unit unconfirmed → "--"
    static MiniMaxSnapshot Snapshot(Dictionary<string, object> row, object five, object week, string status)
    {
        return Snapshot(row, five, week, status, "API");
    }

    public static class MiniMaxTime
    {
        public static string Format(object raw, string source)
        {
            if (raw == null) return "";
            string s = Convert.ToString(raw);
            long val;
            if (!long.TryParse(s, out val)) return s; // already human-readable — pass through
            // Raw numeric with unknown unit — suppress display
            return "";
        }
    }
    static void Walk(object value, List<Dictionary<string, object>> rows)
    {
        Dictionary<string, object> d = value as Dictionary<string, object>;
        if (d != null) { rows.Add(d); foreach (object child in d.Values) Walk(child, rows); return; }
        object[] a = value as object[]; if (a != null) foreach (object child in a) Walk(child, rows);
    }
    static bool Matches(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) pattern = "MiniMax-M*";
        string p = pattern.Replace("*", "");
        return value != null && value.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }
    static bool IsLanguagePlan(string name)
    {
        string n = (name ?? "").ToLowerInvariant();
        return n.IndexOf("video") < 0 && n.IndexOf("speech") < 0 && n.IndexOf("image") < 0 && n.IndexOf("music") < 0;
    }
    static string FindExecutable(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && System.IO.Path.IsPathRooted(configured) && File.Exists(configured)) return configured;
        string fromPath = FindWithWhere("mmx.cmd"); if (!string.IsNullOrEmpty(fromPath)) return fromPath;
        fromPath = FindWithWhere("mmx"); if (!string.IsNullOrEmpty(fromPath)) return fromPath;
        string npm = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "mmx.cmd"); if (File.Exists(npm)) return npm;
        string prefix = NpmPrefix();
        if (!string.IsNullOrEmpty(prefix))
        {
            string candidate = System.IO.Path.Combine(prefix, "mmx.cmd"); if (File.Exists(candidate)) return candidate;
            candidate = System.IO.Path.Combine(prefix, "bin", "mmx.cmd"); if (File.Exists(candidate)) return candidate;
        }
        return "";
    }
    static string FindWithWhere(string name)
    {
        try { ProcessResult r = ProcessRunner.Run("where.exe", name, 3000, MiniMaxEnvironment()); if (r.ExitCode == 0) foreach (string line in r.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) if (File.Exists(line.Trim())) return line.Trim(); } catch { }
        return "";
    }
    static string NpmPrefix() { try { ProcessResult r = ProcessRunner.Run(BuildCommand("npm.cmd", "prefix -g"), 5000, MiniMaxEnvironment()); return r.ExitCode == 0 ? r.StandardOutput.Trim() : ""; } catch { return ""; } }
    public static Dictionary<string, string> MiniMaxEnvironment()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), path = Environment.GetEnvironmentVariable("PATH") ?? "";
        string npmBin = System.IO.Path.Combine(appData, "npm"); if (path.IndexOf(npmBin, StringComparison.OrdinalIgnoreCase) < 0) path = npmBin + ";" + path;
        Dictionary<string, string> values = new Dictionary<string, string>();
        values["USERPROFILE"] = profile; values["HOME"] = profile; values["MMX_CONFIG_DIR"] = System.IO.Path.Combine(profile, ".mmx"); values["APPDATA"] = appData; values["PATH"] = path;
        return values;
    }
}

static class MiniMaxRemainsApi
{
    public static MiniMaxSnapshot Fetch(string subscriptionKey, string modelName)
    {
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://www.minimaxi.com/v1/token_plan/remains");
        request.Method = "GET"; request.Timeout = 15000; request.ReadWriteTimeout = 15000; request.Accept = "application/json";
        request.Headers[HttpRequestHeader.Authorization] = "Bearer " + subscriptionKey;
        try
        {
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                MiniMaxSnapshot result = MiniMaxQuota.Parse(reader.ReadToEnd(), modelName);
                return new MiniMaxSnapshot(result.Available, result.FiveHourRemainingPercent, result.WeeklyRemainingPercent, false, result.LastSuccessAt, "MiniMax Token Plan remains API", ProviderSource.OfficialApi, result.RemainsTime, result.WeeklyRemainsTime);
            }
        }
        catch { throw new Exception("MiniMax Token Plan remains API failed"); }
    }
}

static class WindowsCredentialStore
{
    const int CRED_TYPE_GENERIC = 1, CRED_PERSIST_LOCAL_MACHINE = 2;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Credential { public int Flags, Type; public string TargetName, Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public int CredentialBlobSize; public IntPtr CredentialBlob; public int Persist, AttributeCount; public IntPtr Attributes; public string TargetAlias, UserName; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredWrite(ref Credential credential, int flags);
    [DllImport("advapi32.dll", SetLastError = true)] static extern void CredFree(IntPtr buffer);
    public static string Read(string target)
    {
        IntPtr ptr; if (!CredRead(target, CRED_TYPE_GENERIC, 0, out ptr)) return "";
        try { Credential value = (Credential)Marshal.PtrToStructure(ptr, typeof(Credential)); return value.CredentialBlob == IntPtr.Zero ? "" : Marshal.PtrToStringUni(value.CredentialBlob, value.CredentialBlobSize / 2); }
        finally { CredFree(ptr); }
    }
    public static bool Write(string target, string secret)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(secret ?? ""); IntPtr blob = Marshal.AllocHGlobal(bytes.Length);
        try { Marshal.Copy(bytes, 0, blob, bytes.Length); Credential value = new Credential { Type = CRED_TYPE_GENERIC, TargetName = target, CredentialBlob = blob, CredentialBlobSize = bytes.Length, Persist = CRED_PERSIST_LOCAL_MACHINE, UserName = Environment.UserName }; return CredWrite(ref value, 0); }
        finally { Marshal.FreeHGlobal(blob); }
    }
}

static class DeepSeekBalance
{
    public static DeepSeekSnapshot Fetch(string apiKey, string currency)
    {
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.deepseek.com/user/balance");
        request.Method = "GET"; request.Timeout = 15000; request.ReadWriteTimeout = 15000; request.Accept = "application/json";
        request.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
        try
        {
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream())) return ParseAndSelect(reader.ReadToEnd(), currency);
        }
        catch { throw new Exception("DeepSeek balance request failed"); }
    }
    public static DeepSeekSnapshot ParseAndSelect(string text, string currency)
    {
        Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
        if (root == null || !GetBool(root, "is_available")) throw new Exception("DeepSeek balance unavailable");
        object rowsObject; if (!root.TryGetValue("balance_infos", out rowsObject)) throw new Exception("DeepSeek balance data missing");
        object[] rows = rowsObject as object[]; if (rows == null) throw new Exception("DeepSeek balance data invalid");
        string desired = string.IsNullOrWhiteSpace(currency) ? "CNY" : currency.ToUpperInvariant();
        foreach (object value in rows)
        {
            Dictionary<string, object> row = value as Dictionary<string, object>; if (row == null) continue;
            if (!string.Equals(GetString(row, "currency"), desired, StringComparison.OrdinalIgnoreCase)) continue;
            return new DeepSeekSnapshot(true, GetDecimal(row, "total_balance"), GetDecimal(row, "granted_balance"), GetDecimal(row, "topped_up_balance"), desired, false, DateTime.Now, "DeepSeek official balance", ProviderSource.OfficialApi);
        }
        throw new Exception("DeepSeek requested currency was not found");
    }
    static bool GetBool(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) && Convert.ToBoolean(v); }
    static string GetString(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) ? Convert.ToString(v) : ""; }
    static decimal GetDecimal(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) ? Convert.ToDecimal(v) : 0m; }
}

class ProcessResult
{
    public string StandardOutput = "", StandardError = ""; public int ExitCode = -1; public bool TimedOut;
}
static class ProcessRunner
{
    public static ProcessResult Run(string fileName, string arguments, int timeoutMs)
    { return Run(new MiniMaxCommand(fileName, arguments), timeoutMs, null); }
    public static ProcessResult Run(string fileName, string arguments, int timeoutMs, Dictionary<string, string> environment)
    { return Run(new MiniMaxCommand(fileName, arguments), timeoutMs, environment); }
    public static ProcessResult Run(MiniMaxCommand command, int timeoutMs, Dictionary<string, string> environment)
    {
        ProcessStartInfo psi = new ProcessStartInfo(command.FileName, command.Arguments);
        psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true; psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
        if (environment != null) foreach (KeyValuePair<string, string> value in environment) psi.EnvironmentVariables[value.Key] = value.Value;
        ProcessResult result = new ProcessResult(); StringBuilder output = new StringBuilder(), error = new StringBuilder();
        using (Process process = new Process())
        {
            process.StartInfo = psi;
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) error.AppendLine(e.Data); };
            if (!process.Start()) throw new Exception("process could not start");
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            if (!process.WaitForExit(timeoutMs)) { result.TimedOut = true; try { process.Kill(); } catch { } process.WaitForExit(); }
            result.ExitCode = result.TimedOut ? -1 : process.ExitCode;
        }
        result.StandardOutput = output.ToString(); result.StandardError = error.ToString(); return result;
    }
}

static class CodexAppServerQuota
{

    static string FindCodexExe()
    {
        string env = Environment.GetEnvironmentVariable("CODEX_EXE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        string local = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(local))
        {
            FileInfo best = null;
            foreach (string file in Directory.GetFiles(local, "codex.exe", SearchOption.AllDirectories))
            {
                FileInfo info = new FileInfo(file);
                if (best == null || info.LastWriteTimeUtc > best.LastWriteTimeUtc) best = info;
            }
            if (best != null) return best.FullName;
        }
        // npm global install — codex.cmd in %APPDATA%\npm
        string npmCmd = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd");
        if (File.Exists(npmCmd)) return npmCmd;
        return "codex";
    }

    public static QuotaSnapshot Fetch()
    {
        string exe = FindCodexExe();
        string fileName = exe;
        string args = "app-server --stdio";
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "cmd.exe";
            args = "/d /s /c \"\"" + exe + "\" app-server --stdio";
        }
        ProcessStartInfo psi = new ProcessStartInfo(fileName, args);
        psi.UseShellExecute = false; psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        psi.CreateNoWindow = true; psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_HOME")))
            psi.EnvironmentVariables["CODEX_HOME"] = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

        Process p = null;
        AutoResetEvent got = null;
        string response = null, error = "";
        try
        {
            p = new Process(); got = new AutoResetEvent(false);
            p.StartInfo = psi;
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                int? id = ParseJsonRpcId(e.Data);
                if (id.HasValue && id.Value == 2) { response = e.Data; got.Set(); }
            };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) error += e.Data + "\n"; };
            if (!p.Start()) throw new Exception("codex app-server failed to start");
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex-dashboard\",\"version\":\"1\"},\"capabilities\":{\"experimentalApi\":true,\"requestAttestation\":false,\"optOutNotificationMethods\":[]}}}");
            p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"initialized\",\"params\":{}}");
            p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{}}");
            p.StandardInput.Flush();
            if (!got.WaitOne(15000)) throw new Exception("quota timeout");
            QuotaSnapshot quota = Parse(response);
            if (quota == null) throw new Exception("quota missing 5h/week values");
            return quota;
        }
        finally
        {
            if (got != null) { try { got.Dispose(); } catch { } }
            if (p != null)
            {
                try { p.StandardInput.Close(); } catch { }
                try { if (!p.HasExited) { p.Kill(); p.WaitForExit(3000); } } catch { }
                try { p.Dispose(); } catch { }
            }
        }
    }

    public static int? ParseJsonRpcId(string line)
    {
        try
        {
            Dictionary<string, object> obj = new JavaScriptSerializer().DeserializeObject(line) as Dictionary<string, object>;
            if (obj == null) return null;
            object idObj;
            if (!obj.TryGetValue("id", out idObj)) return null;
            return Convert.ToInt32(idObj);
        }
        catch { return null; }
    }

    static QuotaSnapshot Parse(string text)
    {
        object obj = new JavaScriptSerializer().DeserializeObject(text);
        List<LimitWindow> windows = new List<LimitWindow>();
        Walk(obj, windows);
        int? five = null, week = null; DateTime? fiveReset = null, weekReset = null;
        foreach (LimitWindow w in windows)
        {
            int remaining = Math.Max(0, Math.Min(100, (int)Math.Round(100.0 - w.UsedPercent)));
            if (w.DurationMins == 300) { five = remaining; fiveReset = w.ResetsAt; }
            if (w.DurationMins == 10080) { week = remaining; weekReset = w.ResetsAt; }
        }
        if (!five.HasValue || !week.HasValue) return null;
        return new QuotaSnapshot(true, five.Value, week.Value, "codex app-server", fiveReset, weekReset);
    }

    static void Walk(object obj, List<LimitWindow> windows)
    {
        Dictionary<string, object> d = obj as Dictionary<string, object>;
        if (d != null)
        {
            object duration, used, resetsAt;
            bool hasDuration = d.TryGetValue("windowDurationMins", out duration) || d.TryGetValue("window_minutes", out duration);
            bool hasUsed = d.TryGetValue("usedPercent", out used) || d.TryGetValue("used_percent", out used);
            bool hasReset = d.TryGetValue("resetsAt", out resetsAt) || d.TryGetValue("resets_at", out resetsAt);
            if (hasDuration && hasUsed)
            {
                try { windows.Add(new LimitWindow(Convert.ToInt32(duration), Convert.ToDouble(used), hasReset ? UnixSecondsToLocal(Convert.ToInt64(resetsAt)) : (DateTime?)null)); } catch { }
            }
            foreach (object value in d.Values) Walk(value, windows);
            return;
        }
        object[] a = obj as object[];
        if (a != null) foreach (object value in a) Walk(value, windows);
    }

    static DateTime UnixSecondsToLocal(long seconds) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime(); }
    class LimitWindow { public readonly int DurationMins; public readonly double UsedPercent; public readonly DateTime? ResetsAt; public LimitWindow(int d, double u, DateTime? r) { DurationMins = d; UsedPercent = u; ResetsAt = r; } }
}

class LiquidWindow : Window
{
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    const int DWM_WINDOW_CORNER_PREFERENCE_ROUND = 2;
    const int DWMSBT_TRANSIENTWINDOW = 3;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    enum AccentState { ACCENT_DISABLED = 0, ACCENT_ENABLE_ACRYLICBLURBEHIND = 4 }
    enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public AccentState AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct WindowCompositionAttributeData { public WindowCompositionAttribute Attribute; public IntPtr Data; public int SizeOfData; }
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }

    UsageSnapshot usage = new UsageSnapshot(false, 0, 0, 0, 0, "starting");
    QuotaSnapshot quota = new QuotaSnapshot(false, 0, 0, "starting", null, null);
    MiniMaxSnapshot minimax = new MiniMaxSnapshot(false, 0, 0, false, DateTime.MinValue, "not configured", ProviderSource.Cached);
    DeepSeekSnapshot deepseek = new DeepSeekSnapshot(false, 0m, 0m, 0m, "CNY", false, DateTime.MinValue, "not configured", ProviderSource.Cached);
    DashboardSettings settings;
    DateTime nextRefreshAt = DateTime.Now, lastRefreshAt = DateTime.MinValue;
    bool codexBusy, minimaxBusy, deepseekBusy;
    string dbPath = Environment.ExpandEnvironmentVariables("%USERPROFILE%\\.codex\\state_5.sqlite");
    DispatcherTimer tick;

    const int ResizeGrip = 9;
    const int WM_NCHITTEST = 0x0084;
    const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    TextBlock timeText, codexHeading, miniHeading, deepSeekHeading;
    TextBlock fivePercent, fiveUsed, weekPercent, weekUsed, miniFivePercent, miniFiveUsed, miniWeekPercent, miniWeekUsed, deepSeekPercent, deepSeekUsed, footerLeft, footerRight, footerSub;
    Ellipse statusDot, statusGlow;
    Border shell;
    Border fiveFill, weekFill, fiveTrack, weekTrack, miniFiveFill, miniWeekFill, miniFiveTrack, miniWeekTrack, deepSeekFill, deepSeekTrack;
    HistoryRing codex5hHistory = new HistoryRing(32), codexWeekHistory = new HistoryRing(32);
    HistoryRing minimax5hHistory = new HistoryRing(32), minimaxWeekHistory = new HistoryRing(32);

    public LiquidWindow()
    {
        settings = DashboardSettings.Load();
        Width = settings.windowWidth > 0 ? settings.windowWidth : 360;
        Height = settings.windowHeight > 0 ? settings.windowHeight : DesiredHeight();
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 320; MinHeight = 150;
        ShowInTaskbar = false;
        Topmost = settings.windowTopmost;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        double left = double.IsNaN(settings.windowLeft) ? double.NaN : settings.windowLeft;
        double top = double.IsNaN(settings.windowTop) ? double.NaN : settings.windowTop;
        if (!IsOnScreen(left, top, Width, Height))
        {
            left = SystemParameters.WorkArea.Right - Width - 18;
            top = SystemParameters.WorkArea.Top + 72;
        }
        Left = left; Top = top;
        Closed += delegate
        {
            if (WindowState == WindowState.Normal)
            {
                settings.windowLeft = Left; settings.windowTop = Top;
                settings.windowWidth = Width; settings.windowHeight = Height;
            }
            settings.windowTopmost = Topmost;
            try { settings.Save(); } catch { }
        };
        SourceInitialized += delegate
        {
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source != null) source.AddHook(WndProc);
            EnableSystemGlass();
        };
        MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
        {
            if (IsOnResizeEdge(e.GetPosition(this))) return;
            try { DragMove(); } catch { }
        };

        ContextMenu menu = new ContextMenu();
        MenuItem refresh = new MenuItem(); refresh.Header = "刷新"; refresh.Click += delegate { StartRefresh(); };
        MenuItem providerSettings = new MenuItem(); providerSettings.Header = "设置..."; providerSettings.Click += delegate { ShowProviderSettings(); };
        MenuItem topmost = new MenuItem(); topmost.Header = "置顶"; topmost.Click += delegate { Topmost = !Topmost; };
        MenuItem exit = new MenuItem(); exit.Header = "退出"; exit.Click += delegate { Close(); };
        menu.Items.Add(refresh); menu.Items.Add(providerSettings); menu.Items.Add(new Separator()); menu.Items.Add(topmost); menu.Items.Add(exit);
        ContextMenu = menu;

        Content = BuildUi();
        ApplyPercentEffects();
        tick = new DispatcherTimer();
        tick.Interval = TimeSpan.FromSeconds(1);
        tick.Tick += delegate { if (DateTime.Now >= nextRefreshAt) StartRefresh(); else Render(); };
        tick.Start();
        StartRefresh();
    }

    UIElement BuildUi()
    {
        shell = new Border();
        shell.CornerRadius = new CornerRadius(15);
        // Background is set below with glass settings applied
        // BorderBrush/Thickness removed — the drop shadow + dark gradient define the edge.
        shell.ClipToBounds = true;
        shell.SnapsToDevicePixels = true;

        // Content grid uses Margin (not shell.Padding) so the glass overlay
        // below can extend to the shell's exact rounded outer edge — keeps
        // the highlight perfectly aligned with the panel.
        Grid grid = new Grid();
        grid.Margin = new Thickness(20, 16, 20, 14);
        int row = 0;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

        Grid header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition());
        Grid lamp = new Grid { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = false };
        statusGlow = new Ellipse { Width = 17, Height = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        statusDot = new Ellipse { Width = 5.5, Height = 5.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        lamp.Children.Add(statusGlow); lamp.Children.Add(statusDot);
        timeText = Text("", 12, 0, 170, 184, 204, FontWeights.SemiBold);
        timeText.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(lamp); Grid.SetColumn(timeText, 1); header.Children.Add(timeText);
        Grid.SetRow(header, row++); grid.Children.Add(header);

        // ===== CODEX section =====
        if (settings.codex.enabled)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
            codexHeading = AddSectionHeader(grid, row++, "CODEX");
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
            AddDivider(grid, row++);
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(55) });
            AddCompositeBar(grid, row++, out fivePercent, out fiveUsed, out fiveTrack, out fiveFill, out weekPercent, out weekUsed, out weekTrack, out weekFill);
        }

        // ===== MINIMAX section =====
        if (settings.minimax.enabled)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(settings.codex.enabled ? 10 : 4) });
            row++;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
            miniHeading = AddSectionHeader(grid, row++, "MINIMAX");
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
            AddDivider(grid, row++);
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(55) });
            AddCompositeBar(grid, row++, out miniFivePercent, out miniFiveUsed, out miniFiveTrack, out miniFiveFill, out miniWeekPercent, out miniWeekUsed, out miniWeekTrack, out miniWeekFill);
        }

        // ===== DEEPSEEK section =====
        if (settings.deepseek.enabled)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength((settings.codex.enabled || settings.minimax.enabled) ? 10 : 4) });
            row++;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
            deepSeekHeading = AddSectionHeader(grid, row++, "DEEPSEEK");
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
            AddDivider(grid, row++);
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            AddMeter(grid, row++, "Balance", out deepSeekPercent, out deepSeekUsed, out deepSeekTrack, out deepSeekFill);
        }

        Grid footer = new Grid();
        footer.RowDefinitions.Add(new RowDefinition()); footer.RowDefinitions.Add(new RowDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition());
        footerLeft = Text("", 11, 0, 150, 164, 184, FontWeights.Normal);
        footerRight = Text("", 11, 0, 158, 174, 196, FontWeights.Normal); footerRight.HorizontalAlignment = HorizontalAlignment.Right;
        footerSub = Text("", 10.5, 0, 110, 124, 146, FontWeights.Normal); footerSub.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(footerRight, 1); footer.Children.Add(footerLeft); footer.Children.Add(footerRight);
        Grid.SetRow(footerSub, 1); Grid.SetColumnSpan(footerSub, 2); footer.Children.Add(footerSub);
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        Grid.SetRow(footer, row); grid.Children.Add(footer);

        int cr = Math.Max(0, Math.Min(30, settings.glass.cornerRadius));
        shell.CornerRadius = new CornerRadius(cr);
        shell.Background = new SolidColorBrush(Color.FromArgb(112, 28, 33, 48));
        shell.Child = grid;
        return shell;
    }

    void ApplyPercentEffects()
    {
        if (settings.codex.enabled) { fivePercent.Effect = PercentEffect(); weekPercent.Effect = PercentEffect(); }
        if (settings.minimax.enabled) { miniFivePercent.Effect = PercentEffect(); miniWeekPercent.Effect = PercentEffect(); }
        if (settings.deepseek.enabled) deepSeekPercent.Effect = PercentEffect();
    }

    static DropShadowEffect PercentEffect() { return new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.28 }; }

    double DesiredHeight()
    {
        double h = 62; // header (24) + footer (38)
        if (settings != null && settings.codex.enabled) h += 17 + 3 + 55;
        if (settings != null && settings.minimax.enabled) h += 10 + 17 + 3 + 55;
        if (settings != null && settings.deepseek.enabled) h += ((settings.codex.enabled || settings.minimax.enabled) ? 10 : 4) + 17 + 3 + 42;
        return h + 30; // grid margin (16+14)
    }

    void ShowProviderSettings()
    {
        DashboardSettings candidate = DashboardSettings.Load();
        Window dialog = new Window { Title = "设置", Width = 460, Height = 700, MinWidth = 400, MinHeight = 540, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.CanResize, Background = new SolidColorBrush(Color.FromRgb(31, 36, 48)), Foreground = Brushes.White };
        ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        StackPanel panel = new StackPanel { Margin = new Thickness(20) }; scroll.Content = panel; dialog.Content = scroll;

        // ===== CODEX =====
        panel.Children.Add(Section("Codex"));
        CheckBox codexEnabled = Check("显示 Codex 用量（本地 SQLite + app-server）", candidate.codex.enabled); panel.Children.Add(codexEnabled);

        // ===== MINIMAX =====
        panel.Children.Add(Section("MiniMax（海螺 AI）"));
        CheckBox miniEnabled = Check("启用", candidate.minimax.enabled); panel.Children.Add(miniEnabled);
        TextBox mmxPath = Field(panel, "mmx CLI 路径（留空自动查找 %APPDATA%\\npm\\mmx.cmd）", candidate.minimax.mmxPath);
        TextBox miniModel = Field(panel, "模型匹配（如 MiniMax-M*）", candidate.minimax.quotaModelName);
        bool hasMiniKey = !string.IsNullOrEmpty(WindowsCredentialStore.Read("CodexDashboard.MiniMaxTokenPlan"));
        PasswordBox miniSubscriptionKey = SecretField(panel, "Token Plan 订阅密钥" + (hasMiniKey ? "（已保存）" : "（未设置 — 从海螺控制台获取）"));

        // ===== DEEPSEEK =====
        panel.Children.Add(Section("DeepSeek"));
        panel.Children.Add(new TextBlock { Text = "API Key 获取：platform.deepseek.com → API Keys", FontSize = 10, Opacity = 0.55, Margin = new Thickness(0, 0, 0, 6) });
        CheckBox deepEnabled = Check("启用", candidate.deepseek.enabled); panel.Children.Add(deepEnabled);
        bool hasDeepKey = !string.IsNullOrEmpty(WindowsCredentialStore.Read("CodexDashboard.DeepSeekApiKey"));
        PasswordBox deepApiKey = SecretField(panel, "API Key" + (hasDeepKey ? "（已保存）" : "（未设置）"));
        ComboBox mode = Choice(panel, "余额来源", new[] { "autoThenManual", "officialOnly", "manualOnly" }, candidate.deepseek.balanceMode);
        ComboBox currency = Choice(panel, "货币", new[] { "CNY", "USD" }, candidate.deepseek.currency);
        TextBox manual = Field(panel, "手动余额（API 不可用时回退）", candidate.deepseek.manualBalance.ToString("0.####"));
        TextBox budget = Field(panel, "预算上限（满额参考值）", candidate.deepseek.referenceBudget.ToString("0.####"));

        StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        Button cancel = new Button { Content = "取消", MinWidth = 76, Margin = new Thickness(0, 0, 8, 0) };
        Button save = new Button { Content = "保存", MinWidth = 76, IsDefault = true };
        cancel.Click += delegate { dialog.Close(); };
        save.Click += delegate
        {
            decimal manualValue, budgetValue;
            if (!TryDecimal(manual.Text, out manualValue) || !TryDecimal(budget.Text, out budgetValue) || manualValue < 0m || budgetValue < 0m)
            { MessageBox.Show(dialog, "余额和预算必须为非负数。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!string.IsNullOrEmpty(miniSubscriptionKey.Password) && !WindowsCredentialStore.Write("CodexDashboard.MiniMaxTokenPlan", miniSubscriptionKey.Password))
            { MessageBox.Show(dialog, "MiniMax 密钥保存失败。", "设置", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            if (!string.IsNullOrEmpty(deepApiKey.Password) && !WindowsCredentialStore.Write("CodexDashboard.DeepSeekApiKey", deepApiKey.Password))
            { MessageBox.Show(dialog, "DeepSeek API Key 保存失败。", "设置", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            candidate.codex.enabled = codexEnabled.IsChecked == true;
            candidate.minimax.enabled = miniEnabled.IsChecked == true; candidate.minimax.mmxPath = mmxPath.Text.Trim(); candidate.minimax.quotaModelName = miniModel.Text.Trim();
            candidate.deepseek.enabled = deepEnabled.IsChecked == true; candidate.deepseek.balanceMode = DashboardSettings.NormalizeBalanceMode(Convert.ToString(mode.SelectedItem)); candidate.deepseek.currency = Convert.ToString(currency.SelectedItem); candidate.deepseek.manualBalance = manualValue; candidate.deepseek.referenceBudget = budgetValue;
            try { candidate.Save(); } catch { MessageBox.Show(dialog, "设置保存失败，请检查磁盘权限。", "设置", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            settings = candidate; Height = DesiredHeight(); Content = BuildUi(); ApplyPercentEffects(); StartRefresh(); dialog.Close();
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons); dialog.ShowDialog();
    }

    static TextBlock Section(string text) { return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(133, 181, 255)), Margin = new Thickness(0, 16, 0, 6) }; }
    static CheckBox Check(string text, bool value) { return new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 3, 0, 4) }; }
    static TextBox Field(StackPanel parent, string label, string value) { parent.Children.Add(new TextBlock { Text = label, Opacity = 0.72, Margin = new Thickness(0, 7, 0, 2) }); TextBox box = new TextBox { Text = value ?? "", Padding = new Thickness(6, 3, 6, 3) }; parent.Children.Add(box); return box; }
    static PasswordBox SecretField(StackPanel parent, string label) { parent.Children.Add(new TextBlock { Text = label, Opacity = 0.72, Margin = new Thickness(0, 7, 0, 2) }); PasswordBox box = new PasswordBox { Padding = new Thickness(6, 3, 6, 3) }; parent.Children.Add(box); return box; }
    static ComboBox Choice(StackPanel parent, string label, string[] values, string selected) { parent.Children.Add(new TextBlock { Text = label, Opacity = 0.72, Margin = new Thickness(0, 7, 0, 2) }); ComboBox box = new ComboBox { Padding = new Thickness(4, 2, 4, 2) }; foreach (string value in values) box.Items.Add(value); box.SelectedItem = Array.IndexOf(values, selected) >= 0 ? selected : values[0]; parent.Children.Add(box); return box; }
    static bool TryDecimal(string text, out decimal value) { return decimal.TryParse(text, out value) || decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value); }

    void AddCompositeBar(Grid parent, int row, out TextBlock fivePct, out TextBlock fiveUsed, out Border fiveTrack, out Border fiveFill,
                         out TextBlock weekPct, out TextBlock weekUsed, out Border weekTrack, out Border weekFill)
    {
        Grid block = new Grid();
        block.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        block.ColumnDefinitions.Add(new ColumnDefinition());
        block.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        block.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        block.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });   // 5H text
        block.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });    // 5H bar
        block.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });    // W bar
        block.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });   // W text

        // 5H text row
        TextBlock fLabel = Text("5H", 13, 0, 232, 240, 250, FontWeights.SemiBold);
        fivePct = Text("--", 14, 0, 248, 251, 255, FontWeights.Bold); fivePct.HorizontalAlignment = HorizontalAlignment.Right;
        fiveUsed = Text("", 10, 0, 145, 160, 182, FontWeights.Normal); fiveUsed.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(fiveUsed, 2); Grid.SetColumn(fivePct, 3);
        Grid.SetRow(fLabel, 0); Grid.SetRow(fiveUsed, 0); Grid.SetRow(fivePct, 0);
        block.Children.Add(fLabel); block.Children.Add(fiveUsed); block.Children.Add(fivePct);

        // 5H bar
        fiveTrack = BarTrack(8); fiveFill = BarFill(8);
        Grid bar5 = new Grid(); bar5.Children.Add(fiveTrack); bar5.Children.Add(fiveFill);
        Grid.SetRow(bar5, 1); Grid.SetColumnSpan(bar5, 4); block.Children.Add(bar5);

        // W bar
        weekTrack = BarTrack(4); weekFill = BarFill(4);
        Grid barW = new Grid(); barW.Children.Add(weekTrack); barW.Children.Add(weekFill);
        Grid.SetRow(barW, 2); Grid.SetColumnSpan(barW, 4); block.Children.Add(barW);

        // W text row
        TextBlock wLabel = Text("W", 11, 0, 170, 184, 204, FontWeights.Normal);
        weekPct = Text("--", 12, 0, 210, 220, 230, FontWeights.Bold); weekPct.HorizontalAlignment = HorizontalAlignment.Right;
        weekUsed = Text("", 10, 0, 145, 160, 182, FontWeights.Normal); weekUsed.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(weekUsed, 2); Grid.SetColumn(weekPct, 3);
        Grid.SetRow(wLabel, 3); Grid.SetRow(weekUsed, 3); Grid.SetRow(weekPct, 3);
        block.Children.Add(wLabel); block.Children.Add(weekUsed); block.Children.Add(weekPct);

        Grid.SetRow(block, row); parent.Children.Add(block);
    }
    Border BarTrack(int h) { return new Border { Height = h, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(50, 80, 90, 110)), VerticalAlignment = VerticalAlignment.Top }; }
    Border BarFill(int h) { return new Border { Height = h, CornerRadius = new CornerRadius(2), Width = 4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }; }

    void AddMeter(Grid parent, int row, string label, out TextBlock pct, out TextBlock used, out Border track, out Border fill, int barH = 6)
    {
        Grid line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        line.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        line.RowDefinitions.Add(new RowDefinition { Height = new GridLength(barH + 4) });

        TextBlock lt = Text(label, 13, 0, 232, 240, 250, FontWeights.SemiBold);
        pct = Text("--", 14, 0, 248, 251, 255, FontWeights.Bold); pct.HorizontalAlignment = HorizontalAlignment.Right;
        used = Text("", 10, 0, 145, 160, 182, FontWeights.Normal); used.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(used, 2); Grid.SetColumn(pct, 3);
        line.Children.Add(lt); line.Children.Add(used); line.Children.Add(pct);

        track = BarTrack(barH); fill = BarFill(barH);
        Grid bg = new Grid(); bg.Children.Add(track); bg.Children.Add(fill);
        Grid.SetRow(bg, 1); Grid.SetColumnSpan(bg, 4); line.Children.Add(bg);

        Grid.SetRow(line, row); parent.Children.Add(line);
    }

    TextBlock AddSectionHeader(Grid parent, int row, string label)
    {
        TextBlock heading = Text(label, 10.5, 0, 130, 147, 170, FontWeights.SemiBold);
        heading.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetRow(heading, row); parent.Children.Add(heading);
        return heading;
    }

    void AddDivider(Grid parent, int row)
    {
        Border line = new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), Margin = new Thickness(0, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(line, row); parent.Children.Add(line);
    }

    TextBlock Text(string text, double size, byte a, byte r, byte g, byte b, FontWeight weight)
    {
        return new TextBlock { Text = text, FontSize = size, Foreground = new SolidColorBrush(Color.FromArgb(a == 0 ? (byte)255 : a, r, g, b)), FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
    }

    void StartRefresh()
    {
        nextRefreshAt = DateTime.Now.AddSeconds(settings.refreshSeconds);
        if (settings.codex.enabled && !codexBusy) RefreshCodex();
        if (settings.minimax.enabled && !minimaxBusy) RefreshMiniMax();
        if (settings.deepseek.enabled && !deepseekBusy) RefreshDeepSeek();
        Render();
    }

    void RefreshCodex()
    {
        codexBusy = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            UsageSnapshot nextUsage; QuotaSnapshot nextQuota;
            try { nextUsage = NativeSqlite.ReadState(dbPath); } catch (Exception ex) { nextUsage = new UsageSnapshot(false, 0, 0, 0, 0, ex.Message); }
            try { nextQuota = CodexAppServerQuota.Fetch(); } catch (Exception ex) { nextQuota = new QuotaSnapshot(false, quota.FiveHourRemainingPercent, quota.WeeklyRemainingPercent, ex.Message, quota.FiveHourResetsAt, quota.WeeklyResetsAt); }
            Dispatcher.BeginInvoke(new Action(delegate { usage = nextUsage; quota = nextQuota; codexBusy = false; lastRefreshAt = DateTime.Now; if (quota.Available) { codex5hHistory.Add(DateTime.Now, quota.FiveHourRemainingPercent); codexWeekHistory.Add(DateTime.Now, quota.WeeklyRemainingPercent); } Render(); }));
        });
    }

    void RefreshMiniMax()
    {
        minimaxBusy = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            MiniMaxSnapshot next;
            try { next = MiniMaxQuota.Fetch(settings.minimax); }
            catch (Exception ex) { next = new MiniMaxSnapshot(minimax.Available, minimax.FiveHourRemainingPercent, minimax.WeeklyRemainingPercent, minimax.Available, minimax.LastSuccessAt, SafeProviderError(ex), ProviderSource.Cached); }
            Dispatcher.BeginInvoke(new Action(delegate { minimax = next; minimaxBusy = false; if (minimax.Available) { minimax5hHistory.Add(DateTime.Now, minimax.FiveHourRemainingPercent); minimaxWeekHistory.Add(DateTime.Now, minimax.WeeklyRemainingPercent); } Render(); }));
        });
    }

    void RefreshDeepSeek()
    {
        deepseekBusy = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            DeepSeekSnapshot next = null;
            string key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            if (string.IsNullOrWhiteSpace(key)) key = WindowsCredentialStore.Read("CodexDashboard.DeepSeekApiKey");
            bool allowOfficial = !string.Equals(settings.deepseek.balanceMode, "manualOnly", StringComparison.OrdinalIgnoreCase);
            if (allowOfficial && !string.IsNullOrWhiteSpace(key))
            {
                try { next = DeepSeekBalance.Fetch(key, settings.deepseek.currency); }
                catch (Exception ex) { if (string.Equals(settings.deepseek.balanceMode, "officialOnly", StringComparison.OrdinalIgnoreCase)) next = new DeepSeekSnapshot(deepseek.Available, deepseek.TotalBalance, deepseek.GrantedBalance, deepseek.ToppedUpBalance, deepseek.Currency, deepseek.Available, deepseek.LastSuccessAt, SafeProviderError(ex), ProviderSource.Cached); }
            }
            if (next == null && !string.Equals(settings.deepseek.balanceMode, "officialOnly", StringComparison.OrdinalIgnoreCase))
                next = new DeepSeekSnapshot(true, settings.deepseek.manualBalance, 0m, settings.deepseek.manualBalance, settings.deepseek.currency, false, DateTime.Now, "DeepSeek manual balance", ProviderSource.Manual);
            if (next == null) next = new DeepSeekSnapshot(deepseek.Available, deepseek.TotalBalance, deepseek.GrantedBalance, deepseek.ToppedUpBalance, deepseek.Currency, deepseek.Available, deepseek.LastSuccessAt, "DeepSeek API key is not available", ProviderSource.Cached);
            Dispatcher.BeginInvoke(new Action(delegate { deepseek = next; deepseekBusy = false; Render(); }));
        });
    }

    static string SafeProviderError(Exception ex)
    {
        string message = ex == null ? "provider failed" : ex.Message;
        return message.Length > 120 ? message.Substring(0, 120) : message;
    }

    void Render()
    {
        timeText.Text = DateTime.Now.ToString("HH:mm:ss");
        Color status = codexBusy ? Color.FromRgb(245, 180, 55) : quota.Available ? Color.FromRgb(74, 222, 128) : Color.FromRgb(236, 83, 83);
        statusDot.Fill = new SolidColorBrush(status);
        statusGlow.Fill = GlowBrush(status);

        if (settings.codex.enabled)
        {
            fivePercent.Text = quota.Available ? quota.FiveHourRemainingPercent + "%" : "--";
            weekPercent.Text = quota.Available ? quota.WeeklyRemainingPercent + "%" : "--";
            weekUsed.Text = FormatTokens(usage.WeekTokens);
            fiveUsed.Text = "resets " + FormatResetCountdown(quota.FiveHourResetsAt);
            UpdateMeter(fiveFill, fiveTrack, quota.Available, quota.FiveHourRemainingPercent, Color.FromRgb(74, 222, 128));
            UpdateMeter(weekFill, weekTrack, quota.Available, quota.WeeklyRemainingPercent, Color.FromRgb(74, 222, 128));
        }
        if (settings.minimax.enabled)
        {
            miniFivePercent.Text = minimax.Available ? minimax.FiveHourRemainingPercent + "%" : "--";
            miniWeekPercent.Text = minimax.Available ? minimax.WeeklyRemainingPercent + "%" : "--";
            miniFiveUsed.Text = !string.IsNullOrEmpty(minimax.RemainsTime) ? "resets " + minimax.RemainsTime : "";
            miniWeekUsed.Text = "";
            UpdateMeter(miniFiveFill, miniFiveTrack, minimax.Available, minimax.FiveHourRemainingPercent, Color.FromRgb(74, 222, 128));
            UpdateMeter(miniWeekFill, miniWeekTrack, minimax.Available, minimax.WeeklyRemainingPercent, Color.FromRgb(74, 222, 128));
            string miniTip = (minimax.IsStale ? "Stale: " : "") + minimax.Status;
            if (!string.IsNullOrEmpty(minimax.RemainsTime)) miniTip += "\n5h: " + minimax.RemainsTime;
            miniHeading.ToolTip = miniTip; miniFivePercent.ToolTip = miniTip; miniWeekPercent.ToolTip = miniTip;
        }
        if (settings.deepseek.enabled)
        {
            int? balancePercent = ProviderMath.RemainingPercent(deepseek.TotalBalance, settings.deepseek.referenceBudget);
            deepSeekPercent.Text = (deepseek.Available && balancePercent.HasValue) ? balancePercent.Value + "%" : "--";
            deepSeekUsed.Text = deepseek.Available ? CurrencySymbol(deepseek.Currency) + deepseek.TotalBalance.ToString("0.00") : deepseek.Status;
            UpdateMeter(deepSeekFill, deepSeekTrack, deepseek.Available && balancePercent.HasValue, balancePercent.GetValueOrDefault(), Color.FromRgb(74, 222, 128));
            decimal estimatedCost = EstimateCost();
            string estimate = estimatedCost > 0m && deepseek.TotalBalance >= 0m ? "\nAbout " + Math.Floor(deepseek.TotalBalance / estimatedCost).ToString("0") + " configured tasks" : "";
            string deepTip = "Balance: " + CurrencySymbol(deepseek.Currency) + deepseek.TotalBalance.ToString("0.00") + "\nTopped up: " + CurrencySymbol(deepseek.Currency) + deepseek.ToppedUpBalance.ToString("0.00") + "\nGranted: " + CurrencySymbol(deepseek.Currency) + deepseek.GrantedBalance.ToString("0.00") + "\nReference budget: " + CurrencySymbol(deepseek.Currency) + settings.deepseek.referenceBudget.ToString("0.00") + estimate + "\nStatus: " + (deepseek.Source == ProviderSource.Manual ? "Manual" : deepseek.IsStale ? "Stale" : "Fresh") + "\n" + deepseek.Status;
            deepSeekHeading.ToolTip = deepTip; deepSeekPercent.ToolTip = deepTip; deepSeekUsed.ToolTip = deepTip;
        }
        if (settings.codex.enabled)
        {
            footerLeft.Text = quota.Available ? "tokens " + FormatTokens(usage.TotalTokens) : "";
            footerRight.Text = "";
        }
        else
        {
            footerLeft.Text = "";
            footerRight.Text = "";
        }
        int seconds = Math.Max(0, (int)Math.Ceiling((nextRefreshAt - DateTime.Now).TotalSeconds));
        footerSub.Text = "refresh " + seconds + "s";
    }

    decimal EstimateCost()
    {
        TokenPrices price;
        if (!settings.deepseek.estimate.enabled || !settings.deepseek.pricesPerMillionTokens.TryGetValue(settings.deepseek.estimate.model, out price)) return 0m;
        return ProviderMath.EstimatedCostPerRequest(settings.deepseek.estimate.averageInputTokens, settings.deepseek.estimate.averageOutputTokens, settings.deepseek.estimate.cacheHitRatio, price.inputCacheHit, price.inputCacheMiss, price.output);
    }

    static string CurrencySymbol(string currency) { return string.Equals(currency, "CNY", StringComparison.OrdinalIgnoreCase) ? "\u00a5" : string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase) ? "$" : (currency ?? "") + " "; }

    void UpdateMeter(Border fill, Border track, bool available, int remaining, Color accent)
    {
        Color fillColor = available ? BarColors.ForPercent(remaining) : Color.FromRgb(100, 100, 110);
        fill.Background = new LinearGradientBrush(
            new GradientStopCollection {
                new GradientStop(Color.FromArgb(130, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(245, (byte)Math.Min(255, fillColor.R + 24), (byte)Math.Min(255, fillColor.G + 24), (byte)Math.Min(255, fillColor.B + 24)), 0.18),
                new GradientStop(fillColor, 0.6),
                new GradientStop(Color.FromArgb(238, (byte)Math.Max(0, fillColor.R - 16), (byte)Math.Max(0, fillColor.G - 16), (byte)Math.Max(0, fillColor.B - 16)), 1)
            },
            new Point(0, 0),
            new Point(0, 1));
        double width = track.ActualWidth;
        if (width <= 0) width = Width - 40;
        fill.Width = Math.Max(4, width * Math.Max(0, Math.Min(100, remaining)) / 100.0);
    }

    void RenderGhosts(Border[] ghosts, HistoryRing history, Border fill, Border track)
    {
        if (ghosts == null || ghosts.Length < 3) return;
        double width = track.ActualWidth; if (width <= 0) width = Width - 40;
        LinearGradientBrush brush = fill.Background as LinearGradientBrush;
        Color baseColor = brush != null ? brush.GradientStops[2].Color : Color.FromRgb(74, 222, 128);
        int? cur = history.Current;
        int?[] samples = { history.SampleAtSecondsAgo(300), history.SampleAtSecondsAgo(1200), history.SampleAtSecondsAgo(1800) };
        int?[] positions = { cur, samples[0], samples[1], samples[2] };
        for (int i = 0; i < 3; i++)
        {
            int? from = positions[i + 1], to = positions[i];
            if (!from.HasValue || !to.HasValue || from.Value >= to.Value) { ghosts[i].Width = 0; continue; }
            ghosts[i].Background = new SolidColorBrush(BarColors.BurnGhost(baseColor, i + 1));
            ghosts[i].Width = Math.Max(1, width * (to.Value - from.Value) / 100.0);
            ghosts[i].Margin = new Thickness(width * from.Value / 100.0, 0, 0, 0);
        }
    }

    void EnableSystemGlass()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int corner = DWM_WINDOW_CORNER_PREFERENCE_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            int backdrop = DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            Margins margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            AccentPolicy accent = new AccentPolicy();
            accent.AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
            accent.GradientColor = unchecked((int)0x381E1612);
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                WindowCompositionAttributeData data = new WindowCompositionAttributeData();
                data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
                data.SizeOfData = size;
                data.Data = ptr;
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST) return IntPtr.Zero;
        int x = unchecked((short)((long)lParam & 0xffff));
        int y = unchecked((short)(((long)lParam >> 16) & 0xffff));
        Point p = PointFromScreen(new Point(x, y));
        bool left = p.X <= ResizeGrip, right = p.X >= ActualWidth - ResizeGrip;
        bool top = p.Y <= ResizeGrip, bottom = p.Y >= ActualHeight - ResizeGrip;
        int hit = 0;
        if (left && top) hit = HTTOPLEFT;
        else if (right && top) hit = HTTOPRIGHT;
        else if (left && bottom) hit = HTBOTTOMLEFT;
        else if (right && bottom) hit = HTBOTTOMRIGHT;
        else if (left) hit = HTLEFT;
        else if (right) hit = HTRIGHT;
        else if (top) hit = HTTOP;
        else if (bottom) hit = HTBOTTOM;
        if (hit == 0) return IntPtr.Zero;
        handled = true;
        return new IntPtr(hit);
    }

    bool IsOnResizeEdge(Point p)
    {
        return p.X <= ResizeGrip || p.Y <= ResizeGrip || p.X >= ActualWidth - ResizeGrip || p.Y >= ActualHeight - ResizeGrip;
    }

    static bool IsOnScreen(double left, double top, double width, double height)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || width <= 0 || height <= 0) return false;
        double vsL = SystemParameters.VirtualScreenLeft, vsT = SystemParameters.VirtualScreenTop;
        double vsR = vsL + SystemParameters.VirtualScreenWidth, vsB = vsT + SystemParameters.VirtualScreenHeight;
        // Require at least 50×50 px visible
        return left + width - 50 > vsL && left + 50 < vsR && top + height - 50 > vsT && top + 50 < vsB;
    }

    static RadialGradientBrush GlowBrush(Color color)
    {
        RadialGradientBrush brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(190, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(72, color.R, color.G, color.B), 0.42));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        return brush;
    }

    static DropShadowEffect Glow(Color color, double blur, double opacity)
    {
        return new DropShadowEffect { Color = color, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity };
    }

    static string FormatResetCountdown(DateTime? resetAt)
    {
        if (!resetAt.HasValue) return "--";
        TimeSpan left = resetAt.Value - DateTime.Now;
        if (left.TotalSeconds <= 0) return "now";
        if (left.TotalHours >= 1) return ((int)left.TotalHours) + "h " + left.Minutes + "m";
        return left.Minutes + "m";
    }
    static string FormatTokens(long value)
    {
        if (value >= 1000000000L) return (value / 1000000000.0).ToString("0.00") + "B";
        if (value >= 1000000L) return (value / 1000000.0).ToString("0.00") + "M";
        if (value >= 1000L) return (value / 1000.0).ToString("0.0") + "K";
        return value.ToString();
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        Application app = new Application();
        app.Run(new LiquidWindow());
    }
}
