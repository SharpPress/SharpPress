using SharpPress.Events;
using SharpPress.Models;
using SharpPress.Services.Database;
using System.Security.Claims;

namespace SharpPress.Services
{
    public class UserService : IUserService
    {
        private readonly IValidationService _validationService;
        private readonly IAuthenticationService _authService;
        private readonly FeatherDatabase _featherDatabase;
        private readonly ICacheService _cacheService;
        private readonly IEmailService _emailService;
        private readonly Logger _logger;
        private readonly IEventBus _eventBus;

        public UserService(
            Logger logger,
            IAuthenticationService authService,
            IValidationService validationService,
            ICacheService cacheService,
            IEventBus eventBus,
            IEmailService emailService,
            FeatherDatabase featherDatabase)
        {
            _logger = logger;
            _authService = authService;
            _validationService = validationService;
            _cacheService = cacheService;
            _eventBus = eventBus;
            _emailService = emailService;
            _featherDatabase = featherDatabase;
        }

        public async Task<User> GetUserAsync(ClaimsPrincipal User)
        {
            if (User == null || User.Identity.IsAuthenticated == false || string.IsNullOrWhiteSpace(User.Identity.Name))
                return null;

            string cacheKey = $"user:name:{User.Identity.Name}";
            if (_cacheService.Get<User>(cacheKey) is User cached)
                return cached;

            var _user = await _featherDatabase.GetByLinq<User>(u => u.Username == User.Identity.Name);
            if (_user == null)
                return null;

            _cacheService.Set(cacheKey, _user, TimeSpan.FromSeconds(30));
            return _user;
        }

        public void InvalidateUserCache(string username)
        {
            _cacheService.Remove($"user:name:{username}");
        }

        public async Task<(User user, string message)> CreateUserAsync(RegisterRequest request)
        {
            var (isValid, errors) = _validationService.ValidateModel(request);
            if (!isValid)
            {
                return (null, string.Join(", ", errors));
            }

            var registering = new UserRegisteringEvent(request);
            if (!await _eventBus.PublishCancellableAsync(registering))
            {
                return (null, registering.CancelReason ?? "Registration was cancelled.");
            }

            if (await _featherDatabase.GetByColumn<User>("Username", request.Username) != null)
            {
                return (null, "Username already exists");
            }

            if (await _featherDatabase.GetByColumn<User>("Email", request.Email) != null)
            {
                return (null, "Email already exists");
            }

            var newUser = new User
            {
                Username = request.Username,
                Password = _authService.HashPassword(request.Password),
                Email = request.Email,
                UUID = Guid.NewGuid().ToString(),
                Roles = UserRole.User,
                RefreshToken = _authService.GenerateRefreshToken(),
                RefreshTokenExpiry = DateTime.UtcNow.AddDays(7)
            };
            await _featherDatabase.SaveData(newUser);

            _logger.Log($"✅ User created: {request.Username}");
            await _eventBus.PublishAsync(new UserRegisteredEvent(newUser));

            return (newUser, "User created successfully");
        }

        public async Task<(User user, string message)> AuthenticateUserAsync(LoginRequest request)
        {
            var attempting = new LoginAttemptingEvent(request);
            if (!await _eventBus.PublishCancellableAsync(attempting))
            {
                return (null, attempting.CancelReason ?? "Login was cancelled.");
            }

            if (await _authService.IsAccountLocked(request.Username))
            {
                return (null, "Account is temporarily locked due to multiple failed login attempts");
            }

            var user = await _featherDatabase.GetByColumn<User>("Username", request.Username);

            if (user == null)
            {
                _authService.RecordFailedLoginAttempt(request.Username);
                await _eventBus.PublishAsync(new FailedLoginAttemptEvent(request.Username));
                return (null, "Invalid username or password");
            }

            if (_authService.VerifyPassword(request.Password, user.Password))
            {
                _authService.ResetFailedLoginAttempts(request.Username);

                user.LastLogin = DateTime.UtcNow;

                if (_authService.NeedsRehash(user.Password))
                    user.Password = _authService.HashPassword(request.Password);

                if (request.RememberMe || string.IsNullOrEmpty(user.RefreshToken) || user.RefreshTokenExpiry <= DateTime.UtcNow)
                {
                    user.RefreshToken = _authService.GenerateRefreshToken();
                    user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
                }

                await _featherDatabase.SaveData(user);
                _logger.Log($"✅ User authenticated: {user.Username}");
                await _eventBus.PublishAsync(new UserLoggedInEvent(user));
                return (user, "Authentication successful");
            }
            else
            {
                _authService.RecordFailedLoginAttempt(request.Username);
                await _eventBus.PublishAsync(new FailedLoginAttemptEvent(request.Username));
                return (null, "Invalid username or password");
            }
        }

