namespace CacheExp;

/// <summary>
/// Cache implementation backed by Dictionary and a LinkedList. This results in good LRU eviction and
/// promotion performance since these are O(1) operations on the LinkedList.
/// </summary>
/// <param name="maxItems">Maximum number of items in the cache before LRU eviction.</param>
/// <param name="nowProvider">Current time provider to use when checking or calculating expiry.</param>
/// <typeparam name="T">Type of data to be cached.</typeparam>
/// <remarks>
/// Note that we use Volatile.Read/.Write when accessing Expires. This is to protect against
/// a race condition caused by memory reference reordering done by modern processors, in particular
/// ARM-based Apple Silicon, where this was built.
/// <see href="https://github.com/tpn/pdfs/blob/master/Memory%20Barriers%20-%20a%20Hardware%20View%20for%20Software%20Hackers%20(July%2023%2C%202010).pdf"/>
/// </remarks>
public readonly struct Cache2<T>(int maxItems, Func<DateTime> nowProvider) {
    private class CacheEntry(string key) {
        public readonly string Key = key; // need key to know which dictionary entry to evict
        public T Value;
        public long Expires; // ticks
        public readonly SemaphoreSlim Semaphore = new(1); // instead of object lock for non-thread affine async methods
    }

    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cache = new();
    private readonly LinkedList<CacheEntry> _lru = new();

    public T Fetch(string key, Func<T> valueFactory, TimeSpan timeToLive) {
        if (EntryValue(key, out CacheEntry entry, out T entryValue)) return entryValue;

        entry.Semaphore.Wait(); // per-key lock
        try {
            if (Volatile.Read(ref entry.Expires) > nowProvider().Ticks) return entry.Value; // stampede fetch?
            T val = entry.Value = valueFactory(); // actually get value
            Volatile.Write(ref entry.Expires, (nowProvider() + timeToLive).Ticks); // set expiry after getting value
            return val;
        } finally {
            entry.Semaphore.Release();
        }
    }

    public async Task<T> FetchAsync(string key, Func<Task<T>> valueFactory, TimeSpan timeToLive) {
        if (EntryValue(key, out CacheEntry entry, out T entryValue)) return entryValue;

        if (!await entry.Semaphore.WaitAsync(timeToLive)) return await valueFactory(); // per-key lock
        try {
            if (Volatile.Read(ref entry.Expires) > nowProvider().Ticks) return entry.Value; // stampede fetch?
            T val = entry.Value = await valueFactory(); // actually get value
            Volatile.Write(ref entry.Expires, (nowProvider() + timeToLive).Ticks); // set expiry after getting value
            return val;
        } finally {
            entry.Semaphore.Release();
        }
    }

    private bool EntryValue(string key, out CacheEntry entry, out T entryValue) {
        long now = nowProvider().Ticks;
        lock (_cache) {
            if (!_cache.TryGetValue(key, out var node)) {
                entry = new CacheEntry(key); // create/add empty entry for key
                _cache[key] = node = new(entry);
                _lru.AddFirst(node); // add to lru chain
                while (_cache.Count > maxItems) { // shrink cache if necessary
                    _cache.Remove(_lru.Last.Value.Key);
                    _lru.RemoveLast();
                }
            } else {
                entry = node.Value;
                if (Volatile.Read(ref entry.Expires) > now) {
                    if (node != _lru.First) {
                        _lru.Remove(node); // move to front...
                        _lru.AddFirst(node); // ...of lru chain
                    }
                    entryValue = entry.Value;
                    return true;
                }
            }
        }
        entryValue = default;
        return false;
    }
}

