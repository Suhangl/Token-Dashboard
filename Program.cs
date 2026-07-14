using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    public readonly bool Available, FiveHourAvailable, WeeklyAvailable;
    public readonly int FiveHourRemainingPercent, WeeklyRemainingPercent;
    public readonly DateTime? FiveHourResetsAt, WeeklyResetsAt;
    public readonly string Status;
    public QuotaSnapshot(bool available, int five, int week, string status, DateTime? fiveReset, DateTime? weekReset)
        : this(available, available, five, week, status, fiveReset, weekReset)
    {
    }
    public QuotaSnapshot(bool fiveAvailable, bool weeklyAvailable, int five, int week, string status, DateTime? fiveReset, DateTime? weekReset)
    {
        FiveHourAvailable = fiveAvailable;
        WeeklyAvailable = weeklyAvailable;
        Available = fiveAvailable || weeklyAvailable;
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

static class PresentationText
{
    public static string TMinus(DateTime? resetAt, DateTime now)
    {
        if (!resetAt.HasValue) return "";
        TimeSpan remaining = resetAt.Value - now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        int totalHours = (int)remaining.TotalHours;
        return "\u2212" + totalHours.ToString("00") + ":" + remaining.Minutes.ToString("00");
    }

    public static string MiniMaxTMinus(string remainingText)
    {
        if (string.IsNullOrWhiteSpace(remainingText)) return "";
        string text = remainingText.Trim().ToLowerInvariant();
        int hours = 0, minutes = 0, seconds = 0;
        int hourMarker = text.IndexOf('h');
        int minuteMarker = text.IndexOf('m');
        int secondMarker = text.IndexOf('s');
        if (hourMarker != text.LastIndexOf('h') || minuteMarker != text.LastIndexOf('m') || secondMarker != text.LastIndexOf('s')) return "";
        if ((minuteMarker >= 0 && hourMarker > minuteMarker)
            || (secondMarker >= 0 && hourMarker > secondMarker)
            || (secondMarker >= 0 && minuteMarker > secondMarker)) return "";
        if (hourMarker >= 0 && !int.TryParse(text.Substring(0, hourMarker).Trim(), out hours)) return "";
        int minuteStart = hourMarker >= 0 ? hourMarker + 1 : 0;
        if (minuteMarker >= minuteStart && !int.TryParse(text.Substring(minuteStart, minuteMarker - minuteStart).Trim(), out minutes)) return "";
        int secondStart = minuteMarker >= 0 ? minuteMarker + 1 : hourMarker >= 0 ? hourMarker + 1 : 0;
        if (secondMarker >= secondStart && !int.TryParse(text.Substring(secondStart, secondMarker - secondStart).Trim(), out seconds)) return "";
        int lastMarker = Math.Max(hourMarker, Math.Max(minuteMarker, secondMarker));
        if (lastMarker < 0 || lastMarker != text.Length - 1) return "";
        if (hours < 0 || minutes < 0 || seconds < 0 || seconds >= 60) return "";
        hours += minutes / 60;
        minutes %= 60;
        return "\u2212" + hours.ToString("00") + ":" + minutes.ToString("00");
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

static class QuietGlassPalette
{
    public static readonly Color ShellTop = Color.FromArgb(232, 34, 36, 40);
    public static readonly Color ShellBottom = Color.FromArgb(222, 20, 22, 25);
    public static readonly Color ShellHighlight = Color.FromArgb(42, 255, 255, 255);
    public static readonly Color InnerHighlight = Color.FromArgb(229, 40, 42, 46);
    public static readonly Color Divider = Color.FromArgb(20, 255, 255, 255);
    public static readonly Color Shadow = Colors.Black;
    public static readonly Color Track = Color.FromArgb(92, 72, 76, 82);
    public static readonly Color AvailableAllowance = Color.FromRgb(198, 201, 204);
    public static readonly Color UnavailableAllowance = Colors.Black;
    public static readonly Color PrimaryText = Color.FromRgb(232, 234, 236);
    public static readonly Color SecondaryText = Color.FromRgb(145, 149, 154);
    public static readonly Color SectionText = Color.FromRgb(132, 136, 142);
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
enum TrayIconMode { Default, Percentage }
class ActiveSettings
{
    public int refreshSeconds;
    public CodexSettings codex;
    public MiniMaxSettings minimax;
    public DeepSeekSettings deepseek;
    public int popupDismissDelayMs;
    public int popupHoverDelayMs;
    public double popupLeft, popupTop;
    public bool popupStickyOnLaunch;
    public string trayIconMode;
}
class DashboardSettings
{
    public int refreshSeconds = 60;
    public double windowLeft = double.NaN, windowTop = double.NaN, windowWidth, windowHeight;
    public bool windowTopmost = true;
    public CodexSettings codex = new CodexSettings();
    public MiniMaxSettings minimax = new MiniMaxSettings();
    public DeepSeekSettings deepseek = new DeepSeekSettings();
    public GlassSettings glass = new GlassSettings();
    public int popupDismissDelayMs = 300;
    public int popupHoverDelayMs = 400;
    public double popupLeft = double.NaN, popupTop = double.NaN;
    public bool popupStickyOnLaunch = false;
    public string trayIconMode = "default";
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
        settings.trayIconMode = NormalizeTrayIconMode(settings.trayIconMode) == TrayIconMode.Percentage ? "percentage" : "default";
        return settings;
    }

    public static TrayIconMode NormalizeTrayIconMode(string raw)
    {
        return string.Equals(raw, "percentage", StringComparison.OrdinalIgnoreCase)
            ? TrayIconMode.Percentage
            : TrayIconMode.Default;
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
        ActiveSettings active = new ActiveSettings
        {
            refreshSeconds = refreshSeconds,
            codex = codex,
            minimax = minimax,
            deepseek = deepseek,
            popupDismissDelayMs = popupDismissDelayMs,
            popupHoverDelayMs = popupHoverDelayMs,
            popupLeft = popupLeft,
            popupTop = popupTop,
            popupStickyOnLaunch = popupStickyOnLaunch,
            trayIconMode = trayIconMode
        };
        File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(active), Encoding.UTF8);
    }
    public static string PathForDisplay { get { return FilePath; } }
}

static class DashboardState
{
    public static event Action Changed;
    public static UsageSnapshot Usage = new UsageSnapshot(false, 0, 0, 0, 0, "starting");
    public static QuotaSnapshot CodexQuota = new QuotaSnapshot(false, 0, 0, "starting", null, null);
    public static MiniMaxSnapshot MiniMax = new MiniMaxSnapshot(false, 0, 0, false, DateTime.MinValue, "not configured", ProviderSource.Cached);
    public static DeepSeekSnapshot DeepSeek = new DeepSeekSnapshot(false, 0m, 0m, 0m, "CNY", false, DateTime.MinValue, "not configured", ProviderSource.Cached);
    public static DateTime LastRefreshAt = DateTime.MinValue;

    static void Fire()
    {
        Action handler = Changed;
        if (handler == null) return;
        System.Windows.Application app = System.Windows.Application.Current;
        if (app == null) return;
        app.Dispatcher.BeginInvoke(handler);
    }
    public static void SetUsage(UsageSnapshot s) { Usage = s; LastRefreshAt = DateTime.Now; Fire(); }
    public static void SetCodexQuota(QuotaSnapshot s) { CodexQuota = s; Fire(); }
    public static void SetMiniMax(MiniMaxSnapshot s) { MiniMax = s; Fire(); }
    public static void SetDeepSeek(DeepSeekSnapshot s) { DeepSeek = s; Fire(); }
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
            // Convert numeric: > 1M = milliseconds, otherwise seconds
            long sec = val > 1000000 ? val / 1000 : val;
            if (sec <= 0) return "";
            if (sec < 60) return sec + "s";
            if (sec < 3600) return (sec / 60) + "m " + (sec % 60) + "s";
            return (sec / 3600) + "h " + ((sec % 3600) / 60) + "m";
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
    internal static QuotaSnapshot ParseForTest(string text) { return Parse(text); }

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
            p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":null}");
            p.StandardInput.Flush();
            if (!got.WaitOne(15000)) throw new Exception("quota timeout");
            QuotaSnapshot quota = Parse(response);
            if (quota == null) throw new Exception("quota response contains no recognized rate-limit windows");
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
        List<LimitWindow> windows = CollectRateLimitWindows(obj);
        int? five = null, week = null; DateTime? fiveReset = null, weekReset = null;
        foreach (LimitWindow w in windows)
        {
            int remaining = Math.Max(0, Math.Min(100, (int)Math.Round(100.0 - w.UsedPercent)));
            if (w.DurationMins == 300 && !five.HasValue) { five = remaining; fiveReset = w.ResetsAt; }
            if (w.DurationMins == 10080 && !week.HasValue) { week = remaining; weekReset = w.ResetsAt; }
        }
        if (!five.HasValue && !week.HasValue) return null;
        return new QuotaSnapshot(
            five.HasValue, week.HasValue,
            five.GetValueOrDefault(), week.GetValueOrDefault(),
            "codex app-server", fiveReset, weekReset);
    }

    static List<LimitWindow> CollectRateLimitWindows(object obj)
    {
        List<LimitWindow> windows = new List<LimitWindow>();
        Dictionary<string, object> envelope = obj as Dictionary<string, object>;
        Dictionary<string, object> result = null;
        object resultObj;
        if (envelope != null && envelope.TryGetValue("result", out resultObj))
            result = resultObj as Dictionary<string, object>;
        if (result == null) result = envelope;

        bool collectedPreferred = false;
        object byIdObj;
        Dictionary<string, object> byId = null;
        if (result != null && result.TryGetValue("rateLimitsByLimitId", out byIdObj))
            byId = byIdObj as Dictionary<string, object>;
        if (byId != null)
        {
            object codexBucket;
            if (byId.TryGetValue("codex", out codexBucket))
            {
                Walk(codexBucket, windows);
                collectedPreferred = windows.Count > 0;
            }
            if (!collectedPreferred)
            {
                foreach (object bucket in byId.Values) Walk(bucket, windows);
                collectedPreferred = windows.Count > 0;
            }
        }

        object legacyObj;
        if (result != null && result.TryGetValue("rateLimits", out legacyObj))
            Walk(legacyObj, windows);
        if (windows.Count == 0) Walk(obj, windows);
        return windows;
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

class Bindings
{
    public TextBlock timeText, codexHeading, miniHeading, deepSeekHeading;
    public TextBlock fivePercent, fiveUsed, weekPercent, weekUsed;
    public TextBlock codexTokenUsage;
    public TextBlock miniFivePercent, miniFiveUsed, miniWeekPercent, miniWeekUsed;
    public TextBlock deepSeekPercent, deepSeekUsed, footerSub;
    public System.Windows.Shapes.Ellipse statusDot, statusGlow;
    public System.Windows.Controls.Border fiveFill, fiveTrack, weekFill, weekTrack;
    public System.Windows.Controls.Border miniFiveFill, miniFiveTrack, miniWeekFill, miniWeekTrack;
    public System.Windows.Controls.Border deepSeekFill, deepSeekTrack;
    public FrameworkElement stickyPinButton;
}

class BuildResult
{
    public UIElement Root;
    public Bindings Bindings;
}

static class BuildUiFactory
{
    public static BuildResult Build(DashboardSettings settings)
    {
        Bindings b = new Bindings();
        Border shell = new Border();
        shell.CornerRadius = new CornerRadius(Math.Max(0, Math.Min(30, settings.glass != null ? settings.glass.cornerRadius : 15)));
        shell.Background = new LinearGradientBrush(
            new GradientStopCollection {
                new GradientStop(QuietGlassPalette.ShellTop, 0),
                new GradientStop(QuietGlassPalette.InnerHighlight, 0.025),
                new GradientStop(QuietGlassPalette.ShellBottom, 1)
            },
            new Point(0, 0),
            new Point(1, 1));
        shell.BorderBrush = new SolidColorBrush(QuietGlassPalette.ShellHighlight);
        shell.BorderThickness = new Thickness(1);
        shell.Margin = new Thickness(8);
        shell.Effect = new DropShadowEffect { Color = QuietGlassPalette.Shadow, BlurRadius = 12, ShadowDepth = 2, Opacity = 0.28 };
        shell.ClipToBounds = true; shell.SnapsToDevicePixels = true;

        Grid grid = new Grid();
        grid.Margin = new Thickness(11, 7, 11, 5);
        int row = 0;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

        Grid header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        Grid lamp = new Grid { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = false };
        b.statusGlow = new System.Windows.Shapes.Ellipse { Width = 17, Height = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        b.statusDot = new System.Windows.Shapes.Ellipse { Width = 5.5, Height = 5.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        lamp.Children.Add(b.statusGlow); lamp.Children.Add(b.statusDot);
        b.timeText = MakeText("", 12, 0, QuietGlassPalette.SecondaryText.R, QuietGlassPalette.SecondaryText.G, QuietGlassPalette.SecondaryText.B, FontWeights.Medium);
        b.timeText.HorizontalAlignment = HorizontalAlignment.Center;
        Grid pinHit = new Grid { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Right };
        System.Windows.Shapes.Ellipse pin = new System.Windows.Shapes.Ellipse { Width = 16, Height = 16, Stroke = new SolidColorBrush(QuietGlassPalette.PrimaryText), StrokeThickness = 1.5, Fill = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        pinHit.Children.Add(pin);
        ToolTipService.SetToolTip(pinHit, "钉住 popup");
        b.stickyPinButton = pinHit;
        Grid.SetColumn(lamp, 0); Grid.SetColumn(b.timeText, 1); Grid.SetColumn(pinHit, 2);
        header.Children.Add(lamp); header.Children.Add(b.timeText); header.Children.Add(pinHit);
        Grid.SetRow(header, row++); grid.Children.Add(header);

        AppendProviderSections(grid, settings, b, ref row);
        AppendFooter(grid, b, row);

        shell.Child = grid;
        BuildResult result = new BuildResult();
        result.Root = shell;
        result.Bindings = b;
        return result;
    }

    static void AppendProviderSections(Grid grid, DashboardSettings s, Bindings b, ref int row)
    {
        if (s.codex.enabled)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
            b.codexHeading = AddSectionHeader(grid, row++, "CODEX");
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
            AddDivider(grid, row++);
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(55) });
            AddCompositeBar(grid, b, row++);
        }

        if (s.minimax.enabled)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(s.codex.enabled ? 10 : 4) });
            row++;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
            b.miniHeading = AddSectionHeader(grid, row++, "MINIMAX");
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
            AddDivider(grid, row++);
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(55) });
            AddMiniMaxCompositeBar(grid, b, row++);
        }

        if (s.deepseek.enabled)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength((s.codex.enabled || s.minimax.enabled) ? 10 : 4) });
            row++;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
            b.deepSeekHeading = AddSectionHeader(grid, row++, "DEEPSEEK");
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
            AddDivider(grid, row++);
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            AddMeter(grid, b, row++, "Balance");
        }
    }

    static void AppendFooter(Grid grid, Bindings b, int row)
    {
        Grid footer = new Grid();
        b.footerSub = MakeText("", 10.5, 0, QuietGlassPalette.SecondaryText.R, QuietGlassPalette.SecondaryText.G, QuietGlassPalette.SecondaryText.B, FontWeights.Normal);
        b.footerSub.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(b.footerSub);
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        Grid.SetRow(footer, row); grid.Children.Add(footer);
    }

    internal static TextBlock MakeText(string text, double size, byte a, byte r, byte g, byte bl, FontWeight weight)
    {
        return new TextBlock { Text = text, FontSize = size, Foreground = new SolidColorBrush(Color.FromArgb(a == 0 ? (byte)255 : a, r, g, bl)), FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
    }

    static TextBlock AddSectionHeader(Grid parent, int row, string label)
    {
        TextBlock heading = MakeText(label, 10.5, 0, QuietGlassPalette.SectionText.R, QuietGlassPalette.SectionText.G, QuietGlassPalette.SectionText.B, FontWeights.Medium);
        heading.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetRow(heading, row); parent.Children.Add(heading);
        return heading;
    }

    static void AddDivider(Grid parent, int row)
    {
        Border line = new Border { Height = 1, Background = new SolidColorBrush(QuietGlassPalette.Divider), Margin = new Thickness(0, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(line, row); parent.Children.Add(line);
    }

    static Border BarTrack(int h) { return new Border { Height = h, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(QuietGlassPalette.Track), VerticalAlignment = VerticalAlignment.Top }; }
    static Border BarFill(int h) { return new Border { Height = h, CornerRadius = new CornerRadius(2), Width = 4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }; }

    enum BarSide { Codex, MiniMax }

    static void AddCompositeBar(Grid parent, Bindings b, int row)
    {
        AddCompositeBarImpl(parent, b, row, BarSide.Codex);
    }

    static void AddMiniMaxCompositeBar(Grid parent, Bindings b, int row)
    {
        AddCompositeBarImpl(parent, b, row, BarSide.MiniMax);
    }

    static void AddCompositeBarImpl(Grid parent, Bindings b, int row, BarSide side)
    {
        bool isCodex = side == BarSide.Codex;

        Grid block = new Grid();
        block.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        block.ColumnDefinitions.Add(new ColumnDefinition());
        block.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        block.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        block.RowDefinitions.Add(new RowDefinition { Height = new GridLength(isCodex ? 18 : 22) });
        block.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        block.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
        block.RowDefinitions.Add(new RowDefinition { Height = new GridLength(isCodex ? 14 : 21) });
        if (isCodex) block.RowDefinitions.Add(new RowDefinition { Height = new GridLength(11) });

        TextBlock fivePct = MakeText("--", 14, 0, QuietGlassPalette.PrimaryText.R, QuietGlassPalette.PrimaryText.G, QuietGlassPalette.PrimaryText.B, FontWeights.SemiBold); fivePct.HorizontalAlignment = HorizontalAlignment.Right;
        TextBlock fiveUsed = MakeText("", 10, 0, QuietGlassPalette.SecondaryText.R, QuietGlassPalette.SecondaryText.G, QuietGlassPalette.SecondaryText.B, FontWeights.Normal); fiveUsed.HorizontalAlignment = HorizontalAlignment.Right;
        Border fiveTrack = BarTrack(8); Border fiveFill = BarFill(8);
        TextBlock weekPct = MakeText("--", 12, 0, QuietGlassPalette.PrimaryText.R, QuietGlassPalette.PrimaryText.G, QuietGlassPalette.PrimaryText.B, FontWeights.Medium); weekPct.HorizontalAlignment = HorizontalAlignment.Right;
        TextBlock weekUsed = MakeText("", 10, 0, QuietGlassPalette.SecondaryText.R, QuietGlassPalette.SecondaryText.G, QuietGlassPalette.SecondaryText.B, FontWeights.Normal); weekUsed.HorizontalAlignment = HorizontalAlignment.Right;
        Border weekTrack = BarTrack(4); Border weekFill = BarFill(4);

        if (isCodex)
        {
            b.fivePercent = fivePct; b.fiveUsed = fiveUsed;
            b.fiveTrack = fiveTrack; b.fiveFill = fiveFill;
            b.weekPercent = weekPct; b.weekUsed = weekUsed;
            b.weekTrack = weekTrack; b.weekFill = weekFill;
        }
        else
        {
            b.miniFivePercent = fivePct; b.miniFiveUsed = fiveUsed;
            b.miniFiveTrack = fiveTrack; b.miniFiveFill = fiveFill;
            b.miniWeekPercent = weekPct; b.miniWeekUsed = weekUsed;
            b.miniWeekTrack = weekTrack; b.miniWeekFill = weekFill;
        }

        TextBlock fLabel = MakeText("5H", 10.5, 0, QuietGlassPalette.SecondaryText.R, QuietGlassPalette.SecondaryText.G, QuietGlassPalette.SecondaryText.B, FontWeights.Medium);
        Grid.SetColumn(fiveUsed, 2); Grid.SetColumn(fivePct, 3);
        Grid.SetRow(fLabel, 0); Grid.SetRow(fiveUsed, 0); Grid.SetRow(fivePct, 0);
        block.Children.Add(fLabel); block.Children.Add(fiveUsed); block.Children.Add(fivePct);

        Grid bar5 = new Grid(); bar5.Children.Add(fiveTrack); bar5.Children.Add(fiveFill);
        Grid.SetRow(bar5, 1); Grid.SetColumnSpan(bar5, 4); block.Children.Add(bar5);

        Grid barW = new Grid(); barW.Children.Add(weekTrack); barW.Children.Add(weekFill);
        Grid.SetRow(barW, 2); Grid.SetColumnSpan(barW, 4); block.Children.Add(barW);

        TextBlock wLabel = MakeText("W", 10.5, 0, QuietGlassPalette.SecondaryText.R, QuietGlassPalette.SecondaryText.G, QuietGlassPalette.SecondaryText.B, FontWeights.Normal);
        Grid.SetColumn(weekUsed, 2); Grid.SetColumn(weekPct, 3);
        Grid.SetRow(wLabel, 3); Grid.SetRow(weekUsed, 3); Grid.SetRow(weekPct, 3);
        block.Children.Add(wLabel); block.Children.Add(weekUsed); block.Children.Add(weekPct);

        if (isCodex)
        {
            b.codexTokenUsage = MakeText("", 9, 0, QuietGlassPalette.SecondaryText.R, QuietGlassPalette.SecondaryText.G, QuietGlassPalette.SecondaryText.B, FontWeights.Normal);
            b.codexTokenUsage.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(b.codexTokenUsage, 4); Grid.SetColumnSpan(b.codexTokenUsage, 4);
            block.Children.Add(b.codexTokenUsage);
        }

        Grid.SetRow(block, row); parent.Children.Add(block);
    }

    static void AddMeter(Grid parent, Bindings b, int row, string label, int barH = 6)
    {
        Grid line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        line.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        line.RowDefinitions.Add(new RowDefinition { Height = new GridLength(barH + 4) });

        TextBlock lt = MakeText(label, 10.5, 0, QuietGlassPalette.SecondaryText.R, QuietGlassPalette.SecondaryText.G, QuietGlassPalette.SecondaryText.B, FontWeights.Medium);
        b.deepSeekPercent = MakeText("--", 14, 0, QuietGlassPalette.PrimaryText.R, QuietGlassPalette.PrimaryText.G, QuietGlassPalette.PrimaryText.B, FontWeights.SemiBold); b.deepSeekPercent.HorizontalAlignment = HorizontalAlignment.Right;
        b.deepSeekUsed = MakeText("", 10, 0, QuietGlassPalette.SecondaryText.R, QuietGlassPalette.SecondaryText.G, QuietGlassPalette.SecondaryText.B, FontWeights.Normal); b.deepSeekUsed.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(b.deepSeekUsed, 2); Grid.SetColumn(b.deepSeekPercent, 3);
        line.Children.Add(lt); line.Children.Add(b.deepSeekUsed); line.Children.Add(b.deepSeekPercent);

        b.deepSeekTrack = BarTrack(barH); b.deepSeekFill = BarFill(barH);
        Grid bg = new Grid(); bg.Children.Add(b.deepSeekTrack); bg.Children.Add(b.deepSeekFill);
        Grid.SetRow(bg, 1); Grid.SetColumnSpan(bg, 4); line.Children.Add(bg);

        Grid.SetRow(line, row); parent.Children.Add(line);
    }
}

class PendingPopupPosition
{
    bool _hasValue;
    double _left, _top;

    public bool CaptureIfEligible(bool eligible, double left, double top)
    {
        if (!eligible || double.IsNaN(left) || double.IsNaN(top)) return false;
        _left = left;
        _top = top;
        _hasValue = true;
        return true;
    }

    public bool TryTake(out double left, out double top)
    {
        if (!_hasValue)
        {
            left = double.NaN;
            top = double.NaN;
            return false;
        }

        left = _left;
        top = _top;
        _hasValue = false;
        return true;
    }
}

class PopupWindow : Window
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

    public DashboardSettings Settings;
    public Bindings Bindings;
    DispatcherTimer _saveDebounce;
    DispatcherTimer _countdownTick;
    readonly PendingPopupPosition _pendingPopupPosition = new PendingPopupPosition();
    DateTime _nextRefreshAt;
    bool _isSticky;
    bool _codexBusy, _minimaxBusy, _deepseekBusy;
    string _dbPath = Environment.ExpandEnvironmentVariables("%USERPROFILE%\\.codex\\state_5.sqlite");
    Grid _stickyPinGrid;
    System.Windows.Shapes.Ellipse _pinEllipse;
    SolidColorBrush _pinFill;

    public bool IsSticky { get { return _isSticky; } }

    public PopupWindow(DashboardSettings settings)
    {
        Settings = settings;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = false;
        MinWidth = 320; MinHeight = 150;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        Width = 360;
        Height = DesiredHeight(settings);

        BuildResult buildResult = BuildUiFactory.Build(settings);
        Bindings = buildResult.Bindings;
        Content = buildResult.Root;

        if (!double.IsNaN(settings.popupLeft)) Left = settings.popupLeft;
        if (!double.IsNaN(settings.popupTop)) Top = settings.popupTop;

        SourceInitialized += delegate { EnableSystemGlass(this); };
        Activated += delegate { };
        Deactivated += delegate { if (_isSticky) LeaveSticky(); };
        Closing += delegate(object s2, CancelEventArgs e)
        {
            if (_isSticky) { e.Cancel = true; Hide(); }
            else
            {
                System.Windows.Application app = System.Windows.Application.Current;
                if (app != null) app.Shutdown();
            }
        };

        MouseLeave += delegate { /* handled by TrayController */ };
        MouseEnter += delegate { /* handled by TrayController */ };

        Bindings.timeText.PreviewMouseLeftButtonDown += delegate(object s2, MouseButtonEventArgs e)
        {
            if (e.GetPosition(this).Y <= 24)
            {
                try { DragMove(); } catch { }
                e.Handled = true;
            }
        };

        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveDebounce.Tick += delegate(object s2, EventArgs e2)
        {
            _saveDebounce.Stop();
            double pendingLeft, pendingTop;
            if (!_pendingPopupPosition.TryTake(out pendingLeft, out pendingTop)) return;
            Settings.popupLeft = pendingLeft;
            Settings.popupTop = pendingTop;
            try { Settings.Save(); } catch { }
        };
        LocationChanged += delegate
        {
            if (!_pendingPopupPosition.CaptureIfEligible(_isSticky, Left, Top)) return;
            _saveDebounce.Stop();
            _saveDebounce.Start();
        };

        DashboardState.Changed += Render;
        _nextRefreshAt = DateTime.Now.AddSeconds(Settings.refreshSeconds);
        DashboardState.Changed += delegate { _nextRefreshAt = DateTime.Now.AddSeconds(Settings.refreshSeconds); };
        _countdownTick = new DispatcherTimer();
        _countdownTick.Interval = TimeSpan.FromSeconds(1);
        _countdownTick.Tick += delegate { if (DateTime.Now >= _nextRefreshAt) StartRefresh(); RenderCountdown(); Render(); };
        _countdownTick.Start();
        RenderCountdown();
        Render();
        BindToStickyButton();
        StartRefresh();
    }

    void RenderCountdown()
    {
        if (Bindings == null || Bindings.footerSub == null) return;
        int seconds = Math.Max(0, (int)Math.Ceiling((_nextRefreshAt - DateTime.Now).TotalSeconds));
        Bindings.footerSub.Text = "refresh " + seconds + "s";
    }

    public void BindToStickyButton()
    {
        Grid grid = Bindings.stickyPinButton as Grid;
        if (grid == null) return;

        System.Windows.Shapes.Ellipse pin = null;
        foreach (UIElement child in grid.Children)
        {
            pin = child as System.Windows.Shapes.Ellipse;
            if (pin != null) break;
        }
        if (pin == null) return;

        _stickyPinGrid = grid;
        _pinEllipse = pin;
        _pinFill = new SolidColorBrush(Colors.Transparent);
        pin.Fill = _pinFill;
        ToolTipService.SetToolTip(grid, "钉住 popup");

        grid.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_isSticky)
            {
                LeaveSticky();
            }
            else
            {
                AnimatePin(_pinEllipse, _pinFill, true);
                EnterSticky();
                ToolTipService.SetToolTip(_stickyPinGrid, "已钉住（再次点击 📌 取消）");
            }
        };
    }

    static void AnimatePin(System.Windows.Shapes.Ellipse pin, SolidColorBrush fill, bool toSticky)
    {
        if (pin == null || fill == null) return;
        Color from = fill.Color;
        Color to = toSticky ? Color.FromRgb(230, 230, 230) : Colors.Transparent;
        System.Windows.Media.Animation.ColorAnimation animation = new System.Windows.Media.Animation.ColorAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(200)));
        System.Windows.Media.Animation.CubicEase easing = new System.Windows.Media.Animation.CubicEase();
        easing.EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut;
        animation.EasingFunction = easing;
        fill.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    public void EnterSticky()
    {
        _isSticky = true;
        Topmost = true;
    }

    public void LeaveSticky()
    {
        _isSticky = false;
        Topmost = false;
        AnimatePin(_pinEllipse, _pinFill, false);
        if (_stickyPinGrid != null) ToolTipService.SetToolTip(_stickyPinGrid, "钉住 popup");
        Hide();
    }

    void Render()
    {
        if (Bindings == null || Bindings.timeText == null) return;
        UsageSnapshot usage = DashboardState.Usage;
        QuotaSnapshot quota = DashboardState.CodexQuota;
        MiniMaxSnapshot minimax = DashboardState.MiniMax;
        DeepSeekSnapshot deepseek = DashboardState.DeepSeek;

        Bindings.timeText.Text = DateTime.Now.ToString("HH:mm:ss");
        Color status = quota.Available ? Color.FromRgb(74, 222, 128) : Color.FromRgb(236, 83, 83);
        Bindings.statusDot.Fill = new SolidColorBrush(status);
        Bindings.statusGlow.Fill = GlowBrush(status);

        if (Settings.codex.enabled)
        {
            Bindings.fivePercent.Text = quota.FiveHourAvailable ? quota.FiveHourRemainingPercent + "%" : "--";
            Bindings.weekPercent.Text = quota.WeeklyAvailable ? quota.WeeklyRemainingPercent + "%" : "--";
            Bindings.weekUsed.Text = FormatTokens(usage.WeekTokens);
            Bindings.fiveUsed.Text = quota.FiveHourAvailable
                ? PresentationText.TMinus(quota.FiveHourResetsAt, DateTime.Now)
                : "";
            UpdateMeter(Bindings.fiveFill, Bindings.fiveTrack, quota.FiveHourAvailable, quota.FiveHourRemainingPercent);
            UpdateMeter(Bindings.weekFill, Bindings.weekTrack, quota.WeeklyAvailable, quota.WeeklyRemainingPercent);
            Bindings.codexTokenUsage.Text = quota.Available ? "tokens " + FormatTokens(usage.TotalTokens) : "";
        }
        if (Settings.minimax.enabled)
        {
            Bindings.miniFivePercent.Text = minimax.Available ? minimax.FiveHourRemainingPercent + "%" : "--";
            Bindings.miniWeekPercent.Text = minimax.Available ? minimax.WeeklyRemainingPercent + "%" : "--";
            Bindings.miniFiveUsed.Text = PresentationText.MiniMaxTMinus(minimax.RemainsTime);
            Bindings.miniWeekUsed.Text = "";
            UpdateMeter(Bindings.miniFiveFill, Bindings.miniFiveTrack, minimax.Available, minimax.FiveHourRemainingPercent);
            UpdateMeter(Bindings.miniWeekFill, Bindings.miniWeekTrack, minimax.Available, minimax.WeeklyRemainingPercent);
            string miniTip = (minimax.IsStale ? "Stale: " : "") + minimax.Status;
            if (!string.IsNullOrEmpty(minimax.RemainsTime)) miniTip += "\n5h: " + minimax.RemainsTime;
            Bindings.miniHeading.ToolTip = miniTip;
            Bindings.miniFivePercent.ToolTip = miniTip;
            Bindings.miniWeekPercent.ToolTip = miniTip;
        }
        if (Settings.deepseek.enabled)
        {
            int? balancePercent = ProviderMath.RemainingPercent(deepseek.TotalBalance, Settings.deepseek.referenceBudget);
            Bindings.deepSeekPercent.Text = (deepseek.Available && balancePercent.HasValue) ? balancePercent.Value + "%" : "--";
            Bindings.deepSeekUsed.Text = deepseek.Available ? CurrencySymbol(deepseek.Currency) + deepseek.TotalBalance.ToString("0.00") : deepseek.Status;
            UpdateMeter(Bindings.deepSeekFill, Bindings.deepSeekTrack, deepseek.Available && balancePercent.HasValue, balancePercent.GetValueOrDefault());
            decimal estimatedCost = EstimateCost();
            string estimate = estimatedCost > 0m && deepseek.TotalBalance >= 0m ? "\nAbout " + Math.Floor(deepseek.TotalBalance / estimatedCost).ToString("0") + " configured tasks" : "";
            string deepTip = "Balance: " + CurrencySymbol(deepseek.Currency) + deepseek.TotalBalance.ToString("0.00") + "\nTopped up: " + CurrencySymbol(deepseek.Currency) + deepseek.ToppedUpBalance.ToString("0.00") + "\nGranted: " + CurrencySymbol(deepseek.Currency) + deepseek.GrantedBalance.ToString("0.00") + "\nReference budget: " + CurrencySymbol(deepseek.Currency) + Settings.deepseek.referenceBudget.ToString("0.00") + estimate + "\nStatus: " + (deepseek.Source == ProviderSource.Manual ? "Manual" : deepseek.IsStale ? "Stale" : "Fresh") + "\n" + deepseek.Status;
            Bindings.deepSeekHeading.ToolTip = deepTip;
            Bindings.deepSeekPercent.ToolTip = deepTip;
            Bindings.deepSeekUsed.ToolTip = deepTip;
        }
    }

    decimal EstimateCost()
    {
        TokenPrices price;
        if (!Settings.deepseek.estimate.enabled || !Settings.deepseek.pricesPerMillionTokens.TryGetValue(Settings.deepseek.estimate.model, out price)) return 0m;
        return ProviderMath.EstimatedCostPerRequest(Settings.deepseek.estimate.averageInputTokens, Settings.deepseek.estimate.averageOutputTokens, Settings.deepseek.estimate.cacheHitRatio, price.inputCacheHit, price.inputCacheMiss, price.output);
    }

    void UpdateMeter(Border fill, Border track, bool available, int remaining)
    {
        double width = track.ActualWidth;
        if (width <= 0) width = Width - 40;
        if (!available)
        {
            fill.Background = new SolidColorBrush(MeterFillColor(false, remaining));
            fill.Width = MeterFillWidth(false, remaining, width);
            return;
        }

        fill.Background = new SolidColorBrush(MeterFillColor(true, remaining));
        fill.Width = MeterFillWidth(true, remaining, width);
    }

    static Color MeterFillColor(bool available, int remaining)
    {
        return available ? QuietGlassPalette.AvailableAllowance : QuietGlassPalette.UnavailableAllowance;
    }

    static double MeterFillWidth(bool available, int remaining, double trackWidth)
    {
        if (!available) return Math.Max(4, trackWidth);
        return Math.Max(4, trackWidth * Math.Max(0, Math.Min(100, remaining)) / 100.0);
    }

    internal static Color MeterFillColorForTest(bool available, int remaining)
    {
        return MeterFillColor(available, remaining);
    }

    internal static double MeterFillWidthForTest(bool available, int remaining, double trackWidth)
    {
        return MeterFillWidth(available, remaining, trackWidth);
    }

    public void StartRefresh()
    {
        _nextRefreshAt = DateTime.Now.AddSeconds(Settings.refreshSeconds);
        if (Settings.codex.enabled && !_codexBusy) RefreshCodex();
        if (Settings.minimax.enabled && !_minimaxBusy) RefreshMiniMax();
        if (Settings.deepseek.enabled && !_deepseekBusy) RefreshDeepSeek();
    }

    void RefreshCodex()
    {
        _codexBusy = true;
        UsageSnapshot prevUsage = DashboardState.Usage;
        QuotaSnapshot prevQuota = DashboardState.CodexQuota;
        ThreadPool.QueueUserWorkItem(delegate
        {
            UsageSnapshot nextUsage;
            QuotaSnapshot nextQuota;
            try { nextUsage = NativeSqlite.ReadState(_dbPath); } catch (Exception ex) { nextUsage = new UsageSnapshot(false, 0, 0, 0, 0, ex.Message); }
            try { nextQuota = CodexAppServerQuota.Fetch(); } catch (Exception ex) { nextQuota = new QuotaSnapshot(false, prevQuota.FiveHourRemainingPercent, prevQuota.WeeklyRemainingPercent, ex.Message, prevQuota.FiveHourResetsAt, prevQuota.WeeklyResetsAt); }
            Dispatcher.BeginInvoke(new Action(delegate
            {
                DashboardState.SetUsage(nextUsage);
                DashboardState.SetCodexQuota(nextQuota);
                _codexBusy = false;
            }));
        });
    }

    void RefreshMiniMax()
    {
        _minimaxBusy = true;
        MiniMaxSnapshot prev = DashboardState.MiniMax;
        ThreadPool.QueueUserWorkItem(delegate
        {
            MiniMaxSnapshot next;
            try { next = MiniMaxQuota.Fetch(Settings.minimax); }
            catch (Exception ex) { next = new MiniMaxSnapshot(prev.Available, prev.FiveHourRemainingPercent, prev.WeeklyRemainingPercent, prev.Available, prev.LastSuccessAt, SafeProviderError(ex), ProviderSource.Cached); }
            Dispatcher.BeginInvoke(new Action(delegate
            {
                DashboardState.SetMiniMax(next);
                _minimaxBusy = false;
            }));
        });
    }

    void RefreshDeepSeek()
    {
        _deepseekBusy = true;
        DeepSeekSnapshot prev = DashboardState.DeepSeek;
        ThreadPool.QueueUserWorkItem(delegate
        {
            DeepSeekSnapshot next = null;
            string key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            if (string.IsNullOrWhiteSpace(key)) key = WindowsCredentialStore.Read("CodexDashboard.DeepSeekApiKey");
            bool allowOfficial = !string.Equals(Settings.deepseek.balanceMode, "manualOnly", StringComparison.OrdinalIgnoreCase);
            if (allowOfficial && !string.IsNullOrWhiteSpace(key))
            {
                try { next = DeepSeekBalance.Fetch(key, Settings.deepseek.currency); }
                catch (Exception ex) { if (string.Equals(Settings.deepseek.balanceMode, "officialOnly", StringComparison.OrdinalIgnoreCase)) next = new DeepSeekSnapshot(prev.Available, prev.TotalBalance, prev.GrantedBalance, prev.ToppedUpBalance, prev.Currency, prev.Available, prev.LastSuccessAt, SafeProviderError(ex), ProviderSource.Cached); }
            }
            if (next == null && !string.Equals(Settings.deepseek.balanceMode, "officialOnly", StringComparison.OrdinalIgnoreCase))
                next = new DeepSeekSnapshot(true, Settings.deepseek.manualBalance, 0m, Settings.deepseek.manualBalance, Settings.deepseek.currency, false, DateTime.Now, "DeepSeek manual balance", ProviderSource.Manual);
            if (next == null) next = new DeepSeekSnapshot(prev.Available, prev.TotalBalance, prev.GrantedBalance, prev.ToppedUpBalance, prev.Currency, prev.Available, prev.LastSuccessAt, "DeepSeek API key is not available", ProviderSource.Cached);
            Dispatcher.BeginInvoke(new Action(delegate
            {
                DashboardState.SetDeepSeek(next);
                _deepseekBusy = false;
            }));
        });
    }

    static string SafeProviderError(Exception ex)
    {
        string message = ex == null ? "provider failed" : ex.Message;
        return message.Length > 120 ? message.Substring(0, 120) : message;
    }

    internal static RadialGradientBrush GlowBrush(Color color)
    {
        RadialGradientBrush brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(190, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(72, color.R, color.G, color.B), 0.42));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        return brush;
    }

    internal static string FormatResetCountdown(DateTime? resetAt)
    {
        if (!resetAt.HasValue) return "--";
        TimeSpan left = resetAt.Value - DateTime.Now;
        if (left.TotalSeconds <= 0) return "now";
        if (left.TotalHours >= 1) return ((int)left.TotalHours) + "h " + left.Minutes + "m";
        return left.Minutes + "m";
    }

    internal static string FormatTokens(long value)
    {
        if (value >= 1000000000L) return (value / 1000000000.0).ToString("0.00") + "B";
        if (value >= 1000000L) return (value / 1000000.0).ToString("0.00") + "M";
        if (value >= 1000L) return (value / 1000.0).ToString("0.0") + "K";
        return value.ToString();
    }

    internal static string CurrencySymbol(string currency)
    {
        return string.Equals(currency, "CNY", StringComparison.OrdinalIgnoreCase) ? "\u00a5"
            : string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase) ? "$"
            : (currency ?? "") + " ";
    }

    public static double DesiredHeight(DashboardSettings s)
    {
        double h = 62;
        if (s.codex != null && s.codex.enabled) h += 17 + 3 + 55;
        if (s.minimax != null && s.minimax.enabled) h += ((s.codex != null && s.codex.enabled) ? 10 : 4) + 17 + 3 + 55;
        if (s.deepseek != null && s.deepseek.enabled) h += ((s.codex != null && s.codex.enabled || s.minimax != null && s.minimax.enabled) ? 10 : 4) + 17 + 3 + 42;
        return h + 30;
    }

    internal static void EnableSystemGlass(Window w)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
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
}