        public async Task<(User user, string message)> RefreshTokenAsync(RefreshTokenRequest request)
        {
            var user = await _featherDatabase.GetByColumn<User>("RefreshToken", request.RefreshToken);

            if (user == null)
            {
                return (null, "Invalid refresh token");
            }

            if (user.RefreshTokenExpiry <= DateTime.UtcNow)
            {
                return (null, "Refresh token expired");
            }

            user.RefreshToken = _authService.GenerateRefreshToken();
            user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

            await _featherDatabase.SaveData(user);
            _logger.Log($"✅ Token refreshed for user: {user.Username}");
            return (user, "Token refreshed successfully");
        }

        public async Task<(bool success, string message)> ChangePasswordAsync(string username, string currentPassword, string newPassword)
        {
            var user = await _featherDatabase.GetByColumn<User>("Username", username);

            if (user == null)
            {
                return (false, "User not found");
            }

            if (!_authService.VerifyPassword(currentPassword, user.Password))
            {
                return (false, "Current password is incorrect");
            }

            user.Password = _authService.HashPassword(newPassword);
            await _featherDatabase.SaveData(user);
            InvalidateUserCache(username);

            _logger.Log($"✅ Password changed for user: {username}");
            await _eventBus.PublishAsync(new PasswordChangedEvent(user));
            return (true, "Password changed successfully");
        }

