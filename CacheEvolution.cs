namespace CacheExp;

public class WeatherService {
    public static string GetWeather(string location) {
        Thread.Sleep(250);
        // Simulate fetching weather data from a cache or an external service
        return $"Weather data for {location}: Sunny, 25°C";
    }
    public static async Task<string> GetWeatherAsync(string location) {
        await Task.Delay(250);
        // Simulate fetching weather data from a cache or an external service
        return $"Weather data for {location}: Sunny, 25°C";
    }
}
public class NowService {
    public static DateTime Now() => DateTime.UtcNow;
}

// Step 1: Here's where we start, we have a method called GetWeather that simulates fetching weather data for a given location.
// The method sleeps for 250 milliseconds to simulate a delay in fetching the data, and then returns a string with the weather
// information. We need to implement caching to speed things up and to save on costs.
public class CacheEvolution1 {
    public string GetWeatherCached(string location) {
        // do something to cache the weather data for the location
        return WeatherService.GetWeather(location);
    }
}

// Step 2 - let's just do the simplest thing that could possibly work. We'll add a dictionary to store the cached weather data.
// We're smart, so we know that the weather in "LONDON" is the same as "london", so we'll use a case-insensitive comparer for the dictionary keys.
public class CacheEvolution2 {
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    public string GetWeatherCached(string location) {
        return _cache.GetValueOrDefault(location) ?? (_cache[location] = WeatherService.GetWeather(location));
    }
}

// Step 3 - but we should build a cache class that can be reused for other things, so let's build a simple cache class that
// uses generic types for the key and value, and allows us to specify a key comparer. And we should build it to an interface.
public class CacheEvolution3 {
    private readonly SimpleCache<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    public string GetWeatherCached(string location) {
        return _cache.Fetch(location, WeatherService.GetWeather);
    }
}
public interface ISimpleCache<TKey, TValue> {
    TValue Fetch(TKey key, Func<TKey, TValue> valueFactory);
}
public class SimpleCache<TKey, TValue>(IEqualityComparer<TKey> comparer) : ISimpleCache<TKey, TValue> {
    private readonly Dictionary<TKey, TValue> _cache = new(comparer);
    public TValue Fetch(TKey key, Func<TKey, TValue> valueFactory) {
        // Note: we cannot use are GetValueOrDefault with ?? trick here because the user of the cache might want to store null values.
        // or they might use a non-nullable value type.
        //return _cache.GetValueOrDefault(key) ?? (_cache[key] = valueFactory(key));
        if (!_cache.TryGetValue(key, out var value)) _cache[key] = value = valueFactory(key);
        return value;
    }
}

// Step 4 - items in the cache should expire after a certain amount of time, so let's add an expiration time to the cache class.
public class CacheEvolution4 {
    private readonly CacheWithExpiration<string, string> _cache = new(StringComparer.OrdinalIgnoreCase, NowService.Now);
    public string GetWeatherCached(string location) {
        return _cache.Fetch(location, WeatherService.GetWeather, TimeSpan.FromMinutes(5));
    }
}
public interface ICacheWithExpiration<TKey, TValue> {
    TValue Fetch(TKey key, Func<TKey, TValue> valueFactory, TimeSpan timeToLive);
}
public class CacheWithExpiration<TKey, TValue>(IEqualityComparer<TKey> comparer, Func<DateTime> nowProvider) : ICacheWithExpiration<TKey, TValue> {
    class Entry {
        public TValue value;
        public DateTime expiration; // note we store the expiration time rather than we it was created. This makes it cheaper to check if the entry is expired.
    }
    private readonly Dictionary<TKey, Entry> _cache = new(comparer);
    public TValue Fetch(TKey key, Func<TKey, TValue> valueFactory, TimeSpan timeToLive) {
        if (!_cache.TryGetValue(key, out var entry)) _cache[key] = entry = new Entry();
        if (entry.expiration <= nowProvider()) { // a new entry will have a default expiration of DateTime.MinValue, so it will always be expired.
            entry.value = valueFactory(key);
            entry.expiration = nowProvider() + timeToLive;
        }
        return entry.value;
    }
}

