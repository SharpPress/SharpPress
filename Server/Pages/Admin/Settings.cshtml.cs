using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Events;
using SharpPress.Models;
using SharpPress.Services;

namespace SharpPress.Pages
{
    public class SettingsModel : PageModel
    {
        private readonly ConfigManager _configManager;
        private readonly IUserService _userService;
        private readonly IEventBus _eventBus;

        public SettingsModel(ConfigManager configManager, IUserService userService, IEventBus eventBus)
        {
            _configManager = configManager;
            _userService = userService;
            _eventBus = eventBus;
        }

        [BindProperty]
        public SettingsInput Input { get; set; } = new SettingsInput();

        public string ActiveTab { get; set; } = "general";

        public async Task<IActionResult> OnGet()
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            var s = _configManager.Config.SiteSettings;

            Input.SiteName = s?.General?.SiteName ?? "";
            Input.SiteDescription = s?.General?.SiteDescription ?? "";
            Input.AdminEmail = s?.General?.AdminEmail ?? "";
            Input.Timezone = s?.General?.Timezone ?? "UTC";
            Input.FooterText = s?.General?.FooterText ?? "";
            Input.SiteUrl = _configManager.Config.PanelDomain;
            Input.ForceHttps = s?.Security?.ForceHttps ?? false;
            Input.AllowRegistration = s?.Security?.AllowRegistration ?? false;
            Input.Require2FA = s?.Security?.Require2FA ?? false;
            Input.SessionTimeout = s?.Security?.SessionTimeout ?? 60;
            Input.EnableCache = s?.Advanced?.EnableCache ?? true;
            Input.MaintenanceMode = s?.Advanced?.MaintenanceMode ?? false;
            Input.CustomCss = s?.Advanced?.CustomCss ?? "";

            return Page();
        }

        public async Task<IActionResult> OnPost(string activeTab = "general")
        {
            var user = await _userService.GetUserAsync(User);
            if (user == null || !user.HasRole(UserRole.Admin))
                return RedirectToPage("/Login");

            ActiveTab = activeTab;
            ModelState.Clear();

            var newSettings = new SiteSettings
            {
                General = new GeneralSettings
                {
                    SiteName = Input.SiteName ?? "",
                    SiteDescription = Input.SiteDescription ?? "",
                    AdminEmail = Input.AdminEmail ?? "",
                    Timezone = Input.Timezone ?? "UTC",
                    FooterText = Input.FooterText ?? ""
                },
                Security = new SecuritySettings
                {
                    ForceHttps = Input.ForceHttps,
                    AllowRegistration = Input.AllowRegistration,
                    Require2FA = Input.Require2FA,
                    SessionTimeout = Input.SessionTimeout
                },
                Advanced = new AdvancedSettings
                {
                    EnableCache = Input.EnableCache,
                    MaintenanceMode = Input.MaintenanceMode,
                    CustomCss = Input.CustomCss ?? ""
                }
            };

            var saving = new SettingsSavingEvent(newSettings);
            if (!await _eventBus.PublishCancellableAsync(saving))
            {
                TempData["ErrorMessage"] = saving.CancelReason ?? "Saving the settings was cancelled by a plugin.";
                return RedirectToPage("/Admin/Settings", new { tab = activeTab });
            }

            _configManager.Config.SiteSettings = newSettings;
            _configManager.Config.PanelDomain = ConfigManager.NormalizeUrl(Input.SiteUrl);

            await _configManager.SaveConfig();
            await _eventBus.PublishAsync(new SettingsUpdatedEvent(newSettings, user.Username));
            TempData["SuccessMessage"] = "Settings updated successfully.";

            return RedirectToPage("/Admin/Settings", new { tab = activeTab });
        }

        public class SettingsInput
        {
            public string SiteName { get; set; }
            public string SiteDescription { get; set; }
            public string SiteUrl { get; set; }
            public string AdminEmail { get; set; }
            public string Timezone { get; set; }
            public string FooterText { get; set; }
            public bool ForceHttps { get; set; }
            public bool AllowRegistration { get; set; }
            public bool Require2FA { get; set; }
            public int SessionTimeout { get; set; }
            public bool EnableCache { get; set; }
            public bool MaintenanceMode { get; set; }
            public string CustomCss { get; set; }
        }
    }
}