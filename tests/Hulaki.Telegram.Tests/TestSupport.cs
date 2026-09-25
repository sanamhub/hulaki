using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace Hulaki.Telegram.Tests;

internal static class Time
{
    /// <summary>Runs <paramref name="start"/> while advancing <paramref name="time"/> until it completes, so Task.Delay on the fake clock fires.</summary>
    public static async Task<T> RunAsync<T>(FakeTimeProvider time, Func<Task<T>> start, TimeSpan? step = null)
    {
        var task = start();
        for (var i = 0; i < 10_000 && !task.IsCompleted; i++)
        {
            await Task.Yield();
            time.Advance(step ?? TimeSpan.FromMilliseconds(100));
        }

        return await task;
    }
}
