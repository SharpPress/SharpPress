using System.ComponentModel.DataAnnotations;

namespace SharpPress.Models
{
    public class User : FeatherData
    {
        [StringLength(48)] public string UUID { get; set; } = Guid.NewGuid().ToString();
        [StringLength(96)] public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Email { get; set; } = "";
        public UserRole Roles { get; set; } = UserRole.User;
        public bool TwoFactorEnabled { get; set; } = false;
        public string TwoFactorSecret { get; set; } = "";
        public DateTime LastLogin { get; set; } = DateTime.UtcNow;
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockedUntil { get; set; }
        public string RefreshToken { get; set; } = "";
        public DateTime RefreshTokenExpiry { get; set; } = DateTime.UtcNow.AddDays(7);
        public string PasswordResetToken { get; set; }
        public DateTime PasswordResetTokenExpiry { get; set; }
        public bool IsActive { get; set; }
        public bool IsVerified { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActive { get; set; } = DateTime.UtcNow;

        public bool HasAnyRole(UserRole roles) => (Roles & roles) != 0;
        public bool HasAllRoles(UserRole roles) => (Roles & roles) == roles;
        public bool IsAtLeastUser() => HasAnyRole(UserRole.User | UserRole.Moderator | UserRole.Support | UserRole.Admin);
        public bool HasRole(UserRole role)
        {
            if ((Roles & UserRole.Banned) != 0)
                return false;

            if ((Roles & UserRole.Admin) != 0)
                return true;

            if (role == UserRole.Support)
                return (Roles & UserRole.Support) != 0 || (Roles & UserRole.Admin) != 0;

            if (role == UserRole.Moderator)
                return (Roles & UserRole.Moderator) != 0 || (Roles & UserRole.Support) != 0 || (Roles & UserRole.Admin) != 0;

            if (role == UserRole.User)
                return (Roles & UserRole.User) != 0;

            return (Roles & role) != 0;
        }
    }

    public class FeatherData
    {
        public int Id { get; set; }
    }

    [Flags]
    public enum UserRole
    {
        Banned = 0,
        User = 1 << 0,
        Moderator = 1 << 1,
        Support = 1 << 2,
        Admin = 1 << 4
    }

    public class LoginRequest
    {
        [Required]
        [StringLength(50, MinimumLength = 3)]
        public string Username { get; set; }

        [Required]
        [StringLength(100, MinimumLength = 6)]
        public string Password { get; set; }

        public bool RememberMe { get; set; } = false;
    }

    public class RegisterRequest
    {
        [Required]
        [StringLength(50, MinimumLength = 3)]
        public string Username { get; set; }

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$",
            ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one digit and one special character")]
        public string Password { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        public string Role { get; set; } = "player";
    }

    public class PasswordResetRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }
    }

    public class ChangePasswordRequest
    {
        [Required]
        public string Email { get; set; }
        [Required]
        public string Token { get; set; }
        [Required]
        [StringLength(100, MinimumLength = 8)]
        [RegularExpression(@"^(?=.[a-z])(?=.[A-Z])(?=.\d)(?=.[^\da-zA-Z]).{8,}$",
        ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one digit and one special character")]
        public string NewPassword { get; set; }
    }

    public class RefreshTokenRequest
    {
        [Required]
        public string RefreshToken { get; set; }
    }

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class SiteSettings
    {
        public GeneralSettings General { get; set; }
        public SecuritySettings Security { get; set; }
        public AdvancedSettings Advanced { get; set; }
    }

    public class GeneralSettings
    {
        public string SiteName { get; set; }
        public string SiteDescription { get; set; }
        public string AdminEmail { get; set; }
        public string Timezone { get; set; }
        public string FooterText { get; set; }
    }

    public class SecuritySettings
    {
        public bool ForceHttps { get; set; }
        public bool AllowRegistration { get; set; }
        public bool Require2FA { get; set; }
        public int SessionTimeout { get; set; }
    }

    public class AdvancedSettings
    {
        public bool EnableCache { get; set; }
        public bool MaintenanceMode { get; set; }
        public string CustomCss { get; set; }
    }
}
