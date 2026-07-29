using System.Text.RegularExpressions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Notifications;

namespace ProductionLinePlanner.Infrastructure.Notifications;

public sealed class NotificationTemplateResolver : INotificationTemplateResolver
{
    private static readonly Regex TokenPattern = new(
        @"\{(?<name>[A-Za-z][A-Za-z0-9]*)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Result<IReadOnlyCollection<string>> ParseTokens(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return Result<IReadOnlyCollection<string>>.Failure(new Error(
                "TemplateRequired",
                "Notification templates cannot be empty."));
        }

        var matches = TokenPattern.Matches(template);
        var unmatchedText = TokenPattern.Replace(template, string.Empty);
        if (unmatchedText.Contains('{') || unmatchedText.Contains('}'))
        {
            return Result<IReadOnlyCollection<string>>.Failure(new Error(
                "MalformedTemplate",
                "Notification placeholders must use the {TokenName} format."));
        }

        var tokens = matches
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return Result<IReadOnlyCollection<string>>.Success(tokens);
    }

    public Result<string> Resolve(
        string template,
        IReadOnlyCollection<string> allowedTokens,
        IReadOnlyDictionary<string, string> tokenValues)
    {
        var validated = Validate(template, allowedTokens);
        if (validated.IsFailure) return Result<string>.Failure(validated.Error!);

        if (tokenValues is null)
        {
            return Result<string>.Failure(new Error(
                "TemplateTokenValuesRequired",
                "Notification template token values are required."));
        }

        var parsedTokens = validated.Value!;

        var missingTokens = parsedTokens
            .Where(token => !tokenValues.ContainsKey(token))
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();
        if (missingTokens.Length > 0)
        {
            return Result<string>.Failure(new Error(
                "MissingTemplateTokenValue",
                $"Missing values for notification template tokens: {string.Join(", ", missingTokens)}."));
        }

        var rendered = TokenPattern.Replace(
            template,
            match => tokenValues[match.Groups["name"].Value] ?? string.Empty);

        return string.IsNullOrWhiteSpace(rendered)
            ? Result<string>.Failure(new Error("EmptyRenderedTemplate", "The rendered notification template cannot be empty."))
            : Result<string>.Success(rendered);
    }

    public Result<IReadOnlyCollection<string>> Validate(
        string template,
        IReadOnlyCollection<string> allowedTokens)
    {
        var parsed = ParseTokens(template);
        if (parsed.IsFailure) return parsed;

        var allowed = new HashSet<string>(allowedTokens ?? [], StringComparer.Ordinal);
        var unknownTokens = parsed.Value!
            .Where(token => !allowed.Contains(token))
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();
        return unknownTokens.Length > 0
            ? Result<IReadOnlyCollection<string>>.Failure(new Error(
                "UnknownTemplateToken",
                $"Unknown notification template tokens: {string.Join(", ", unknownTokens)}."))
            : parsed;
    }
}
