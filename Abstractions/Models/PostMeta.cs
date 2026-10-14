using System.ComponentModel.DataAnnotations;

namespace SharpPress.Models
{
    public class PostMeta : FeatherData
    {
        public int PostId { get; set; }
        [StringLength(128)] public string MetaKey { get; set; } = "";
        public string MetaValue { get; set; } = "";
    }
}
