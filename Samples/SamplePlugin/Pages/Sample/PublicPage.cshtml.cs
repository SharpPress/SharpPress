using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Services;

namespace SamplePlugin.Pages
{
    public class PublicPageModel : PageModel
    {
        private readonly IFilterPipeline _filters;

        public PublicPageModel(IFilterPipeline filters)
        {
            _filters = filters;
        }

        public string Rendered { get; private set; } = "";
        public string Greeting { get; private set; } = "";

        public void OnGet()
        {
            Rendered = _filters.Apply(FilterKeys.PostContentRender, "Year [year], and [shout]loud text[/shout].");
            Greeting = SampleState.Context?.Settings.Get<SampleSettings>().Greeting ?? "";
        }
    }
}
