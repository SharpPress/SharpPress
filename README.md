# SharpPress

A plugin-first CMS built with C# / .NET 8. Think WordPress but for people who like C# and don't want to fight EF Core migrations.

It gives you an admin panel out of the box (posts, users, media, the usual), but every piece of it can be swapped or extended with plugins. Drop a DLL in the `plugins` folder and it just loads — no restart needed.

## Why this exists

I wanted something that felt like a normal CMS to use, but where I could actually hack on the internals without fighting the framework. So: MySQL for storage, a powerful custom ORM that builds tables from your C# classes automatically, JWT + cookie auth, and a plugin system that hot-loads DLLs into isolated contexts so they don't step on each other's dependencies.

## What it can do

- **Posts, pages, blog stuff** — categories, tags, scheduled publishing, slugs like `/blog/my-post`
- **Users** — JWT + cookies, 2FA, roles (User/Mod/Support/Admin), auto lockout after too many failed logins
- **Media** — upload images/video/audio/docs, auto thumbnails, safe file paths
- **Plugins** — hot-loaded, run in their own `AssemblyLoadContext`, can be turned on/off without restarting the server
- **Real-time stuff** — WebSocket server + an event bus so plugins can react to things happening in the system
- **The boring important stuff** — gzip compression, in-memory caching, health check endpoint, graceful shutdown

## Getting it running

You need .NET 8 SDK and MySQL 8.

```bash
git clone https://github.com/your-username/SharpPress.git
cd SharpPress/Server
dotnet restore
dotnet build
```

Edit `config.json` (it generates itself on first run) and drop in your MySQL details:

```json
{
  "MySQL_Config": {
    "host": "localhost",
    "port": 3306,
    "database_name": "sharppress",
    "database_username": "root",
    "database_password": "your_password"
  }
}
```

Then just run it:

```bash
dotnet run
```

Defaults to `http://localhost:12001`. Admin panel is at `/Admin`.

First time it boots, it'll set up all the MySQL tables and folders on its own — no manual schema work needed.

## Writing a plugin

Plugins are just class libraries that implement `IPlugin`:

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

Build it, drop the DLL in `plugins/`, done. Check `SamplePlugin.cs` in the repo root if you want a working example to start from.

## Deploying

Run it behind Nginx with SSL termination (sample config's in `Nginx Configuration.txt`), or just set it up as a systemd service:

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

## Security notes

Passwords are hashed with PBKDF2 (10k iterations, random salt per user). Auth cookies are HttpOnly/Secure. User HTML gets sanitized before it's stored. File paths are checked for traversal attempts. Destructive SQL (DROP/DELETE/TRUNCATE) is blocked at the ORM level — only SELECT is allowed through raw queries.

Found a security issue? Check [SECURITY.md](SECURITY.md) before posting it publicly.

## License

MIT
