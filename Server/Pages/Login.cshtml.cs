using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpPress.Models;
using SharpPress.Services;
using SharpPress.Services.Database;
using System.ComponentModel.DataAnnotations;

namespace SharpPress.Pages
{
    public class LoginModel : PageModel
    {
        [BindProperty]
        public InputModel Input { get; set; }

        private readonly Logger _logger;
        private readonly FeatherDatabase _database;
        private readonly IAuthenticationService _authService;

        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }
        public bool ShowTwoFactor { get; set; }

        public LoginModel(FeatherDatabase database, IAuthenticationService authService, Logger logger)
        {
            _database = database;
            _authService = authService;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var isLoggedIn = await _database.GetByColumn<User>("Username", User.Identity?.Name);
            if (isLoggedIn != null)
                return Redirect("/");

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string return_url = "")
        {
            if (!ModelState.IsValid)
                return Page();

            try
            {
                if (await _authService.IsAccountLocked(Input.Email))
                {
                    ErrorMessage = "حساب شما به دلیل تلاش‌های ناموفق زیاد موقتاً قفل شده است.";
                    return Page();
                }

                var user = await _database.GetByColumn<User>("Email", Input.Email);
                if (user == null || !_authService.VerifyPassword(Input.Password, user.Password))
                {
                    _authService.RecordFailedLoginAttempt(Input.Email);
                    ErrorMessage = "ایمیل یا رمز عبور اشتباه است.";
                    return Page();
                }

                if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
                {
                    ErrorMessage = "حساب شما توسط مدیر قفل شده است.";
                    return Page();
                }

                if (user.TwoFactorEnabled)
                {
                    if (string.IsNullOrWhiteSpace(Input.TwoFactorCode))
                    {
                        ShowTwoFactor = true;
                        return Page();
                    }

                    if (!_authService.VerifyTotpCode(user.TwoFactorSecret, Input.TwoFactorCode))
                    {
                        _authService.RecordFailedLoginAttempt(Input.Email);
                        ShowTwoFactor = true;
                        ErrorMessage = "کد دو مرحله‌ای نامعتبر است.";
                        return Page();
                    }
                }

                _authService.ResetFailedLoginAttempts(Input.Email);

                user.LastLogin = DateTime.UtcNow;
                if (_authService.NeedsRehash(user.Password))
                    user.Password = _authService.HashPassword(Input.Password);

                await _database.SaveData(user);

                string token = _authService.GenerateJwtToken(user);
                Response.Cookies.Append("X-Access-Token", token, _authService.CreateAuthCookieOptions());

                if (user.HasRole(UserRole.Admin))
                    return Redirect(string.IsNullOrWhiteSpace(return_url) ? "/admin" : return_url);

                return Redirect(string.IsNullOrWhiteSpace(return_url) ? "/" : return_url);
            }
            catch (Exception ex)
            {
                ErrorMessage = "خطای سرور";
                _logger.LogError($"OnPostLogin Error: {ex.Message}");
                return Page();
            }
        }

        public class InputModel
        {
            [Required(ErrorMessage = "ایمیل الزامی است")]
            [EmailAddress(ErrorMessage = "ایمیل معتبر نیست")]
            public string Email { get; set; } = "";

            [Required(ErrorMessage = "رمز عبور الزامی است")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";

            public string TwoFactorCode { get; set; } = "";

            public bool RememberMe { get; set; }
        }
    }
}