using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiPiClaw.Team;

public static class BaselinePredictionStrategy
{
    public const string Version = "multi_source_v3";

    public static BaselinePredictionRecord Predict(
        WorldCupMatch match,
        WorldCupWatchObject home,
        WorldCupWatchObject away,
        IReadOnlyList<DataSnapshotRecord>? snapshots = null)
    {
        if (!CanPredict(match, home, away, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        snapshots ??= [];
        var homeRank = ExtractFifaRank(home);
        var awayRank = ExtractFifaRank(away);
        var homeFifaPoints = ExtractMetadataDouble(home, "fifa_points");
        var awayFifaPoints = ExtractMetadataDouble(away, "fifa_points");
        var homeElo = ExtractLatestSnapshotDouble(snapshots, home.Id, "team_elo", "elo_rating");
        var awayElo = ExtractLatestSnapshotDouble(snapshots, away.Id, "team_elo", "elo_rating");
        var homeForm = ExtractLatestSnapshotDouble(snapshots, home.Id, "team_recent_form", "opponent_adjusted_form_score")
            ?? ExtractLatestSnapshotDouble(snapshots, home.Id, "team_recent_form", "recent_form_score");
        var awayForm = ExtractLatestSnapshotDouble(snapshots, away.Id, "team_recent_form", "opponent_adjusted_form_score")
            ?? ExtractLatestSnapshotDouble(snapshots, away.Id, "team_recent_form", "recent_form_score");

        var fifaPointEdge = (homeFifaPoints, awayFifaPoints) is ({ } hp, { } ap)
            ? Clamp((hp - ap) / 130.0, -1.6, 1.6)
            : 0.0;
        var rankEdge = Clamp((awayRank - homeRank) / 45.0, -1.2, 1.2);
        var fifaScore = homeFifaPoints.HasValue && awayFifaPoints.HasValue
            ? 0.70 * fifaPointEdge + 0.30 * rankEdge
            : rankEdge;
        var eloEdge = (homeElo, awayElo) is ({ } he, { } ae)
            ? Clamp((he - ae) / 360.0, -1.4, 1.4)
            : 0.0;
        var formEdge = (homeForm, awayForm) is ({ } hf, { } af)
            ? Clamp(hf - af, -0.8, 0.8)
            : 0.0;
        var hostEdge = CalculateHostEdge(match, home, away);
        var homeSnapshotSignal = BuildTeamSnapshotSignal(snapshots, home.Id);
        var awaySnapshotSignal = BuildTeamSnapshotSignal(snapshots, away.Id);
        var snapshotEdge = homeSnapshotSignal - awaySnapshotSignal;

        var weightedScore =
            0.40 * fifaScore +
            0.35 * eloEdge +
            0.15 * formEdge +
            hostEdge +
            snapshotEdge;
        var favoritePressure = -0.045 * Math.Sign(weightedScore) * Math.Min(Math.Abs(weightedScore), 1.0);
        var upsetGuard = Math.Abs(weightedScore) > 0.80 ? -0.055 * Math.Sign(weightedScore) : 0.0;
        var score = weightedScore + favoritePressure + upsetGuard;

        var dataQuality = EstimateDataQuality(homeFifaPoints, awayFifaPoints, homeElo, awayElo, homeForm, awayForm, snapshots, home.Id, away.Id);
        var drawProbability = CalculateDrawProbability(match, score, formEdge, dataQuality);
        var nonDraw = 1.0 - drawProbability;
        var homeShare = 1.0 / (1.0 + Math.Exp(-score * 1.72));

        var homeWin = Round(nonDraw * homeShare);
        var draw = Round(drawProbability);
        var awayWin = Round(1.0 - homeWin - draw);
        if (awayWin < 0)
        {
            awayWin = 0;
            var total = homeWin + draw;
            homeWin = Round(homeWin / total);
            draw = Round(1.0 - homeWin);
        }

        var factors = new JsonArray();
        factors.Add((JsonNode)BuildFactor("fifa_points_edge", "FIFA ranking points edge", 0.40 * fifaPointEdge, 0.40));
        factors.Add((JsonNode)BuildFactor("fifa_rank_edge", "FIFA rank edge", 0.12 * rankEdge, 0.12));
        factors.Add((JsonNode)BuildFactor("elo_edge", "World Football Elo edge", 0.35 * eloEdge, 0.35));
        factors.Add((JsonNode)BuildFactor("recent_form_edge", "Opponent-adjusted recent form edge", 0.15 * formEdge, 0.15));
        factors.Add((JsonNode)BuildFactor("host_context", "Host and venue context", hostEdge, 0.08));
        factors.Add((JsonNode)BuildFactor("favorite_pressure", "Favorite pressure adjustment", favoritePressure, 0.045));
        factors.Add((JsonNode)BuildFactor("upset_guard", "Upset guard", upsetGuard, 0.055));
        factors.Add((JsonNode)BuildFactor("draw_calibration", "Draw probability calibration", drawProbability - 0.255, 0.10));
        factors.Add((JsonNode)BuildFactor("home_snapshot_signal", "Home evidence signal", homeSnapshotSignal, 0.12));
        factors.Add((JsonNode)BuildFactor("away_snapshot_signal", "Away evidence signal", -awaySnapshotSignal, 0.12));

        var payload = new JsonObject
        {
            ["schema"] = "strategy_factors_v2",
            ["strategy_version"] = Version,
            ["home_rank"] = homeRank,
            ["away_rank"] = awayRank,
            ["home_fifa_points"] = homeFifaPoints,
            ["away_fifa_points"] = awayFifaPoints,
            ["home_elo"] = homeElo,
            ["away_elo"] = awayElo,
            ["home_recent_form"] = homeForm,
            ["away_recent_form"] = awayForm,
            ["aggregate_score"] = Round(score),
            ["pre_guard_score"] = Round(weightedScore),
            ["data_quality"] = Round(dataQuality),
            ["evidence_snapshot_ids"] = JsonSerializer.SerializeToNode(snapshots.Take(14).Select(item => item.Id).ToList(), AppJsonContext.Default.ListString),
            ["factors"] = factors
        };

        return new BaselinePredictionRecord
        {
            Id = $"baseline_{match.Id}_{Version}",
            MatchId = match.Id,
            StrategyVersion = Version,
            HomeWinProbability = homeWin,
            DrawProbability = draw,
            AwayWinProbability = awayWin,
            Method = "snapshot_aware_factor",
            InputSnapshotIdsJson = payload.ToJsonString(),
            Explanation = $"{home.DisplayName} vs {away.DisplayName}: FIFA rank {homeRank}/{awayRank}, " +
                          $"FIFA points {FormatNullable(homeFifaPoints)}/{FormatNullable(awayFifaPoints)}, " +
                          $"Elo {FormatNullable(homeElo)}/{FormatNullable(awayElo)}, recent form {FormatNullable(homeForm)}/{FormatNullable(awayForm)}, " +
                          $"data quality {dataQuality:0.00}, score {score:0.00}."
        };
    }

    public static bool CanPredict(
        WorldCupMatch match,
        WorldCupWatchObject? home,
        WorldCupWatchObject? away,
        out string reason)
    {
        if (home == null || away == null)
        {
            reason = "Cannot create prediction because one side of the match is not a resolved team.";
            return false;
        }

        if (IsPlaceholderTeamId(match.HomeObjectId) || IsPlaceholderTeamId(match.AwayObjectId)
            || IsPlaceholderTeam(home) || IsPlaceholderTeam(away))
        {
            reason = $"Cannot create prediction for unresolved fixture {match.Id}: {match.HomeObjectId} vs {match.AwayObjectId}.";
            return false;
        }

        if (IsDemoOnly(home) || IsDemoOnly(away))
        {
            reason = $"Cannot create production prediction for demo-only teams in {match.Id}.";
            return false;
        }

        reason = "";
        return true;
    }

    private static JsonObject BuildFactor(string id, string label, double contribution, double weight)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["label"] = label,
            ["home_contribution"] = Round(contribution),
            ["weight"] = Round(weight)
        };
    }

