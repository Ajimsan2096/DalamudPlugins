using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LinguaBridge;

/// <summary>
/// The translations shown in the /lb window, newest first, capped at a maximum size. Written from worker threads
/// (translations arrive in the background), read by the window every frame.
/// </summary>
public sealed class TranslationHistory
{
    private readonly ConcurrentQueue<HistoryEntry> entries = new();
    private readonly Func<int> maxSize;
    private long version;

    /// <param name="maxSize">How many entries to keep (read each time, so a settings change applies at once).</param>
    public TranslationHistory(Func<int> maxSize) => this.maxSize = maxSize;

    /// <summary>Newest first. A copy: safe to enumerate while translations keep arriving.</summary>
    public IReadOnlyList<HistoryEntry> Snapshot() => entries.Reverse().ToList();

    /// <summary>Goes up by one for every change (the count stops changing once the history is full).</summary>
    public long Version => Interlocked.Read(ref version);

    public void Add(HistoryEntry entry)
    {
        entries.Enqueue(entry);
        while (entries.Count > Math.Max(10, maxSize()) && entries.TryDequeue(out _)) { }
        Changed();
    }

    public void Clear()
    {
        entries.Clear();
        Changed();
    }

    /// <summary>An entry was updated in place (e.g. a skipped message got translated).</summary>
    public void Changed() => Interlocked.Increment(ref version);
}
