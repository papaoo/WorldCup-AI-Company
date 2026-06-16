using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PiPiClaw.Team;

public static class DataSourceAdapter
{
    public static async Task<DataSnapshotBatchImportRequest> LoadSnapshotsAsync(DataSourceImportRequest request, CancellationToken cancellationToken = default)
    {
        var sourceName = string.IsNullOrWhiteSpace(request.SourceName) ? "external_json" : request.SourceName.Trim();
        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            return await LoadProviderSnapshotsAsync(request, sourceName, cancellationToken);
        }

        var json = await ReadJsonAsync(request, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var batch = new DataSnapshotBatchImportRequest
        {
            Source = sourceName
        };

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                batch.Items.Add(ParseSnapshotItem(item, sourceName));
            }
            return batch;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Data source JSON must be an object or an array.");
        }

        if (root.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(source.GetString()))
        {
            batch.Source = source.GetString()!;
        }

        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Data source object must contain an items array.");
        }

        foreach (var item in items.EnumerateArray())
        {
            batch.Items.Add(ParseSnapshotItem(item, batch.Source));
        }
        return batch;
    }

    private static async Task<string> ReadJsonAsync(DataSourceImportRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            var path = Path.GetFullPath(request.FilePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Data source file not found: {path}");
            }
            return await File.ReadAllTextAsync(path, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.Url))
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Data source url must be an absolute http or https URL.");
            }
            return await GetStringWithRetryAsync(uri.ToString(), 30, cancellationToken: cancellationToken);
        }

        throw new ArgumentException("Either file_path or url is required.");
    }

    private static DataSnapshotCreateRequest ParseSnapshotItem(JsonElement item, string sourceName)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Each data source item must be an object.");
        }

        var source = ReadString(item, "source");
        var snapshotType = ReadString(item, "snapshot_type");
        var objectId = ReadString(item, "object_id");
        var matchId = ReadString(item, "match_id");
        var contentJson = ReadContentJson(item);

        if (string.IsNullOrWhiteSpace(contentJson))
        {
            throw new ArgumentException("Each data source item must contain content_json or content.");
        }

        return new DataSnapshotCreateRequest
        {
            Source = string.IsNullOrWhiteSpace(source) ? sourceName : source!,
            SnapshotType = string.IsNullOrWhiteSpace(snapshotType) ? "team_intel" : snapshotType!,
            ObjectId = objectId,
            MatchId = matchId,
            ContentJson = contentJson
        };
    }

    private static string? ReadString(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string ReadContentJson(JsonElement item)
    {
        if (item.TryGetProperty("content_json", out var contentJson))
        {
            return contentJson.ValueKind == JsonValueKind.String
                ? contentJson.GetString() ?? ""
                : JsonSerializer.Serialize(contentJson, AppJsonContext.Default.JsonElement);
        }

        if (item.TryGetProperty("content", out var content))
        {
            return JsonSerializer.Serialize(content, AppJsonContext.Default.JsonElement);
        }

        return "";
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadProviderSnapshotsAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var provider = request.Provider!.Trim().ToLowerInvariant();
        return provider switch
        {
            "gnews" => await LoadGNewsAsync(request, sourceName, cancellationToken),
            "the_odds_api" => await LoadTheOddsApiAsync(request, sourceName, cancellationToken),
            "football_data" => await LoadFootballDataAsync(request, sourceName, cancellationToken),
            "worldcup26" => await LoadWorldCup26Async(request, sourceName, cancellationToken),
            "fifa_ranking" => await LoadFifaRankingAsync(request, sourceName, cancellationToken),
            "world_football_elo" => await LoadWorldFootballEloAsync(request, sourceName, cancellationToken),
            "international_results" => await LoadInternationalResultsAsync(request, sourceName, cancellationToken),
            "openfootball_schedule" => await LoadOpenFootballScheduleAsync(request, sourceName, cancellationToken),
            "fixturedownload_schedule" => await LoadFixtureDownloadScheduleAsync(request, sourceName, cancellationToken),
            "espn_scoreboard" => await LoadEspnScoreboardAsync(request, sourceName, cancellationToken),
            "espn_summary" => await LoadEspnSummaryAsync(request, sourceName, cancellationToken),
            "rss_news" => await LoadRssNewsAsync(request, sourceName, cancellationToken),
            _ => throw new ArgumentException($"Unsupported data source provider: {request.Provider}")
        };
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadWorldCup26Async(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(request.Query) ? "games" : request.Query.Trim().ToLowerInvariant();
        var endpoint = query switch
        {
            "teams" => "teams",
            "team" => "teams",
            "games" => "games",
            "matches" => "games",
            "groups" => "groups",
            "group" => "groups",
            "stadiums" => "stadiums",
            "venues" => "stadiums",
            _ => throw new ArgumentException($"Unsupported worldcup26 query: {request.Query}")
        };

        var url = $"https://worldcup26.ir/get/{endpoint}";
        var json = await GetStringWithRetryAsync(url, 60, cancellationToken: cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };

        var propertyName = endpoint switch
        {
            "games" => "games",
            "teams" => "teams",
            "groups" => "groups",
            "stadiums" => "stadiums",
            _ => endpoint
        };
        if (!doc.RootElement.TryGetProperty(propertyName, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return batch;
        }

        foreach (var item in items.EnumerateArray())
        {
            var snapshotType = endpoint switch
            {
                "games" => "fixture_intel",
                "teams" => "team_profile",
                "groups" => "group_standing",
                "stadiums" => "stadium_profile",
                _ => "source_payload"
            };
            batch.Items.Add(new DataSnapshotCreateRequest
            {
                Source = sourceName,
                SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? snapshotType : request.SnapshotType!,
                ObjectId = ResolveWorldCup26ObjectId(endpoint, item),
                MatchId = endpoint == "games" ? $"match_wc26_{ReadJsonString(item, "id")}" : request.MatchId,
                ContentJson = new JsonObject
                {
                    ["provider"] = "worldcup26",
                    ["endpoint"] = endpoint,
                    ["payload"] = JsonNode.Parse(item.GetRawText())
                }.ToJsonString()
            });
        }
        return batch;
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadWorldFootballEloAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var url = string.IsNullOrWhiteSpace(request.Url)
            ? "https://www.eloratings.net/World.tsv"
            : request.Url.Trim();
        var text = await GetStringWithRetryAsync(url, 45, cancellationToken: cancellationToken);
        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };
        var capturedDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = rawLine.Split('\t');
            if (parts.Length < 4) continue;
            var eloCode = parts[2].Trim().ToUpperInvariant();
            var fifaCode = EloCodeToFifaCode(eloCode);
            if (string.IsNullOrWhiteSpace(fifaCode)) continue;
            if (!int.TryParse(parts[0], out var rank)) continue;
            if (!int.TryParse(parts[3], out var rating)) continue;

            batch.Items.Add(new DataSnapshotCreateRequest
            {
                Source = sourceName,
                SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? "team_elo" : request.SnapshotType!,
                ObjectId = $"team_{fifaCode.ToLowerInvariant()}",
                ContentJson = new JsonObject
                {
                    ["provider"] = "world_football_elo",
                    ["source_url"] = url,
                    ["captured_date_utc"] = capturedDate,
                    ["elo_code"] = eloCode,
                    ["country_code"] = fifaCode,
                    ["elo_rank"] = rank,
                    ["elo_rating"] = rating,
                    ["raw_tsv"] = rawLine
                }.ToJsonString()
            });
        }

        return batch;
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadInternationalResultsAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var canonicalUrl = string.IsNullOrWhiteSpace(request.Url)
            ? "https://cdn.jsdelivr.net/gh/martj42/international_results@master/results.csv"
            : request.Url.Trim();
        var sourceUrls = BuildInternationalResultsUrls(canonicalUrl);
        string csv;
        string retrievalUrl;
        List<string> retrievalNotes;
        try
        {
            (csv, retrievalUrl, retrievalNotes) = await FetchFirstAvailableTextAsync(sourceUrls, timeoutSeconds: 18, cancellationToken);
        }
        catch (Exception ex)
        {
            return new DataSnapshotBatchImportRequest
            {
                Source = sourceName,
                AllowEmptySuccess = true,
                Notes =
                [
                    $"international_results degraded: {ex.Message}",
                    "Using previously imported team_recent_form snapshots until the public CSV source is reachable."
                ]
            };
        }
        var since = DateTime.UtcNow.Date.AddYears(-3);
        if (!string.IsNullOrWhiteSpace(request.DateFrom)
            && DateTime.TryParse(request.DateFrom, out var parsedSince))
        {
            since = parsedSince.Date;
        }

        var aggregates = BuildRecentFormSeed();
        var matchCandidates = new List<RecentMatchCandidate>();
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
        {
            var fields = ParseCsvLine(line);
            if (fields.Count < 9) continue;
            if (!DateTime.TryParse(fields[0], out var date) || date.Date < since) continue;
            if (!int.TryParse(fields[3], out var homeScore) || !int.TryParse(fields[4], out var awayScore)) continue;

            var homeCode = TeamNameToFifaCode(fields[1]);
            var awayCode = TeamNameToFifaCode(fields[2]);
            if (!string.IsNullOrWhiteSpace(homeCode))
            {
                AddRecentResult(aggregates, homeCode, homeScore, awayScore, date, fields[5], true);
                if (!string.IsNullOrWhiteSpace(awayCode))
                {
                    matchCandidates.Add(new RecentMatchCandidate(homeCode, awayCode, homeScore, awayScore, date, fields[5], true));
                }
            }
            if (!string.IsNullOrWhiteSpace(awayCode))
            {
                AddRecentResult(aggregates, awayCode, awayScore, homeScore, date, fields[5], false);
                if (!string.IsNullOrWhiteSpace(homeCode))
                {
                    matchCandidates.Add(new RecentMatchCandidate(awayCode, homeCode, awayScore, homeScore, date, fields[5], false));
                }
            }
        }
        AddOpponentAdjustedRecentForm(aggregates, matchCandidates);

        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };
        batch.Notes.AddRange(retrievalNotes);
        batch.Notes.Add($"international_results loaded via {retrievalUrl}.");
        foreach (var aggregate in aggregates.Values.Where(item => item.Matches > 0))
        {
            var pointsPerMatch = aggregate.Matches == 0 ? 0 : aggregate.Points / (double)aggregate.Matches;
            var goalDiffPerMatch = aggregate.Matches == 0 ? 0 : (aggregate.GoalsFor - aggregate.GoalsAgainst) / (double)aggregate.Matches;
            var recentFormScore = Math.Clamp((pointsPerMatch - 1.0) / 2.0 + goalDiffPerMatch / 6.0, -0.65, 0.65);
            var opponentAdjustedFormScore = aggregate.AdjustedWeight <= 0
                ? recentFormScore
                : Math.Clamp(aggregate.AdjustedScore / aggregate.AdjustedWeight, -0.75, 0.75);
            batch.Items.Add(new DataSnapshotCreateRequest
            {
                Source = sourceName,
                SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? "team_recent_form" : request.SnapshotType!,
                ObjectId = $"team_{aggregate.FifaCode.ToLowerInvariant()}",
                ContentJson = new JsonObject
                {
                    ["provider"] = "international_results",
                    ["source_url"] = "martj42/international_results/results.csv",
                    ["date_from"] = since.ToString("yyyy-MM-dd"),
                    ["country_code"] = aggregate.FifaCode,
                    ["matches"] = aggregate.Matches,
                    ["wins"] = aggregate.Wins,
                    ["draws"] = aggregate.Draws,
                    ["losses"] = aggregate.Losses,
                    ["goals_for"] = aggregate.GoalsFor,
                    ["goals_against"] = aggregate.GoalsAgainst,
                    ["points_per_match"] = Math.Round(pointsPerMatch, 3),
                    ["goal_diff_per_match"] = Math.Round(goalDiffPerMatch, 3),
                    ["recent_form_score"] = Math.Round(recentFormScore, 6),
                    ["opponent_adjusted_form_score"] = Math.Round(opponentAdjustedFormScore, 6),
                    ["opponent_adjusted_weight"] = Math.Round(aggregate.AdjustedWeight, 3),
                    ["latest_match_date"] = aggregate.LatestDate?.ToString("yyyy-MM-dd"),
                    ["sample_tournaments"] = JsonSerializer.SerializeToNode(aggregate.Tournaments.Take(8).ToList(), AppJsonContext.Default.ListString)
                }.ToJsonString()
            });
        }

        return batch;
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadFifaRankingAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var rankingPageUrl = string.IsNullOrWhiteSpace(request.Url)
            ? "https://football-technology.fifa.com/fifa-world-ranking/men"
            : request.Url.Trim();
        var pageHtml = await GetStringWithRetryAsync(rankingPageUrl, 45, cancellationToken: cancellationToken);
        var rankingMeta = ExtractFifaRankingMeta(pageHtml);
        var scheduleId = string.IsNullOrWhiteSpace(request.Query) ? rankingMeta.ScheduleId : request.Query.Trim();
        if (string.IsNullOrWhiteSpace(scheduleId))
        {
            throw new InvalidOperationException("fifa_ranking_missing_schedule_id: could not resolve FIFA ranking schedule id.");
        }

        var url = $"https://api.fifa.com/api/v3/fifarankings/rankings/rankingsbyschedule?rankingScheduleId={Uri.EscapeDataString(scheduleId)}&count=250&language=en-GB";
        var json = await GetStringWithRetryAsync(url, 45, configure: client =>
        {
            client.DefaultRequestHeaders.Referrer = new Uri(rankingPageUrl);
        }, cancellationToken: cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };
        if (!doc.RootElement.TryGetProperty("Results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return batch;
        }

        foreach (var item in results.EnumerateArray())
        {
            var countryCode = ReadJsonString(item, "IdCountry");
            if (string.IsNullOrWhiteSpace(countryCode))
            {
                continue;
            }

            var rank = ReadJsonString(item, "Rank");
            var totalPoints = ReadJsonString(item, "TotalPoints");
            var previousRank = ReadJsonString(item, "PrevRank");
            var previousPoints = ReadJsonString(item, "PrevPoints");
            var teamName = ReadLocalizedDescription(item, "TeamName") ?? countryCode;
            var confederation = ReadJsonString(item, "ConfederationName");
            var objectId = $"team_{countryCode.ToLowerInvariant()}";

            batch.Items.Add(new DataSnapshotCreateRequest
            {
                Source = sourceName,
                SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? "team_ranking" : request.SnapshotType!,
                ObjectId = objectId,
                ContentJson = new JsonObject
                {
                    ["provider"] = "fifa_official_fdcp",
                    ["source_url"] = url,
                    ["ranking_page_url"] = rankingPageUrl,
                    ["schedule_id"] = scheduleId,
                    ["last_update_date"] = rankingMeta.LastUpdateDate,
                    ["next_update_date"] = rankingMeta.NextUpdateDate,
                    ["country_code"] = countryCode,
                    ["team_name"] = teamName,
                    ["confederation"] = confederation,
                    ["fifa_rank"] = TryParseInt(rank),
                    ["total_points"] = TryParseDecimal(totalPoints),
                    ["previous_rank"] = TryParseInt(previousRank),
                    ["previous_points"] = TryParseDecimal(previousPoints),
                    ["payload"] = JsonNode.Parse(item.GetRawText())
                }.ToJsonString()
            });
        }
        return batch;
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadOpenFootballScheduleAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var url = string.IsNullOrWhiteSpace(request.Url)
            ? "https://raw.githubusercontent.com/openfootball/worldcup.json/master/2026/worldcup.json"
            : request.Url.Trim();
        var json = await GetStringWithRetryAsync(url, 60, cancellationToken: cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };
        if (!doc.RootElement.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.Array)
        {
            return batch;
        }

        var index = 0;
        foreach (var match in matches.EnumerateArray())
        {
            index++;
            batch.Items.Add(new DataSnapshotCreateRequest
            {
                Source = sourceName,
                SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? "fixture_crosscheck" : request.SnapshotType!,
                MatchId = $"match_wc26_{index}",
                ContentJson = new JsonObject
                {
                    ["provider"] = "openfootball",
                    ["dataset"] = "worldcup.json/2026",
                    ["match_index"] = index,
                    ["payload"] = JsonNode.Parse(match.GetRawText())
                }.ToJsonString()
            });
        }
        return batch;
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadFixtureDownloadScheduleAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var url = string.IsNullOrWhiteSpace(request.Url)
            ? "https://fixturedownload.com/feed/json/fifa-world-cup-2026"
            : request.Url.Trim();
        var json = await GetStringWithRetryAsync(url, 60, cancellationToken: cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return batch;
        }

        foreach (var match in doc.RootElement.EnumerateArray())
        {
            var matchNumber = ReadJsonString(match, "MatchNumber") ?? "";
            batch.Items.Add(new DataSnapshotCreateRequest
            {
                Source = sourceName,
                SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? "fixture_crosscheck" : request.SnapshotType!,
                MatchId = string.IsNullOrWhiteSpace(matchNumber) ? null : $"match_wc26_{matchNumber}",
                ContentJson = new JsonObject
                {
                    ["provider"] = "fixturedownload",
                    ["competition"] = "fifa-world-cup-2026",
                    ["payload"] = JsonNode.Parse(match.GetRawText())
                }.ToJsonString()
            });
        }
        return batch;
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadRssNewsAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var feeds = string.IsNullOrWhiteSpace(request.Url)
            ? ["https://www.espn.com/espn/rss/soccer/news", "https://feeds.theguardian.com/theguardian/football/rss"]
            : request.Url.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var query = request.Query?.Trim();
        var articles = new JsonArray();
        var feedRuns = new JsonArray();

        var runs = await Task.WhenAll(feeds.Take(4).Select(feed => FetchRssFeedAsync(feed, query, cancellationToken)));
        foreach (var run in runs)
        {
            foreach (var article in run.Articles)
            {
                articles.Add(article);
            }
            feedRuns.Add((JsonNode)new JsonObject
            {
                ["feed"] = run.Feed,
                ["passed"] = run.Passed,
                ["article_count"] = run.Articles.Count,
                ["error_message"] = run.ErrorMessage
            });
        }

        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };
        var succeeded = runs.Count(run => run.Passed);
        var failed = runs.Length - succeeded;
        var slowest = runs.Length == 0 ? 0 : runs.Max(run => run.ElapsedMs);
        batch.Notes.Add($"RSS feed diagnostics: feeds={runs.Length}, succeeded={succeeded}, failed={failed}, articles={articles.Count}, slowest={slowest}ms.");
        foreach (var run in runs.Where(run => !run.Passed).Take(3))
        {
            batch.Notes.Add($"RSS feed failed: {run.Feed}: {run.ErrorMessage}");
        }
        if (articles.Count == 0)
        {
            batch.Notes.Add("RSS news source returned no matching articles; keep predictions on structured data only.");
        }
        batch.Items.Add(new DataSnapshotCreateRequest
        {
            Source = sourceName,
            SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? "news_intel" : request.SnapshotType!,
            ObjectId = request.ObjectId,
            MatchId = request.MatchId,
            ContentJson = new JsonObject
            {
                ["provider"] = "rss_news",
                ["query"] = query,
                ["feed_runs"] = feedRuns,
                ["articles"] = articles
            }.ToJsonString()
        });
        return batch;
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadEspnScoreboardAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var (from, to) = ResolveEspnScoreboardRange(request);
        var dateRange = $"{from:yyyyMMdd}-{to:yyyyMMdd}";
        var url = string.IsNullOrWhiteSpace(request.Url)
            ? $"https://site.api.espn.com/apis/site/v2/sports/soccer/fifa.world/scoreboard?dates={dateRange}&limit=120"
            : request.Url.Trim();
        var json = await GetStringWithRetryAsync(url, 25, attempts: 2, cancellationToken: cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };
        var fixtureMap = await LoadEspnFixtureMapAsync(cancellationToken);
        if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            batch.Notes.Add($"ESPN scoreboard returned no events for {dateRange}.");
            return batch;
        }

        foreach (var ev in events.EnumerateArray())
        {
            var eventId = ReadJsonString(ev, "id") ?? "";
            if (!ev.TryGetProperty("competitions", out var competitions) || competitions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var competition = competitions.EnumerateArray().FirstOrDefault();
            if (competition.ValueKind != JsonValueKind.Object
                || !competition.TryGetProperty("competitors", out var competitors)
                || competitors.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            JsonElement? home = null;
            JsonElement? away = null;
            foreach (var competitor in competitors.EnumerateArray())
            {
                var side = ReadJsonString(competitor, "homeAway") ?? "";
                if (side.Equals("home", StringComparison.OrdinalIgnoreCase)) home = competitor;
                if (side.Equals("away", StringComparison.OrdinalIgnoreCase)) away = competitor;
            }
            if (home == null || away == null)
            {
                continue;
            }

            var homeCode = ReadEspnTeamAbbreviation(home.Value);
            var awayCode = ReadEspnTeamAbbreviation(away.Value);
            var matchId = ResolveEspnWorldCupMatchId(fixtureMap, homeCode, awayCode, ev, competition);
            var status = ResolveEspnStatus(competition);
            var homeScore = TryParseInt(ReadJsonString(home.Value, "score"));
            var awayScore = TryParseInt(ReadJsonString(away.Value, "score"));
            var details = competition.TryGetProperty("details", out var detailItems) && detailItems.ValueKind == JsonValueKind.Array
                ? BuildEspnEventDetails(detailItems)
                : new JsonArray();
            var stats = BuildEspnTeamStats(home.Value, away.Value);

            var payload = new JsonObject
            {
                ["provider"] = "espn_scoreboard",
                ["source_url"] = url,
                ["event_id"] = eventId,
                ["match_id_guess"] = matchId,
                ["date"] = ReadJsonString(ev, "date"),
                ["status"] = status,
                ["status_detail"] = ReadEspnStatusDetail(competition),
                ["home_code"] = homeCode,
                ["away_code"] = awayCode,
                ["home_team"] = ReadEspnTeamName(home.Value),
                ["away_team"] = ReadEspnTeamName(away.Value),
                ["home_score"] = homeScore,
                ["away_score"] = awayScore,
                ["venue"] = ReadEspnVenue(competition, ev),
                ["attendance"] = TryParseInt(ReadJsonString(competition, "attendance")),
                ["details"] = details,
                ["team_statistics"] = stats,
                ["payload"] = JsonNode.Parse(ev.GetRawText())
            };

            batch.Items.Add(new DataSnapshotCreateRequest
            {
                Source = sourceName,
                SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? "fixture_status" : request.SnapshotType!,
                MatchId = matchId,
                ContentJson = payload.ToJsonString()
            });

            AddEspnMarketSignalSnapshot(batch, sourceName, competition, eventId, matchId, homeCode, awayCode, url, status);

            if (status is "running" or "finished")
            {
                AddEspnTeamSnapshot(batch, sourceName, home.Value, eventId, matchId, homeCode, "home", stats, status);
                AddEspnTeamSnapshot(batch, sourceName, away.Value, eventId, matchId, awayCode, "away", stats, status);
            }
        }

        batch.Notes.Add($"ESPN scoreboard loaded {batch.Items.Count} snapshots for {dateRange}.");
        return batch;
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadEspnSummaryAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var (from, to) = ResolveEspnScoreboardRange(request);
        var dateRange = $"{from:yyyyMMdd}-{to:yyyyMMdd}";
        var scoreboardUrl = $"https://site.api.espn.com/apis/site/v2/sports/soccer/fifa.world/scoreboard?dates={dateRange}&limit=80";
        var scoreboardJson = await GetStringWithRetryAsync(scoreboardUrl, 25, attempts: 2, cancellationToken: cancellationToken);
        using var scoreboardDoc = JsonDocument.Parse(scoreboardJson);
        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };
        var fixtureMap = await LoadEspnFixtureMapAsync(cancellationToken);
        if (!scoreboardDoc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            batch.Notes.Add($"ESPN summary skipped: scoreboard returned no events for {dateRange}.");
            return batch;
        }

        var candidates = new List<EspnSummaryCandidate>();
        foreach (var ev in events.EnumerateArray())
        {
            if (!TryReadEspnEventSides(ev, out var competition, out var home, out var away))
            {
                continue;
            }

            var status = ResolveEspnStatus(competition);
            var eventId = ReadJsonString(ev, "id") ?? "";
            var kickoffText = ReadJsonString(ev, "date") ?? ReadJsonString(competition, "date") ?? "";
            var kickoff = DateTimeOffset.TryParse(kickoffText, out var parsedKickoff)
                ? parsedKickoff.UtcDateTime
                : DateTime.UtcNow;
            var withinLineupWindow = kickoff <= DateTime.UtcNow.AddHours(8) && kickoff >= DateTime.UtcNow.AddHours(-36);
            if (status == "scheduled" && !withinLineupWindow)
            {
                continue;
            }

            var matchId = ResolveEspnWorldCupMatchId(
                fixtureMap,
                ReadEspnTeamAbbreviation(home),
                ReadEspnTeamAbbreviation(away),
                ev,
                competition);
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(matchId))
            {
                continue;
            }
            candidates.Add(new EspnSummaryCandidate(eventId, matchId, status));
        }

        foreach (var candidate in candidates.Take(8))
        {
            var summaryUrl = $"https://site.api.espn.com/apis/site/v2/sports/soccer/fifa.world/summary?event={Uri.EscapeDataString(candidate.EventId)}";
            try
            {
                var summaryJson = await GetStringWithRetryAsync(summaryUrl, 25, attempts: 2, cancellationToken: cancellationToken);
                using var summaryDoc = JsonDocument.Parse(summaryJson);
                var root = summaryDoc.RootElement;
                var lineups = BuildEspnLineups(root);
                var eventsSummary = BuildEspnSummaryEvents(root);
                var form = BuildEspnSummaryForm(root);
                var summaryPayload = new JsonObject
                {
                    ["provider"] = "espn_summary",
                    ["source_url"] = summaryUrl,
                    ["event_id"] = candidate.EventId,
                    ["status"] = candidate.Status,
                    ["lineup_team_count"] = lineups.Count,
                    ["event_count"] = eventsSummary.Count,
                    ["form_team_count"] = form.Count,
                    ["lineups"] = lineups,
                    ["events"] = eventsSummary,
                    ["recent_form"] = form
                };

                batch.Items.Add(new DataSnapshotCreateRequest
                {
                    Source = sourceName,
                    SnapshotType = "match_summary",
                    MatchId = candidate.MatchId,
                    ContentJson = summaryPayload.ToJsonString()
                });

                foreach (var lineup in lineups.OfType<JsonObject>())
                {
                    var code = lineup["team_code"]?.GetValue<string>() ?? "";
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    batch.Items.Add(new DataSnapshotCreateRequest
                    {
                        Source = sourceName,
                        SnapshotType = "lineup_fact",
                        ObjectId = $"team_{code.ToLowerInvariant()}",
                        MatchId = candidate.MatchId,
                        ContentJson = new JsonObject
                        {
                            ["provider"] = "espn_summary",
                            ["source_url"] = summaryUrl,
                            ["event_id"] = candidate.EventId,
                            ["status"] = candidate.Status,
                            ["lineup"] = lineup.DeepClone()
                        }.ToJsonString()
                    });
                }
            }
            catch (Exception ex)
            {
                batch.Notes.Add($"ESPN summary failed for event {candidate.EventId}: {ex.Message}");
            }
        }

        batch.AllowEmptySuccess = true;
        batch.Notes.Add($"ESPN summary loaded {batch.Items.Count} snapshots from {candidates.Count} candidate events for {dateRange}.");
        return batch;
    }

    private static bool TryReadEspnEventSides(JsonElement ev, out JsonElement competition, out JsonElement home, out JsonElement away)
    {
        competition = default;
        home = default;
        away = default;
        if (!ev.TryGetProperty("competitions", out var competitions) || competitions.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        competition = competitions.EnumerateArray().FirstOrDefault();
        if (competition.ValueKind != JsonValueKind.Object
            || !competition.TryGetProperty("competitors", out var competitors)
            || competitors.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var foundHome = false;
        var foundAway = false;
        foreach (var competitor in competitors.EnumerateArray())
        {
            var side = ReadJsonString(competitor, "homeAway") ?? "";
            if (side.Equals("home", StringComparison.OrdinalIgnoreCase))
            {
                home = competitor;
                foundHome = true;
            }
            if (side.Equals("away", StringComparison.OrdinalIgnoreCase))
            {
                away = competitor;
                foundAway = true;
            }
        }

        return foundHome && foundAway;
    }

    private static (DateTime From, DateTime To) ResolveEspnScoreboardRange(DataSourceImportRequest request)
    {
        var today = DateTime.UtcNow.Date;
        var from = today.AddDays(-1);
        var to = today.AddDays(7);
        if (!string.IsNullOrWhiteSpace(request.DateFrom) && DateTime.TryParse(request.DateFrom, out var parsedFrom))
        {
            from = parsedFrom.Date;
        }
        if (!string.IsNullOrWhiteSpace(request.DateTo) && DateTime.TryParse(request.DateTo, out var parsedTo))
        {
            to = parsedTo.Date;
        }
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var parts = request.Query.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var before) && int.TryParse(parts[1], out var after))
            {
                from = today.AddDays(-Math.Clamp(before, 0, 30));
                to = today.AddDays(Math.Clamp(after, 0, 60));
            }
        }
        if (to < from) (from, to) = (to, from);
        return (from, to);
    }

    private static string ResolveEspnWorldCupMatchId(
        IReadOnlyList<EspnFixtureMap> fixtureMap,
        string homeCode,
        string awayCode,
        JsonElement ev,
        JsonElement competition)
    {
        var kickoff = ReadJsonString(ev, "date") ?? ReadJsonString(competition, "date") ?? "";
        if (DateTimeOffset.TryParse(kickoff, out var parsedKickoff))
        {
            var fixture = fixtureMap.FirstOrDefault(item =>
                item.Home.Equals(homeCode, StringComparison.OrdinalIgnoreCase)
                && item.Away.Equals(awayCode, StringComparison.OrdinalIgnoreCase)
                && Math.Abs((item.KickoffUtc - parsedKickoff.UtcDateTime).TotalHours) <= 18);
            if (fixture != null)
            {
                return $"match_wc26_{fixture.MatchNumber}";
            }
        }

        return "";
    }

    private static async Task<List<EspnFixtureMap>> LoadEspnFixtureMapAsync(CancellationToken cancellationToken)
    {
        var map = new List<EspnFixtureMap>(EspnKnownFixtures);
        try
        {
            var json = await GetStringWithRetryAsync(
                "https://fixturedownload.com/feed/json/fifa-world-cup-2026",
                12,
                attempts: 1,
                cancellationToken: cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return map;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var number = TryParseInt(ReadJsonString(item, "MatchNumber"));
                var home = TeamNameToFifaCode(ReadJsonString(item, "HomeTeam") ?? "");
                var away = TeamNameToFifaCode(ReadJsonString(item, "AwayTeam") ?? "");
                var kickoff = ReadJsonString(item, "DateUtc") ?? "";
                if (number == null
                    || string.IsNullOrWhiteSpace(home)
                    || string.IsNullOrWhiteSpace(away)
                    || !DateTimeOffset.TryParse(kickoff, out var parsedKickoff))
                {
                    continue;
                }

                if (map.Any(existing => existing.MatchNumber == number.Value))
                {
                    continue;
                }
                map.Add(new EspnFixtureMap(number.Value, home, away, parsedKickoff.UtcDateTime));
            }
        }
        catch
        {
            // The static seed keeps the first match days available if the fixture feed is temporarily unavailable.
        }

        return map;
    }

    private static string ResolveEspnStatus(JsonElement competition)
    {
        if (competition.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.Object
            && status.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.Object)
        {
            var state = ReadJsonString(type, "state") ?? "";
            var name = ReadJsonString(type, "name") ?? "";
            var completed = ReadJsonString(type, "completed") ?? "";
            if (completed.Equals("true", StringComparison.OrdinalIgnoreCase) || state.Equals("post", StringComparison.OrdinalIgnoreCase))
            {
                return "finished";
            }
            if (state.Equals("in", StringComparison.OrdinalIgnoreCase)
                || name.Contains("IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
            {
                return "running";
            }
        }

        return "scheduled";
    }

    private static string ReadEspnStatusDetail(JsonElement competition)
    {
        if (!competition.TryGetProperty("status", out var status)
            || status.ValueKind != JsonValueKind.Object
            || !status.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.Object)
        {
            return "";
        }
        return ReadJsonString(type, "shortDetail")
            ?? ReadJsonString(type, "detail")
            ?? ReadJsonString(type, "description")
            ?? "";
    }

    private static string ReadEspnTeamAbbreviation(JsonElement competitor)
    {
        if (competitor.TryGetProperty("team", out var team) && team.ValueKind == JsonValueKind.Object)
        {
            var abbreviation = ReadJsonString(team, "abbreviation");
            if (!string.IsNullOrWhiteSpace(abbreviation)) return NormalizeEspnTeamCode(abbreviation);
            var displayName = ReadJsonString(team, "displayName") ?? ReadJsonString(team, "name") ?? "";
            var code = TeamNameToFifaCode(displayName);
            if (!string.IsNullOrWhiteSpace(code)) return code;
        }
        return "";
    }

    private static string ReadEspnTeamName(JsonElement competitor)
    {
        if (competitor.TryGetProperty("team", out var team) && team.ValueKind == JsonValueKind.Object)
        {
            return ReadJsonString(team, "displayName")
                ?? ReadJsonString(team, "name")
                ?? ReadJsonString(team, "location")
                ?? "";
        }
        return "";
    }

    private static string ReadEspnVenue(JsonElement competition, JsonElement ev)
    {
        if (competition.TryGetProperty("venue", out var venue) && venue.ValueKind == JsonValueKind.Object)
        {
            var full = ReadJsonString(venue, "fullName") ?? ReadJsonString(venue, "displayName");
            if (!string.IsNullOrWhiteSpace(full)) return full;
        }
        if (ev.TryGetProperty("venue", out var eventVenue) && eventVenue.ValueKind == JsonValueKind.Object)
        {
            return ReadJsonString(eventVenue, "displayName") ?? "";
        }
        return "";
    }

    private static JsonArray BuildEspnEventDetails(JsonElement details)
    {
        var result = new JsonArray();
        foreach (var detail in details.EnumerateArray().Take(40))
        {
            result.Add((JsonNode)new JsonObject
            {
                ["clock"] = detail.TryGetProperty("clock", out var clock) && clock.ValueKind == JsonValueKind.Object
                    ? ReadJsonString(clock, "displayValue")
                    : null,
                ["type"] = detail.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Object
                    ? ReadJsonString(type, "text") ?? ReadJsonString(type, "id")
                    : null,
                ["team_id"] = detail.TryGetProperty("team", out var team) && team.ValueKind == JsonValueKind.Object
                    ? ReadJsonString(team, "id")
                    : null,
                ["score_value"] = TryParseInt(ReadJsonString(detail, "scoreValue")),
                ["yellow_card"] = ReadJsonString(detail, "yellowCard"),
                ["red_card"] = ReadJsonString(detail, "redCard"),
                ["scoring_play"] = ReadJsonString(detail, "scoringPlay"),
                ["summary"] = ReadJsonString(detail, "text") ?? ReadJsonString(detail, "shortText")
            });
        }
        return result;
    }

    private static JsonArray BuildEspnTeamStats(JsonElement home, JsonElement away)
    {
        var result = new JsonArray();
        AddEspnStatsForSide(result, home, "home");
        AddEspnStatsForSide(result, away, "away");
        return result;
    }

    private static void AddEspnMarketSignalSnapshot(
        DataSnapshotBatchImportRequest batch,
        string sourceName,
        JsonElement competition,
        string eventId,
        string matchId,
        string homeCode,
        string awayCode,
        string sourceUrl,
        string status)
    {
        if (!competition.TryGetProperty("odds", out var oddsItems) || oddsItems.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var firstOdds = oddsItems.EnumerateArray()
            .FirstOrDefault(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("moneyline", out _));
        if (firstOdds.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var provider = firstOdds.TryGetProperty("provider", out var providerElement) && providerElement.ValueKind == JsonValueKind.Object
            ? ReadJsonString(providerElement, "name") ?? "ESPN market feed"
            : "ESPN market feed";
        var moneyline = firstOdds.TryGetProperty("moneyline", out var moneylineElement) && moneylineElement.ValueKind == JsonValueKind.Object
            ? moneylineElement
            : default;
        if (moneyline.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var homeOdds = ReadEspnCloseOdds(moneyline, "home");
        var drawOdds = ReadEspnCloseOdds(moneyline, "draw");
        var awayOdds = ReadEspnCloseOdds(moneyline, "away");
        var homeProb = AmericanOddsToProbability(homeOdds);
        var drawProb = AmericanOddsToProbability(drawOdds);
        var awayProb = AmericanOddsToProbability(awayOdds);
        var total = homeProb + drawProb + awayProb;
        var normalizedHome = total > 0 ? homeProb / total : 0;
        var normalizedDraw = total > 0 ? drawProb / total : 0;
        var normalizedAway = total > 0 ? awayProb / total : 0;
        if (total <= 0)
        {
            return;
        }

        batch.Items.Add(new DataSnapshotCreateRequest
        {
            Source = sourceName,
            SnapshotType = "market_signal",
            MatchId = string.IsNullOrWhiteSpace(matchId) ? null : matchId,
            ContentJson = new JsonObject
            {
                ["provider"] = "espn_scoreboard",
                ["source_url"] = sourceUrl,
                ["event_id"] = eventId,
                ["status"] = status,
                ["market_provider"] = provider,
                ["home_code"] = homeCode,
                ["away_code"] = awayCode,
                ["home_moneyline"] = homeOdds,
                ["draw_moneyline"] = drawOdds,
                ["away_moneyline"] = awayOdds,
                ["home_implied_probability"] = Math.Round(normalizedHome, 4),
                ["draw_implied_probability"] = Math.Round(normalizedDraw, 4),
                ["away_implied_probability"] = Math.Round(normalizedAway, 4),
                ["overround"] = Math.Round(total - 1, 4),
                ["moneyline"] = JsonNode.Parse(moneyline.GetRawText()),
                ["spread"] = firstOdds.TryGetProperty("pointSpread", out var spread) ? JsonNode.Parse(spread.GetRawText()) : null,
                ["total"] = firstOdds.TryGetProperty("total", out var totalMarket) ? JsonNode.Parse(totalMarket.GetRawText()) : null
            }.ToJsonString()
        });
    }

    private static int? ReadEspnCloseOdds(JsonElement moneyline, string side)
    {
        if (!moneyline.TryGetProperty(side, out var sideElement) || sideElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (sideElement.TryGetProperty("close", out var close) && close.ValueKind == JsonValueKind.Object)
        {
            return TryParseInt(ReadJsonString(close, "odds"));
        }
        if (sideElement.TryGetProperty("open", out var open) && open.ValueKind == JsonValueKind.Object)
        {
            return TryParseInt(ReadJsonString(open, "odds"));
        }
        return TryParseInt(ReadJsonString(sideElement, "odds"));
    }

    private static double AmericanOddsToProbability(int? odds)
    {
        if (odds == null || odds == 0) return 0;
        return odds > 0
            ? 100.0 / (odds.Value + 100.0)
            : Math.Abs(odds.Value) / (Math.Abs(odds.Value) + 100.0);
    }

    private static JsonArray BuildEspnLineups(JsonElement root)
    {
        var result = new JsonArray();
        if (!root.TryGetProperty("boxscore", out var boxscore)
            || boxscore.ValueKind != JsonValueKind.Object
            || !boxscore.TryGetProperty("teams", out var teams)
            || teams.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var teamBox in teams.EnumerateArray())
        {
            var team = teamBox.TryGetProperty("team", out var teamElement) && teamElement.ValueKind == JsonValueKind.Object
                ? teamElement
                : default;
            var code = NormalizeEspnTeamCode(ReadJsonString(team, "abbreviation") ?? "");
            var starters = new JsonArray();
            var substitutes = new JsonArray();
            if (teamBox.TryGetProperty("statistics", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                foreach (var group in groups.EnumerateArray())
                {
                    if (!group.TryGetProperty("athletes", out var athletes) || athletes.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var athlete in athletes.EnumerateArray())
                    {
                        var player = BuildEspnPlayer(athlete);
                        if (ReadJsonString(athlete, "starter").Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            starters.Add(player);
                        }
                        else
                        {
                            substitutes.Add(player);
                        }
                    }
                }
            }

            result.Add((JsonNode)new JsonObject
            {
                ["team_code"] = code,
                ["team_name"] = ReadJsonString(team, "displayName") ?? ReadJsonString(team, "name") ?? "",
                ["logo"] = ReadJsonString(team, "logo"),
                ["starter_count"] = starters.Count,
                ["substitute_count"] = substitutes.Count,
                ["starters"] = starters,
                ["substitutes"] = substitutes
            });
        }
        return result;
    }

    private static JsonObject BuildEspnPlayer(JsonElement athleteRow)
    {
        var athlete = athleteRow.TryGetProperty("athlete", out var athleteElement) && athleteElement.ValueKind == JsonValueKind.Object
            ? athleteElement
            : default;
        return new JsonObject
        {
            ["id"] = ReadJsonString(athlete, "id"),
            ["name"] = ReadJsonString(athlete, "displayName") ?? ReadJsonString(athlete, "fullName") ?? "",
            ["short_name"] = ReadJsonString(athlete, "shortName"),
            ["jersey"] = ReadJsonString(athleteRow, "jersey"),
            ["position"] = athleteRow.TryGetProperty("position", out var position) && position.ValueKind == JsonValueKind.Object
                ? ReadJsonString(position, "abbreviation") ?? ReadJsonString(position, "displayName")
                : null,
            ["starter"] = ReadJsonString(athleteRow, "starter"),
            ["subbed_in"] = ReadJsonString(athleteRow, "subbedIn"),
            ["subbed_out"] = ReadJsonString(athleteRow, "subbedOut"),
            ["headshot"] = athlete.TryGetProperty("headshot", out var headshot) && headshot.ValueKind == JsonValueKind.Object
                ? ReadJsonString(headshot, "href")
                : null
        };
    }

    private static JsonArray BuildEspnSummaryEvents(JsonElement root)
    {
        var result = new JsonArray();
        if (!root.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var detail in details.EnumerateArray().Take(60))
        {
            var athletes = new JsonArray();
            if (detail.TryGetProperty("athletesInvolved", out var involved) && involved.ValueKind == JsonValueKind.Array)
            {
                foreach (var athlete in involved.EnumerateArray().Take(4))
                {
                    athletes.Add((JsonNode)new JsonObject
                    {
                        ["id"] = ReadJsonString(athlete, "id"),
                        ["name"] = ReadJsonString(athlete, "displayName") ?? ReadJsonString(athlete, "fullName"),
                        ["position"] = ReadJsonString(athlete, "position"),
                        ["team_id"] = athlete.TryGetProperty("team", out var athleteTeam) && athleteTeam.ValueKind == JsonValueKind.Object
                            ? ReadJsonString(athleteTeam, "id")
                            : null
                    });
                }
            }

            result.Add((JsonNode)new JsonObject
            {
                ["clock"] = detail.TryGetProperty("clock", out var clock) && clock.ValueKind == JsonValueKind.Object
                    ? ReadJsonString(clock, "displayValue")
                    : null,
                ["type"] = detail.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Object
                    ? ReadJsonString(type, "text") ?? ReadJsonString(type, "id")
                    : null,
                ["team_id"] = detail.TryGetProperty("team", out var team) && team.ValueKind == JsonValueKind.Object
                    ? ReadJsonString(team, "id")
                    : null,
                ["score_value"] = TryParseInt(ReadJsonString(detail, "scoreValue")),
                ["scoring_play"] = ReadJsonString(detail, "scoringPlay"),
                ["yellow_card"] = ReadJsonString(detail, "yellowCard"),
                ["red_card"] = ReadJsonString(detail, "redCard"),
                ["athletes"] = athletes
            });
        }
        return result;
    }

    private static JsonArray BuildEspnSummaryForm(JsonElement root)
    {
        var result = new JsonArray();
        if (!root.TryGetProperty("boxscore", out var boxscore)
            || boxscore.ValueKind != JsonValueKind.Object
            || !boxscore.TryGetProperty("form", out var form)
            || form.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in form.EnumerateArray())
        {
            var team = item.TryGetProperty("team", out var teamElement) && teamElement.ValueKind == JsonValueKind.Object
                ? teamElement
                : default;
            var events = new JsonArray();
            if (item.TryGetProperty("events", out var formEvents) && formEvents.ValueKind == JsonValueKind.Array)
            {
                foreach (var formEvent in formEvents.EnumerateArray().Take(8))
                {
                    events.Add((JsonNode)new JsonObject
                    {
                        ["date"] = ReadJsonString(formEvent, "gameDate"),
                        ["score"] = ReadJsonString(formEvent, "score"),
                        ["result"] = ReadJsonString(formEvent, "gameResult"),
                        ["competition"] = ReadJsonString(formEvent, "competitionName"),
                        ["opponent"] = formEvent.TryGetProperty("opponent", out var opponent) && opponent.ValueKind == JsonValueKind.Object
                            ? ReadJsonString(opponent, "displayName")
                            : null
                    });
                }
            }

            result.Add((JsonNode)new JsonObject
            {
                ["team_code"] = NormalizeEspnTeamCode(ReadJsonString(team, "abbreviation") ?? ""),
                ["team_name"] = ReadJsonString(team, "displayName"),
                ["events"] = events
            });
        }
        return result;
    }

    private static void AddEspnStatsForSide(JsonArray result, JsonElement competitor, string side)
    {
        if (!competitor.TryGetProperty("statistics", out var stats) || stats.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var item = new JsonObject
        {
            ["side"] = side,
            ["team_code"] = ReadEspnTeamAbbreviation(competitor),
            ["team_name"] = ReadEspnTeamName(competitor)
        };
        foreach (var stat in stats.EnumerateArray())
        {
            var name = ReadJsonString(stat, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (name is "possessionPct" or "shotsOnTarget" or "totalShots" or "wonCorners" or "foulsCommitted" or "goalAssists" or "offsides" or "yellowCards" or "redCards")
            {
                item[name] = ReadJsonString(stat, "displayValue") ?? ReadJsonString(stat, "value");
            }
        }
        result.Add(item);
    }

    private static void AddEspnTeamSnapshot(
        DataSnapshotBatchImportRequest batch,
        string sourceName,
        JsonElement competitor,
        string eventId,
        string matchId,
        string code,
        string side,
        JsonArray matchStats,
        string status)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }
        batch.Items.Add(new DataSnapshotCreateRequest
        {
            Source = sourceName,
            SnapshotType = "team_match_stats",
            ObjectId = $"team_{code.ToLowerInvariant()}",
            MatchId = string.IsNullOrWhiteSpace(matchId) ? null : matchId,
            ContentJson = new JsonObject
            {
                ["provider"] = "espn_scoreboard",
                ["event_id"] = eventId,
                ["status"] = status,
                ["side"] = side,
                ["team_code"] = code,
                ["team_name"] = ReadEspnTeamName(competitor),
                ["score"] = TryParseInt(ReadJsonString(competitor, "score")),
                ["winner"] = ReadJsonString(competitor, "winner"),
                ["form"] = ReadJsonString(competitor, "form"),
                ["records"] = competitor.TryGetProperty("records", out var records) ? JsonNode.Parse(records.GetRawText()) : null,
                ["statistics"] = competitor.TryGetProperty("statistics", out var stats) ? JsonNode.Parse(stats.GetRawText()) : null,
                ["match_statistics_summary"] = matchStats.DeepClone()
            }.ToJsonString()
        });
    }

    private static string NormalizeEspnTeamCode(string code)
    {
        return code.Trim().ToUpperInvariant() switch
        {
            "RSA" => "RSA",
            "KOR" => "KOR",
            "CZE" => "CZE",
            "BIH" => "BIH",
            "CPV" => "CPV",
            "CRC" => "CRC",
            "GER" => "GER",
            "IRN" => "IRN",
            "SUI" => "SUI",
            "USA" => "USA",
            "NED" => "NED",
            "POR" => "POR",
            _ => code.Trim().ToUpperInvariant()
        };
    }

    private sealed record EspnFixtureMap(int MatchNumber, string Home, string Away, DateTime KickoffUtc);

    private sealed record EspnSummaryCandidate(string EventId, string MatchId, string Status);

    private static readonly List<EspnFixtureMap> EspnKnownFixtures =
    [
        new(1, "MEX", "RSA", new DateTime(2026, 6, 11, 19, 0, 0, DateTimeKind.Utc)),
        new(2, "KOR", "CZE", new DateTime(2026, 6, 12, 2, 0, 0, DateTimeKind.Utc)),
        new(3, "CAN", "BIH", new DateTime(2026, 6, 12, 19, 0, 0, DateTimeKind.Utc)),
        new(4, "USA", "PAR", new DateTime(2026, 6, 13, 1, 0, 0, DateTimeKind.Utc)),
        new(5, "HAI", "SCO", new DateTime(2026, 6, 14, 1, 0, 0, DateTimeKind.Utc)),
        new(6, "AUS", "TUR", new DateTime(2026, 6, 14, 4, 0, 0, DateTimeKind.Utc)),
        new(7, "BRA", "MAR", new DateTime(2026, 6, 13, 22, 0, 0, DateTimeKind.Utc)),
        new(8, "QAT", "SUI", new DateTime(2026, 6, 13, 19, 0, 0, DateTimeKind.Utc)),
        new(9, "CIV", "ECU", new DateTime(2026, 6, 14, 23, 0, 0, DateTimeKind.Utc)),
        new(10, "GER", "CUW", new DateTime(2026, 6, 14, 17, 0, 0, DateTimeKind.Utc)),
        new(11, "NED", "JPN", new DateTime(2026, 6, 14, 20, 0, 0, DateTimeKind.Utc)),
        new(12, "SWE", "TUN", new DateTime(2026, 6, 15, 2, 0, 0, DateTimeKind.Utc))
    ];

    private static async Task<RssFeedRun> FetchRssFeedAsync(string feed, string? query, CancellationToken cancellationToken)
    {
        var run = new RssFeedRun { Feed = feed };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var linkedTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            var xml = await GetStringWithRetryAsync(feed, 8, attempts: 1, cancellationToken: linkedTimeout.Token);
            var doc = XDocument.Parse(xml);
            foreach (var item in doc.Descendants("item").Take(20))
            {
                var title = item.Element("title")?.Value ?? "";
                var description = item.Element("description")?.Value ?? "";
                if (!RssQueryMatches(title, description, query))
                {
                    continue;
                }
                run.Articles.Add((JsonNode)new JsonObject
                {
                    ["title"] = title,
                    ["description"] = description,
                    ["url"] = item.Element("link")?.Value,
                    ["published_at"] = item.Element("pubDate")?.Value,
                    ["feed"] = feed
                });
            }
            run.Passed = true;
        }
        catch (Exception ex)
        {
            // RSS is an auxiliary intelligence source; one failed feed must not block the source batch.
            run.Passed = false;
            run.ErrorMessage = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            run.ElapsedMs = stopwatch.ElapsedMilliseconds;
        }

        return run;
    }

    private static bool RssQueryMatches(string title, string description, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        var text = $"{title} {description}";
        var phrases = query
            .Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length >= 3)
            .ToList();
        if (phrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var tokens = Regex
            .Split(query, @"[^\p{L}\p{N}]+")
            .Where(item => IsUsefulRssQueryToken(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (tokens.Count == 0) return true;

        var teamTokens = tokens
            .Where(item => !IsGenericRssIntelToken(item))
            .ToList();
        var intelTokens = tokens
            .Where(IsGenericRssIntelToken)
            .ToList();

        if (teamTokens.Count > 0 && intelTokens.Count > 0)
        {
            return teamTokens.Any(token => RssTextContainsToken(text, token));
        }

        return tokens.Any(token => RssTextContainsToken(text, token));
    }

    private static bool RssTextContainsToken(string text, string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Length <= 3 && trimmed.All(char.IsLetterOrDigit))
        {
            return Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(trimmed.ToUpperInvariant())}(?![\p{{L}}\p{{N}}])", RegexOptions.CultureInvariant);
        }
        return Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(trimmed)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsUsefulRssQueryToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 3) return false;
        return token.Trim().ToLowerInvariant() switch
        {
            "the" => false,
            "and" => false,
            "for" => false,
            "with" => false,
            "from" => false,
            "world" => false,
            "cup" => false,
            "fifa" => false,
            "football" => false,
            "soccer" => false,
            "news" => false,
            _ => true
        };
    }

    private static bool IsGenericRssIntelToken(string token)
    {
        return token.Trim().ToLowerInvariant() switch
        {
            "injury" => true,
            "injuries" => true,
            "injured" => true,
            "lineup" => true,
            "lineups" => true,
            "squad" => true,
            "squads" => true,
            "roster" => true,
            "rosters" => true,
            "starter" => true,
            "starters" => true,
            "starting" => true,
            "preview" => true,
            "scouting" => true,
            _ => false
        };
    }

    private static List<string> BuildInternationalResultsUrls(string configuredUrl)
    {
        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            urls.Add(configuredUrl);
        }
        urls.Add("https://raw.githubusercontent.com/martj42/international_results/master/results.csv");
        urls.Add("https://cdn.jsdelivr.net/gh/martj42/international_results@master/results.csv");
        return urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<(string Text, string Url, List<string> Notes)> FetchFirstAvailableTextAsync(
        IReadOnlyList<string> urls,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        foreach (var url in urls)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var linkedTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linkedTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                var text = await GetStringWithRetryAsync(url, timeoutSeconds, attempts: 1, cancellationToken: linkedTimeout.Token);
                stopwatch.Stop();
                notes.Add($"source_fetch_ok: {url} in {stopwatch.ElapsedMilliseconds}ms.");
                return (text, url, notes);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                notes.Add($"source_fetch_failed: {url} in {stopwatch.ElapsedMilliseconds}ms: {ex.Message}");
            }
        }

        throw new InvalidOperationException($"all_source_fetches_failed: {string.Join(" | ", notes)}");
    }

    private static FifaRankingMeta ExtractFifaRankingMeta(string html)
    {
        var marker = "<script id=\"__NEXT_DATA__\" type=\"application/json\">";
        var start = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return new FifaRankingMeta();
        }

        start += marker.Length;
        var end = html.IndexOf("</script>", start, StringComparison.OrdinalIgnoreCase);
        if (end <= start)
        {
            return new FifaRankingMeta();
        }

        var json = html[start..end];
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("props", out var props)
            || !props.TryGetProperty("pageProps", out var pageProps)
            || !pageProps.TryGetProperty("pageData", out var pageData)
            || !pageData.TryGetProperty("ranking", out var ranking))
        {
            return new FifaRankingMeta();
        }

        var meta = new FifaRankingMeta
        {
            LastUpdateDate = ReadJsonString(ranking, "lastUpdateDate"),
            NextUpdateDate = ReadJsonString(ranking, "nextUpdateDate")
        };

        if (ranking.TryGetProperty("allAvailableDates", out var dates) && dates.ValueKind == JsonValueKind.Array)
        {
            foreach (var date in dates.EnumerateArray())
            {
                var id = ReadJsonString(date, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    meta.ScheduleId = id;
                    meta.ScheduleDate = ReadJsonString(date, "date") ?? ReadJsonString(date, "matchWindowEndDate");
                    break;
                }
            }
        }

        return meta;
    }

    private static Dictionary<string, RecentFormAggregate> BuildRecentFormSeed()
    {
        return FifaCodeToCanonicalName.Keys.ToDictionary(
            code => code,
            code => new RecentFormAggregate { FifaCode = code },
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AddRecentResult(
        Dictionary<string, RecentFormAggregate> aggregates,
        string fifaCode,
        int goalsFor,
        int goalsAgainst,
        DateTime date,
        string tournament,
        bool home)
    {
        if (!aggregates.TryGetValue(fifaCode, out var aggregate))
        {
            aggregate = new RecentFormAggregate { FifaCode = fifaCode };
            aggregates[fifaCode] = aggregate;
        }

        aggregate.Matches++;
        aggregate.GoalsFor += goalsFor;
        aggregate.GoalsAgainst += goalsAgainst;
        if (goalsFor > goalsAgainst)
        {
            aggregate.Wins++;
            aggregate.Points += 3;
        }
        else if (goalsFor == goalsAgainst)
        {
            aggregate.Draws++;
            aggregate.Points += 1;
        }
        else
        {
            aggregate.Losses++;
        }

        if (aggregate.LatestDate == null || date > aggregate.LatestDate)
        {
            aggregate.LatestDate = date;
        }
        if (!string.IsNullOrWhiteSpace(tournament))
        {
            aggregate.Tournaments.Add(tournament);
        }
    }

    private static void AddOpponentAdjustedRecentForm(
        IReadOnlyDictionary<string, RecentFormAggregate> aggregates,
        IReadOnlyList<RecentMatchCandidate> candidates)
    {
        foreach (var item in candidates)
        {
            if (!aggregates.TryGetValue(item.TeamCode, out var team)
                || !aggregates.TryGetValue(item.OpponentCode, out var opponent)
                || opponent.Matches == 0)
            {
                continue;
            }

            var points = item.GoalsFor > item.GoalsAgainst ? 3 : item.GoalsFor == item.GoalsAgainst ? 1 : 0;
            var goalDiff = item.GoalsFor - item.GoalsAgainst;
            var opponentPointsPerMatch = opponent.Points / (double)Math.Max(1, opponent.Matches);
            var opponentGoalDiff = (opponent.GoalsFor - opponent.GoalsAgainst) / (double)Math.Max(1, opponent.Matches);
            var opponentStrength = Math.Clamp((opponentPointsPerMatch - 1.25) / 2.0 + opponentGoalDiff / 6.0, -0.35, 0.35);
            var resultScore = (points - 1.0) / 2.0 + Math.Clamp(goalDiff / 4.0, -0.35, 0.35);
            var homeAwayAdjustment = item.Home ? 0.0 : 0.035;
            var value = resultScore + opponentStrength * 0.38 + homeAwayAdjustment;
            var weight = CompetitionWeight(item.Tournament) * RecencyWeight(item.Date);

            team.AdjustedScore += value * weight;
            team.AdjustedWeight += weight;
        }
    }

    private static double CompetitionWeight(string tournament)
    {
        var normalized = tournament.Trim().ToLowerInvariant();
        if (normalized.Contains("world cup", StringComparison.OrdinalIgnoreCase)) return 1.25;
        if (normalized.Contains("euro", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("copa", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("africa cup", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("asian cup", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("gold cup", StringComparison.OrdinalIgnoreCase)) return 1.12;
        if (normalized.Contains("qualification", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("qualifier", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("nations league", StringComparison.OrdinalIgnoreCase)) return 1.02;
        if (normalized.Contains("friendly", StringComparison.OrdinalIgnoreCase)) return 0.72;
        return 0.90;
    }

    private static double RecencyWeight(DateTime date)
    {
        var days = Math.Max(0, (DateTime.UtcNow.Date - date.Date).TotalDays);
        return Math.Clamp(1.18 - days / 1500.0, 0.55, 1.18);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }
            if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        fields.Add(current.ToString());
        return fields;
    }

    private static string? TeamNameToFifaCode(string name)
    {
        var key = NormalizeTeamName(name);
        return TeamNameToFifaCodeMap.TryGetValue(key, out var code) ? code : null;
    }

    private static string NormalizeTeamName(string name)
    {
        return name.Trim()
            .ToLowerInvariant()
            .Replace("’", "'")
            .Replace("`", "'")
            .Replace("é", "e")
            .Replace("è", "e")
            .Replace("ê", "e")
            .Replace("ë", "e")
            .Replace("ô", "o")
            .Replace("ö", "o")
            .Replace("ã", "a")
            .Replace("á", "a")
            .Replace("à", "a")
            .Replace("ç", "c");
    }

    private static string? EloCodeToFifaCode(string eloCode)
    {
        return EloCodeToFifaCodeMap.TryGetValue(eloCode.Trim().ToUpperInvariant(), out var code) ? code : null;
    }

    private static readonly Dictionary<string, string> EloCodeToFifaCodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DZ"] = "ALG", ["AO"] = "ANG", ["AR"] = "ARG", ["AU"] = "AUS", ["AT"] = "AUT",
        ["BE"] = "BEL", ["BA"] = "BIH", ["BR"] = "BRA", ["CA"] = "CAN", ["CN"] = "CHN",
        ["CI"] = "CIV", ["CM"] = "CMR", ["CD"] = "COD", ["CO"] = "COL", ["CV"] = "CPV",
        ["CR"] = "CRC", ["HR"] = "CRO", ["CW"] = "CUW", ["CZ"] = "CZE", ["DK"] = "DEN",
        ["EC"] = "ECU", ["EG"] = "EGY", ["EN"] = "ENG", ["ES"] = "ESP", ["FR"] = "FRA",
        ["DE"] = "GER", ["GH"] = "GHA", ["HT"] = "HAI", ["HN"] = "HON", ["IR"] = "IRN",
        ["IQ"] = "IRQ", ["IS"] = "ISL", ["IT"] = "ITA", ["JO"] = "JOR", ["JP"] = "JPN",
        ["KR"] = "KOR", ["SA"] = "KSA", ["MA"] = "MAR", ["MX"] = "MEX", ["NL"] = "NED",
        ["NG"] = "NGR", ["NO"] = "NOR", ["NZ"] = "NZL", ["PA"] = "PAN", ["PY"] = "PAR",
        ["PT"] = "POR", ["QA"] = "QAT", ["ZA"] = "RSA", ["SQ"] = "SCO", ["SN"] = "SEN",
        ["SI"] = "SLO", ["RS"] = "SRB", ["CH"] = "SUI", ["SE"] = "SWE", ["SC"] = "SYC", ["TO"] = "TGA",
        ["TN"] = "TUN", ["TR"] = "TUR", ["UA"] = "UKR", ["UY"] = "URU", ["US"] = "USA",
        ["UZ"] = "UZB", ["WA"] = "WAL"
    };

    private static readonly Dictionary<string, string> FifaCodeToCanonicalName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALG"] = "Algeria", ["ANG"] = "Angola", ["ARG"] = "Argentina", ["AUS"] = "Australia",
        ["AUT"] = "Austria", ["BEL"] = "Belgium", ["BIH"] = "Bosnia and Herzegovina",
        ["BRA"] = "Brazil", ["CAN"] = "Canada", ["CHN"] = "China", ["CIV"] = "Cote d'Ivoire",
        ["CMR"] = "Cameroon", ["COD"] = "Congo DR", ["COL"] = "Colombia", ["CPV"] = "Cape Verde",
        ["CRC"] = "Costa Rica", ["CRO"] = "Croatia", ["CUW"] = "Curacao", ["CZE"] = "Czech Republic",
        ["DEN"] = "Denmark", ["ECU"] = "Ecuador", ["EGY"] = "Egypt", ["ENG"] = "England",
        ["ESP"] = "Spain", ["FRA"] = "France", ["GER"] = "Germany", ["GHA"] = "Ghana",
        ["HAI"] = "Haiti", ["HON"] = "Honduras", ["IRN"] = "Iran", ["IRQ"] = "Iraq",
        ["ISL"] = "Iceland", ["ITA"] = "Italy", ["JOR"] = "Jordan", ["JPN"] = "Japan",
        ["KOR"] = "South Korea", ["KSA"] = "Saudi Arabia", ["MAR"] = "Morocco", ["MEX"] = "Mexico",
        ["NED"] = "Netherlands", ["NGR"] = "Nigeria", ["NOR"] = "Norway", ["NZL"] = "New Zealand",
        ["PAN"] = "Panama", ["PAR"] = "Paraguay", ["POR"] = "Portugal", ["QAT"] = "Qatar",
        ["RSA"] = "South Africa", ["SCO"] = "Scotland", ["SEN"] = "Senegal", ["SLO"] = "Slovenia",
        ["SRB"] = "Serbia", ["SUI"] = "Switzerland", ["SWE"] = "Sweden", ["TGA"] = "Tonga",
        ["TUN"] = "Tunisia", ["TUR"] = "Turkey", ["UKR"] = "Ukraine", ["URU"] = "Uruguay",
        ["USA"] = "United States", ["UZB"] = "Uzbekistan", ["WAL"] = "Wales"
    };

    private static readonly Dictionary<string, string> TeamNameToFifaCodeMap = BuildTeamNameToFifaCodeMap();

    private static Dictionary<string, string> BuildTeamNameToFifaCodeMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in FifaCodeToCanonicalName)
        {
            map[NormalizeTeamName(pair.Value)] = pair.Key;
        }
        map[NormalizeTeamName("Côte d'Ivoire")] = "CIV";
        map[NormalizeTeamName("Ivory Coast")] = "CIV";
        map[NormalizeTeamName("DR Congo")] = "COD";
        map[NormalizeTeamName("Democratic Republic of Congo")] = "COD";
        map[NormalizeTeamName("Congo DR")] = "COD";
        map[NormalizeTeamName("Curaçao")] = "CUW";
        map[NormalizeTeamName("Curacao")] = "CUW";
        map[NormalizeTeamName("Czechia")] = "CZE";
        map[NormalizeTeamName("Korea Republic")] = "KOR";
        map[NormalizeTeamName("South Korea")] = "KOR";
        map[NormalizeTeamName("USA")] = "USA";
        map[NormalizeTeamName("United States")] = "USA";
        return map;
    }

    private sealed class RecentFormAggregate
    {
        public string FifaCode { get; set; } = "";
        public int Matches { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public int GoalsFor { get; set; }
        public int GoalsAgainst { get; set; }
        public int Points { get; set; }
        public DateTime? LatestDate { get; set; }
        public HashSet<string> Tournaments { get; } = new(StringComparer.OrdinalIgnoreCase);
        public double AdjustedScore { get; set; }
        public double AdjustedWeight { get; set; }
    }

    private sealed record RecentMatchCandidate(
        string TeamCode,
        string OpponentCode,
        int GoalsFor,
        int GoalsAgainst,
        DateTime Date,
        string Tournament,
        bool Home);

    private static string? ReadLocalizedDescription(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? fallback = null;
        foreach (var value in values.EnumerateArray())
        {
            var description = ReadJsonString(value, "Description");
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            fallback ??= description;
            var locale = ReadJsonString(value, "Locale");
            if (string.Equals(locale, "en-GB", StringComparison.OrdinalIgnoreCase)
                || string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase))
            {
                return description;
            }
        }

        return fallback;
    }

    private static int? TryParseInt(string? value)
    {
        return int.TryParse(value, out var result) ? result : null;
    }

    private static decimal? TryParseDecimal(string? value)
    {
        return decimal.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static string? ResolveWorldCup26ObjectId(string endpoint, JsonElement item)
    {
        if (endpoint == "teams")
        {
            var code = ReadJsonString(item, "fifa_code");
            return string.IsNullOrWhiteSpace(code) ? null : $"team_{code.ToLowerInvariant()}";
        }
        if (endpoint == "stadiums")
        {
            var id = ReadJsonString(item, "id");
            return string.IsNullOrWhiteSpace(id) ? null : $"stadium_{id}";
        }
        if (endpoint == "groups")
        {
            var name = ReadJsonString(item, "name");
            return string.IsNullOrWhiteSpace(name) ? null : $"group_{name.ToLowerInvariant()}";
        }
        return null;
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadGNewsAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("GNEWS_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("needs_api_key: set GNEWS_API_KEY to enable GNews collection.");
        }

        var query = string.IsNullOrWhiteSpace(request.Query) ? "FIFA World Cup football injury lineup" : request.Query.Trim();
        var url = $"https://gnews.io/api/v4/search?q={Uri.EscapeDataString(query)}&lang=en&max=10&apikey={Uri.EscapeDataString(apiKey)}";
        var json = await GetStringWithRetryAsync(url, 30, cancellationToken: cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };
        if (!doc.RootElement.TryGetProperty("articles", out var articles) || articles.ValueKind != JsonValueKind.Array)
        {
            return batch;
        }

        var summaries = new JsonArray();
        foreach (var article in articles.EnumerateArray().Take(8))
        {
            summaries.Add((JsonNode)new JsonObject
            {
                ["title"] = ReadJsonString(article, "title"),
                ["description"] = ReadJsonString(article, "description"),
                ["url"] = ReadJsonString(article, "url"),
                ["published_at"] = ReadJsonString(article, "publishedAt"),
                ["source"] = article.TryGetProperty("source", out var source) ? ReadJsonString(source, "name") : null
            });
        }

        batch.Items.Add(new DataSnapshotCreateRequest
        {
            Source = sourceName,
            SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? "news_intel" : request.SnapshotType!,
            ObjectId = request.ObjectId,
            MatchId = request.MatchId,
            ContentJson = new JsonObject
            {
                ["provider"] = "gnews",
                ["query"] = query,
                ["articles"] = summaries
            }.ToJsonString()
        });
        return batch;
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadTheOddsApiAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("THE_ODDS_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("needs_api_key: set THE_ODDS_API_KEY to enable odds collection.");
        }

        var sportKey = string.IsNullOrWhiteSpace(request.SportKey) ? "soccer_fifa_world_cup" : request.SportKey.Trim();
        var url = $"https://api.the-odds-api.com/v4/sports/{Uri.EscapeDataString(sportKey)}/odds?apiKey={Uri.EscapeDataString(apiKey)}&regions=us,uk,eu&markets=h2h&oddsFormat=decimal";
        var json = await GetStringWithRetryAsync(url, 30, cancellationToken: cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return batch;
        }

        foreach (var game in doc.RootElement.EnumerateArray().Take(12))
        {
            batch.Items.Add(new DataSnapshotCreateRequest
            {
                Source = sourceName,
                SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? "market_signal" : request.SnapshotType!,
                ObjectId = request.ObjectId,
                MatchId = request.MatchId,
                ContentJson = new JsonObject
                {
                    ["provider"] = "the_odds_api",
                    ["sport_key"] = sportKey,
                    ["id"] = ReadJsonString(game, "id"),
                    ["commence_time"] = ReadJsonString(game, "commence_time"),
                    ["home_team"] = ReadJsonString(game, "home_team"),
                    ["away_team"] = ReadJsonString(game, "away_team"),
                    ["bookmakers"] = game.TryGetProperty("bookmakers", out var bookmakers)
                        ? JsonNode.Parse(bookmakers.GetRawText())
                        : null
                }.ToJsonString()
            });
        }
        return batch;
    }

    private static async Task<DataSnapshotBatchImportRequest> LoadFootballDataAsync(DataSourceImportRequest request, string sourceName, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("FOOTBALL_DATA_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("needs_api_key: set FOOTBALL_DATA_API_KEY to enable football-data.org collection.");
        }

        var competitionCode = string.IsNullOrWhiteSpace(request.CompetitionCode) ? "WC" : request.CompetitionCode.Trim();
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.DateFrom)) query.Add($"dateFrom={Uri.EscapeDataString(request.DateFrom)}");
        if (!string.IsNullOrWhiteSpace(request.DateTo)) query.Add($"dateTo={Uri.EscapeDataString(request.DateTo)}");
        var suffix = query.Count == 0 ? "" : $"?{string.Join("&", query)}";
        var url = $"https://api.football-data.org/v4/competitions/{Uri.EscapeDataString(competitionCode)}/matches{suffix}";
        var json = await GetStringWithRetryAsync(url, 30, configure: client => client.DefaultRequestHeaders.Add("X-Auth-Token", apiKey), cancellationToken: cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var batch = new DataSnapshotBatchImportRequest { Source = sourceName };
        batch.Items.Add(new DataSnapshotCreateRequest
        {
            Source = sourceName,
            SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? "fixture_intel" : request.SnapshotType!,
            ObjectId = request.ObjectId,
            MatchId = request.MatchId,
            ContentJson = new JsonObject
            {
                ["provider"] = "football_data",
                ["competition_code"] = competitionCode,
                ["date_from"] = request.DateFrom,
                ["date_to"] = request.DateTo,
                ["payload"] = JsonNode.Parse(doc.RootElement.GetRawText())
            }.ToJsonString()
        });
        return batch;
    }

    private static string? ReadJsonString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static async Task<string> GetStringWithRetryAsync(
        string url,
        int timeoutSeconds,
        int attempts = 3,
        Action<HttpClient>? configure = null,
        CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PiPiClaw-Team/0.2");
                configure?.Invoke(client);
                return await client.GetStringAsync(url, cancellationToken);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"http_fetch_cancelled_or_timed_out: {url}", ex);
            }
            catch (Exception ex) when (attempt < attempts)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException($"http_fetch_failed: {url}: {lastError?.Message}");
    }

    private sealed class RssFeedRun
    {
        public string Feed { get; set; } = "";
        public bool Passed { get; set; }
        public long ElapsedMs { get; set; }
        public List<JsonNode> Articles { get; set; } = [];
        public string ErrorMessage { get; set; } = "";
    }

    private sealed class FifaRankingMeta
    {
        public string? ScheduleId { get; set; }
        public string? ScheduleDate { get; set; }
        public string? LastUpdateDate { get; set; }
        public string? NextUpdateDate { get; set; }
    }
}
