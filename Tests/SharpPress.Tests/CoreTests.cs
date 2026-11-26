using SharpPress.Events;
using SharpPress.Plugins;
using SharpPress.Services;

namespace SharpPress.Tests
{
    public class CoreTests
    {
        private sealed class VetoEvent : CancellableEvent
        {
        }

        private static Logger CreateLogger() => new(Path.Combine(Path.GetTempPath(), "sharppress-tests"));

        [Theory]
        [InlineData("Plugin.dll", true)]
        [InlineData("my-plugin_2", true)]
        [InlineData("../evil", false)]
        [InlineData("a/b", false)]
        [InlineData("", false)]
        public void SafeNameRejectsTraversal(string name, bool expected)
        {
            Assert.Equal(expected, PluginPaths.IsSafeName(name));
        }

        [Fact]
        public async Task AppVersionComparesAgainstItself()
        {
            var current = AppVersion.Version;
            var parsed = await AppVersion.ToVersion($"v{current.Major}.{current.Minor}.{current.Patch}");
            var next = await AppVersion.ToVersion($"{current.Major}.{current.Minor}.{current.Patch + 1}");

            Assert.False(await AppVersion.IsNewer(parsed!));
            Assert.True(await AppVersion.IsNewer(next!));
        }

        [Fact]
        public void IsInsideRejectsSiblingPrefix()
        {
            var root = Path.Combine(Path.GetTempPath(), "root");

            Assert.True(PluginPaths.IsInside(root, Path.Combine(root, "a.txt")));
            Assert.False(PluginPaths.IsInside(root, root + "-other"));
        }

        [Fact]
        public void ScopeRunsCleanupsInReverseOrderOnce()
        {
            var order = new List<int>();
            var scope = new PluginScope();
            scope.OnDispose(() => order.Add(1));
            scope.OnDispose(() => order.Add(2));

            scope.Dispose();
            scope.Dispose();

            Assert.Equal(new[] { 2, 1 }, order);
        }

        [Fact]
        public void ShortcodesRenderAndRemovalRestoresText()
        {
            var filters = new FilterPipeline(CreateLogger());
            var registration = filters.AddShortcode("shout", c => c.Content.ToUpperInvariant());

            Assert.Equal("HI", filters.RenderShortcodes("[shout]hi[/shout]"));

            registration.Dispose();

            Assert.Equal("[shout]hi[/shout]", filters.RenderShortcodes("[shout]hi[/shout]"));
        }

        [Fact]
        public async Task CancellationStopsLaterHandlers()
        {
            var bus = new InMemoryEventBus(CreateLogger());
            var reached = false;
            bus.Subscribe<VetoEvent>(e => { e.Cancel("no"); return Task.CompletedTask; }, 0);
            bus.Subscribe<VetoEvent>(_ => { reached = true; return Task.CompletedTask; }, 1);

            var allowed = await bus.PublishCancellableAsync(new VetoEvent());

            Assert.False(allowed);
            Assert.False(reached);
        }
    }
}
