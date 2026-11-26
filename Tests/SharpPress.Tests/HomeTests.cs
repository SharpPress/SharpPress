using SharpPress.Helpers;
using SharpPress.Models;

namespace SharpPress.Tests
{
    public class HomeTests
    {
        [Fact]
        public void HomePageStepsAsideForAStaticIndex()
        {
            var root = Directory.CreateTempSubdirectory().FullName;

            Assert.True(HomeRoute.ShouldShowHomePage("/", root));
            Assert.False(HomeRoute.ShouldShowHomePage("/about", root));

            File.WriteAllText(Path.Combine(root, "index.html"), "x");

            Assert.False(HomeRoute.ShouldShowHomePage("/", root));
            Directory.Delete(root, true);
        }

        [Theory]
        [InlineData(PostType.BlogPost, "/blog/a")]
        [InlineData(PostType.Article, "/article/a")]
        [InlineData(PostType.Page, "/page/a")]
        public void PostLinksMatchTheSlugRoutes(PostType type, string expected)
        {
            Assert.Equal(expected, HomeRoute.LinkFor(new Post { Type = type, Slug = "a" }));
        }

        [Fact]
        public void SourceFilesContainOnlyEnglishText()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SharpPress.sln")))
                directory = directory.Parent;

            var foreign = new System.Text.RegularExpressions.Regex(@"[\p{IsArabic}\p{IsCyrillic}\p{IsHebrew}\p{IsCJKUnifiedIdeographs}]");
            var files = new[] { "Server", "Abstractions", "Samples" }
                .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(directory!.FullName, folder), "*.cs*", SearchOption.AllDirectories))
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

            Assert.DoesNotContain(files, file => foreign.IsMatch(File.ReadAllText(file)));
        }
    }
}
