namespace SharpPress.Services
{
    public static class FilterKeys
    {
        public const string PostContentRender = "post.content.render";
        public const string PostTitle = "post.title";
        public const string PostExcerpt = "post.excerpt";
        public const string PageHead = "page.head";
        public const string PageFooter = "page.footer";
        public const string RssItemDescription = "rss.item.description";
        public const string SitemapUrls = "sitemap.urls";
    }

    public sealed class ShortcodeContext
    {
        public string Name { get; init; } = "";
        public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
        public string Content { get; init; } = "";
    }

    public interface IFilterPipeline
    {
        T Apply<T>(string key, T value);
        IDisposable AddFilter<T>(string key, Func<T, T> filter, int priority = 0);
        IDisposable AddShortcode(string name, Func<ShortcodeContext, string> handler);
        string RenderShortcodes(string content);
    }
}
