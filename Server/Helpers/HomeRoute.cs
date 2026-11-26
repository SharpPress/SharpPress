using SharpPress.Models;

namespace SharpPress.Helpers
{
    public static class HomeRoute
    {
        public const string PagePath = "/Home";

        public static bool ShouldShowHomePage(string? requestPath, string publicRoot)
            => requestPath == "/"
               && !File.Exists(Path.Combine(publicRoot, "index.php"))
               && !File.Exists(Path.Combine(publicRoot, "index.html"));

        public static string LinkFor(Post post)
        {
            var prefix = post.Type switch
            {
                PostType.Page => "page",
                PostType.Article => "article",
                _ => "blog"
            };

            return $"/{prefix}/{Uri.EscapeDataString(post.Slug ?? "")}";
        }
    }
}