static class SettingsDialog
{
    public static void ShowProviderSettings(DashboardSettings settings)
    {
        DashboardSettings candidate = DashboardSettings.Load();
        Window dialog = new Window { Title = "设置", Width = 460, Height = 700, MinWidth = 400, MinHeight = 540, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize, Background = new SolidColorBrush(Color.FromRgb(31, 36, 48)), Foreground = Brushes.White };
        Application app = Application.Current;
        if (app != null && app.MainWindow != null) dialog.Owner = app.MainWindow;
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
            settings.codex.enabled = candidate.codex.enabled; settings.minimax.enabled = candidate.minimax.enabled; settings.minimax.mmxPath = candidate.minimax.mmxPath; settings.minimax.quotaModelName = candidate.minimax.quotaModelName; settings.deepseek.enabled = candidate.deepseek.enabled; settings.deepseek.balanceMode = candidate.deepseek.balanceMode; settings.deepseek.currency = candidate.deepseek.currency; settings.deepseek.manualBalance = candidate.deepseek.manualBalance; settings.deepseek.referenceBudget = candidate.deepseek.referenceBudget; dialog.Close();
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons); dialog.ShowDialog();
    }

    static TextBlock Section(string text) { return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(133, 181, 255)), Margin = new Thickness(0, 16, 0, 6) }; }
    static CheckBox Check(string text, bool value) { return new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 3, 0, 4) }; }
    static TextBox Field(StackPanel parent, string label, string value) { parent.Children.Add(new TextBlock { Text = label, Opacity = 0.72, Margin = new Thickness(0, 7, 0, 2) }); TextBox box = new TextBox { Text = value ?? "", Padding = new Thickness(6, 3, 6, 3) }; parent.Children.Add(box); return box; }
    static PasswordBox SecretField(StackPanel parent, string label) { parent.Children.Add(new TextBlock { Text = label, Opacity = 0.72, Margin = new Thickness(0, 7, 0, 2) }); PasswordBox box = new PasswordBox { Padding = new Thickness(6, 3, 6, 3) }; parent.Children.Add(box); return box; }
    static ComboBox Choice(StackPanel parent, string label, string[] values, string selected) { parent.Children.Add(new TextBlock { Text = label, Opacity = 0.72, Margin = new Thickness(0, 7, 0, 2) }); ComboBox box = new ComboBox { Padding = new Thickness(4, 2, 4, 2) }; foreach (string value in values) box.Items.Add(value); box.SelectedItem = Array.IndexOf(values, selected) >= 0 ? selected : values[0]; parent.Children.Add(box); return box; }
    static bool TryDecimal(string text, out decimal value) { return decimal.TryParse(text, out value) || decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value); }
}

