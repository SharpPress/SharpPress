using Newtonsoft.Json;
using SharpPress.Models;
using SharpPress.Services.General;
using System.Security.Cryptography;

namespace SharpPress.Services
{
    public class ConfigManager
    {
        private const string LegacyJwtSecret = "your-super-secret-jwt-key-change-this-in-production-32-chars-min";

        private readonly string _configFile;
        private readonly Logger _logger;

        public ServerConfig Config { get; private set; }

        public ConfigManager(string configFile = "config.json", Logger logger = null)
        {
            _configFile = configFile;
            _logger = logger ?? new Logger();
            LoadConfig();

            Endpoints.SetCacheEnabled(Config.EnableStaticFileCache);
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    string json = File.ReadAllText(_configFile);
                    Config = JsonConvert.DeserializeObject<ServerConfig>(json) ?? new ServerConfig();
                }
                else
                {
                    Config = new ServerConfig();
                }

                if (string.IsNullOrWhiteSpace(Config.JwtSecret) || Config.JwtSecret == LegacyJwtSecret)
                {
                    Config.JwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
                    File.WriteAllText(_configFile, JsonConvert.SerializeObject(Config, Formatting.Indented));
                    _logger.Log("🔐 Generated a new JwtSecret, everyone will need to sign in again");
                }
                else
                {
                    _logger.Log("✅ Config loaded.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading config: {ex.Message}");
                Config = new ServerConfig();
            }
        }

        public async Task SaveConfig()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                await File.WriteAllTextAsync(_configFile, json);
                _logger.Log("✅ Config saved.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving config: {ex.Message}");
            }
        }
    }
}