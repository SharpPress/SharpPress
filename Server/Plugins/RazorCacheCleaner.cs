using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using System.Collections;
using System.Reflection;

namespace SharpPress.Services
{
    public sealed class RazorCacheCleaner
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const int MaxDepth = 5;

        private readonly IServiceProvider _serviceProvider;
        private readonly Logger _logger;

        public RazorCacheCleaner(IServiceProvider serviceProvider, Logger logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        private static readonly string[] StaticCachePrefixes =
        {
            "Microsoft.AspNetCore",
            "Microsoft.Extensions",
            "Microsoft.Net.Http",
            "System.Text.Json",
            "System.ComponentModel",
            "System.Linq.Expressions",
            "System.Diagnostics.DiagnosticSource",
            "Newtonsoft.Json",
            "MySqlConnector",
            "SharpPress"
        };

        public void Purge(Assembly assembly)
        {
            try
            {
                System.ComponentModel.TypeDescriptor.Refresh(assembly);
                RunUpdateHandlers(assembly);

                var visited = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
                PurgeStatics(assembly, visited);

                foreach (var root in Roots())
                    PurgeGraph(root, assembly, visited, MaxDepth);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not purge Razor caches: {ex.Message}");
            }
        }

        private void RunUpdateHandlers(Assembly target)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == target || assembly.IsDynamic || assembly.IsCollectible) continue;

                IEnumerable<System.Reflection.Metadata.MetadataUpdateHandlerAttribute> attributes;
                try { attributes = assembly.GetCustomAttributes<System.Reflection.Metadata.MetadataUpdateHandlerAttribute>().ToList(); }
                catch { continue; }

