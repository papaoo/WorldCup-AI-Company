using System.Text.Json;
using System.Text.RegularExpressions;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    private List<string?> ResolveNewsSignalObjectIds(
        DataSnapshotRecord snapshot,
        string articleText,
        IReadOnlyList<WorldCupWatchObject> teams)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.MatchId))
        {
            var match = GetMatchById(snapshot.MatchId);
            if (match != null)
            {
                var matchTeamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    match.HomeObjectId,
                    match.AwayObjectId
                };
                return teams
                    .Where(team => matchTeamIds.Contains(team.Id) && ArticleMentionsTeam(articleText, team))
                    .Select(team => (string?)team.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ObjectId))
        {
            var target = teams.FirstOrDefault(team => team.Id.Equals(snapshot.ObjectId, StringComparison.OrdinalIgnoreCase));
            return target != null && ArticleMentionsTeam(articleText, target)
                ? [target.Id]
                : [];
        }

        return teams
            .Where(team => ArticleMentionsTeam(articleText, team))
            .Select(team => (string?)team.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool NewsArticleHasTeamEntity(
        string articleText,
        IReadOnlyList<WorldCupWatchObject> teams)
    {
        return teams.Any(team => ArticleMentionsTeam(articleText, team));
    }

    private static bool SignalEvidenceMatchesTeam(
        IntelligenceSignalRecord signal,
        WorldCupWatchObject team)
    {
        if (string.IsNullOrWhiteSpace(signal.ObjectId)
            || !signal.ObjectId.Equals(team.Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (signal.SignalType is not ("injury_risk" or "lineup_news" or "news_update"))
        {
            return true;
        }

        var text = ExtractSignalEvidenceExcerpt(signal.EvidenceJson);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = signal.Summary;
        }

        return ArticleMentionsTeam(text, team)
            && !IsLikelyHostOrVenueMention(text, team);
    }

    private static bool ArticleMentionsTeam(string text, WorldCupWatchObject team)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var alias in BuildTeamAliases(team))
        {
            if (alias.Length <= 3)
            {
                if (ContainsShortAliasToken(text, alias)) return true;
                continue;
            }

            if (ContainsPhraseToken(text, alias)) return true;
        }

        return false;
    }

    private static string ExtractSignalEvidenceExcerpt(string evidenceJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(evidenceJson);
            return doc.RootElement.TryGetProperty("excerpt", out var excerpt) ? excerpt.ToString() : "";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsLikelyHostOrVenueMention(string text, WorldCupWatchObject team)
    {
        if (!team.Symbol.Equals("MEX", StringComparison.OrdinalIgnoreCase)
            && !team.Symbol.Equals("USA", StringComparison.OrdinalIgnoreCase)
            && !team.Symbol.Equals("CAN", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lowered = text.ToLowerInvariant();
        var matchedByHostCountryNameOnly = !ContainsToken(text, team.Symbol)
            && BuildTeamAliases(team)
                .Where(alias => !alias.Equals(team.Symbol, StringComparison.OrdinalIgnoreCase))
                .Any(alias => ContainsPhraseToken(text, alias));
        if (matchedByHostCountryNameOnly && !ContainsAnyTeamFootballContext(lowered, team))
        {
            return true;
        }

        if (lowered.Contains($"{team.Name.ToLowerInvariant()} city", StringComparison.Ordinal)
            || lowered.Contains("host", StringComparison.Ordinal)
            || lowered.Contains("venue", StringComparison.Ordinal)
            || lowered.Contains("stadium", StringComparison.Ordinal)
            || lowered.Contains("visa", StringComparison.Ordinal))
        {
            return !ContainsAnyTeamFootballContext(lowered, team);
        }

        if (lowered.Contains("canada, the us and mexico", StringComparison.Ordinal)
            || lowered.Contains("canada, us and mexico", StringComparison.Ordinal)
            || lowered.Contains("canada, the united states and mexico", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsAnyTeamFootballContext(string lowered, WorldCupWatchObject team)
    {
        var aliases = BuildTeamAliases(team)
            .Where(alias => !alias.Equals("USA", StringComparison.OrdinalIgnoreCase))
            .Select(alias => alias.ToLowerInvariant())
            .ToList();
        return aliases.Any(alias =>
            lowered.Contains($"{alias} squad", StringComparison.Ordinal)
            || lowered.Contains($"{alias} roster", StringComparison.Ordinal)
            || lowered.Contains($"{alias} lineup", StringComparison.Ordinal)
            || lowered.Contains($"{alias} coach", StringComparison.Ordinal)
            || lowered.Contains($"{alias} player", StringComparison.Ordinal)
            || lowered.Contains($"{alias} forward", StringComparison.Ordinal)
            || lowered.Contains($"{alias} goalkeeper", StringComparison.Ordinal)
            || lowered.Contains($"{alias} defeat", StringComparison.Ordinal)
            || lowered.Contains($"{alias} rout", StringComparison.Ordinal)
            || lowered.Contains($"{alias} win", StringComparison.Ordinal));
    }

    private static List<string> BuildTeamAliases(WorldCupWatchObject team)
    {
        var aliases = new List<string>();
        AddAlias(aliases, team.Symbol);
        AddAlias(aliases, team.Name);
        AddAlias(aliases, team.DisplayName);

        try
        {
            using var doc = JsonDocument.Parse(team.MetadataJson);
            AddAlias(aliases, ReadMetadataText(doc.RootElement, "name"));
            AddAlias(aliases, ReadMetadataText(doc.RootElement, "name_en"));
            AddAlias(aliases, ReadMetadataText(doc.RootElement, "country"));
            AddAlias(aliases, ReadMetadataText(doc.RootElement, "fifa_code"));
        }
        catch
        {
            // Metadata is auxiliary for entity matching; ignore malformed local rows.
        }

        switch (team.Symbol.ToUpperInvariant())
        {
            case "USA":
                AddAlias(aliases, "United States");
                AddAlias(aliases, "USMNT");
                break;
            case "RSA":
                AddAlias(aliases, "South Africa");
                break;
            case "ENG":
                AddAlias(aliases, "England");
                break;
            case "KOR":
                AddAlias(aliases, "South Korea");
                break;
            case "PRK":
                AddAlias(aliases, "North Korea");
                break;
            case "CIV":
                AddAlias(aliases, "Ivory Coast");
                AddAlias(aliases, "Cote d'Ivoire");
                AddAlias(aliases, "Côte d'Ivoire");
                break;
        }

        return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddAlias(List<string> aliases, string? value)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return;
        if (cleaned.Equals("To be announced", StringComparison.OrdinalIgnoreCase)) return;
        aliases.Add(cleaned);
    }

    private static string? ReadMetadataText(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;
    }

    private static bool ContainsPhraseToken(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase)) return false;
        return Regex.IsMatch(
            text,
            $@"(?<![A-Za-z0-9]){Regex.Escape(phrase)}(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ContainsShortAliasToken(string text, string alias)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(alias)) return false;
        return Regex.IsMatch(
            text,
            $@"(?<![A-Za-z0-9]){Regex.Escape(alias.ToUpperInvariant())}(?![A-Za-z0-9])",
            RegexOptions.CultureInvariant);
    }
}
