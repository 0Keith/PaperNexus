using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PaperNexus.Core;

public record struct JobExecutionContext()
{
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public bool LastExecutionSucceeded { get; set; }
    public string ErrorMessage { get; set; }
}

public record JobConfig(
    CronExpression? CronExpression = null,
    bool ExecuteOnStartup = false,
    bool DebugOnStartup = false,
    bool ExecuteOnStartupAfterFailure = false,
    bool DebugOnStartupAfterFailure = false);

public interface IScheduleScopedJob
{
    Task<JobConfig> GetJobConfigAsync();
    Task ExecuteAsync();
}

public abstract class ScheduledJobService : IHostedService, IDisposable
{
    protected ILogger Logger { get; }
    public string JobName { get; set; }
    public bool ExecuteOnStartup { get; set; }
    public bool DebugOnStartup { get; set; }
    public bool ExecuteOnStartupAfterFailure { get; set; }
    public bool DebugOnStartupAfterFailure { get; set; }

    protected abstract Task Execute();
    protected abstract Task<DateTimeOffset> GetNextExecutionAsync(JobExecutionContext context);

    protected ScheduledJobService(ILogger logger)
    {
        Logger = logger.ThrowIfNull();
        JobName = GetType().FullName;
    }

    private Task _scheduleTask;
    private bool _stopped;
    private readonly CancellationTokenSource _cts = new();

    // IHostedService implementation: fires off the scheduler loop without blocking startup.
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduleTask = ScheduleExecutions(_cts.Token);
        return Task.CompletedTask;
    }

    // Signals the loop to stop and waits up to 5 seconds for a clean exit.
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopped = true;
        _cts.Cancel();
        return Task.WhenAny(_scheduleTask ?? Task.CompletedTask, Task.Delay(5000, cancellationToken));
    }

    // Releases the CancellationTokenSource WaitHandle. StopAsync should be called first
    // to cancel the scheduler loop; Dispose only releases the unmanaged handle.
    public void Dispose() => _cts.Dispose();

    // Main scheduler loop for the legacy ScheduledJobService base class.
    // Polls GetNextExecutionAsync each iteration so schedule changes in settings take effect
    // without restarting the service. Delays longer than 1 minute are broken into 1-minute
    // slices so the loop can check _stopped promptly. Failed executions are retried after 1 minute.
    private async Task ScheduleExecutions(CancellationToken cancellationToken)
    {
        var attempts = 0;
        var maxDelay = TimeSpan.FromMinutes(1);
        var watch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.Now;
        var nextExecutionLogged = false;
        while (true)
        {
            if (_stopped)
                return;
            try
            {
                var lastExecution = await LoadContext();
                var nextExecution = await GetNextExecutionAsync(lastExecution);
                var delay = nextExecution - DateTimeOffset.Now;
                // Startup overrides: collapse the delay to zero on the very first attempt
                // depending on the configured startup execution flags.
                if (ExecuteOnStartup && attempts == 0)
                {
                    nextExecution = DateTimeOffset.Now;
                    delay = TimeSpan.Zero;
                }
                else if (ExecuteOnStartupAfterFailure && attempts == 0 && lastExecution.LastExecutionSucceeded == false)
                {
                    nextExecution = DateTimeOffset.Now;
                    delay = TimeSpan.Zero;
                }
                else if (DebugOnStartup && attempts == 0 && Debugger.IsAttached)
                {
                    nextExecution = DateTimeOffset.Now;
                    delay = TimeSpan.Zero;
                }
                else if (DebugOnStartupAfterFailure && attempts == 0 && lastExecution.LastExecutionSucceeded == false && Debugger.IsAttached)
                {
                    nextExecution = DateTimeOffset.Now;
                    delay = TimeSpan.Zero;
                }

                if (delay > maxDelay)
                {
                    // Log the upcoming execution time once, then sleep in 1-minute chunks
                    if (!nextExecutionLogged)
                    {
                        Logger.LogInformation($"{JobName}: Next execution at {nextExecution:O}");
                        nextExecutionLogged = true;
                    }
                    await Task.Delay(maxDelay, cancellationToken);
                }
                else
                {
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, cancellationToken);

                    attempts++;
                    watch.Restart();
                    startedAt = DateTimeOffset.Now;
                    nextExecutionLogged = false;
                    await Execute();
                    await SaveContext(new()
                    {
                        StartedAt = startedAt,
                        FinishedAt = DateTimeOffset.Now,
                        Duration = watch.Elapsed,
                        LastExecutionSucceeded = true
                    });
                }
            }
            catch (TaskCanceledException)
            {
                Logger.LogInformation($"Canceled Job: {JobName}");
                return;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Unhandled Exception in Job: {JobName}");
                try
                {
                    await SaveContext(new()
                    {
                        StartedAt = startedAt,
                        FinishedAt = DateTimeOffset.Now,
                        Duration = watch.Elapsed,
                        LastExecutionSucceeded = false,
                        ErrorMessage = ex.ToString(),
                    });
                }
                catch (Exception saveEx) { Logger.LogWarning(saveEx, "Failed to persist job context for {JobName} after error.", JobName); }
                // Back off for 1 minute before retrying after an unhandled exception
                await Task.Delay(maxDelay, cancellationToken);
            }
        }
    }

    private ValueTask<JobExecutionContext> LoadContext() => JobTimerStore.LoadContextAsync(JobName);
    private Task SaveContext(JobExecutionContext context) => JobTimerStore.SaveContextAsync(JobName, context);
}

