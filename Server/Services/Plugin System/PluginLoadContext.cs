using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace SharpPress.Services
{
    public sealed class PluginLoadContext : AssemblyLoadContext
    {
        private static readonly string[] SharedPrefixes =
        {
            "SharpPress",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions",
            "Microsoft.Net.Http",
            "Microsoft.Win32",
            "System",
            "netstandard",
            "mscorlib",
            "Newtonsoft.Json",
            "MySqlConnector"
        };

        private readonly string _pluginDirectory;
        private readonly AssemblyDependencyResolver? _resolver;

        public PluginLoadContext(string pluginPath, string name) : base(name, isCollectible: true)
        {
            _pluginDirectory = Path.GetDirectoryName(Path.GetFullPath(pluginPath)) ?? AppContext.BaseDirectory;
            try { _resolver = new AssemblyDependencyResolver(pluginPath); }
            catch { _resolver = null; }
        }

        public Assembly LoadMain(byte[] assemblyBytes, byte[]? symbolBytes)
        {
            using var assemblyStream = new MemoryStream(assemblyBytes, writable: false);
            if (symbolBytes == null) return LoadFromStream(assemblyStream);
            using var symbolStream = new MemoryStream(symbolBytes, writable: false);
            return LoadFromStream(assemblyStream, symbolStream);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (name == null || IsShared(name)) return null;

            var path = _resolver?.ResolveAssemblyToPath(assemblyName);
            if (path == null)
            {
                var candidate = Path.Combine(_pluginDirectory, name + ".dll");
                if (File.Exists(candidate)) path = candidate;
            }

            if (path == null) return null;

            using var stream = new MemoryStream(File.ReadAllBytes(path), writable: false);
            return LoadFromStream(stream);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }

        public static bool IsShared(string assemblyName)
        {
            foreach (var prefix in SharedPrefixes)
            {
                if (assemblyName.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                if (assemblyName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        public static bool IsPluginAssembly(byte[] bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata) return false;
                var reader = pe.GetMetadataReader();
                foreach (var handle in reader.AssemblyReferences)
                {
                    var reference = reader.GetAssemblyReference(handle);
                    if (reader.GetString(reference.Name).Equals("SharpPress.Abstractions", StringComparison.Ordinal))
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
