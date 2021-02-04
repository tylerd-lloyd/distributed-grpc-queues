using Grpc.Core;

using GrpcCache.Service.Domain.Models;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GrpcCache.Service
{
    public class CacheService : Cache.CacheBase
    {
        private readonly ILogger<CacheService> _logger;
        private readonly IDistributedCache _distributedCache;
        private readonly IDictionary<string, int> _locks;

        public CacheService(ILogger<CacheService> logger, IDistributedCache distributedMemoryCache)
        {
            _logger = logger;
            _distributedCache = distributedMemoryCache;
        }

        public override async Task<LockResponse> RequestLock(LockRequest request, ServerCallContext context)
        {
            var o = await _distributedCache.GetAsync(request.ConversationId);

            if (o == null)
            {
                return new LockResponse { AcquiredLock = false, Reason = LockResponse.Types.Reason.NotFound };
            }

            var entry = JsonSerializer.Deserialize<CacheEntry>(o);

            if (entry.IsLocked)
            {
                return new LockResponse { AcquiredLock = false, Reason = LockResponse.Types.Reason.Locked };
            }

            if (!entry.Activities.Any())
            {
                return new LockResponse { AcquiredLock = false, Reason = LockResponse.Types.Reason.QueueEmpty };
            }

            entry.IsLocked = true; // TODO need to put a timer on the lock

            await _distributedCache.SetAsync(request.ConversationId, JsonSerializer.SerializeToUtf8Bytes(entry));

            _locks[request.ConversationId] = 564738;

            return new LockResponse { AcquiredLock = true, Reason = LockResponse.Types.Reason.Success, LockId = 564738 };
        }

        public override async Task<Ack> EnqueueMessages(IAsyncStreamReader<BotActivity> requestStream, ServerCallContext context)
        {
            Queue<BotActivity> q = new Queue<BotActivity>();
            CacheEntry cacheItem = new CacheEntry();
            string conversationId = null;
            await foreach (var item in requestStream.ReadAllAsync())
            {
                if (string.IsNullOrEmpty(conversationId))
                {
                    conversationId = item.ConversationId;

                    var data = await _distributedCache.GetAsync(conversationId);

                    if (data != null)
                    {
                        using var ms = new MemoryStream(data);
                        cacheItem = await JsonSerializer.DeserializeAsync<CacheEntry>(ms);
                        q = cacheItem.Activities;
                    }
                }

                q.Enqueue(item);
            }

            var opts = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
            };

            cacheItem.Activities = q;

            await _distributedCache.SetAsync(conversationId, JsonSerializer.SerializeToUtf8Bytes(cacheItem), opts);

            return new Ack { Ack_ = 1 };
        }

        public override async Task DequeueMessages(AuthorizedMessageRequest request, IServerStreamWriter<BotActivity> responseStream, ServerCallContext context)
        {
            if (!_locks.ContainsKey(request.ConversationId)
                || _locks[request.ConversationId] != request.LockId
                || (await _distributedCache.GetAsync(request.ConversationId) == null))
            {
                return;
            }

            var data = await _distributedCache.GetAsync(request.ConversationId);

            var entry = JsonSerializer.Deserialize<CacheEntry>(data);

            while (entry.Activities.Count > 0)
            {
                await responseStream.WriteAsync(entry.Activities.Dequeue());
            }

            entry.IsLocked = false;

            _locks.Remove(request.ConversationId);

            await _distributedCache.SetAsync(request.ConversationId, JsonSerializer.SerializeToUtf8Bytes(entry));
        }
    }
}