// Step 5 - we need to be able to limit the number of items in the cache, so let's add a maximum size. When the cache becomes full, we'll evict the
// least recently used item from the cache.
public class CacheEvolution5 {
    private readonly LruCache<string, string> _cache = new(StringComparer.OrdinalIgnoreCase, NowService.Now, 1000);
    public string GetWeatherCached(string location) {
        return _cache.Fetch(location, WeatherService.GetWeather, TimeSpan.FromMinutes(5));
    }
}
public class LruCache<TKey, TValue>(IEqualityComparer<TKey> comparer, Func<DateTime> nowProvider, int maxSize) : ICacheWithExpiration<TKey, TValue> {
    class Entry {
        public TValue value;
        public DateTime expiration;
    }
    private readonly OrderedDictionary<TKey, Entry> _cache = new(comparer);
    public TValue Fetch(TKey key, Func<TKey, TValue> valueFactory, TimeSpan timeToLive) {
        if (!_cache.TryGetValue(key, out var entry)) {
            _cache[key] = entry = new Entry(); // new entry is added to the end of the list
            if (_cache.Count > maxSize) _cache.RemoveAt(0); // remove the least recently used item
        } else {
            _cache.Remove(key); // remove the item from the cache so we can add it back to the end of the list
            _cache[key] = entry;
        }
        if (entry.expiration <= nowProvider()) {
            entry.value = valueFactory(key);
            entry.expiration = nowProvider() + timeToLive;
        }
        return entry.value;
    }
}

// Step 6 - we see that the cache has bad performance when promoting items and evicting items. This is because we are using an OrderedDictionary
// which is implemented as a dictionary and a list. The list has O(n) performance for removing items, so instead, we should use a dictionary and
// a linked list which will have O(1) performance for removing items.
public class CacheEvolution6 {
    private readonly LruCache2<string, string> _cache = new(StringComparer.OrdinalIgnoreCase, NowService.Now, 1000);
    public string GetWeatherCached(string location) {
        return _cache.Fetch(location, WeatherService.GetWeather, TimeSpan.FromMinutes(5));
    }
}
public class LruCache2<TKey, TValue>(IEqualityComparer<TKey> comparer, Func<DateTime> nowProvider, int maxSize) : ICacheWithExpiration<TKey, TValue> {
    class Entry(TKey key) {
        public readonly TKey key = key; // note that we need to store the key so we know what item to remove from the dictionary when we evict an item from the cache.
        public TValue value;
        public DateTime expiration;
    }
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _cache = new(comparer);
    private readonly LinkedList<Entry> _lru = new();
    public TValue Fetch(TKey key, Func<TKey, TValue> valueFactory, TimeSpan timeToLive) {
        if (!_cache.TryGetValue(key, out var node)) {
            _cache[key] = node = new LinkedListNode<Entry>(new Entry(key));
            _lru.AddFirst(node); // new entry is added to the head of the list
            if (_cache.Count > maxSize) {
                _cache.Remove(_lru.Last.Value.key); // remove the least recently used item from the dictionary
                _lru.RemoveLast(); // remove the least recently used item from the linked list
            }
        } else {
            _lru.Remove(node);
            _lru.AddFirst(node); // move the item to the head of the list
        }
        Entry entry = node.Value;
        if (entry.expiration <= nowProvider()) {
            entry.value = valueFactory(key);
            entry.expiration = nowProvider() + timeToLive;
        }
        return entry.value;
    }
}

// Step 7 - our implementation is not thread safe. We could just lock wrap the entire method, but that would be terribly inefficient. Instead,
// we should only lock wrap the code that modifies the dictionary and linked list, but not the call to the value factory.
public class CacheEvolution7 {
    private readonly LruCache3<string, string> _cache = new(StringComparer.OrdinalIgnoreCase, NowService.Now, 1000);
    public string GetWeatherCached(string location) {
        return _cache.Fetch(location, WeatherService.GetWeather, TimeSpan.FromMinutes(5));
    }
}
public class LruCache3<TKey, TValue>(IEqualityComparer<TKey> comparer, Func<DateTime> nowProvider, int maxSize) : ICacheWithExpiration<TKey, TValue> {
    class Entry(TKey key) {
        public readonly TKey key = key;
        public TValue value;
        public DateTime expiration;
    }
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _cache = new(comparer);
    private readonly LinkedList<Entry> _lru = new();
    public TValue Fetch(TKey key, Func<TKey, TValue> valueFactory, TimeSpan timeToLive) {
        Entry entry;
        lock (_cache) {
            if (!_cache.TryGetValue(key, out var node)) {
                _cache[key] = node = new LinkedListNode<Entry>(new Entry(key));
                _lru.AddFirst(node);
                if (_cache.Count > maxSize) {
                    _cache.Remove(_lru.Last.Value.key);
                    _lru.RemoveLast();
                }
            } else {
                _lru.Remove(node);
                _lru.AddFirst(node);
            }
            entry = node.Value;
        }
        if (entry.expiration <= nowProvider()) {
            entry.value = valueFactory(key);
            entry.expiration = nowProvider() + timeToLive;
        }
        return entry.value;
    }
}

