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

public abstract class ScheduledJobService : IHostedService
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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduleTask = ScheduleExecutions(_cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopped = true;
        _cts.Cancel();
        return Task.WhenAny(_scheduleTask ?? Task.CompletedTask, Task.Delay(5000, cancellationToken));
    }

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
                await SaveContext(new()
                {
                    StartedAt = startedAt,
                    FinishedAt = DateTimeOffset.Now,
                    Duration = watch.Elapsed,
                    LastExecutionSucceeded = false,
                    ErrorMessage = ex.ToString(),
                });
                await Task.Delay(maxDelay, cancellationToken);
            }
        }
    }

    private static readonly SemaphoreSlim _timerLock = new(1);
    private static readonly FileInfo _timerFile = new("./timers.json");
    private static readonly ConcurrentDictionary<string, JobExecutionContext> _timers = new();

    private async ValueTask<JobExecutionContext> LoadContext()
    {
        if (_timers.TryGetValue(JobName, out var context))
            return context;
        using (await _timerLock.EnterAsync())
        {
            _timerFile.Refresh();
            if (_timerFile.Exists)
            {
                var json = await File.ReadAllTextAsync(_timerFile.FullName);
                var timers = JsonConvert.DeserializeObject<Dictionary<string, JobExecutionContext>>(json);
                if (timers.TryGetValue(JobName, out context))
                {
                    foreach (var timer in timers)
                        _timers.TryAdd(timer.Key, timer.Value);
                    return context;
                }
            }
            return default;
        }
    }

    private async Task SaveContext(JobExecutionContext context)
    {
        _timers[JobName] = context;
        using (await _timerLock.EnterAsync())
        {
            var json = JsonConvert.SerializeObject(_timers, Formatting.Indented);
            await File.WriteAllTextAsync(_timerFile.FullName, json);
        }
    }
}

public sealed class ScheduledJobHostedService<TJob> : IHostedService where TJob : IScheduleScopedJob
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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduleTask = ScheduleExecutions(_cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopped = true;
        _cts.Cancel();
        return Task.WhenAny(_scheduleTask ?? Task.CompletedTask, Task.Delay(5000, cancellationToken));
    }

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
                using var scope = _scopeFactory.CreateScope();
                var job = ActivatorUtilities.CreateInstance<TJob>(scope.ServiceProvider);
                var config = await job.GetJobConfigAsync();
                var lastExecution = await LoadContext();
                var nextExecution = config.CronExpression?.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local)
                    ?? DateTimeOffset.MaxValue;
                var delay = nextExecution - DateTimeOffset.Now;
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
                await SaveContext(new()
                {
                    StartedAt = startedAt,
                    FinishedAt = DateTimeOffset.Now,
                    Duration = watch.Elapsed,
                    LastExecutionSucceeded = false,
                    ErrorMessage = ex.ToString(),
                });
                await Task.Delay(maxDelay, cancellationToken);
            }
        }
    }

    private static readonly SemaphoreSlim _timerLock = new(1);
    private static readonly FileInfo _timerFile = new("./timers.json");
    private static readonly ConcurrentDictionary<string, JobExecutionContext> _timers = new();

    private async ValueTask<JobExecutionContext> LoadContext()
    {
        if (_timers.TryGetValue(_jobName, out var context))
            return context;
        using (await _timerLock.EnterAsync())
        {
            _timerFile.Refresh();
            if (_timerFile.Exists)
            {
                var json = await File.ReadAllTextAsync(_timerFile.FullName);
                var timers = JsonConvert.DeserializeObject<Dictionary<string, JobExecutionContext>>(json);
                if (timers.TryGetValue(_jobName, out context))
                {
                    foreach (var timer in timers)
                        _timers.TryAdd(timer.Key, timer.Value);
                    return context;
                }
            }
            return default;
        }
    }

    private async Task SaveContext(JobExecutionContext context)
    {
        _timers[_jobName] = context;
        using (await _timerLock.EnterAsync())
        {
            var json = JsonConvert.SerializeObject(_timers, Formatting.Indented);
            await File.WriteAllTextAsync(_timerFile.FullName, json);
        }
    }
}
