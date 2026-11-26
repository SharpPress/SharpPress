using SharpPress.Plugins;

namespace SharpPress.Tests
{
    public class ArchitectureTests
    {
        private static string[] References(System.Reflection.Assembly assembly)
            => assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        [Fact]
        public void AbstractionsDoesNotReferenceServer()
        {
            Assert.DoesNotContain("SharpPress", References(typeof(IPlugin).Assembly));
        }

        [Fact]
        public void SamplePluginDoesNotReferenceServer()
        {
            Assert.DoesNotContain("SharpPress", References(typeof(global::SamplePlugin.SampleDemoPlugin).Assembly));
        }
    }
}
