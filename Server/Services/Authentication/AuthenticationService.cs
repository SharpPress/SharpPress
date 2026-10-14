using System;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using SharpPress.Models;
using SharpPress.Services;

namespace SharpPress.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private const string HashPrefix = "$PBKDF2-SHA256$";
        private const int Pbkdf2Iterations = 210000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        private readonly string _jwtSecret;
        private readonly SymmetricSecurityKey _jwtKey;
        private readonly ServerConfig _config;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, int> _failedLoginAttempts = new();
        private readonly ConcurrentDictionary<string, DateTime> _lockedAccounts = new();

        public AuthenticationService(ServerConfig config, Logger logger)
        {
            _jwtSecret = config.JwtSecret;
            _jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
            _config = config;
            _logger = logger;
        }

        public string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);

            return $"{HashPrefix}{Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public bool VerifyPassword(string inputPassword, string storedPassword)
        {
            try
            {
                if (storedPassword.StartsWith(HashPrefix, StringComparison.Ordinal))
                {
                    string[] parts = storedPassword.Substring(HashPrefix.Length).Split('$');
                    if (parts.Length != 3)
                        return false;

                    int iterations = int.Parse(parts[0]);
                    byte[] salt = Convert.FromBase64String(parts[1]);
                    byte[] expected = Convert.FromBase64String(parts[2]);

                    byte[] actual = Rfc2898DeriveBytes.Pbkdf2(inputPassword, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                    return CryptographicOperations.FixedTimeEquals(actual, expected);
                }

                byte[] hashBytes = Convert.FromBase64String(storedPassword);
                if (hashBytes.Length != 36)
                    return false;

                byte[] legacySalt = hashBytes[..16];
                byte[] legacyHash = hashBytes[16..];
                byte[] legacyActual = Rfc2898DeriveBytes.Pbkdf2(inputPassword, legacySalt, 10000, HashAlgorithmName.SHA1, 20);

                return CryptographicOperations.FixedTimeEquals(legacyActual, legacyHash);
            }
            catch
            {
                return false;
            }
        }

        public bool NeedsRehash(string storedPassword)
        {
            return !storedPassword.StartsWith(HashPrefix, StringComparison.Ordinal);
        }

        public string GenerateTwoFactorSecret()
        {
            return Base32Encode(RandomNumberGenerator.GetBytes(20));
        }

        public bool VerifyTotpCode(string base32Secret, string code)
        {
            if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code))
                return false;

            code = code.Replace(" ", "").Trim();
            if (code.Length != 6 || !code.All(char.IsDigit))
                return false;

            byte[] key = Base32Decode(base32Secret);
            if (key.Length == 0)
                return false;

            long step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            for (long drift = -1; drift <= 1; drift++)
            {
                if (ComputeTotp(key, step + drift) == code)
                    return true;
            }

            return false;
        }

        private static string ComputeTotp(byte[] key, long step)
        {
            byte[] counter = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(counter);

            using var hmac = new HMACSHA1(key);
            byte[] hash = hmac.ComputeHash(counter);

            int offset = hash[^1] & 0x0F;
            int binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];

            return (binary % 1000000).ToString("D6");
        }

        private static string Base32Encode(byte[] data)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var sb = new StringBuilder();

            for (int i = 0; i < data.Length; i += 5)
            {
                int b0 = data[i];
                int b1 = i + 1 < data.Length ? data[i + 1] : 0;
                int b2 = i + 2 < data.Length ? data[i + 2] : 0;
                int b3 = i + 3 < data.Length ? data[i + 3] : 0;
                int b4 = i + 4 < data.Length ? data[i + 4] : 0;

                sb.Append(alphabet[(b0 >> 3) & 31]);
                sb.Append(alphabet[((b0 & 7) << 2) | ((b1 >> 6) & 3)]);
                sb.Append(alphabet[(b1 >> 1) & 31]);
                sb.Append(alphabet[((b1 & 1) << 4) | ((b2 >> 4) & 15)]);
                sb.Append(alphabet[((b2 & 15) << 1) | ((b3 >> 7) & 1)]);
                sb.Append(alphabet[(b3 >> 2) & 31]);
                sb.Append(alphabet[((b3 & 3) << 3) | ((b4 >> 5) & 7)]);
                sb.Append(alphabet[b4 & 31]);
            }

            return sb.ToString();
        }

        private static byte[] Base32Decode(string input)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            string clean = input.Replace("=", "").Replace(" ", "").ToUpperInvariant();

            var output = new List<byte>();
            int buffer = 0, bits = 0;

            foreach (char c in clean)
            {
                int value = alphabet.IndexOf(c);
                if (value < 0)
                    return Array.Empty<byte>();

                buffer = (buffer << 5) | value;
                bits += 5;

                if (bits >= 8)
                {
                    bits -= 8;
                    output.Add((byte)((buffer >> bits) & 0xFF));
                }
            }

            return output.ToArray();
        }

        public string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Roles.ToString()),
                    new Claim("UUID", user.UUID),
                    new Claim("Email", user.Email)
                }),
                Expires = DateTime.UtcNow.AddHours(_config.JwtExpiryHours),
                SigningCredentials = new SigningCredentials(_jwtKey, SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        public CookieOptions CreateAuthCookieOptions()
        {
            return new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddHours(_config.JwtExpiryHours),
                IsEssential = true
            };
        }

        public string? TryRenewTokenIfExpiringSoon(string currentToken, User user, int minutesBeforeExpiry = 60)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(currentToken)) return null;

                var jwtToken = handler.ReadJwtToken(currentToken);
                var expClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Exp);

                if (expClaim == null) return null;

                var expTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expClaim.Value));
                var now = DateTimeOffset.UtcNow;

                if (expTime - now <= TimeSpan.FromMinutes(minutesBeforeExpiry))
                {
                    return GenerateJwtToken(user);
                }
            }
            catch { }

            return null;
        }

        public ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _jwtKey,
                ValidateLifetime = false
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            SecurityToken securityToken;
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out securityToken);
            var jwtSecurityToken = securityToken as JwtSecurityToken;

            if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                throw new SecurityTokenException("Invalid token");
            }

            return principal;
        }

        public bool ValidateJwtToken(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _jwtKey,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public string GetRoleFromToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _jwtKey,
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            }, out _);

            return principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        }


        public async Task<bool> IsAccountLocked(string username)
        {
            if (_lockedAccounts.TryGetValue(username, out var lockTime))
            {
                if (lockTime > DateTime.UtcNow)
                {
                    return true;
                }
                else
                {
                    _lockedAccounts.TryRemove(username, out _);
                    _failedLoginAttempts.TryRemove(username, out _);
                }
            }
            return false;
        }

        public void RecordFailedLoginAttempt(string username)
        {
            var attempts = _failedLoginAttempts.AddOrUpdate(username, 1, (key, value) => value + 1);

            if (attempts >= _config.MaxFailedLoginAttempts)
            {
                var lockTime = DateTime.UtcNow.AddMinutes(_config.LockoutDurationMinutes);
                _lockedAccounts.AddOrUpdate(username, lockTime, (key, value) => lockTime);
                _logger.Log($"🔒 Account {username} locked until {lockTime}");
            }
        }

        public void ResetFailedLoginAttempts(string username)
        {
            _failedLoginAttempts.TryRemove(username, out _);
            _lockedAccounts.TryRemove(username, out _);
        }

        public string GeneratePasswordResetToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        public bool ValidatePasswordResetToken(User user, string token)
        {
            return user != null &&
                   !string.IsNullOrEmpty(token) &&
                   user.PasswordResetToken == token &&
                   user.PasswordResetTokenExpiry > DateTime.UtcNow;
        }

        public string GenerateResetToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }
    }
}