    private static double BuildTeamSnapshotSignal(IReadOnlyList<DataSnapshotRecord> snapshots, string teamId)
    {
        var teamSnapshots = snapshots
            .Where(snapshot => snapshot.ObjectId == teamId)
            .Take(10)
            .ToList();
        if (teamSnapshots.Count == 0) return 0;

        var rawScore = 0.0;
        foreach (var snapshot in teamSnapshots)
        {
            var text = snapshot.ContentJson.ToLowerInvariant();
            rawScore += CountSignals(text, ["strong", "stable", "continuity", "pressing", "control", "positive", "win", "wins", "advantage", "稳定", "压迫", "控球", "连胜", "优势"], 0.035);
            rawScore += CountSignals(text, ["injury", "injuries", "doubtful", "risk", "complacency", "rotation", "fatigue", "negative", "伤停", "存疑", "风险", "轮换", "疲劳"], -0.045);
            if (text.Contains("\"confidence\":\"high\"", StringComparison.Ordinal)) rawScore *= 1.06;
            if (text.Contains("\"confidence\":\"low\"", StringComparison.Ordinal)) rawScore *= 0.78;
        }
        return Clamp(rawScore, -0.22, 0.22);
    }

    private static double CountSignals(string text, IReadOnlyList<string> keywords, double weight)
    {
        return keywords.Count(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) * weight;
    }

