namespace SharpPress.Plugins
{
    public static class PluginApi
    {
        public const int Version = 1;
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class PluginApiVersionAttribute : Attribute
    {
        public int Version { get; }

        public PluginApiVersionAttribute(int version)
        {
            Version = version;
        }
    }
}
