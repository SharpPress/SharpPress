using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;

namespace SharpPress.Pages
{
    public class PostViewModel : PageModel
    {
        public Post? Post { get; set; }
        public List<Category> Categories { get; set; } = new();
        public List<Tag> Tags { get; set; } = new();

        public IActionResult OnGet()
        {
            if (HttpContext.Items.TryGetValue("ResolvedPost", out var resolvedPost) && resolvedPost is Post post)
            {
                Post = post;
            }

            if (Post == null)
            {
                return NotFound();
            }

            return Page();
        }
    }
}