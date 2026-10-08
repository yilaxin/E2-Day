using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DailyRecord.Core;

public record NewsSource(string Name, string Url, string Language);
public record NewsResult(List<NewsArticle> Articles, int SuccessfulSources, int TotalSources);

public sealed class NewsService
{
    public static readonly NewsSource[] Sources =
    [
        new("微软", "https://news.microsoft.com/source/asia/feed/?lang=zh-hans", "简体中文"),
        new("Google", "https://blog.google/intl/zh-tw/rss/", "繁体中文"),
        new("OpenAI", "https://openai.com/news/rss.xml", "英文"),
        new("DeepMind", "https://deepmind.google/blog/rss.xml", "英文"),
        new("Microsoft Research", "https://www.microsoft.com/en-us/research/blog/feed/", "英文")
    ];

    public async Task<NewsResult> FetchAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(14), MaxResponseContentBufferSize = 12_000_000 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DailyRecord/1.0 (personal-rss-reader)");
        var calls = Sources.Select(async source =>
        {
            try
            {
                using var response = await client.GetAsync(source.Url, cancellationToken);
                response.EnsureSuccessStatusCode();
                var xml = await response.Content.ReadAsStringAsync(cancellationToken);
                return (Success: true, Articles: Parse(xml, source, now));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException or InvalidDataException)
            { return (Success: false, Articles: new List<NewsArticle>()); }
        });
        var results = await Task.WhenAll(calls);
        var successes = results.Count(r => r.Success);
        if (successes == 0) throw new HttpRequestException("资讯源暂时无法连接，已保留上次缓存。");
        var articles = results.SelectMany(r => r.Articles)
            .GroupBy(a => a.Url.TrimEnd('/'), StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderByDescending(a => a.Language.Contains("中文"))
            .ThenByDescending(a => a.PublishedAt)
            .Take(5).ToList();
        return new(articles, successes, Sources.Length);
    }

    public static List<NewsArticle> Parse(string xml, NewsSource source, DateTimeOffset now)
    {
        if (xml.Length > 12_000_000) throw new InvalidDataException("资讯内容过大。");
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 12_000_000
        });
        var document = XDocument.Load(reader);
        string Value(XElement e, params string[] names) => e.Elements()
            .FirstOrDefault(x => names.Contains(x.Name.LocalName))?.Value ?? "";
        var list = new List<NewsArticle>();
        foreach (var item in document.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            var title = Clean(Value(item, "title"));
            var summary = Clean(Value(item, "description", "summary"));
            var link = item.Elements().FirstOrDefault(e => e.Name.LocalName == "link" &&
                (e.Attribute("rel")?.Value is null or "alternate"));
            var url = link?.Attribute("href")?.Value ?? link?.Value ?? "";
            var dateText = Value(item, "pubDate", "published", "updated", "date");
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) continue;
            if (!DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)) continue;
            if (date > now.AddDays(1) || date < now.AddDays(-14)) continue;
            if (!IsAi(title + " " + summary)) continue;
            list.Add(new()
            {
                Title = title, Summary = summary.Length > 220 ? summary[..220] + "…" : summary,
                Url = uri.AbsoluteUri, Source = source.Name, Language = source.Language, PublishedAt = date
            });
        }
        return list;
    }

    public static bool IsAi(string text) => Regex.IsMatch(text,
        @"\b(AI|GPT[\w-]*|LLM[\w-]*|Gemini|Claude|Copilot|ChatGPT|DeepMind|machine learning|language model|neural|agentic|reasoning model)\b|人工智能|人工智慧|大模型|大語言模型|大语言模型|生成式|智能体|智慧代理",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string Clean(string value) => Regex.Replace(WebUtility.HtmlDecode(
        Regex.Replace(value, "<[^>]*>", " ")), @"\s+", " ").Trim();
}
