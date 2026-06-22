using SharpPress.Models;
using SharpPress.Services;

namespace SharpPress.Middlewares
{
    public class SlugRoutingMiddleware
    {
        private readonly RequestDelegate _next;

        public SlugRoutingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, PostService postService, TaxonomyService taxonomyService)
        {
            var path = context.Request.Path.Value;

            if (!string.IsNullOrEmpty(path) && path != "/")
            {
                var segments = path.Trim('/').Split('/');
                if (segments.Length >= 2)
                {
                    var prefix = segments[0].ToLowerInvariant();
                    var slug = segments[1];

                    if (prefix == "blog" || prefix == "article" || prefix == "page")
                    {
                        var post = await postService.GetPostBySlugAsync(slug);
                        if (post != null && post.Status == PostStatus.Published)
                        {
                            context.Items["ResolvedPost"] = post;
                            context.Request.Path = "/PostView";
                            await _next(context);
                            return;
                        }
                    }
                    else if (prefix == "category")
                    {
                        var category = await taxonomyService.GetCategoryBySlugAsync(slug);
                        if (category != null)
                        {
                            context.Items["ResolvedCategory"] = category;
                            context.Request.Path = "/CategoryView";
                            await _next(context);
                            return;
                        }
                    }
                    else if (prefix == "tag")
                    {
                        var tag = await taxonomyService.GetTagBySlugAsync(slug);
                        if (tag != null)
                        {
                            context.Items["ResolvedTag"] = tag;
                            context.Request.Path = "/TagView";
                            await _next(context);
                            return;
                        }
                    }
                }
            }

            await _next(context);
        }
    }
}