    private static double CalculateHostEdge(WorldCupMatch match, WorldCupWatchObject home, WorldCupWatchObject away)
    {
        var homeCode = home.Symbol.Trim().ToUpperInvariant();
        var awayCode = away.Symbol.Trim().ToUpperInvariant();
        var venue = match.Venue.ToLowerInvariant();
        var homeHost = IsHostTeam(homeCode);
        var awayHost = IsHostTeam(awayCode);

        if (homeHost && !awayHost && VenueHintsHome(venue, homeCode)) return 0.10;
        if (awayHost && !homeHost && VenueHintsHome(venue, awayCode)) return -0.10;
        if (homeHost && !awayHost) return 0.055;
        if (awayHost && !homeHost) return -0.055;
        if (!match.Stage.Equals("group", StringComparison.OrdinalIgnoreCase) && !IsNeutralVenue(match)) return 0.035;
        return 0.0;
    }

    private static double CalculateDrawProbability(WorldCupMatch match, double score, double formEdge, double dataQuality)
    {
        var drawBase = match.Stage.Equals("group", StringComparison.OrdinalIgnoreCase) ? 0.258 : 0.226;
        var strengthParityBoost = Math.Max(0, 1.0 - Math.Abs(score) / 0.62) * 0.085;
        var formParityBoost = Math.Max(0, 1.0 - Math.Abs(formEdge) / 0.34) * 0.026;
        var uncertaintyBoost = (1.0 - dataQuality) * 0.018;
        var friendlyBoost = match.Venue.Contains("friendly", StringComparison.OrdinalIgnoreCase) ? 0.018 : 0.0;
        var knockoutPenalty = IsKnockoutStage(match.Stage) ? 0.018 : 0.0;
        var mismatchPenalty = Math.Min(Math.Abs(score), 1.6) * 0.036;
        return Clamp(drawBase + strengthParityBoost + formParityBoost + uncertaintyBoost + friendlyBoost - knockoutPenalty - mismatchPenalty, 0.16, 0.37);
    }

