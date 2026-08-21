using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;
using SharpPress.Services.Database;
using System.ComponentModel.DataAnnotations;

namespace SharpPress.Pages
{
    public class SetupModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public int Step { get; set; } = 1;

        [BindProperty]
        public DatabaseInput Step1 { get; set; } = new();

        [BindProperty]
        public AdminInput Step2 { get; set; } = new();

        [BindProperty]
        public SiteInput Step3 { get; set; } = new();

        public string? ErrorMessage { get; set; }

        private readonly Logger _logger;
        private readonly FeatherDatabase _database;
        private readonly AuthenticationService _authService;
        private readonly ConfigManager _configManager;

        public SetupModel(FeatherDatabase database, AuthenticationService authService, ConfigManager configManager, Logger logger)
        {
            _database = database;
            _authService = authService;
            _configManager = configManager;
            _logger = logger;
        }

        public IActionResult OnGet()
        {
            var config = _configManager.Config;

            if (config.SetupStep >= 4)
                return RedirectToPage("/Login");

            if (Step < 1 || Step > config.SetupStep)
                Step = config.SetupStep;

            Step1.Host = config.MySQL_Config.host;
            Step1.Port = config.MySQL_Config.port;
            Step1.Database = config.MySQL_Config.database_name;
            Step1.Username = config.MySQL_Config.database_username;
            Step1.Password = config.MySQL_Config.database_password;

            Step3.SiteUrl = config.PanelDomain;
            Step3.Timezone = config.SiteSettings?.General?.Timezone ?? "UTC";
            Step3.AllowRegistration = config.SiteSettings?.Security?.AllowRegistration ?? false;
            Step3.ForceHttps = config.SiteSettings?.Security?.ForceHttps ?? false;

            return Page();
        }

        public async Task<IActionResult> OnPostStep1Async()
        {
            var config = _configManager.Config;

            if (config.SetupStep >= 4)
                return RedirectToPage("/Login");

            if (!IsValidFor("Step1."))
            {
                Step = 1;
                return Page();
            }

            config.MySQL_Config.host = Step1.Host.Trim();
            config.MySQL_Config.port = Step1.Port;
            config.MySQL_Config.database_name = Step1.Database.Trim();
            config.MySQL_Config.database_username = Step1.Username.Trim();
            config.MySQL_Config.database_password = Step1.Password ?? "";

            try
            {
                _database.Configure(config.MySQL_Config);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Could not connect to MySQL: {ex.Message}";
                Step = 1;
                return Page();
            }

            if (config.SetupStep < 2)
                config.SetupStep = 2;

            await _configManager.SaveConfig();
            await Program.PrepareDatabase(_database, _logger);

            return Redirect("/setup?step=2");
        }

        public async Task<IActionResult> OnPostStep2Async()
        {
            var config = _configManager.Config;

            if (config.SetupStep >= 4)
                return RedirectToPage("/Login");

            if (config.SetupStep < 2)
                return Redirect("/setup");

            if (await AdminExistsAsync())
                return Redirect("/setup?step=3");

            if (!IsValidFor("Step2."))
            {
                Step = 2;
                return Page();
            }

            if (Step2.Password != Step2.ConfirmPassword)
            {
                ErrorMessage = "Passwords do not match.";
                Step = 2;
                return Page();
            }

            if (await _database.GetByColumn<User>("Username", Step2.Username) != null)
            {
                ErrorMessage = "That username is already taken.";
                Step = 2;
                return Page();
            }

            if (await _database.GetByColumn<User>("Email", Step2.Email) != null)
            {
                ErrorMessage = "That email is already in use.";
                Step = 2;
                return Page();
            }

            var admin = new User
            {
                Username = Step2.Username,
                Email = Step2.Email,
                Password = _authService.HashPassword(Step2.Password),
                UUID = Guid.NewGuid().ToString(),
                Roles = UserRole.Admin,
                IsActive = true,
                IsVerified = true
            };
            await _database.SaveData(admin);

            config.SiteSettings = new SiteSettings
            {
                General = new GeneralSettings
                {
                    SiteName = Step2.SiteName,
                    SiteDescription = Step2.SiteDescription,
                    AdminEmail = Step2.Email,
                    Timezone = "UTC",
                    FooterText = ""
                },
                Security = config.SiteSettings?.Security ?? new SecuritySettings(),
                Advanced = config.SiteSettings?.Advanced ?? new AdvancedSettings()
            };

            if (config.SetupStep < 3)
                config.SetupStep = 3;

            await _configManager.SaveConfig();
            _logger.Log($"✅ Setup step 2 done, admin account created: {Step2.Username}");

            string token = _authService.GenerateJwtToken(admin);
            Response.Cookies.Append("X-Access-Token", token, _authService.CreateAuthCookieOptions());

            return Redirect("/setup?step=3");
        }

