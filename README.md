# SharpPress

A plugin-first CMS built with C# / .NET 8. No EF Core, no migrations, no magic.

Admin panel included (posts, users, media). Everything is extensible via plugins — drop a DLL in `plugins/` and it loads without a restart.

## What it is

MySQL for storage. A custom ORM that builds tables from C# classes. JWT + cookie auth. A plugin system that hot-loads DLLs into isolated `AssemblyLoadContext`s.

## Features

- Posts, pages, categories, tags, scheduled publishing
- Users: JWT + cookies, TOTP 2FA, roles (User/Mod/Support/Admin), login lockout
- Media uploads with auto thumbnails
- Hot-loaded plugins, enable/disable without restart
- Event bus, WebSocket server
- RSS, sitemap.xml, robots.txt

## Requirements

.NET 8 SDK, MySQL 8.

## Installation

```bash
git clone https://github.com/your-username/SharpPress.git
cd SharpPress/Server
dotnet restore
dotnet build
dotnet run
```

Runs on `http://localhost:12001` by default (override with `HTTP_PORT`).

Tables are created on first boot. Then go to `/setup` to create the admin account. No default credentials exist.

## Security

- Passwords: PBKDF2-SHA256, 210k iterations, random salt per user
- JWT secret generated on first boot, stored in `config.json`
- Auth cookies HttpOnly/Secure
- Raw queries restricted to SELECT; DROP/DELETE/TRUNCATE blocked at the ORM level
- Path traversal checks on file operations
- User HTML sanitized before storage

Report security issues via [SECURITY.md](SECURITY.md), not publicly.

## Writing a plugin

Plugins are class libraries implementing `IPlugin`:

```csharp
public class MyPlugin : IPlugin
{
    public string Name => "MyPlugin";
    public string Version => "1.0.0";
    public string Author => "Your Name";
    public string? Description => "does a thing";

    public async Task OnLoadAsync(IPluginContext context) { }
    public async Task OnUpdateAsync(IPluginContext context) { }
    public async Task OnUnloadAsync() { }
}
```

Build, drop the DLL in `plugins/`. See `SamplePlugin.cs` for a working example.

## Deployment

Behind Nginx with SSL termination (sample config in `Nginx Configuration.txt`) or as a systemd service:

```bash
dotnet publish -c Release -o /opt/sharppress
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

## License

MIT