using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using SharpPress.Services;

namespace SharpPress.Models
{

    public class Version
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }
    }

    public class MySQL_Config
    {
        public string host { get; set; } = "localhost";
        public int port { get; set; } = 3306;
        public string database_name { get; set; } = "sharppress";
        public string database_username { get; set; } = "root";
        public string database_password { get; set; } = "";
    }





    public class PluginSecurityOptions
    {
        public string DefaultMode { get; set; } = "Deny";
        public List<ServicePolicyEntry> Policies { get; set; } = new();
    }

    public class ServicePolicyEntry
    {
        public string ServiceType { get; set; } = "";
        public int RequiredPermission { get; set; }
    }
    public interface IManualServer
    {
        Task Start();
        Task StopAsync();
    }

    public class MarketPlugin
    {
        public string Name { get; set; } = "plugin";
        public string Description { get; set; } = "description";
        public string DownloadLink { get; set; } = "";
        public bool isPackaged = false;
    }

    public class Data
    {
        public int protocolv { get; set; } = 1;
        public string command { get; set; } = "";
        public string jsond { get; set; } = "";
    }




    public class ServerConfig
    {
        public bool DebugMode { get; set; } = false;
        public string JwtSecret { get; set; } = "";
        public int SetupStep { get; set; } = 1;
        public SiteSettings SiteSettings { get; set; } = new SiteSettings();
        public string PanelDomain { get; set; } = "example.com";
        public int MaxFailedLoginAttempts { get; set; } = 5;
        public int LockoutDurationMinutes { get; set; } = 3;
        public int JwtExpiryHours { get; set; } = 24;
        public bool EnableCompression { get; set; } = true;
        public bool EnableStaticFileCache { get; set; } = true;
        public int CacheExpiryMinutes { get; set; } = 15;
        public string email_host { get; set; } = "smtp.gmail.com";
        public int email_port { get; set; } = 587;
        public string email_username { get; set; } = "your-smtp-email-username";
        public string email_password { get; set; } = "your-smtp-email-password";
        public bool email_useSsl { get; set; } = false;
        public MySQL_Config MySQL_Config { get; set; } = new MySQL_Config();
    }




    public class CreateSiteRequest
    {
        [StringLength(100, MinimumLength = 4)]
        [Required] public string Name { get; set; }

        [Length(50000, 1000)]
        [Required] public int Port { get; set; }
    }

    public class DeleteSiteRequest
    {
        [StringLength(100, MinimumLength = 4)]
        public string Name { get; set; }
    }





    /// <summary>
    /// Holds all file path and folder constants for the application.
    /// </summary>
    public class FilePaths
    {
        public string ConfigFile { get; set; } = "config.json";
        public string UsersFile { get; set; } = "users.json";
        public string VideosFolder { get; set; } = "videos";
        public string MediaFolder { get; set; } = "media";
        public string LogsFolder { get; set; } = "logs";
    }


    /// <summary>
    /// Holds all startup settings for the server, such as port numbers.
    /// </summary>
    public class ServerSettings
    {
        public int httpPort { get; set; }
        public int sftpPort { get; set; }
    }

    public interface IServerTemplate
    {
        [Required] public string Name { get; set; }
        [Required] public string Version { get; set; }
        [Required] public string ServerFilesDownloadLink { get; set; }
        [Required] [Length(1000, 60000)] public int[] AllowedPorts { get; set; }
        [Required] public int MaxRamMB { get; set; }
        [Required] public string ServerPath { get; set; }
        [Required] public Process Process { get; set; }


        Task<string> GetConsoleOutput();
        Task DownloadServerFiles();
        Task InstallServerFiles();
        Task RunServer();
        Task StopServer();
        Task UninstallServer();
    }

    /// <summary>
    /// Base exception for all MiniDB related errors.
    /// </summary>
    public class MiniDBException : Exception
    {
        public MiniDBException(string message) : base(message) { }
        public MiniDBException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown when a requested key is not found in the database.
    /// </summary>
    public class MiniDBKeyNotFoundException : MiniDBException
    {
        public MiniDBKeyNotFoundException(string key) : base($"The key '{key}' was not found in the database.") { }
    }

    /// <summary>
    /// Thrown when there is a type mismatch between the requested type and the stored object's type.
    /// </summary>
    public class MiniDBTypeMismatchException : MiniDBException
    {
        public MiniDBTypeMismatchException(string key, Type requestedType, Type actualType)
            : base($"Type mismatch for key '{key}'. Requested type: {requestedType.Name}, Actual type: {actualType.Name}.") { }
    }

    public class StopProcessRequest
    {
        public string ProcessName { get; set; }
    }
}