interface INotifyIconBackend
{
    event System.Windows.Forms.MouseEventHandler MouseMove;
    event EventHandler MouseLeave;
    event EventHandler Click;
    event EventHandler RightClick;
    void Show();
    void Hide();
    void SetIcon(System.Drawing.Icon icon);
    void SetContextMenu(System.Windows.Forms.ContextMenu menu);
    void Dispose();
}

class TrayIconBackend : INotifyIconBackend
{
    internal System.Windows.Forms.NotifyIcon _icon;
    public event System.Windows.Forms.MouseEventHandler MouseMove;
    public event EventHandler MouseLeave;
    public event EventHandler Click;
    public event EventHandler RightClick;

    public TrayIconBackend()
    {
        _icon = new System.Windows.Forms.NotifyIcon();
        _icon.MouseMove += delegate(object s, System.Windows.Forms.MouseEventArgs e) { var h = MouseMove; if (h != null) h(s, e); };
        _icon.Click += delegate { var h = Click; if (h != null) h(this, EventArgs.Empty); };
        _icon.MouseClick += delegate(object s, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Right) return;
            var h = RightClick; if (h != null) h(this, EventArgs.Empty);
        };
    }

    public void Show() { _icon.Visible = true; }
    public void Hide() { _icon.Visible = false; }
    public void SetIcon(System.Drawing.Icon icon) { _icon.Icon = icon; }
    public void SetContextMenu(System.Windows.Forms.ContextMenu menu) { _icon.ContextMenu = menu; }
    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}