// Step 8 - this is good, but we have a problem when multiple threads are trying to fetch the same key at the same time.
// They will all call the value factory, which is not what we want. We want only one thread to call the value factory,
// and the other threads to wait for the result. We can do this by using a per-key lock before calling the valueFactory.
public class CacheEvolution8 {
    private readonly LruCache4<string, string> _cache = new(StringComparer.OrdinalIgnoreCase, NowService.Now, 1000);
    public string GetWeatherCached(string location) {
        return _cache.Fetch(location, WeatherService.GetWeather, TimeSpan.FromMinutes(5));
    }
}
public class LruCache4<TKey, TValue>(IEqualityComparer<TKey> comparer, Func<DateTime> nowProvider, int maxSize) : ICacheWithExpiration<TKey, TValue> {
    class Entry(TKey key) {
        public readonly TKey key = key;
        public TValue value;
        public DateTime expiration;
    }
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _cache = new(comparer);
    private readonly LinkedList<Entry> _lru = new();
    public TValue Fetch(TKey key, Func<TKey, TValue> valueFactory, TimeSpan timeToLive) {
        Entry entry;
        lock (_cache) {
            if (!_cache.TryGetValue(key, out var node)) {
                _cache[key] = node = new LinkedListNode<Entry>(new Entry(key));
                _lru.AddFirst(node);
                if (_cache.Count > maxSize) {
                    _cache.Remove(_lru.Last.Value.key);
                    _lru.RemoveLast();
                }
            } else {
                _lru.Remove(node);
                _lru.AddFirst(node);
            }
            entry = node.Value;
        }
        lock (entry) { // lock on the entry so that only one thread can call the valueFactory for this key at a time.
            if (entry.expiration <= nowProvider()) {
                entry.value = valueFactory(key);
                entry.expiration = nowProvider() + timeToLive;
            }
            return entry.value;
        }
    }
}

// Step 9 - let's add support for async value factories and an Evict and Clear method.
public class CacheEvolution9 {
    private readonly LruCache5<string, string> _cache = new(StringComparer.OrdinalIgnoreCase, NowService.Now, 1000);
    public string GetWeatherCached(string location) {
        return _cache.Fetch(location, WeatherService.GetWeather, TimeSpan.FromMinutes(5));
    }
    public async Task<string> GetWeatherCachedAsync(string location) {
        return await _cache.FetchAsync(location, WeatherService.GetWeatherAsync, TimeSpan.FromMinutes(5));
    }
}
public interface ILruCache<TKey, TValue> {
    TValue Fetch(TKey key, Func<TKey, TValue> valueFactory, TimeSpan timeToLive);
    Task<TValue> FetchAsync(TKey key, Func<TKey, Task<TValue>> valueFactory, TimeSpan timeToLive);
    void Evict(TKey key);
    void Clear();
}
public class LruCache5<TKey, TValue>(IEqualityComparer<TKey> comparer, Func<DateTime> nowProvider, int maxSize) : ILruCache<TKey, TValue> {
    class Entry(TKey key) {
        public readonly TKey key = key;
        public TValue value;
        public DateTime expiration;
        public readonly SemaphoreSlim semaphore = new(1); // semaphore to allow only one thread to call the valueFactory for this key at a time.
        // Note: we use a semaphore rather than a lock so that we can use async/await with the FetchAsync method. We cannot use a lock with async/await
        // because the thread that holds the lock might not be the same thread that releases the lock, which would cause a deadlock.
    }
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _cache = new(comparer);
    private readonly LinkedList<Entry> _lru = new();
    // let's make the _cache locked part common between sync/async.
    private bool EntryValue(TKey key, out Entry entry) {
        lock (_cache) {
            if (!_cache.TryGetValue(key, out var node)) {
                _cache[key] = node = new LinkedListNode<Entry>(new Entry(key));
                _lru.AddFirst(node);
                if (_cache.Count > maxSize) {
                    _cache.Remove(_lru.Last.Value.key);
                    _lru.RemoveLast();
                }
            } else {
                _lru.Remove(node);
                _lru.AddFirst(node);
            }
            entry = node.Value;
            return entry.expiration > nowProvider();
        }
    }
    public TValue Fetch(TKey key, Func<TKey, TValue> valueFactory, TimeSpan timeToLive) {
        if (EntryValue(key, out Entry entry)) return entry.value;
        entry.semaphore.Wait(); // wait for the semaphore to be released before calling the valueFactory
        try {
            if (entry.expiration > nowProvider()) return entry.value; // check again in case another thread has already updated the value
            TValue val = entry.value = valueFactory(key);
            entry.expiration = nowProvider() + timeToLive;
            return val;
        }
        finally {
            entry.semaphore.Release();
        }
    }