// Generic IHostedService wrapper for IScheduleScopedJob implementations.
// A fresh DI scope is created for each execution so jobs receive fresh service instances.
public sealed class ScheduledJobHostedService<TJob> : IHostedService, IDisposable where TJob : IScheduleScopedJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly string _jobName;
    private Task _scheduleTask;
    private bool _stopped;
    private readonly CancellationTokenSource _cts = new();

    public ScheduledJobHostedService(IServiceScopeFactory scopeFactory, ILogger<ScheduledJobHostedService<TJob>> logger)
    {
        _scopeFactory = scopeFactory.ThrowIfNull();
        _logger = logger.ThrowIfNull();
        _jobName = typeof(TJob).FullName;
    }

    // IHostedService implementation: starts the loop without blocking the host startup.
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduleTask = ScheduleExecutions(_cts.Token);
        return Task.CompletedTask;
    }

    // Signals the loop to stop and waits up to 5 seconds for a clean exit.
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopped = true;
        _cts.Cancel();
        return Task.WhenAny(_scheduleTask ?? Task.CompletedTask, Task.Delay(5000, cancellationToken));
    }

    // Releases the CancellationTokenSource WaitHandle. StopAsync should be called first
    // to cancel the scheduler loop; Dispose only releases the unmanaged handle.
    public void Dispose() => _cts.Dispose();

    // Scheduler loop for IScheduleScopedJob: re-reads the job config each iteration so
    // runtime changes to the cron schedule or enabled flag are honoured without restart.
    // A null CronExpression in JobConfig means the job is disabled; delay becomes MaxValue.
    private async Task ScheduleExecutions(CancellationToken cancellationToken)
    {
        var attempts = 0;
        var maxDelay = TimeSpan.FromMinutes(1);
        var watch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.Now;
        var nextExecutionLogged = false;
        while (true)
        {
            if (_stopped)
                return;
            try
            {
                // Create a fresh scope for each loop iteration so the job can safely resolve
                // scoped services (e.g. settings re-reads) without holding stale state.
                using var scope = _scopeFactory.CreateScope();
                var job = ActivatorUtilities.CreateInstance<TJob>(scope.ServiceProvider);
                var config = await job.GetJobConfigAsync();
                var lastExecution = await LoadContext();
                // If no CronExpression is set the job is disabled; schedule to far future
                var nextExecution = config.CronExpression?.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local)
                    ?? DateTimeOffset.MaxValue;
                var delay = nextExecution - DateTimeOffset.Now;
                // Startup overrides: collapse delay to zero on the very first attempt
                if (config.ExecuteOnStartup && attempts == 0)
                {
                    nextExecution = DateTimeOffset.Now;
                    delay = TimeSpan.Zero;
                }
                else if (config.ExecuteOnStartupAfterFailure && attempts == 0 && lastExecution.LastExecutionSucceeded == false)
                {
                    nextExecution = DateTimeOffset.Now;
                    delay = TimeSpan.Zero;
                }
                else if (config.DebugOnStartup && attempts == 0 && Debugger.IsAttached)
                {
                    nextExecution = DateTimeOffset.Now;
                    delay = TimeSpan.Zero;
                }
                else if (config.DebugOnStartupAfterFailure && attempts == 0 && lastExecution.LastExecutionSucceeded == false && Debugger.IsAttached)
                {
                    nextExecution = DateTimeOffset.Now;
                    delay = TimeSpan.Zero;
                }

                if (delay > maxDelay)
                {
                    // Long delay: log once and sleep in 1-minute chunks to remain responsive to stop signals
                    if (!nextExecutionLogged)
                    {
                        _logger.LogInformation($"{_jobName}: Next execution at {nextExecution:O}");
                        nextExecutionLogged = true;
                    }
                    await Task.Delay(maxDelay, cancellationToken);
                }
                else
                {
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, cancellationToken);

                    attempts++;
                    watch.Restart();
                    startedAt = DateTimeOffset.Now;
                    nextExecutionLogged = false;
                    await job.ExecuteAsync();
                    await SaveContext(new()
                    {
                        StartedAt = startedAt,
                        FinishedAt = DateTimeOffset.Now,
                        Duration = watch.Elapsed,
                        LastExecutionSucceeded = true
                    });
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation($"Canceled Job: {_jobName}");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unhandled Exception in Job: {_jobName}");
                try
                {
                    await SaveContext(new()
                    {
                        StartedAt = startedAt,
                        FinishedAt = DateTimeOffset.Now,
                        Duration = watch.Elapsed,
                        LastExecutionSucceeded = false,
                        ErrorMessage = ex.ToString(),
                    });
                }
                catch (Exception saveEx) { _logger.LogWarning(saveEx, "Failed to persist job context for {JobName} after error.", _jobName); }
                // Back off 1 minute before retrying after an unhandled exception
                await Task.Delay(maxDelay, cancellationToken);
            }
        }
    }

    private ValueTask<JobExecutionContext> LoadContext() => JobTimerStore.LoadContextAsync(_jobName);
    private Task SaveContext(JobExecutionContext context) => JobTimerStore.SaveContextAsync(_jobName, context);
}

