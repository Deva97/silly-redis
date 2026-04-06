using System.Collections.Concurrent;

namespace SillyRedis.DataStructures
{
    public class RedisList
    {
        private readonly ConcurrentDictionary<string, CachedValue<object>> _registry;
        private readonly Func<string, object> _getKeyLock;

        public RedisList(ConcurrentDictionary<string, CachedValue<object>> registry, Func<string, object> getKeyLock)
        {
            _registry = registry;
            _getKeyLock = getKeyLock;
        }

        public int CreateOrAppend(string key, string[] values, int reverted)
        {
            if (reverted == 1) Array.Reverse(values);

            lock (_getKeyLock(key))
            {
                List<string> list;
                if (_registry.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList)
                {
                    list = existingList;
                }
                else
                {
                    list = [];
                    _registry[key] = new CachedValue<object>(list, DateTime.MaxValue);
                }
                list.AddRange(values);
                return list.Count;
            }
        }

        public string[] Range(string key, int start, int end)
        {
            lock (_getKeyLock(key))
            {
                if (_registry.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList)
                {
                    int count = existingList.Count;
                    if (start < 0) start = count + start;
                    if (end < 0) end = count + end;
                    start = Math.Max(0, start);
                    end = Math.Min(count - 1, end);
                    if (start > end) return [];
                    return [.. existingList.GetRange(start, end - start + 1)];
                }
            }
            return [];
        }

        public int Length(string key)
        {
            lock (_getKeyLock(key))
            {
                if (_registry.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList)
                {
                    return existingList.Count;
                }
                return 0;
            }
        }

        public string Pop(string key, int count = 1)
        {
            lock (_getKeyLock(key))
            {
                if (_registry.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList)
                {
                    int removeCount = Math.Min(count, existingList.Count);
                    var elements = existingList.GetRange(0, removeCount);
                    existingList.RemoveRange(0, removeCount);
                    return count == 1
                        ? RESProtocol.EncodeBulkString(elements[0])
                        : RESProtocol.EncodeArray([.. elements]);
                }
                return count == 1 ? RESProtocol.EncodeNullBulkString() : RESProtocol.EncodeArray([]);
            }
        }
    }
}
