using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using SharpPress.Helpers;
using SharpPress.Middleware;
using SharpPress.Middlewares;
using SharpPress.Models;
using SharpPress.Modules;
using SharpPress.Plugins;
using SharpPress.Servers;
using SharpPress.Services;
using SharpPress.Services.Database;
using SharpPress.Services.General;
using System.Text;
using Version = SharpPress.Models.Version;

namespace SharpPress
{
    public static class AppVersion
    {
        public static Version Version { get; private set; } = new Version() { Major = 1, Minor = 2, Patch = 1 };

        public static Task<Version?> ToVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return Task.FromResult<Version?>(null);

            var parts = version.Trim().TrimStart('v', 'V').Split('.');
            if (parts.Length == 0 || parts.Length > 3)
                return Task.FromResult<Version?>(null);

            var numbers = new int[3];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out numbers[i]))
                    return Task.FromResult<Version?>(null);
            }

            return Task.FromResult<Version?>(new Version() { Major = numbers[0], Minor = numbers[1], Patch = numbers[2] });
        }

        public static async Task<bool> IsNewer(Version version)
        {
            if (version.Major != Version.Major)
                return version.Major > Version.Major;

            if (version.Minor != Version.Minor)
                return version.Minor > Version.Minor;

            return version.Patch > Version.Patch;
        }
    }

    public class Program
    {
        private static int httpPort = 12001;

        public static async Task Main(string[] args)
        {
            var httpPortEnv = Environment.GetEnvironmentVariable("HTTP_PORT");
            if (!string.IsNullOrEmpty(httpPortEnv) && int.TryParse(httpPortEnv, out int envHttpPort))
            {
                httpPort = envHttpPort;
            }
            else if (args.Length >= 1 && int.TryParse(args[0], out int argHttpPort))
            {
                httpPort = argHttpPort;
            }

            var builder = WebApplication.CreateBuilder(args);

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(httpPort);
            });

            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor |
                    ForwardedHeaders.XForwardedProto;
            });

            var logger = new Logger();
            var loggerAdapter = new LoggerAdapter(logger);
            var configManager = new ConfigManager(logger: logger);

            var moduleStateService = new ModuleStateService();
            var moduleManager = new ModuleManager(logger, moduleStateService);

            await moduleManager.LoadModulesAsync();
            moduleManager.RunBeforeBuild(builder);

            builder.Services.AddSingleton(logger);
            builder.Services.AddSingleton(configManager);
            builder.Services.AddSingleton(provider => provider.GetRequiredService<ConfigManager>().Config);
            builder.Services.AddSingleton(moduleManager);
            builder.Services.AddSingleton(moduleStateService);

            builder.Services.AddControllersWithViews(options =>
            {
                options.Filters.Add<PluginEnabledFilter>();
            });

            builder.Services.AddRazorPages();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("DynamicPolicy", policyBuilder =>
                {
                    policyBuilder
                        .WithOrigins(ConfigManager.NormalizeUrl(configManager.Config.PanelDomain))
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials();
                });
            });

            builder.Services.AddMemoryCache();
            builder.Services.AddHealthChecks()
                .AddCheck<DatabaseHealthCheck>("mysql", timeout: TimeSpan.FromSeconds(5));
            builder.Services.Configure<HostOptions>(options =>
            {
                options.ShutdownTimeout = TimeSpan.FromSeconds(30);
            });
            builder.Services.AddHttpClient();
            builder.Services.AddHttpContextAccessor();

            builder.Services.AddSingleton<ServiceOverrideRegistry>();
            builder.Services.AddOverridable<IEmailService, EmailService>(provider => new EmailService(config: provider.GetRequiredService<ConfigManager>().Config));
            builder.Services.AddSingleton<FilePaths>();
            builder.Services.AddSingleton(provider => new ServerSettings { httpPort = httpPort });
            builder.Services.AddOverridable<ICacheService, CacheService>();
            builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
            builder.Services.AddSingleton<PluginStateService>();
            builder.Services.AddSingleton<PluginActionDescriptorChangeProvider>();
            builder.Services.AddSingleton<IActionDescriptorChangeProvider>(sp => sp.GetRequiredService<PluginActionDescriptorChangeProvider>());
            builder.Services.AddSingleton<Services.EventHandler>();
            builder.Services.AddSingleton(provider => new FeatherDatabase(loggerAdapter, provider.GetRequiredService<ConfigManager>().Config.MySQL_Config, provider.GetRequiredService<ConfigManager>().Config));
            builder.Services.AddOverridable<IUserService, UserService>();
            builder.Services.AddSingleton<WebSocketServer>();
            builder.Services.AddOverridable<IValidationService, ValidationService>();
            builder.Services.AddSingleton<PluginManager>();
            builder.Services.AddSingleton<PluginEndpointDataSource>();
            builder.Services.AddSingleton<RazorCacheCleaner>();
            builder.Services.AddSingleton<PluginSettingsStore>();
            builder.Services.AddOverridable<IScheduler, SchedulerService>();
            builder.Services.AddSingleton<PluginMiddlewareRegistry>();
            builder.Services.AddSingleton<DownloadJobProcessor>();
            builder.Services.AddOverridable<IAuthenticationService, AuthenticationService>();
            builder.Services.AddSingleton<PackageManager>();
            builder.Services.AddOverridable<IVideoStreamingService, VideoStreamingService>();
            builder.Services.AddSingleton<IAdminMenuService, AdminMenuService>();
            builder.Services.AddOverridable<IFilterPipeline, FilterPipeline>();
            builder.Services.AddOverridable<IPostMetaService, PostMetaService>();
            builder.Services.AddOverridable<IDashboardService, DashboardService>();
            builder.Services.AddOverridable<IPostService, PostService>();
            builder.Services.AddOverridable<ITaxonomyService, TaxonomyService>();
            builder.Services.AddOverridable<IMediaService, MediaService>();

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = "Cookies";
            })
            .AddCookie(options =>
            {
                options.LoginPath = "/Login";
                options.LogoutPath = "/Login";
                options.AccessDeniedPath = "/Login";
            })
            .AddJwtBearer("Bearer", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(configManager.Config.JwtSecret))
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Cookies.TryGetValue("X-Access-Token", out var token))
                            context.Token = token;

                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception is SecurityTokenExpiredException)
                            context.Response.Cookies.Delete("X-Access-Token");

                        return Task.CompletedTask;
                    }
                };
            });

            builder.Services.AddAuthorization();

            logger.PrepareLogs();

            var app = builder.Build();
            var serviceProvider = app.Services;

            app.UseCors("DynamicPolicy");

            app.UseAuthentication();
            app.UseAuthorization();

            await moduleManager.RunAfterBuildAsync(app);

            app.UseForwardedHeaders();
            app.UseMiddleware<SetupRedirectMiddleware>();
            app.UseMiddleware<UserControlMiddleware>();
            app.UsePluginRoutes();
            app.UseMiddleware<SlugRoutingMiddleware>();
            app.UseRouting();


            var applicationPartManager = serviceProvider.GetRequiredService<ApplicationPartManager>();
            var pluginManager = serviceProvider.GetRequiredService<PluginManager>();
            var env = serviceProvider.GetRequiredService<IWebHostEnvironment>();

            pluginManager.Initialize(applicationPartManager, env);

            await pluginManager.LoadPluginsAsync();
            await Endpoints.RegisterServices(serviceProvider.GetRequiredService<ServerConfig>());

            app.MapControllers();
            app.MapRazorPages();
            app.MapHealthChecks("/health");
            ((IEndpointRouteBuilder)app).DataSources.Add(serviceProvider.GetRequiredService<PluginEndpointDataSource>());

            Endpoints.Map(app, pluginManager);

            var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() =>
            {
                Endpoints.SignalShutdown();

                logger.Log("🛑 Shutdown signal received. Stopping services...");
                ShutdownApplication(serviceProvider, logger).GetAwaiter().GetResult();
            });

            logger.Log($"⚠️ Ensure Nginx is configured to proxy port 443 to localhost:{httpPort}");
            logger.Log($"🚀 Server started successfully on HTTP port {httpPort}!");

            _ = Task.Run(async () =>
            {
                while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
                {
                    if (app.Lifetime.ApplicationStopping.IsCancellationRequested)
                        break;

                    await Task.Delay(250, app.Lifetime.ApplicationStopping);
                    await pluginManager.UpdateLoadedPluginsAsync();
                }
            });

            var database = serviceProvider.GetRequiredService<FeatherDatabase>();
            await PrepareDatabase(database, logger);

            serviceProvider.GetRequiredService<IScheduler>().Every("core:publish-scheduled-posts", TimeSpan.FromMinutes(1), async _ =>
            {
                if (database.IsConfigured)
                    await serviceProvider.GetRequiredService<IPostService>().PublishScheduledPostsAsync();
            });

            await app.RunAsync();
        }

        public static async Task PrepareDatabase(FeatherDatabase database, Logger logger)
        {
            try
            {
                await database.CreateTable<User>();
                await database.CreateIndex<User>("Username");
                await database.CreateIndex<User>("UUID");
                await database.CreateTable<Post>();
                await database.CreateIndex<Post>("Slug");
                await database.CreateTable<Category>();
                await database.CreateIndex<Category>("Slug");
                await database.CreateTable<Tag>();
                await database.CreateIndex<Tag>("Slug");
                await database.CreateTable<PostMeta>();
                await database.CreateIndex<PostMeta>("PostId");
                await database.CreateTable<PostCategory>();
                await database.CreateIndex<PostCategory>("PostId");
                await database.CreateIndex<PostCategory>("CategoryId");
                await database.CreateTable<PostTag>();
                await database.CreateIndex<PostTag>("PostId");
                await database.CreateIndex<PostTag>("TagId");
                await database.CreateTable<MediaItem>();
                await database.CreateIndex<MediaItem>("MediaType");
            }
            catch (Exception ex)
            {
                logger.LogError($"Tables were not created, database unreachable: {ex.Message}");
            }
        }

        private static async Task ShutdownApplication(IServiceProvider serviceProvider, Logger logger)
        {
            try
            {
                Endpoints.SignalShutdown();

                var downloadProcessor = serviceProvider.GetService<DownloadJobProcessor>();
                if (downloadProcessor != null)
                {
                    logger.Log("🛑 Stopping Download Processor...");
                    await downloadProcessor.StopAsync();
                }

                var plugins = serviceProvider.GetService<PluginManager>();
                if (plugins != null)
                {
                    logger.Log("🛑 Unloading Plugins...");
                    await plugins.UnloadAllPluginsAsync();
                }

                logger.Log("✅ All services stopped gracefully.");
            }
            catch (Exception ex)
            {
                logger.LogError($"Error during shutdown: {ex.Message}");
            }
        }
    }
}