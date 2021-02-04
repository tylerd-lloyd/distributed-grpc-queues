using System.Collections.Generic;

namespace GrpcCache.Service.Domain.Models
{
    public class CacheEntry
    {
        public bool IsLocked { get; set; }

        public Queue<BotActivity> Activities { get; set; }
    }
}
