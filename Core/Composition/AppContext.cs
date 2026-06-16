namespace PiPiClaw.Team;

/// <summary>
/// Shared application state. This keeps the AOT-friendly static wiring that used to live in Program.cs.
/// </summary>
public static class AppContext
{
    public static AppConfig Config { get; set; } = new();
    public static string ConfigPath { get; set; } = "team_config.json";
    public static readonly string AutoCollectionConfigPath = Path.Combine("data", "worldcup_auto_sources.json");
    public static readonly string ModelBacktestCachePath = Path.Combine("data", "worldcup_model_backtest_cache.json");
    public static DataSourceAutoCollectionRunResult? LastAutoCollectionRun;
    public static DataSourceAutoCollectionRunResult? CurrentAutoCollectionRun;
    public static AutoLlmReportRunResult? LastAutoLlmReportRun;
    public static readonly SemaphoreSlim AutoCollectionLock = new(1, 1);
    public static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(30) };
    public static readonly WorldCupStore WorldCupStore = new(Path.Combine("data", "worldcup_company.db"));
    public static readonly LlmGateway LlmGateway = new(HttpClient);

#if DEBUG
    public const string BossMarketUrl = "http://ddns.work:8888";
#else
    public const string BossMarketUrl = "http://ddns.work:8888";
#endif
}