// Centralised, process-wide timer persistence store shared by all job scheduler types.
//
// Previously, ScheduledJobService and ScheduledJobHostedService<T> each declared their own
// static _timerLock/_timerFile/_timers fields and duplicate LoadContext/SaveContext methods.
// Because static fields on a generic type are per-type-argument, every
// ScheduledJobHostedService<TJob> had its own isolated _timers dictionary. On each save
// it serialised only its one entry to timers.json, silently overwriting every other job's
// persisted context. This made ExecuteOnStartupAfterFailure unreliable for all jobs other
// than the last one to write. JobTimerStore fixes this by being the single shared owner of
// the in-memory cache and the file — all scheduler implementations call into it.
internal static class JobTimerStore
{
    // Single lock and file reference shared across every job scheduler in the process.
    private static readonly SemaphoreSlim _lock = new(1);
    private static readonly FileInfo _file = new(Path.Combine(AppContext.BaseDirectory, "timers.json"));
    private static readonly ConcurrentDictionary<string, JobExecutionContext> _cache = new();

    // Returns the last execution context for jobName from the in-memory cache.
    // On first access (cache miss) reads and deserialises timers.json, warming the cache
    // for all jobs at once to minimise future file I/O.
    internal static async ValueTask<JobExecutionContext> LoadContextAsync(string jobName)
    {
        if (_cache.TryGetValue(jobName, out var context))
            return context;

        using (await _lock.EnterAsync())
        {
            _file.Refresh();
            if (_file.Exists)
            {
                var json = await File.ReadAllTextAsync(_file.FullName);
                var all = JsonConvert.DeserializeObject<Dictionary<string, JobExecutionContext>>(json);
                if (all is not null)
                {
                    // Warm cache for all jobs at once so the next call for any job is a cache hit
                    foreach (var entry in all)
                        _cache.TryAdd(entry.Key, entry.Value);
                    if (_cache.TryGetValue(jobName, out context))
                        return context;
                }
            }
            return default;
        }
    }

    // Writes jobName's execution result to the in-memory cache and persists the full
    // dictionary to timers.json atomically (write-to-temp + rename) so no other job's
    // entry is lost even if this write races with another job's save.
    internal static async Task SaveContextAsync(string jobName, JobExecutionContext context)
    {
        _cache[jobName] = context;
        using (await _lock.EnterAsync())
        {
            var json = JsonConvert.SerializeObject(_cache, Formatting.Indented);
            var dir = _file.DirectoryName!;
            var tempPath = Path.Combine(dir, $".timers-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, _file.FullName, overwrite: true);
            }
            catch
            {
                try { File.Delete(tempPath); } catch { }
                throw;
            }
        }
    }
}