static class BitmapFactory
{
    public static System.Drawing.Icon CreateTrayIcon(System.Drawing.Color healthyDot, System.Drawing.Color errorDot)
    {
        using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(16, 16))
        using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            DrawDot(g, 4, 8, healthyDot);
            DrawDot(g, 12, 8, errorDot);

            IntPtr hicon = bmp.GetHicon();
            return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hicon).Clone();
        }
    }

    static void DrawDot(System.Drawing.Graphics g, int cx, int cy, System.Drawing.Color color)
    {
        using (System.Drawing.SolidBrush brush = new System.Drawing.SolidBrush(color))
        {
            g.FillEllipse(brush, cx - 3, cy - 3, 6, 6);
        }
        using (System.Drawing.Pen pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(160, 168, 184), 1))
        {
            g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
        }
    }
}

enum TrayEdge
{
    Top,
    Bottom
}

class TrayController : IDisposable
{
    const int PopupGap = 8;
    const int AutoHideReserve = 64;
    const int TrayAnchorTolerance = 32;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    readonly INotifyIconBackend _backend;
    readonly DashboardSettings _settings;
    readonly PopupWindow _popup;
    readonly DispatcherTimer _hoverTimer;
    readonly DispatcherTimer _dismissTimer;
    readonly DispatcherTimer _cursorPollTimer;
    bool _cursorOverTray;
    bool _hasTrayAnchor;
    System.Drawing.Point _lastTrayPoint;