        public async Task<IActionResult> OnPostStep3Async()
        {
            var config = _configManager.Config;

            if (config.SetupStep >= 4)
                return RedirectToPage("/Login");

            if (config.SetupStep < 3)
                return Redirect(config.SetupStep < 2 ? "/setup" : "/setup?step=2");

            if (!IsValidFor("Step3."))
            {
                Step = 3;
                return Page();
            }

            config.PanelDomain = ConfigManager.NormalizeUrl(Step3.SiteUrl);

            config.SiteSettings = new SiteSettings
            {
                General = new GeneralSettings
                {
                    SiteName = config.SiteSettings?.General?.SiteName ?? "SharpPress",
                    SiteDescription = config.SiteSettings?.General?.SiteDescription ?? "",
                    AdminEmail = config.SiteSettings?.General?.AdminEmail ?? "",
                    Timezone = string.IsNullOrWhiteSpace(Step3.Timezone) ? "UTC" : Step3.Timezone,
                    FooterText = config.SiteSettings?.General?.FooterText ?? ""
                },
                Security = new SecuritySettings
                {
                    ForceHttps = Step3.ForceHttps,
                    AllowRegistration = Step3.AllowRegistration,
                    Require2FA = config.SiteSettings?.Security?.Require2FA ?? false,
                    SessionTimeout = config.SiteSettings?.Security?.SessionTimeout ?? 60
                },
                Advanced = config.SiteSettings?.Advanced ?? new AdvancedSettings()
            };

            config.SetupStep = 4;

            await _configManager.SaveConfig();
            _logger.Log("✅ Setup finished");

            return Redirect("/Admin");
        }

        private async Task<bool> AdminExistsAsync()
        {
            return await _database.Query<User>().Where(u => (int)u.Roles >= (int)UserRole.Admin).FirstOrDefaultAsync() != null;
        }

        private bool IsValidFor(string prefix)
        {
            foreach (var entry in ModelState)
                if (entry.Key.StartsWith(prefix) && entry.Value.ValidationState != ModelValidationState.Valid)
                    return false;

            return true;
        }

        public class DatabaseInput
        {
            [Required(ErrorMessage = "Host is required")]
            public string Host { get; set; } = "localhost";

            [Required]
            [Range(1, 65535)]
            public int Port { get; set; } = 3306;

            [Required(ErrorMessage = "Database name is required")]
            public string Database { get; set; } = "sharppress";

            [Required(ErrorMessage = "Username is required")]
            public string Username { get; set; } = "root";

            [DataType(DataType.Password)]
            public string? Password { get; set; }
        }

        public class AdminInput
        {
            [Required(ErrorMessage = "Site name is required")]
            [StringLength(100, MinimumLength = 2)]
            public string SiteName { get; set; } = "";

            [StringLength(300)]
            public string SiteDescription { get; set; } = "";

            [Required(ErrorMessage = "Username is required")]
            [StringLength(48, MinimumLength = 3)]
            public string Username { get; set; } = "";

            [Required(ErrorMessage = "Email is required")]
            [EmailAddress]
            public string Email { get; set; } = "";

            [Required(ErrorMessage = "Password is required")]
            [StringLength(100, MinimumLength = 8)]
            [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$",
                ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one digit and one special character")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";

            [Required(ErrorMessage = "Please confirm the password")]
            [DataType(DataType.Password)]
            public string ConfirmPassword { get; set; } = "";
        }

        public class SiteInput
        {
            [Required(ErrorMessage = "Site address is required")]
            public string SiteUrl { get; set; } = "";

            public string Timezone { get; set; } = "UTC";

            public bool AllowRegistration { get; set; }

            public bool ForceHttps { get; set; }
        }
    }
}
