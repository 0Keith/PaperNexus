using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Excogitated.Core;

public static class Extensions
{
    public static T ThrowIfNull<T>(this T value, [CallerArgumentExpression("value")] string parameterName = null)
    {
        return value ?? throw new ArgumentNullException(nameof(value));
    }

    public static bool IsNullOrWhiteSpace(this string value) => string.IsNullOrWhiteSpace(value);

    public static async Task CopyTo(this StreamReader reader, ILogger logger, LogLevel level)
    {
        reader.ThrowIfNull();
        logger.ThrowIfNull();
        var line = await reader.ReadLineAsync();
        while (line is not null)
        {
            logger.Log(level, line);
        }
    }

    public static async Task<IDisposable> EnterAsync(this SemaphoreSlim semaphore)
    {
        semaphore.ThrowIfNull();
        await semaphore.WaitAsync();
        return new SemaphoreRelease(semaphore);
    }

    private class SemaphoreRelease : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public void Dispose() => _semaphore.Release();
        public SemaphoreRelease(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }
    }

    public static T Max<T>(this T value1, T value2) where T : IComparable<T>
    {
        return value1.CompareTo(value2) < 0 ? value1 : value2;
    }

    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> items, TKey key, Func<TKey, TValue> valueFactory)
    {
        items.ThrowIfNull();
        key.ThrowIfNull();
        if (items.TryGetValue(key, out var value))
            return value;
        valueFactory.ThrowIfNull();
        value = valueFactory(key);
        items[key] = value;
        return value;
    }
}
