using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;

namespace SharpPress.Pages
{
    public class HomeModel : PageModel
    {
        private readonly IPostService _postService;
        private readonly ServerConfig _config;

        public HomeModel(IPostService postService, ServerConfig config)
        {
            _postService = postService;
            _config = config;
        }

        public string SiteName { get; private set; } = "SharpPress";
        public string SiteDescription { get; private set; } = "";
        public List<Post> Posts { get; private set; } = new();

        public async Task OnGetAsync()
        {
            var general = _config.SiteSettings?.General;
            if (!string.IsNullOrWhiteSpace(general?.SiteName))
                SiteName = general.SiteName;
            SiteDescription = general?.SiteDescription ?? "";

            Posts = (await _postService.GetPublishedPostsAsync())
                .Where(p => p.Type != PostType.Page)
                .OrderByDescending(p => p.PublishedAt)
                .Take(20)
                .ToList();
        }
    }
}
