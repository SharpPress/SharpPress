using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Plugins;
using SharpPress.Services;

namespace SharpPress.Pages
{
    public class PluginSettingsModel : PageModel
    {
        private readonly IUserService _userService;
        private readonly PluginSettingsStore _store;

        public PluginSettingsModel(IUserService userService, PluginSettingsStore store)
        {
            _userService = userService;
            _store = store;
        }

        public PluginSettingsSection? Section { get; private set; }
        public object? Values { get; private set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(string name)
        {
            if (!await IsAdminAsync())
                return RedirectToPage("/Login");

            return Load(name);
        }

        public async Task<IActionResult> OnPostAsync(string name)
        {
            if (!await IsAdminAsync())
                return RedirectToPage("/Login");

            var section = _store.FindSection(name);
            if (section == null)
                return NotFound();

            var values = _store.Get(name, section.SettingsType);

            foreach (var property in PluginSettingsStore.EditableProperties(section.SettingsType))
            {
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var raw = Request.Form[property.Name].ToString();

                try
                {
                    object? converted;
                    if (type == typeof(bool)) converted = Request.Form[property.Name].Count > 0 && !raw.Equals("false", StringComparison.OrdinalIgnoreCase);
                    else if (string.IsNullOrEmpty(raw) && type != typeof(string)) converted = Nullable.GetUnderlyingType(property.PropertyType) != null ? null : property.GetValue(values);
                    else if (type == typeof(string)) converted = raw;
                    else if (type.IsEnum) converted = Enum.Parse(type, raw);
                    else converted = Convert.ChangeType(raw, type, System.Globalization.CultureInfo.InvariantCulture);

                    property.SetValue(values, converted);
                }
                catch (Exception)
                {
                    StatusMessage = $"'{property.Name}' has an invalid value.";
                    return RedirectToPage(new { name });
                }
            }

            _store.Save(name, values);
            StatusMessage = "Settings saved.";
            return RedirectToPage(new { name });
        }

        private IActionResult Load(string name)
        {
            Section = _store.FindSection(name);
            if (Section == null)
                return NotFound();

            Values = _store.Get(name, Section.SettingsType);
            return Page();
        }

        private async Task<bool> IsAdminAsync()
        {
            var user = await _userService.GetUserAsync(User);
            return user != null && user.HasRole(UserRole.Admin);
        }
    }
}