        public async Task<(bool success, string message)> ConfirmPasswordResetAsync(ChangePasswordRequest request)
        {
            var user = await _featherDatabase.GetByColumn<User>("Email", request.Email);

            if (user == null || !user.PasswordResetToken.Equals(request.Token, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Invalid email or reset token.");
            }

            if (!_authService.ValidatePasswordResetToken(user, request.Token))
            {
                return (false, "Invalid or expired reset token.");
            }

            user.Password = _authService.HashPassword(request.NewPassword);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiry = DateTime.MinValue;

            await _featherDatabase.SaveData(user);
            InvalidateUserCache(user.Username);
            _logger.Log($"✅ Password reset successfully for user: {user.Email}");
            await _eventBus.PublishAsync(new PasswordResetEvent(user));
            return (true, "Your password has been reset successfully. You can now log in.");
        }

        public async Task<(bool success, string message)> UpdateUserProfileAsync(string username, User updatedUser)
        {
            var user = await _featherDatabase.GetByColumn<User>("Username", username);

            if (user == null)
            {
                return (false, "User not found");
            }

            if (!string.IsNullOrEmpty(updatedUser.Email) && updatedUser.Email != user.Email)
            {
                var owner = await _featherDatabase.GetByColumn<User>("Email", updatedUser.Email);
                if (owner != null && owner.Id != user.Id)
                {
                    return (false, "Email already exists");
                }
                user.Email = updatedUser.Email;
            }

            await _featherDatabase.SaveData(user);
            InvalidateUserCache(username);
            _logger.Log($"✅ Profile updated for user: {username}");
            await _eventBus.PublishAsync(new UserProfileUpdatedEvent(user));
            return (true, "Profile updated successfully");
        }

        public async Task<(bool success, string message)> DeleteUserAsync(string username)
        {
            var user = await _featherDatabase.GetByColumn<User>("Username", username);

            if (user == null)
            {
                return (false, "User not found");
            }

            if (user.HasRole(UserRole.Admin))
            {
                return (false, "Cannot delete admin user");
            }
            await _featherDatabase.Delete<User>(user.Id);
            InvalidateUserCache(username);

            _logger.Log($"✅ User deleted: {username}");
            await _eventBus.PublishAsync(new UserDeletedEvent(user));
            return (true, "User deleted successfully");
        }

        public async Task<User> GetUserByUsername(string username)
        {
            return await _featherDatabase.GetByColumn<User>("Username", username);
        }

        public async Task<User> GetUserByEmail(string email)
        {
            return await _featherDatabase.GetByColumn<User>("Email", email);
        }

        public async Task<User> GetUserByUuid(Guid uuid)
        {
            return await _featherDatabase.GetByColumn<User>("uuid", uuid);
        }

        public async Task<(bool success, string message)> LogoutAsync(string username)
        {
            var user = await _featherDatabase.GetByColumn<User>("Username", username);

            if (user == null)
            {
                return (false, "User not found");
            }

            user.RefreshToken = "";
            user.RefreshTokenExpiry = DateTime.UtcNow;
            await _featherDatabase.SaveData(user);

            _logger.Log($"✅ User logged out: {username}");
            await _eventBus.PublishAsync(new UserLoggedOutEvent(user));
            return (true, "Logout successful");
        }

        public async Task<(bool success, string message, string secret)> EnableTwoFactorAsync(string username)
        {
            var user = await _featherDatabase.GetByColumn<User>("Username", username);

            if (user == null)
            {
                return (false, "User not found", "");
            }

            user.TwoFactorSecret = _authService.GenerateTwoFactorSecret();
            user.TwoFactorEnabled = true;
            await _featherDatabase.SaveData(user);

            _logger.Log($"✅ 2FA enabled for user: {username}");
            await _eventBus.PublishAsync(new TwoFactorEnabledEvent(user));
            return (true, "Two-factor authentication enabled", user.TwoFactorSecret);
        }

        public async Task<(bool success, string message)> DisableTwoFactorAsync(string username)
        {
            var user = await _featherDatabase.GetByColumn<User>("Username", username);

            if (user == null)
            {
                return (false, "User not found");
            }

            user.TwoFactorEnabled = false;
            user.TwoFactorSecret = "";
            await _featherDatabase.SaveData(user);

            _logger.Log($"✅ 2FA disabled for user: {username}");
            await _eventBus.PublishAsync(new TwoFactorDisabledEvent(user));
            return (true, "Two-factor authentication disabled");
        }

        public async Task<(bool success, string message)> VerifyTwoFactorAsync(string username, string code)
        {
            var user = await _featherDatabase.GetByColumn<User>("Username", username);

            if (user == null)
            {
                return (false, "User not found");
            }

            if (!user.TwoFactorEnabled)
            {
                return (false, "Two-factor authentication is not enabled");
            }

            if (!_authService.VerifyTotpCode(user.TwoFactorSecret, code))
            {
                return (false, "Invalid verification code");
            }

            _logger.Log($"✅ 2FA verified for user: {username}");
            return (true, "Two-factor authentication verified");
        }

        public async Task<List<User>> GetUsersByRole(UserRole role)
        {
            var users = await _featherDatabase.GetAll<User>();
            return users.Where(u => u.HasAnyRole(role)).ToList();
        }

        public async Task<(bool success, string message)> ChangeUserRoleAsync(string username, UserRole newRole)
        {
            var user = await _featherDatabase.GetByColumn<User>("Username", username);

            if (user == null)
            {
                return (false, "User not found");
            }

            if (user.HasRole(UserRole.Admin) && newRole != UserRole.Admin)
            {
                var admins = await _featherDatabase.GetListByLinq<User>(u => u.Roles == UserRole.Admin);
                if (admins.Count <= 1)
                {
                    return (false, "Cannot change role of the last admin user");
                }
            }

            var oldRole = user.Roles;
            user.Roles = newRole;
            await _featherDatabase.SaveData(user);
            InvalidateUserCache(username);

            _logger.Log($"✅ Role changed for user {username} to {newRole}");
            await _eventBus.PublishAsync(new UserRoleChangedEvent(user, oldRole, newRole));

            if (newRole == UserRole.Banned && oldRole != UserRole.Banned)
                await _eventBus.PublishAsync(new UserBannedEvent(user));
            else if (oldRole == UserRole.Banned && newRole != UserRole.Banned)
                await _eventBus.PublishAsync(new UserUnbannedEvent(user));

            return (true, "Role changed successfully");
        }

        public async Task<(bool success, string message)> LockUserAsync(string username, int lockDurationMinutes = 30)
        {
            var user = await _featherDatabase.GetByColumn<User>("Username", username);

            if (user == null)
            {
                return (false, "User not found");
            }

            user.LockedUntil = DateTime.UtcNow.AddMinutes(lockDurationMinutes);
            await _featherDatabase.SaveData(user);
            InvalidateUserCache(username);

            _logger.Log($"🔒 User locked: {username} until {user.LockedUntil}");
            await _eventBus.PublishAsync(new UserLockedEvent(user, lockDurationMinutes));
            return (true, $"User locked for {lockDurationMinutes} minutes");
        }

        public async Task<(bool success, string message)> UnlockUserAsync(string username)
        {
            var user = await _featherDatabase.GetByColumn<User>("Username", username);

            if (user == null)
            {
                return (false, "User not found");
            }

            user.LockedUntil = null;
            user.FailedLoginAttempts = 0;

            _authService.ResetFailedLoginAttempts(username);
            await _featherDatabase.SaveData(user);
            InvalidateUserCache(username);

            _logger.Log($"🔓 User unlocked: {username}");
            await _eventBus.PublishAsync(new UserUnlockedEvent(user));
            return (true, "User unlocked successfully");
        }
    }
}