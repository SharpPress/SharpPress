using SharpPress.Events;
using SharpPress.Models;
using SharpPress.Plugins;
using System.ComponentModel.DataAnnotations;

namespace SamplePlugin
{
    public class SampleSettings
    {
        [Display(Name = "Greeting")]
        public string Greeting { get; set; } = "Hello";

        [Display(Name = "Shout the greeting")]
        public bool Shout { get; set; }

        [Display(Name = "Self-test runs")]
        public int RunCount { get; set; }
    }

    public class VisitCounter : FeatherData
    {
        public string Path { get; set; } = "";
        public int Count { get; set; }
    }

    public class SampleTestEvent : BaseEvent
    {
        public string Message { get; }

        public SampleTestEvent(string message)
        {
            Message = message;
        }
    }

    public static class SampleState
    {
        public static IPluginContext? Context { get; set; }
        public static DateTime LoadedAt { get; set; } = DateTime.UtcNow;
        public static int MiddlewareHits;
        public static int HeartbeatCount;
        public static DateTime? LastHeartbeat { get; set; }
        public static int TestEvents;
        public static int PostEvents;
        public static int SettingsEvents;
        public static int LoginEvents;
        public static int EmailAccesses;
        public static int CacheReads;
        public static string LastPublishedTitle { get; set; } = "none yet";
    }
}
