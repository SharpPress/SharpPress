using System.Text.RegularExpressions;
using SharpPress.Helpers;

namespace SharpPress.Tests
{
    public class UiTests
    {
        private static string Root()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SharpPress.sln")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new InvalidOperationException("SharpPress.sln was not found.");
        }

        private static IEnumerable<string> Pages()
            => new[] { Path.Combine("Server", "Pages"), Path.Combine("Samples", "SamplePlugin", "Pages") }
                .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(Root(), folder), "*.cshtml", SearchOption.AllDirectories));

        private static string Asset(string name)
            => File.ReadAllText(Path.Combine(Root(), "Server", "wwwroot", "assets", name));

        [Fact]
        public void PagesHaveNoOwnStyleBlocks()
        {
            Assert.DoesNotContain(Pages(), page => File.ReadAllText(page).Contains("<style"));
        }

        [Fact]
        public void PagesLoadNothingFromOtherHosts()
        {
            var external = new Regex("<(script|link|img)[^>]+(src|href)=\"(https?:)?//", RegexOptions.IgnoreCase);

            Assert.DoesNotContain(Pages(), page => external.IsMatch(File.ReadAllText(page)));
            Assert.DoesNotMatch("url\\(|@import|https?://", Asset("app.css"));
        }

        [Fact]
        public void StylesheetHasNoGradientsBlurOrGlow()
        {
            Assert.DoesNotMatch("gradient|blur\\(|backdrop-filter|box-shadow:\\s*0 0|text-shadow", Asset("app.css"));
        }

        [Fact]
        public void StatusPagesUseTheSharedStylesheetAndEncodeTheAddress()
        {
            var pages = new[] { StatusPages.Banned("a@b.co"), StatusPages.Maintenance("a@b.co"), StatusPages.NotFound() };

            Assert.All(pages, page => Assert.Contains("/assets/app.css", page));
            Assert.DoesNotContain("\"><script>", StatusPages.Banned("\"><script>"));
        }
    }
}