    public PopupWindow Popup { get { return _popup; } }

    public TrayController(DashboardSettings settings, INotifyIconBackend backend, PopupWindow popup)
    {
        _settings = settings;
        _backend = backend;
        _popup = popup;
        _cursorOverTray = false;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(settings.popupHoverDelayMs) };
        _hoverTimer.Tick += delegate { _hoverTimer.Stop(); ShowPopup(); };

        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(settings.popupDismissDelayMs) };
        _dismissTimer.Tick += delegate
        {
            _dismissTimer.Stop();
            if (!_popup.IsSticky) _popup.Hide();
        };

        // Cursor poll: NotifyIcon has no MouseLeave event; poll Control.MousePosition
        // every 100ms to detect when the cursor leaves the active monitor's top or
        // bottom tray band. Only run while the popup is visible.
        _cursorPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _cursorPollTimer.Tick += delegate { OnCursorPollTick(); };
        _popup.IsVisibleChanged += delegate
        {
            if (_popup.IsVisible) _cursorPollTimer.Start();
            else _cursorPollTimer.Stop();
        };

        _backend.MouseMove += delegate { OnTrayMouseMove(); };
        _popup.MouseEnter += delegate { _dismissTimer.Stop(); };
        _popup.MouseLeave += delegate { if (!_popup.IsSticky) _dismissTimer.Start(); };

        try
        {
            _backend.SetIcon(BitmapFactory.CreateTrayIcon(
                System.Drawing.ColorTranslator.FromHtml("#74DE80"),
                System.Drawing.ColorTranslator.FromHtml("#EC5353")));
            _backend.Show();
        }
        catch { /* fallback: handled in Task 9 if tray unavailable */ }

        System.Windows.Forms.ContextMenu menu = new System.Windows.Forms.ContextMenu();
        System.Windows.Forms.MenuItem pinItem = new System.Windows.Forms.MenuItem("钉住 popup");
        pinItem.Click += delegate
        {
            if (!_popup.IsSticky)
            {
                _popup.EnterSticky();
                ShowStickyPopup();
            }
            pinItem.Checked = _popup.IsSticky;
        };
        menu.Popup += delegate { pinItem.Checked = _popup.IsSticky; };

        System.Windows.Forms.MenuItem settingsItem = new System.Windows.Forms.MenuItem("设置…");
        settingsItem.Click += delegate
        {
            SettingsDialog.ShowProviderSettings(_settings);
            _popup.Height = PopupWindow.DesiredHeight(_settings);
        };

        System.Windows.Forms.MenuItem exitItem = new System.Windows.Forms.MenuItem("退出");
        exitItem.Click += delegate { System.Windows.Application.Current.Shutdown(); };

        menu.MenuItems.Add(pinItem);
        menu.MenuItems.Add(settingsItem);
        menu.MenuItems.Add(new System.Windows.Forms.MenuItem("-"));
        menu.MenuItems.Add(exitItem);

        TrayIconBackend tib = backend as TrayIconBackend;
        System.Windows.Forms.NotifyIcon ni = tib == null ? null : tib._icon;
        if (ni != null) ni.ContextMenu = menu;

        _backend.Click += delegate { ShowPopup(); };
    }

    void OnTrayMouseMove()
    {
        _dismissTimer.Stop();
        _lastTrayPoint = System.Windows.Forms.Control.MousePosition;
        _hasTrayAnchor = true;
        _cursorOverTray = true;
        if (!_hoverTimer.IsEnabled && !_popup.IsVisible) _hoverTimer.Start();
    }

    void OnCursorPollTick()
    {
        System.Drawing.Point cursor = System.Windows.Forms.Control.MousePosition;
        bool overTrayArea = IsOverTrayArea(cursor);
        if (overTrayArea)
        {
            _cursorOverTray = true;
            _dismissTimer.Stop();
        }
        else if (_cursorOverTray)
        {
            _cursorOverTray = false;
            if (_popup.IsVisible && !_popup.IsSticky && !_popup.IsMouseOver)
            {
                _dismissTimer.Start();
            }
        }
    }

    bool IsOverTrayArea(System.Drawing.Point cursor)
    {
        // Use the cursor's own monitor's work area, not SystemParameters.WorkArea
        // (which is the primary monitor only). On dual-screen setups where the
        // tray icon lives on the secondary, the primary-only check would
        // immediately fail and dismiss the popup 300ms after it appeared.
        System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromPoint(cursor);
        if (screen == null) return false;
        if (_hasTrayAnchor) return IsWithinTrayAnchor(cursor, _lastTrayPoint, TrayAnchorTolerance);
        System.Drawing.Rectangle wa = screen.WorkingArea;
        TrayEdge edge = InferTrayEdge(cursor, screen.Bounds);
        bool inEdgeBand = edge == TrayEdge.Top
            ? cursor.Y <= wa.Top + 16
            : cursor.Y >= wa.Bottom - 16;
        return inEdgeBand;
    }

    static bool IsWithinTrayAnchor(
        System.Drawing.Point cursor, System.Drawing.Point anchor, int tolerance)
    {
        return tolerance >= 0
            && Math.Abs(cursor.X - anchor.X) <= tolerance
            && Math.Abs(cursor.Y - anchor.Y) <= tolerance;
    }

    internal static bool IsWithinTrayAnchorForTest(
        System.Drawing.Point cursor, System.Drawing.Point anchor, int tolerance)
    {
        return IsWithinTrayAnchor(cursor, anchor, tolerance);
    }

    public void ShowPopup()
    {
        ShowAutoPositionedPopup();
    }

    public void ShowStickyPopup()
    {
        Rect virtualDipBounds = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        if (IsSavedDipPositionOnVirtualScreen(
            _settings.popupLeft, _settings.popupTop, _popup.Width, _popup.Height, virtualDipBounds))
        {
            _popup.Left = _settings.popupLeft;
            _popup.Top = _settings.popupTop;
            _popup.Show();
            return;
        }

        ShowAutoPositionedPopup();
    }

    void ShowAutoPositionedPopup()
    {
        System.Drawing.Point cursorPos = System.Windows.Forms.Control.MousePosition;
        System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromPoint(cursorPos);

        // Keep a visible WPF/DIP fallback in case native positioning is unavailable.
        _popup.Left = SystemParameters.WorkArea.Right - _popup.Width - PopupGap;
        _popup.Top = SystemParameters.WorkArea.Bottom - _popup.Height - PopupGap;
        _popup.Show();

        IntPtr hwnd = new WindowInteropHelper(_popup).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Stage 1: move the HWND into the cursor's monitor. Crossing the monitor
        // boundary can change its DPI-dependent size, so the old size is not used.
        System.Drawing.Point probeOrigin = CalculateProbeOrigin(screen.Bounds, screen.WorkingArea);
        if (!SetWindowPos(hwnd, IntPtr.Zero, probeOrigin.X, probeOrigin.Y, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE)) return;

        // Stage 2: remeasure in the target monitor's process-coordinate space, then
        // calculate and apply the final origin without mixing in WPF DIP values.
        NativeRect targetRect;
        if (!GetWindowRect(hwnd, out targetRect)) return;
        System.Drawing.Size targetSize = new System.Drawing.Size(
            Math.Max(1, targetRect.Right - targetRect.Left),
            Math.Max(1, targetRect.Bottom - targetRect.Top));
        System.Drawing.Point finalOrigin = CalculatePopupOrigin(
            cursorPos, screen.Bounds, screen.WorkingArea, targetSize);
        // If this final move fails, the stage-1 probe remains safely visible.
        if (!SetWindowPos(hwnd, IntPtr.Zero, finalOrigin.X, finalOrigin.Y, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE)) return;
    }

    static bool IsSavedDipPositionOnVirtualScreen(
        double left, double top, double width, double height, Rect virtualDipBounds)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || width <= 0 || height <= 0) return false;
        Rect savedRect = new Rect(left, top, width, height);
        Rect visibleRect = Rect.Intersect(savedRect, virtualDipBounds);
        return !visibleRect.IsEmpty && visibleRect.Width >= 50 && visibleRect.Height >= 50;
    }

    internal static bool IsSavedDipPositionOnVirtualScreenForTest(
        double left, double top, double width, double height, Rect virtualDipBounds)
    {
        return IsSavedDipPositionOnVirtualScreen(left, top, width, height, virtualDipBounds);
    }

    static TrayEdge InferTrayEdge(System.Drawing.Point cursor, System.Drawing.Rectangle bounds)
    {
        int distanceToTop = Math.Abs(cursor.Y - bounds.Top);
        int distanceToBottom = Math.Abs(bounds.Bottom - cursor.Y);
        return distanceToTop <= distanceToBottom ? TrayEdge.Top : TrayEdge.Bottom;
    }

    internal static TrayEdge InferTrayEdgeForTest(System.Drawing.Point cursor, System.Drawing.Rectangle bounds)
    {
        return InferTrayEdge(cursor, bounds);
    }

    static System.Drawing.Point CalculateProbeOrigin(
        System.Drawing.Rectangle bounds, System.Drawing.Rectangle workingArea)
    {
        int safeTop = Math.Max(workingArea.Top, bounds.Top + AutoHideReserve);
        return new System.Drawing.Point(workingArea.Left + PopupGap, safeTop + PopupGap);
    }

    internal static System.Drawing.Point CalculateProbeOriginForTest(
        System.Drawing.Rectangle bounds, System.Drawing.Rectangle workingArea)
    {
        return CalculateProbeOrigin(bounds, workingArea);
    }

    static System.Drawing.Point CalculatePopupOrigin(
        System.Drawing.Point cursor,
        System.Drawing.Rectangle bounds,
        System.Drawing.Rectangle workingArea,
        System.Drawing.Size popupSize)
    {
        TrayEdge edge = InferTrayEdge(cursor, bounds);
        int x = cursor.X - popupSize.Width - PopupGap;
        int minX = workingArea.Left + PopupGap;
        int maxX = workingArea.Right - popupSize.Width - PopupGap;
        x = Math.Max(minX, Math.Min(x, maxX));

        int y;
        if (edge == TrayEdge.Top)
        {
            int safeTop = Math.Max(workingArea.Top, bounds.Top + AutoHideReserve);
            y = safeTop + PopupGap;
        }
        else
        {
            int safeBottom = Math.Min(workingArea.Bottom, bounds.Bottom - AutoHideReserve);
            y = safeBottom - popupSize.Height - PopupGap;
        }

        int minY = workingArea.Top + PopupGap;
        int maxY = workingArea.Bottom - popupSize.Height - PopupGap;
        y = Math.Max(minY, Math.Min(y, maxY));
        return new System.Drawing.Point(x, y);
    }

    internal static System.Drawing.Point CalculatePopupOriginForTest(
        System.Drawing.Point cursor,
        System.Drawing.Rectangle bounds,
        System.Drawing.Rectangle workingArea,
        System.Drawing.Size popupSize)
    {
        return CalculatePopupOrigin(cursor, bounds, workingArea, popupSize);
    }

    public void Dispose()
    {
        _cursorPollTimer.Stop();
        _hoverTimer.Stop();
        _dismissTimer.Stop();
        _popup.Close();
        _backend.Dispose();
    }

}

static class Program
{
    [STAThread]
    static void Main()
    {
        DashboardSettings settings = DashboardSettings.Load();
        Application app = new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        Window bootstrapper = new Window { Width = 0, Height = 0, ShowInTaskbar = false, Visibility = Visibility.Hidden, WindowStyle = WindowStyle.None };
        bootstrapper.Show();
        bootstrapper.Hide();

        PopupWindow popup = new PopupWindow(settings);
        TrayIconBackend backend = new TrayIconBackend();
        TrayController controller = new TrayController(settings, backend, popup);

        app.Exit += delegate { controller.Dispose(); };

        if (settings.popupStickyOnLaunch)
        {
            popup.EnterSticky();
            controller.ShowStickyPopup();
        }

        app.Run(bootstrapper);
    }
}
