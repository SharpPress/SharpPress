using SharpPress.Models;

namespace SharpPress.Plugins
{
    public sealed class PluginRouteOptions
    {
        public UserRole? RequiredRole { get; set; }
        public bool AllowAnonymous { get; set; }
    }
}
