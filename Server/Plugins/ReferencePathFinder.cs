using System.Collections;
using System.Reflection;

namespace SharpPress.Plugins
{
    public static class ReferencePathFinder
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        private static readonly string[] StaticRootPrefixes = { "Microsoft.AspNetCore", "Microsoft.Extensions", "SharpPress", "Newtonsoft.Json", "MySqlConnector", "System.Text.Json" };

        public static string? Find(IEnumerable<object> roots, Assembly target, int maxNodes = 3_000_000)
        {
            var nodes = new List<Node>();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var queue = new Queue<int>();

            void Enqueue(object? value, int parent, string label)
            {
                if (value == null) return;
                var type = value.GetType();
                if (type.IsPrimitive || type.IsEnum || value is string || value is Pointer) return;
                if (type.IsValueType) return;
                if (!visited.Add(value)) return;

                nodes.Add(new Node(value, parent, label));
                queue.Enqueue(nodes.Count - 1);
            }

            foreach (var root in roots)
                Enqueue(root, -1, $"root {root.GetType().Name}");

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = assembly.GetName().Name;
                if (name == null || !StaticRootPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))) continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type.ContainsGenericParameters) continue;

                    FieldInfo[] fields;
                    try { fields = type.GetFields(AnyStatic); }
                    catch { continue; }

                    foreach (var field in fields)
                    {
                        if (!field.IsStatic || field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType.IsValueType) continue;

                        object? value;
                        try { value = field.GetValue(null); }
                        catch { continue; }

                        Enqueue(value, -1, $"static {type.FullName}.{field.Name}");
                    }
                }
            }

            while (queue.Count > 0 && nodes.Count < maxNodes)
            {
                var index = queue.Dequeue();
                var node = nodes[index];
                var value = node.Value;
                var type = value.GetType();

                if (value is Type typeValue)
                {
                    if (TypeBelongs(typeValue, target)) return Describe(nodes, index);
                    continue;
                }

                if (type.Assembly == target) return Describe(nodes, index);

                if (value is Delegate del)
                {
                    if (del.Method.DeclaringType != null && TypeBelongs(del.Method.DeclaringType, target)) return Describe(nodes, index);
                    Enqueue(del.Target, index, "Delegate.Target");
                    if (del is MulticastDelegate)
                    {
                        foreach (var inner in del.GetInvocationList())
                            if (!ReferenceEquals(inner, del)) Enqueue(inner, index, "InvocationList");
                    }
                    continue;
                }

                if (value is Assembly assemblyValue)
                {
                    if (assemblyValue == target) return Describe(nodes, index);
                    continue;
                }

                if (value is Module moduleValue)
                {
                    if (moduleValue.Assembly == target) return Describe(nodes, index);
                    continue;
                }

                if (value is MemberInfo member)
                {
                    if (member.DeclaringType != null && TypeBelongs(member.DeclaringType, target)) return Describe(nodes, index);
                    if (member is MethodInfo { IsGenericMethod: true } genericMethod && genericMethod.GetGenericArguments().Any(a => TypeBelongs(a, target)))
                        return Describe(nodes, index);
                    continue;
                }

                if (value is Array array)
                {
                    if (array.GetType().GetElementType()!.IsValueType)
                    {
                        if (array.GetType().GetElementType()!.IsPrimitive) continue;
                    }

                    var position = 0;
                    foreach (var item in array)
                    {
                        if (item != null && item.GetType().IsValueType)
                            Expand(item, index, $"[{position}]", Enqueue);
                        else
                            Enqueue(item, index, $"[{position}]");

                        position++;
                        if (position > 200_000) break;
                    }
                    continue;
                }

                for (var current = type; current != null && current != typeof(object); current = current.BaseType)
                {
                    FieldInfo[] fields;
                    try { fields = current.GetFields(AnyInstance | BindingFlags.DeclaredOnly); }
                    catch { continue; }

                    foreach (var field in fields)
                    {
                        if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string)) continue;

                        object? fieldValue;
                        try { fieldValue = field.GetValue(value); }
                        catch { continue; }

                        if (fieldValue != null && field.FieldType.IsValueType)
                        {
                            Expand(fieldValue, index, $"{current.Name}.{field.Name}", Enqueue);
                            continue;
                        }

                        Enqueue(fieldValue, index, $"{current.Name}.{field.Name}");
                    }
                }
            }

            return null;
        }

        private static void Expand(object boxed, int parent, string label, Action<object?, int, string> enqueue)
        {
            var type = boxed.GetType();
            if (type.IsPrimitive || type.IsEnum) return;

            foreach (var field in type.GetFields(AnyInstance))
            {
                if (field.FieldType.IsPrimitive || field.FieldType.IsEnum) continue;

                object? value;
                try { value = field.GetValue(boxed); }
                catch { continue; }

                if (value == null) continue;

                if (field.FieldType.IsValueType) Expand(value, parent, $"{label}.{field.Name}", enqueue);
                else enqueue(value, parent, $"{label}.{field.Name}");
            }
        }

        private static bool TypeBelongs(Type type, Assembly target)
        {
            if (type.Assembly == target) return true;
            if (type.HasElementType) return TypeBelongs(type.GetElementType()!, target);
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                    if (TypeBelongs(argument, target)) return true;
            }

            return false;
        }

        private static string Describe(List<Node> nodes, int index)
        {
            var parts = new List<string>();
            for (var i = index; i >= 0 && parts.Count < 40; i = nodes[i].Parent)
                parts.Add($"{nodes[i].Label} ({nodes[i].Value.GetType().Name})");

            parts.Reverse();
            return string.Join(" -> ", parts);
        }

        private sealed record Node(object Value, int Parent, string Label);
    }
}
