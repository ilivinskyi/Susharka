using System;
using System.Collections.Generic;
using System.Linq;

namespace Susharka.Core;

/// <summary>One photo pegged to the line.</summary>
public sealed class Pegged
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Path { get; set; }
    /// <summary>Resting tilt in degrees: every photo hangs a little crooked, like on a real line.</summary>
    public double Tilt { get; }
    /// <summary>Bumped when the file is edited so views reload the image.</summary>
    public int Revision { get; set; }

    public Pegged(string path, double tilt)
    {
        Path = path;
        Tilt = tilt;
    }
}

public enum RemovalReason
{
    /// <summary>Pushed off the far end because the line is full; the file is kept.</summary>
    Overflow,
    /// <summary>The user let it go (× button, "Take everything down"); the file is kept.</summary>
    LetGo,
    /// <summary>The file vanished (moved into a folder, dropped on the Recycle Bin, deleted).</summary>
    Gone,
}

/// <summary>The clothesline model: an ordered list of photos, oldest first, capped at <see cref="MaxItems"/>.</summary>
public sealed class Line
{
    public const double MaxTilt = 2.5;

    private readonly List<Pegged> _items = new();
    private readonly Random _random;

    public Line(int maxItems = 8, Random? random = null)
    {
        MaxItems = maxItems;
        _random = random ?? new Random();
    }

    public int MaxItems { get; set; }
    public IReadOnlyList<Pegged> Items => _items;

    public event Action<Pegged, bool>? Added;               // item, animated
    public event Action<Pegged, RemovalReason>? Removed;
    public event Action<Pegged>? Updated;
    public event Action? Changed;

    public double NextTilt() => (_random.NextDouble() * 2 - 1) * MaxTilt;

    /// <summary>Hangs a new photo at the end. When the line is full, the oldest photos fall off.</summary>
    public Pegged Hang(string path, bool animated = true)
    {
        var existing = _items.FirstOrDefault(i => SamePath(i.Path, path));
        if (existing != null)
        {
            existing.Revision++;
            Updated?.Invoke(existing);
            return existing;
        }

        var item = new Pegged(path, NextTilt());
        _items.Add(item);
        Added?.Invoke(item, animated);
        TrimToCapacity();
        Changed?.Invoke();
        return item;
    }

    public void TrimToCapacity()
    {
        while (_items.Count > MaxItems)
        {
            var oldest = _items[0];
            _items.RemoveAt(0);
            Removed?.Invoke(oldest, RemovalReason.Overflow);
        }
    }

    public bool Remove(Pegged item, RemovalReason reason)
    {
        if (!_items.Remove(item)) return false;
        Removed?.Invoke(item, reason);
        Changed?.Invoke();
        return true;
    }

    public void Clear(RemovalReason reason = RemovalReason.LetGo)
    {
        foreach (var item in _items.ToList()) Remove(item, reason);
    }

    public void Touch(Pegged item)
    {
        item.Revision++;
        Updated?.Invoke(item);
    }

    /// <summary>Drops photos whose files no longer exist.</summary>
    public int Prune(Func<string, bool> exists)
    {
        var gone = _items.Where(i => !exists(i.Path)).ToList();
        foreach (var item in gone) Remove(item, RemovalReason.Gone);
        return gone.Count;
    }

    /// <summary>Quietly re-hangs photos saved in a previous session, skipping missing files.</summary>
    public void Restore(IEnumerable<string> paths, Func<string, bool> exists)
    {
        foreach (var p in paths.Where(exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var item = new Pegged(p, NextTilt());
            _items.Add(item);
            Added?.Invoke(item, false);
        }
        TrimToCapacity();
        Changed?.Invoke();
    }

    public List<string> Snapshot() => _items.Select(i => i.Path).ToList();

    public Pegged? Find(string path) => _items.FirstOrDefault(i => SamePath(i.Path, path));

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