    public async Task<TValue> FetchAsync(TKey key, Func<TKey, Task<TValue>> valueFactory, TimeSpan timeToLive) {
        if (EntryValue(key, out Entry entry)) return entry.value;
        await entry.semaphore.WaitAsync(); // wait for the semaphore to be released before calling the valueFactory
        try {
            if (entry.expiration > nowProvider()) return entry.value; // check again in case another thread has already updated the value
            TValue val = entry.value = await valueFactory(key);
            entry.expiration = nowProvider() + timeToLive;
            return val;
        } finally {
            entry.semaphore.Release();
        }
    }

    public void Evict(TKey key) {
        lock (_cache) {
            if (_cache.TryGetValue(key, out LinkedListNode<Entry> node)) {
                node.Value.expiration = DateTime.MinValue;
                _cache.Remove(key);
                _lru.Remove(node);
            }
        }
    }

    public void Clear() {
        lock (_cache) {
            _cache.Clear();
            _lru.Clear();
        }
    }
}

// Step 10 - turns out, there's a very subtle race condition in the code above. On some processors, in particular ARM, memory reference reordering
// can cause the value to be returned before the expiration time is set, which can cause the value to be recomputed unnecessarily.
// Also, we should only wait for the semaphore for the timeToLive, and if it times out, we should call the valueFactory again.
public class CacheEvolution10 {
    private readonly LruCache6<string, string> _cache = new(StringComparer.OrdinalIgnoreCase, NowService.Now, 1000);
    public string GetWeatherCached(string location) {
        return _cache.Fetch(location, WeatherService.GetWeather, TimeSpan.FromMinutes(5));
    }
    public async Task<string> GetWeatherCachedAsync(string location) {
        return await _cache.FetchAsync(location, WeatherService.GetWeatherAsync, TimeSpan.FromMinutes(5));
    }
}
public class LruCache6<TKey, TValue>(IEqualityComparer<TKey> comparer, Func<DateTime> nowProvider, int maxSize) : ILruCache<TKey, TValue> {
    class Entry(TKey key) {
        public readonly TKey key = key;
        public TValue value;
        public long expiration; // Note we store the expiration time as a long rather than a DateTime.
                                // This makes it possible to use volatile reads and writes to avoid the memory reference reordering issue.
        public readonly SemaphoreSlim semaphore = new(1);
    }
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _cache = new(comparer);
    private readonly LinkedList<Entry> _lru = new();
    private bool EntryValue(TKey key, out Entry entry) {
        lock (_cache) {
            if (!_cache.TryGetValue(key, out var node)) {
                _cache[key] = node = new LinkedListNode<Entry>(new Entry(key));
                _lru.AddFirst(node);
                if (_cache.Count > maxSize) {
                    _cache.Remove(_lru.Last.Value.key);
                    _lru.RemoveLast();
                }
            } else {
                _lru.Remove(node);
                _lru.AddFirst(node);
            }
            entry = node.Value;
            return Volatile.Read(ref entry.expiration) > nowProvider().Ticks;
        }
    }
    public TValue Fetch(TKey key, Func<TKey, TValue> valueFactory, TimeSpan timeToLive) {
        if (EntryValue(key, out Entry entry)) return entry.value;
        if (!entry.semaphore.Wait(timeToLive)) return valueFactory(key);
        try {
            if (Volatile.Read(ref entry.expiration) > nowProvider().Ticks) return entry.value; // check again in case another thread has already updated the value
            TValue val = entry.value = valueFactory(key);
            Volatile.Write(ref entry.expiration, (nowProvider() + timeToLive).Ticks);
            return val;
        } finally {
            entry.semaphore.Release();
        }
    }

    public async Task<TValue> FetchAsync(TKey key, Func<TKey, Task<TValue>> valueFactory, TimeSpan timeToLive) {
        if (EntryValue(key, out Entry entry)) return entry.value;
        if (!await entry.semaphore.WaitAsync(timeToLive)) return await valueFactory(key);
        try {
            if (Volatile.Read(ref entry.expiration) > nowProvider().Ticks) return entry.value; // check again in case another thread has already updated the value
            TValue val = entry.value = await valueFactory(key);
            Volatile.Write(ref entry.expiration, (nowProvider() + timeToLive).Ticks);
            return val;
        } finally {
            entry.semaphore.Release();
        }
    }

    public void Evict(TKey key) {
        lock (_cache) {
            if (_cache.TryGetValue(key, out LinkedListNode<Entry> node)) {
                node.Value.expiration = 0;
                _cache.Remove(key);
                _lru.Remove(node);
            }
        }
    }

    public void Clear() {
        lock (_cache) {
            _cache.Clear();
            _lru.Clear();
        }
    }
}