                foreach (var attribute in attributes)
                {
                    try
                    {
                        var method = attribute.HandlerType.GetMethod(
                            "ClearCache",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { typeof(Type[]) },
                            null);

                        method?.Invoke(null, new object?[] { null });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Cache handler {attribute.HandlerType.Name} failed: {ex.InnerException?.Message ?? ex.Message}");
                    }
                }
            }
        }

        private void PurgeStatics(Assembly target, Dictionary<object, int> visited)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == target || assembly.IsDynamic || assembly.IsCollectible) continue;

                var name = assembly.GetName().Name;
                if (name == null || !StaticCachePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))) continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type.ContainsGenericParameters) continue;

                    FieldInfo[] fields;
                    try { fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                    catch { continue; }

                    foreach (var field in fields)
                    {
                        if (field.IsLiteral || field.FieldType.IsValueType || field.FieldType == typeof(string)) continue;

                        object? value;
                        try { value = field.GetValue(null); }
                        catch { continue; }

                        PurgeGraph(value, target, visited, MaxDepth);
                    }
                }
            }
        }

        private IEnumerable<object> Roots()
        {
            var types = new[]
            {
                typeof(IViewCompilerProvider),
                typeof(IRazorViewEngine),
                typeof(IRazorPageFactoryProvider),
                typeof(PageLoader),
                typeof(IPageActivatorProvider),
                typeof(IPageModelActivatorProvider),
                typeof(IPageFactoryProvider),
                typeof(IModelMetadataProvider),
                typeof(IObjectModelValidator),
                typeof(IActionInvokerFactory),
                typeof(IActionDescriptorCollectionProvider),
                typeof(IHtmlGenerator)
            };

            yield return _serviceProvider;

            foreach (var type in types)
            {
                object? service = null;
                try { service = _serviceProvider.GetService(type); }
                catch { service = null; }

                if (service != null) yield return service;
            }

            var provider = _serviceProvider.GetService<IViewCompilerProvider>();
            var compiler = provider?.GetCompiler();
            if (compiler != null) yield return compiler;
        }

        private static void PurgeGraph(object? node, Assembly assembly, Dictionary<object, int> visited, int depth)
        {
            if (node == null || depth < 0) return;

            var type = node.GetType();
            if (type.IsPrimitive || type.IsEnum || node is string || node is Type || node is Delegate) return;
            if (visited.TryGetValue(node, out var seenDepth) && seenDepth >= depth) return;
            visited[node] = depth;

            if (node is MemoryCache memoryCache)
            {
                memoryCache.Compact(1.0);
                return;
            }

            if (node is IDictionary dictionary)
            {
                PurgeDictionary(dictionary, assembly);

                var values = new List<object?>();
                try
                {
                    foreach (DictionaryEntry entry in dictionary)
                        values.Add(entry.Value);
                }
                catch (InvalidOperationException)
                {
                }

                foreach (var value in values)
                    PurgeGraph(value, assembly, visited, depth - 1);

                return;
            }

            if (node is Array array)
            {
                foreach (var item in array)
                    if (item != null && IsMvcType(item.GetType())) PurgeGraph(item, assembly, visited, depth - 1);
                return;
            }

            if (!IsMvcType(type))
                return;

            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (var field in current.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string)) continue;

                    object? value;
                    try { value = field.GetValue(node); }
                    catch { continue; }

                    PurgeGraph(value, assembly, visited, depth - 1);
                }
            }
        }

        private static bool IsMvcType(Type type)
            => type.Namespace != null
                && (type.Namespace.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                    || type.Namespace.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)
                    || type.Namespace.StartsWith("System.Text.Json", StringComparison.Ordinal)
                    || type.Namespace.StartsWith("System.ComponentModel", StringComparison.Ordinal)
                    || type.Namespace.StartsWith("Newtonsoft.Json", StringComparison.Ordinal)
                    || type.Namespace.StartsWith("MySqlConnector", StringComparison.Ordinal)
                    || type.Namespace.StartsWith("SharpPress", StringComparison.Ordinal));

        private static void PurgeDictionary(IDictionary dictionary, Assembly assembly)
        {
            if (dictionary.IsFixedSize || dictionary.IsReadOnly) return;

            var stale = new List<object>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (References(entry.Key, assembly, 3) || References(entry.Value, assembly, 3))
                    stale.Add(entry.Key);
            }

            foreach (var key in stale)
                dictionary.Remove(key);
        }

        private static bool References(object? value, Assembly assembly, int depth)
        {
            if (value == null || depth < 0) return false;

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string) return false;

            if (value is Type t) return TypeBelongs(t, assembly);
            if (value is MemberInfo member)
                return (member.DeclaringType != null && TypeBelongs(member.DeclaringType, assembly))
                    || (member is MethodInfo { IsGenericMethod: true } genericMethod && genericMethod.GetGenericArguments().Any(a => TypeBelongs(a, assembly)));

            if (value is ParameterInfo parameter)
                return TypeBelongs(parameter.ParameterType, assembly) || References(parameter.Member, assembly, depth - 1);

            if (value is Module module) return module.Assembly == assembly;
            if (value is Assembly assemblyValue) return assemblyValue == assembly;
            if (type.Assembly == assembly) return true;

            if (value is Delegate del)
                return TypeBelongs(del.Method.DeclaringType, assembly) || References(del.Target, assembly, depth - 1);

            if (value is Task task)
            {
                if (!task.IsCompletedSuccessfully || !task.GetType().IsGenericType) return false;
                return References(task.GetType().GetProperty("Result")?.GetValue(task), assembly, depth - 1);
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    if (References(item, assembly, depth - 1)) return true;

                return false;
            }

            if (type.Assembly == typeof(string).Assembly) return false;

            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (var field in current.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string)) continue;

                    object? fieldValue;
                    try { fieldValue = field.GetValue(value); }
                    catch { continue; }

                    if (References(fieldValue, assembly, depth - 1)) return true;
                }
            }

            return false;
        }

        private static bool TypeBelongs(Type? type, Assembly assembly)
        {
            if (type == null) return false;
            if (type.Assembly == assembly) return true;
            if (type.HasElementType) return TypeBelongs(type.GetElementType(), assembly);
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                    if (TypeBelongs(argument, assembly)) return true;
            }

            return false;
        }
    }
}