    private static bool VenueHintsHome(string venue, string code)
    {
        return code switch
        {
            "MEX" => venue.Contains("mex", StringComparison.OrdinalIgnoreCase) || venue.Contains("guadalajara", StringComparison.OrdinalIgnoreCase) || venue.Contains("monterrey", StringComparison.OrdinalIgnoreCase),
            "USA" => venue.Contains("usa", StringComparison.OrdinalIgnoreCase) || venue.Contains("united states", StringComparison.OrdinalIgnoreCase) || venue.Contains("atlanta", StringComparison.OrdinalIgnoreCase) || venue.Contains("dallas", StringComparison.OrdinalIgnoreCase) || venue.Contains("new york", StringComparison.OrdinalIgnoreCase) || venue.Contains("los angeles", StringComparison.OrdinalIgnoreCase),
            "CAN" => venue.Contains("canada", StringComparison.OrdinalIgnoreCase) || venue.Contains("toronto", StringComparison.OrdinalIgnoreCase) || venue.Contains("vancouver", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsHostTeam(string code)
    {
        return code is "MEX" or "USA" or "CAN";
    }

    private static double EstimateDataQuality(
        double? homeFifaPoints,
        double? awayFifaPoints,
        double? homeElo,
        double? awayElo,
        double? homeForm,
        double? awayForm,
        IReadOnlyList<DataSnapshotRecord> snapshots,
        string homeId,
        string awayId)
    {
        var score = 0.25;
        if (homeFifaPoints.HasValue && awayFifaPoints.HasValue) score += 0.25;
        if (homeElo.HasValue && awayElo.HasValue) score += 0.25;
        if (homeForm.HasValue && awayForm.HasValue) score += 0.15;
        if (snapshots.Any(item => item.MatchId != null) && snapshots.Any(item => item.ObjectId == homeId) && snapshots.Any(item => item.ObjectId == awayId)) score += 0.10;
        return Clamp(score, 0.20, 1.0);
    }

    private static double? ExtractLatestSnapshotDouble(
        IReadOnlyList<DataSnapshotRecord> snapshots,
        string objectId,
        string snapshotType,
        string propertyName)
    {
        foreach (var snapshot in snapshots
            .Where(item => item.ObjectId == objectId && item.SnapshotType.Equals(snapshotType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(snapshot.ContentJson);
                if (snapshotType.Equals("team_elo", StringComparison.OrdinalIgnoreCase)
                    && !IsExpectedEloSnapshot(doc.RootElement, objectId))
                {
                    continue;
                }

                if (doc.RootElement.TryGetProperty(propertyName, out var value))
                {
                    if (value.TryGetDouble(out var number)) return number;
                    if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
                }
            }
            catch
            {
                // Ignore malformed snapshots; other sources can still carry the prediction.
            }
        }
        return null;
    }

    private static bool IsExpectedEloSnapshot(JsonElement root, string objectId)
    {
        if (!root.TryGetProperty("elo_code", out var eloCodeProperty)) return true;
        var eloCode = eloCodeProperty.GetString();
        if (string.IsNullOrWhiteSpace(eloCode)) return true;

        var fifaCode = ObjectIdToFifaCode(objectId);
        if (string.IsNullOrWhiteSpace(fifaCode)) return true;

        var expected = ExpectedEloCode(fifaCode);
        return string.IsNullOrWhiteSpace(expected)
            || eloCode.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string ObjectIdToFifaCode(string objectId)
    {
        const string prefix = "team_";
        return objectId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? objectId[prefix.Length..].Trim().ToUpperInvariant()
            : "";
    }

    private static string ExpectedEloCode(string fifaCode)
    {
        return fifaCode.Trim().ToUpperInvariant() switch
        {
            "ALG" => "DZ", "ANG" => "AO", "ARG" => "AR", "AUS" => "AU", "AUT" => "AT",
            "BEL" => "BE", "BIH" => "BA", "BRA" => "BR", "CAN" => "CA", "CHN" => "CN",
            "CIV" => "CI", "CMR" => "CM", "COD" => "CD", "COL" => "CO", "CPV" => "CV",
            "CRC" => "CR", "CRO" => "HR", "CUW" => "CW", "CZE" => "CZ", "DEN" => "DK",
            "ECU" => "EC", "EGY" => "EG", "ENG" => "EN", "ESP" => "ES", "FRA" => "FR",
            "GER" => "DE", "GHA" => "GH", "HAI" => "HT", "HON" => "HN", "IRN" => "IR",
            "IRQ" => "IQ", "ISL" => "IS", "ITA" => "IT", "JOR" => "JO", "JPN" => "JP",
            "KOR" => "KR", "KSA" => "SA", "MAR" => "MA", "MEX" => "MX", "NED" => "NL",
            "NGR" => "NG", "NOR" => "NO", "NZL" => "NZ", "PAN" => "PA", "PAR" => "PY",
            "POR" => "PT", "QAT" => "QA", "RSA" => "ZA", "SCO" => "SQ", "SEN" => "SN",
            "SLO" => "SI", "SRB" => "RS", "SUI" => "CH", "SWE" => "SE", "SYC" => "SC",
            "TGA" => "TO", "TUN" => "TN", "TUR" => "TR", "UKR" => "UA", "URU" => "UY",
            "USA" => "US", "UZB" => "UZ", "WAL" => "WA",
            _ => ""
        };
    }

    private static int ExtractFifaRank(WorldCupWatchObject team)
    {
        try
        {
            using var doc = JsonDocument.Parse(team.MetadataJson);
            if (doc.RootElement.TryGetProperty("fifa_rank", out var rank) && rank.TryGetInt32(out var value))
            {
                return Math.Clamp(value, 1, 210);
            }
        }
        catch
        {
            // Metadata can be incomplete in demo imports. Fall back to a neutral rank.
        }
        return 50;
    }

    private static double? ExtractMetadataDouble(WorldCupWatchObject team, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(team.MetadataJson);
            if (!doc.RootElement.TryGetProperty(propertyName, out var value)) return null;
            if (value.TryGetDouble(out var number)) return number;
            return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPlaceholderTeamId(string objectId)
    {
        return string.IsNullOrWhiteSpace(objectId)
            || objectId.StartsWith("slot_", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("tba", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("placeholder", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaceholderTeam(WorldCupWatchObject team)
    {
        return IsPlaceholderTeamId(team.Id)
            || team.Symbol.Contains("TBA", StringComparison.OrdinalIgnoreCase)
            || team.Name.Contains("announced", StringComparison.OrdinalIgnoreCase)
            || team.DisplayName.Contains("announced", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDemoOnly(WorldCupWatchObject team)
    {
        try
        {
            using var doc = JsonDocument.Parse(team.MetadataJson);
            return doc.RootElement.TryGetProperty("demo", out var demo)
                && demo.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNeutralVenue(WorldCupMatch match)
    {
        return match.Venue.Contains("Demo", StringComparison.OrdinalIgnoreCase)
            || match.Stage.Equals("group", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnockoutStage(string stage)
    {
        var normalized = stage.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal).Replace(" ", "_", StringComparison.Ordinal);
        return normalized is "round_of_32"
            or "round_of_16"
            or "quarter_final"
            or "quarterfinal"
            or "semi_final"
            or "semifinal"
            or "third_place"
            or "final"
            or "knockout";
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(Math.Max(value, min), max);
    }

    private static double Round(double value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    private static string FormatNullable(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "n/a";
    }
}