[TestClass]
public sealed class Cache2Tests {
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1), OneMinute = TimeSpan.FromMinutes(1);
    
    [TestMethod]
    public void BasicSmokeTest() {
        DateTime curTime = DateTime.MinValue;
        Cache2<int> c = new(5, () => curTime);
        
        int counter = 0;
        Func<int> factory = () => Interlocked.Increment(ref counter);
        
        Assert.AreEqual(1, c.Fetch("a", factory, TimeSpan.Zero));
        Assert.AreEqual(2, c.Fetch("a", factory, OneSecond));
        Assert.AreEqual(2, c.Fetch("a", factory, TimeSpan.Zero));
        curTime += OneSecond;
        Assert.AreEqual(3, c.Fetch("a", factory, OneSecond));
    }

    [TestMethod]
    public async Task Async_BasicSmokeTest() {
        DateTime curTime = DateTime.MinValue;
        Cache2<int> c = new(5, () => curTime);
        
        int counter = 0;
        Func<Task<int>> factory = () => Task.FromResult(Interlocked.Increment(ref counter));

        Assert.AreEqual(1, await c.FetchAsync("a", factory, TimeSpan.Zero));
        Assert.AreEqual(2, await c.FetchAsync("a", factory, OneSecond));
        Assert.AreEqual(2, await c.FetchAsync("a", factory, TimeSpan.Zero)); // still fresh
        curTime += OneSecond;
        Assert.AreEqual(3, await c.FetchAsync("a", factory, OneSecond)); // expired, recomputed
    }

    [TestMethod]
    public void BasicLruTest() {
        Cache2<int> c = new(3, () => DateTime.UnixEpoch);
        // add a,b,c
        Assert.AreEqual(1, c.Fetch("a", () => 1, OneSecond));
        Assert.AreEqual(2, c.Fetch("b", () => 2, OneSecond));
        Assert.AreEqual(3, c.Fetch("c", () => 3, OneSecond));

        // fetch a from cache, so now b is LRU
        Assert.AreEqual(1, c.Fetch("a", () => throw new Exception("should fetch from cache"), TimeSpan.Zero));
        
        // add d to evict b
        Assert.AreEqual(4, c.Fetch("d", () => 4, OneSecond));
        // fetch b should get different value
        Assert.AreEqual(-2, c.Fetch("b", () => -2, OneSecond));
    }
    
    [TestMethod]
    public async Task Async_BasicLruTest() {
        Cache2<int> c = new(3, () => DateTime.MinValue);
        // add a,b,c
        Assert.AreEqual(1, await c.FetchAsync("a", () => Task.FromResult(1), OneSecond));
        Assert.AreEqual(2, await c.FetchAsync("b", () => Task.FromResult(2), OneSecond));
        Assert.AreEqual(3, await c.FetchAsync("c", () => Task.FromResult(3), OneSecond));

        // fetch a from cache, so now b is LRU
        Assert.AreEqual(1, await c.FetchAsync("a", () => throw new Exception("should fetch from cache"), TimeSpan.Zero));
        
        // add d to evict b
        Assert.AreEqual(4, await c.FetchAsync("d", () => Task.FromResult(4), OneSecond));
        // fetch b should get different value
        Assert.AreEqual(-2, await c.FetchAsync("b", () => Task.FromResult(-2), OneSecond));
    }

    // Stampede protection: many threads racing on the same cold key must only
    // invoke the factory once, and all callers must observe the same value.
    [TestMethod]
    [DoNotParallelize]
    public void Stampede_ConcurrentMissesInvokeFactoryOnce() {
        Cache2<int> c = new(100, () => DateTime.MinValue);
        
        int factoryCalls = 0;
        Func<int> valueFactory = () => {
            Interlocked.Increment(ref factoryCalls);
            Thread.Sleep(50); // widen the race window
            return 42;
        };

        using var ready = new ManualResetEventSlim(false);
        var results = new int[64];
        var tasks = Enumerable.Range(0, results.Length).Select(i => Task.Run(() => {
            ready.Wait();
            results[i] = c.Fetch("k", valueFactory, OneSecond);
        })).ToArray();
        ready.Set(); // release all threads at once
        Assert.IsTrue(Task.WaitAll(tasks, OneMinute), "Fetch deadlocked.");

        Assert.AreEqual(1, factoryCalls, "factory must run exactly once for a concurrent stampede on one key");
        foreach (var r in results) Assert.AreEqual(42, r, "every caller must see the same cached value");
    }
    
    // Async stampede protection: many concurrent callers on the same cold key,
    // whose factory genuinely suspends (await Task.Delay), must invoke the factory
    // exactly once and all observe the same value.
    [TestMethod]
    [DoNotParallelize]
    public void Async_Stampede_ConcurrentMissesInvokeFactoryOnce() {
        Cache2<int> c = new(100, () => DateTime.MinValue);
        
        int factoryCalls = 0;
        Func<Task<int>> valueFactory = async () => {
            Interlocked.Increment(ref factoryCalls);
            await Task.Delay(50); // real async suspension widens the race window
            return 42;
        };

        using var ready = new ManualResetEventSlim(false);
        var results = new int[64];
        var tasks = Enumerable.Range(0, results.Length).Select(i => Task.Run(async () => {
            ready.Wait();
            results[i] = await c.FetchAsync("k", valueFactory, OneSecond);
        })).ToArray();
        ready.Set(); // release all callers at once
        Assert.IsTrue(Task.WaitAll(tasks, OneMinute), "FetchAsync deadlocked.");

        Assert.AreEqual(1, factoryCalls, "factory must run exactly once for a concurrent async stampede on one key");
        foreach (var r in results) Assert.AreEqual(42, r, "every caller must see the same cached value");
    }

    // Stampede protection on refresh: when an entry expires, a burst of concurrent
    // callers must recompute the value only once, not once per caller.
    [TestMethod]
    [DoNotParallelize]
    public void Stampede_ConcurrentExpiredRefreshInvokesFactoryOnce() {
        DateTime curTime = DateTime.MinValue;
        Cache2<int> c = new(100, () => curTime);

        // Prime the cache with a short-lived value.
        c.Fetch("k", () => 1, OneSecond);
        curTime += OneSecond; // force expiry

        int factoryCalls = 0;
        Func<int> valueFactory = () => {
            Interlocked.Increment(ref factoryCalls);
            Thread.Sleep(50);
            return 2;
        };

        using var ready = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 64).Select(_ => Task.Run(() => {
            ready.Wait();
            c.Fetch("k", valueFactory, OneSecond);
        })).ToArray();
        ready.Set();
        Assert.IsTrue(Task.WaitAll(tasks, OneMinute), "Fetch deadlocked.");

        Assert.AreEqual(1, factoryCalls, "an expired entry must be refreshed exactly once under a concurrent stampede");
    }

    // Async stampede protection on refresh: a burst of concurrent callers hitting an
    // expired entry must recompute it exactly once.
    [TestMethod]
    [DoNotParallelize]
    public async Task Async_Stampede_ConcurrentExpiredRefreshInvokesFactoryOnce() {
        DateTime curTime = DateTime.MinValue;
        Cache2<int> c = new(100, () => curTime);

        await c.FetchAsync("k", () => Task.FromResult(1), OneSecond); // prime
        curTime += OneSecond; // force expiry

        int factoryCalls = 0;
        Func<Task<int>> valueFactory = async () => {
            Interlocked.Increment(ref factoryCalls);
            await Task.Delay(50);
            return 2;
        };

        using var ready = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 64).Select(_ => Task.Run(async () => {
            ready.Wait();
            await c.FetchAsync("k", valueFactory, OneSecond);
        })).ToArray();
        ready.Set();
        Assert.IsTrue(Task.WaitAll(tasks, OneMinute), "FetchAsync deadlocked.");

        Assert.AreEqual(1, factoryCalls, "an expired entry must be refreshed exactly once under a concurrent async stampede");
    }

    // Thread-safety: many threads across many distinct keys must each get their own
    // correct value, and each key's factory must run exactly once (no eviction here).
    [TestMethod]
    [DoNotParallelize]
    public void ThreadSafety_ConcurrentDistinctKeysAreConsistent() {
        const int keys = 200, threadsPerKey = 8;
        Cache2<int> c = new(keys + 1, () => DateTime.MinValue); // large enough to avoid eviction

        var perKeyCalls = new int[keys];
        using var ready = new ManualResetEventSlim(false);
        
        var tasks = new Task[keys * threadsPerKey];
        for (int k = 0, task = 0; k < keys; k++) {
            int key = k;
            for (int t = 0; t < threadsPerKey; t++) {
                tasks[task++] = Task.Run(() => {
                    ready.Wait();
                    var v = c.Fetch("key" + key, () => {
                        Interlocked.Increment(ref perKeyCalls[key]);
                        return key;
                    }, OneSecond);
                    Assert.AreEqual(key, v, "a key must never return another key's value");
                });
            }
        }
        ready.Set();
        Assert.IsTrue(Task.WaitAll(tasks, OneMinute), "Fetch deadlocked.");

        for (int k = 0; k < keys; k++)
            Assert.AreEqual(1, perKeyCalls[k], $"factory for key{k} must run exactly once");
    }
    
    // Async thread-safety: many concurrent callers across many distinct keys must
    // each get their own correct value, and each key's factory must run exactly once.
    [TestMethod]
    [DoNotParallelize]
    public void Async_ThreadSafety_ConcurrentDistinctKeysAreConsistent() {
        const int keys = 200, callersPerKey = 8;
        Cache2<int> c = new(keys + 1, () => DateTime.MinValue); // large enough to avoid eviction

        var perKeyCalls = new int[keys];
        using var ready = new ManualResetEventSlim(false);

        var tasks = new Task[keys * callersPerKey];
        for (int k = 0, task = 0; k < keys; k++) {
            for (int t = 0; t < callersPerKey; t++) {
                int key = k;
                tasks[task++] = Task.Run(async () => {
                    ready.Wait();
                    int v = await c.FetchAsync("key" + key, async () => {
                        Interlocked.Increment(ref perKeyCalls[key]);
                        await Task.Yield();
                        return key;
                    }, OneSecond);
                    Assert.AreEqual(key, v, "a key must never return another key's value");
                });
            }
        }
        ready.Set();
        Assert.IsTrue(Task.WaitAll(tasks, OneMinute), "FetchAsync deadlocked.");

        for (int k = 0; k < keys; k++)
            Assert.AreEqual(1, perKeyCalls[k], $"factory for key{k} must run exactly once");
    }

    // Thread-safety under heavy eviction: a small cache hammered by many threads
    // and many keys must not deadlock, throw, or corrupt entries.
    [TestMethod]
    [DoNotParallelize]
    public void ThreadSafety_EvictionUnderContentionDoesNotCorrupt() {
        Cache2<int> c = new(10, () => DateTime.MinValue); // tiny cache -> constant eviction churn

        int mismatches = 0, exceptions = 0;
        using var ready = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 32).Select(t => Task.Run(() => {
            ready.Wait();
            var rnd = new Random(t);
            try {
                for (int i = 0; i < 5000; i++) {
                    int n = rnd.Next(100);
                    int v = c.Fetch("k" + n, () => n, OneSecond);
                    if (v != n) Interlocked.Increment(ref mismatches);
                }
            } catch {
                Interlocked.Increment(ref exceptions);
            }
        })).ToArray();
        ready.Set();
        Assert.IsTrue(Task.WaitAll(tasks, OneMinute), "cache deadlocked under eviction contention");
        Assert.AreEqual(0, exceptions, "concurrent eviction must not throw (e.g. collection corruption)");
        Assert.AreEqual(0, mismatches, "a key must always return the value its own factory produced");
    }
    
    [TestMethod]
    [DoNotParallelize]
    public void Async_ThreadSafety_EvictionUnderContentionDoesNotCorrupt() {
        Cache2<int> c = new(10, () => DateTime.MinValue); // tiny cache -> constant eviction churn

        int mismatches = 0, exceptions = 0;
        using var ready = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 32).Select(t => Task.Run(async () => {
            ready.Wait();
            var rnd = new Random(t);
            try {
                for (int i = 0; i < 5000; i++) {
                    int n = rnd.Next(100);
                    int v = await c.FetchAsync("k" + n, () => Task.FromResult(n), OneSecond);
                    if (v != n) Interlocked.Increment(ref mismatches);
                }
            } catch {
                Interlocked.Increment(ref exceptions);
            }
        })).ToArray();
        ready.Set();
        Assert.IsTrue(Task.WaitAll(tasks, OneMinute), "cache deadlocked under eviction contention");
        Assert.AreEqual(0, exceptions, "concurrent eviction must not throw (e.g. collection corruption)");
        Assert.AreEqual(0, mismatches, "a key must always return the value its own factory produced");
    }
}