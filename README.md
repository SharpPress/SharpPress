# SharpPress

A plugin-first content management system built on C# and .NET 8. SharpPress stores data in MySQL through a lightweight built-in ORM and is extended through hot-loadable plugins that can add routes, pages, events, settings, scheduled jobs and even replace core services without a restart.

## Highlights

- **Content:** posts, pages, categories, tags, scheduled publishing, media library with thumbnails, RSS, sitemap and robots.txt.
- **Users and security:** JWT and cookie authentication, TOTP two-factor authentication, role-based access (User, Moderator, Support, Admin) and login lockout.
- **Admin panel:** dashboard, content management, user management, plugin management and a plugin store.
- **Plugin platform:** load, replace and unload plugins at runtime. The plugin API covers HTTP routes, MVC controllers and Razor Pages, middleware, an event bus with cancellable events, content filters and shortcodes, typed settings, custom post fields, a scheduler, private database tables and service replacement.
- **Storage:** a custom ORM that creates and updates tables from C# classes. No EF Core and no migration files.

## Requirements

- .NET 8 SDK
- MySQL 8

## Getting started

```bash
git clone https://github.com/your-username/SharpPress.git
cd SharpPress/Server
dotnet restore
dotnet run
```

The site listens on `http://localhost:12001` by default; set the `HTTP_PORT` environment variable to change it. Open `/setup` to enter the database connection and create the first administrator account. No default credentials exist.

## Project layout

| Path | Purpose |
|---|---|
| `Server/` | The web application: pages, endpoints, services, ORM and the plugin host. |
| `Abstractions/` | `SharpPress.Abstractions`, the contracts plugins compile against: `IPlugin`, `IPluginContext`, service interfaces, events and models. |
| `Samples/SamplePlugin/` | A buildable plugin that demonstrates every plugin feature. |
| `docs/` | Additional documentation. |

## Security

- Passwords are hashed with PBKDF2-SHA256 (210,000 iterations, random salt per user).
- The JWT secret is generated on first start and stored in `config.json`.
- Authentication cookies are HttpOnly and Secure.
- Raw SQL is restricted to `SELECT`; destructive statements are blocked at the ORM level.
- File operations are checked against path traversal.
- Plugin uploads, installs, enabling and disabling require an administrator.

Please report vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

## Plugins

Plugins are class libraries that reference only `SharpPress.Abstractions` and implement `IPlugin`.

```csharp
[assembly: PluginApiVersion(1)]

public class MyPlugin : IPlugin
{
    public string Name => "MyPlugin";
    public string Version => "1.0.0";
    public string Author => "Your Name";
    public string? Description => "Says hello.";

    public Task OnLoadAsync(IPluginContext context)
    {
        context.MapGet("/hello/{name}", (string name) => Results.Text($"Hello {name}"));
        return Task.CompletedTask;
    }

    public Task OnUpdateAsync(IPluginContext context) => Task.CompletedTask;
    public Task OnUnloadAsync() => Task.CompletedTask;
}
```

Project file:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <ProjectReference Include="..\SharpPress.Abstractions.csproj">
    <Private>false</Private>
    <ExcludeAssets>runtime</ExcludeAssets>
  </ProjectReference>
</ItemGroup>
```

Use `Microsoft.NET.Sdk.Razor` with `<AddRazorSupportForMvc>true</AddRazorSupportForMvc>` when the plugin ships Razor Pages or controllers.

### Installing and updating

Upload the DLL in the admin panel, install it from the plugin store, or copy it to `plugins/` (or `plugins/MyPlugin/MyPlugin.dll` with its dependencies alongside). The server watches the folder, so adding, replacing or deleting a file loads, hot-swaps or unloads the plugin with no restart. Plugin assemblies are loaded from memory, so files are never locked, and a replacement that fails to load is rolled back to the previous version.

### Plugin API

Everything registered through `IPluginContext` is tracked and removed automatically when the plugin is disabled, replaced or deleted.

| Member | Description |
|---|---|
| `MapGet`, `MapPost`, `Map`, `MapRoute` | Endpoints with route templates, parameters, dependency injection and optional role requirements. Paths under `/admin` require an administrator by default. |
| `UseMiddleware` | Ordered request middleware that can be added and removed at runtime. |
| `MapStaticFiles` | Serves a folder from the plugin or its data directory. |
| `Events` | Subscribe to events. Cancellable events (`PostCreatingEvent`, `PostUpdatingEvent`, `PostDeletingEvent`, `UserRegisteringEvent`, `LoginAttemptingEvent`, `MediaUploadingEvent`, `SettingsSavingEvent`) let a plugin veto or modify an action. |
| `Filters` | Content filters (`FilterKeys.*`) and `[shortcode]` handlers. |
| `Settings` | Typed per-plugin settings with an automatically generated admin form. |
| `PostMeta` | Custom fields shown in the post editor and stored per post. |
| `Scheduler` | Recurring and daily jobs that remember their last run. |
| `Database` | Tables for the plugin's own entity types with versioned migrations. |
| `Services` | Replace or decorate core services such as `IPostService`, `IUserService`, `IMediaService`, `IEmailService` and `ICacheService`. |
| `RegisterAdminMenuItem`, `RegisterDashboardWidget` | Admin sidebar entries and dashboard panels. |
| `PluginStopping`, `DataDirectory`, `PluginDirectory` | Cancellation token and plugin folders. |

### Sample plugin

`Samples/SamplePlugin` uses every feature above. Build it, upload `SamplePlugin.dll` in the admin panel and open **Sample feature check** in the sidebar to see each feature run live.

### Unloading

Plugins, including those with controllers and Razor Pages, unload completely. [docs/PluginUnloading.md](docs/PluginUnloading.md) explains how this works and how to diagnose a plugin that will not unload (enable `DebugMode` in `config.json` to log what still references it).

## Deployment

Run SharpPress behind a reverse proxy such as Nginx with TLS termination (see `Nginx Configuration.txt`) or as a systemd service.

```bash
dotnet publish Server -c Release -o /opt/sharppress
```

```ini
[Unit]
Description=SharpPress CMS
After=network.target mysql.service

[Service]
WorkingDirectory=/opt/sharppress
ExecStart=/usr/bin/dotnet SharpPress.dll
Restart=always
RestartSec=10
Environment=HTTP_PORT=12001
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md).

## License

Released under the MIT License. See [LICENSE](LICENSE).
