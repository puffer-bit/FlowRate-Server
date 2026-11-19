using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Server.Infrastructure.Coordinator.LinkPreviews;

/// <summary>
/// What the head of a web page says about it: Open Graph and Twitter card properties, the description
/// and the title. Only the tags are read; the page is never run or laid out.
/// </summary>
public sealed partial class PageMetadata
{
    public string? SiteName { get; private init; }
    public string? Title { get; private init; }
    public string? Description { get; private init; }
    public string? ImageUrl { get; private init; }
    public int ImageWidth { get; private init; }
    public int ImageHeight { get; private init; }

    /// <summary>The <c>twitter:card</c> of the page: <c>summary_large_image</c>, <c>summary</c> or null.</summary>
    public string? Card { get; private init; }

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"([a-zA-Z_:.-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))",
        RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"</head\s*>|<body\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadEnd();

    [GeneratedRegex(@"<!--.*?-->|<script\b.*?</script\s*>|<style\b.*?</style\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Skipped();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"<meta\b[^>]*charset\s*=\s*[""']?\s*([a-zA-Z0-9_.:-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaCharset();

    static PageMetadata()
    {
        // windows-1251, koi8-r and the like are still met on older sites
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static PageMetadata Parse(string html)
    {
        var end = HeadEnd().Match(html);
        var head = Skipped().Replace(end.Success ? html[..end.Index] : html, " ");

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTag().Matches(head))
        {
            string? key = null, content = null;
            foreach (Match attribute in Attribute().Matches(tag.Value))
            {
                var name = attribute.Groups[1].Value;
                var value = attribute.Groups[2].Success ? attribute.Groups[2].Value
                    : attribute.Groups[3].Success ? attribute.Groups[3].Value
                    : attribute.Groups[4].Value;
                if (name.Equals("property", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("name", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("itemprop", StringComparison.OrdinalIgnoreCase))
                    key ??= value.Trim();
                else if (name.Equals("content", StringComparison.OrdinalIgnoreCase))
                    content = value;
            }

            // The first value wins, as in the clients that read these tags
            if (key is { Length: > 0 } && Clean(content) is { } cleaned)
                properties.TryAdd(key, cleaned);
        }

        string? First(params string[] keys)
        {
            foreach (var key in keys)
                if (properties.TryGetValue(key, out var value))
                    return value;
            return null;
        }

        var titleTag = TitleTag().Match(head);
        return new PageMetadata
        {
            SiteName = First("og:site_name", "application-name"),
            Title = First("og:title", "twitter:title") ?? (titleTag.Success ? Clean(titleTag.Groups[1].Value) : null),
            Description = First("og:description", "twitter:description", "description"),
            ImageUrl = First("og:image:secure_url", "og:image:url", "og:image", "twitter:image", "twitter:image:src", "image"),
            ImageWidth = int.TryParse(First("og:image:width"), out var width) ? width : 0,
            ImageHeight = int.TryParse(First("og:image:height"), out var height) ? height : 0,
            Card = First("twitter:card")?.ToLowerInvariant()
        };
    }

    /// <summary>
    /// The encoding of a page: from its header, else from a byte order mark or a meta tag near the start,
    /// else UTF-8.
    /// </summary>
    public static Encoding EncodingOf(ReadOnlySpan<byte> start, string? headerCharset)
    {
        if (TryGetEncoding(headerCharset) is { } declared)
            return declared;

        if (start.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            return Encoding.UTF8;
        if (start.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            return Encoding.Unicode;
        if (start.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            return Encoding.BigEndianUnicode;

        var ascii = Encoding.Latin1.GetString(start[..Math.Min(start.Length, 2048)]);
        var meta = MetaCharset().Match(ascii);
        return (meta.Success ? TryGetEncoding(meta.Groups[1].Value) : null) ?? Encoding.UTF8;
    }

    private static Encoding? TryGetEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        try
        {
            return Encoding.GetEncoding(name.Trim().Trim('"', '\''));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? Clean(string? value)
    {
        if (value == null)
            return null;
        var text = Whitespace().Replace(WebUtility.HtmlDecode(value), " ").Trim();
        // Control characters would be refused by the clients' checks of the protocol
        text = new string(text.Where(c => !char.IsControl(c)).ToArray());
        return text.Length == 0 ? null : text;
    }

    /// <summary>Cuts a text to the length a field may have, ending it with an ellipsis.</summary>
    public static string? Cut(string? text, int maxLength)
    {
        if (text == null || text.Length <= maxLength)
            return text;
        var cut = text[..(maxLength - 1)];
        // Not between the halves of a surrogate pair
        if (char.IsHighSurrogate(cut[^1]))
            cut = cut[..^1];
        return cut.TrimEnd() + "…";
    }
}
