# Why plugins did not unload, and how SharpPress fixes it

A plugin is loaded into a collectible `AssemblyLoadContext`. Unloading is cooperative: the context is only freed once nothing outside it references its types, instances, methods or the assembly itself. ASP.NET Core keeps many static and per-service caches keyed by `Type`, so the moment a plugin controller, Razor Page, model or serialized type touches the framework, the framework can pin the plugin's context and the old assembly stays in memory for good. That is what happened with MVC plugins here.

## What actually holds a plugin (found with heap dumps)

Each of these was proven with `dotnet-dump` (`collect`, then `gcroot` on a surviving plugin object) and a reference-path finder built into the server:

| Holder | Where | Cause |
|---|---|---|
| `Microsoft.Extensions.Internal.PropertyHelper.PropertiesCache` | static, in every MVC assembly | Known upstream bug [dotnet/aspnetcore#21744](https://github.com/dotnet/aspnetcore/issues/21744), open since 2020: entries for plugin types are never removed. |
| `System.ComponentModel.TypeDescriptor` provider cache | static, behind a closed generic lambda cache | Created whenever something asks for a `TypeConverter` of a plugin type. |
| `ParameterBindingMethodCache` | static, in the HTTP layer | Caches methods of plugin handlers. |
| `DefaultModelMetadataProvider._modelMetadataCache` | per-service | Keyed by `ModelMetadataIdentity`, which holds `ParameterInfo`/`FieldInfo` of plugin types. |
| DI `ServiceProvider._serviceAccessors` and `CallSiteFactory._callSiteCache` | per-service | Cache a call site for every type resolved from the container, including plugin page models. |
| `Newtonsoft.Json` reflection caches | static | Serializing a plugin type (for example plugin settings). |
| Razor view lookup and compiled-view caches | per-service | Hold compiled page types. |
| Delegates and middleware chains | host | Hot middleware chains and endpoint delegates referenced plugin closures until rebuilt. |

## What the host does on unload

`PluginManager` follows this order for every unload, replace and disable (see `Server/Services/Plugin System/PluginManager.cs`):

1. Call the plugin's `OnUnloadAsync`.
2. Dispose the plugin scope: routes, middleware, event handlers, filters, shortcodes, scheduled jobs, settings sections, post meta fields, dashboard widgets, menu items, service overrides and application parts are all removed.
3. `RazorCacheCleaner.Purge` (`Server/Plugins/RazorCacheCleaner.cs`):
   - `TypeDescriptor.Refresh(assembly)`.
   - Runs every `MetadataUpdateHandler` `ClearCache` in the process (the same hook .NET Hot Reload uses).
   - Walks the static fields of the framework assemblies and the services that hold caches and removes every dictionary entry whose key or value references the plugin (types, members, parameters, delegates, tasks, generic arguments).
4. Drop the host's references (`Assembly`, plugin instance, context) inside a `NoInlining` method so no local keeps them alive, then `AssemblyLoadContext.Unload()`.
5. Loop `GC.Collect` / `WaitForPendingFinalizers` until a `WeakReference` to the context clears, and log a warning if it never does.

Plugin DLLs are also loaded from memory, so the file on disk is never locked even if step 5 fails.

## Diagnosing a plugin that will not unload

Set `"DebugMode": true` in `config.json`. When a context survives, the server logs `Reference path for '<plugin>': ...`, a chain from a static or a DI service to an object that references the plugin. Use that chain to find the cache that needs clearing.

For cases the in-process finder cannot see (closed generic statics, thread statics, GC handles) use a heap dump:

```bash
dotnet tool install --global dotnet-dump
dotnet-dump collect -p <pid> --type Full -o plugin.dmp
dotnet-dump analyze plugin.dmp -c "dumpheap -type <PluginNamespace>" -c "gcroot <address of a surviving instance>" -c exit
```

`gcroot` prints the exact retention chain. Add the offending cache to `RazorCacheCleaner` (or fix it in your plugin, for example by not handing plugin types to long-lived static caches).

## Rules for plugin authors

- Do not store plugin types, `Type`/`MethodInfo` objects or plugin instances in static fields of the host or of shared libraries.
- Do not start threads, timers or tasks that outlive `OnUnloadAsync`; use `context.Scheduler` and `context.PluginStopping`.
- Everything registered through `IPluginContext` is cleaned up automatically. Anything registered some other way (for example manually subscribing to a static event) must be undone in `OnUnloadAsync`.
- Native libraries and drivers that pin their own handles can still block unloading; test such a dependency in isolation.

## Sources

- [dotnet/aspnetcore#21744, property caches are not cleaned up when using AssemblyParts](https://github.com/dotnet/aspnetcore/issues/21744)
- [dotnet/runtime, unloadability design](https://github.com/dotnet/runtime/blob/main/docs/design/features/unloadability.md)
- [Microsoft Learn, how to use and debug assembly unloadability](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability)
