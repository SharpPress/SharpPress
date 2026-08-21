using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;
using SharpPress.Services.Database;
using SharpPress.Middlewares;
using System.ComponentModel.DataAnnotations;

namespace SharpPress.Pages
{
    public class SetupModel : PageModel
    {
        [BindProperty]
        public InputModel Input { get; set; } = new();

        private readonly Logger _logger;
        private readonly FeatherDatabase _database;
        private readonly AuthenticationService _authService;
        private readonly ConfigManager _configManager;
        private readonly SetupGate _setupGate;

        public string? ErrorMessage { get; set; }

        public SetupModel(FeatherDatabase database, AuthenticationService authService, ConfigManager configManager, SetupGate setupGate, Logger logger)
        {
            _database = database;
            _authService = authService;
            _configManager = configManager;
            _setupGate = setupGate;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (await AdminExistsAsync())
                return RedirectToPage("/Login");

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (await AdminExistsAsync())
                return RedirectToPage("/Login");

            if (!ModelState.IsValid)
                return Page();

            try
            {
                if (Input.Password != Input.ConfirmPassword)
                {
                    ErrorMessage = "Passwords do not match.";
                    return Page();
                }

                if (await _database.GetByColumn<User>("Username", Input.Username) != null)
                {
                    ErrorMessage = "That username is already taken.";
                    return Page();
                }

                if (await _database.GetByColumn<User>("Email", Input.Email) != null)
                {
                    ErrorMessage = "That email is already in use.";
                    return Page();
                }

                var admin = new User
                {
                    Username = Input.Username,
                    Email = Input.Email,
                    Password = _authService.HashPassword(Input.Password),
                    UUID = Guid.NewGuid().ToString(),
                    Roles = UserRole.Admin,
                    IsActive = true,
                    IsVerified = true
                };
                await _database.SaveData(admin);

                _configManager.Config.SiteSettings = new SiteSettings
                {
                    General = new GeneralSettings
                    {
                        SiteName = Input.SiteName,
                        SiteDescription = Input.SiteDescription,
                        AdminEmail = Input.Email,
                        Timezone = "UTC",
                        FooterText = ""
                    },
                    Security = _configManager.Config.SiteSettings?.Security ?? new SecuritySettings(),
                    Advanced = _configManager.Config.SiteSettings?.Advanced ?? new AdvancedSettings()
                };
                await _configManager.SaveConfig();

                _logger.Log($"✅ Setup completed, admin account created: {Input.Username}");
                _setupGate.MarkCompleted();

                string token = _authService.GenerateJwtToken(admin);
                Response.Cookies.Append("X-Access-Token", token, _authService.CreateAuthCookieOptions());

                return Redirect("/Admin");
            }
            catch (Exception ex)
            {
                ErrorMessage = "Something went wrong while setting up.";
                _logger.LogError($"Setup Error: {ex.Message}");
                return Page();
            }
        }

        private async Task<bool> AdminExistsAsync()
        {
            return await _database.Query<User>().Where(u => (int)u.Roles >= (int)UserRole.Admin).FirstOrDefaultAsync() != null;
        }

        public class InputModel
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
    }
}
