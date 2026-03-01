using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PaperNexus.Core;

public class FileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly SemaphoreSlim _messageCount = new(0);
    private readonly Task _writeTask;
    private bool _running = true;

    private bool Running => _running || _messages.Count > 0 || _messageCount.CurrentCount > 0;

    public FileLoggerProvider()
    {
        _writeTask = StartWriter();
    }

    public void Dispose() => DisposeAsync().AsTask().Wait();
    public async ValueTask DisposeAsync()
    {
        _running = false;
        await Task.WhenAny(_writeTask, Task.Delay(5000));
    }

    private async Task StartWriter()
    {
        var assName = Assembly.GetEntryAssembly()?.GetName().Name ?? "app";
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        var logFileName = Path.Combine(logDir, $"{assName}.log");
        var logFile = new FileInfo(logFileName);
        if (!logFile.Directory.Exists)
            logFile.Directory.Create();
        while (Running)
        {
            try
            {
                await _messageCount.WaitAsync(5000);
                if (_messages.Count > 0)
                {
                    using var stream = logFile.Open(FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream);
                    while (_messages.TryDequeue(out var message))
                        await writer.WriteLineAsync(message);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    private void Log(string message)
    {
        _messages.Enqueue(message);
        _messageCount.Release();
    }

    private class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FileLoggerProvider _fileLoggerProvider;

        public FileLogger(string categoryName, FileLoggerProvider fileLoggerProvider)
        {
            _categoryName = categoryName;
            _fileLoggerProvider = fileLoggerProvider;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            private NullScope() { }
            public void Dispose() { }
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var message = formatter?.Invoke(state, exception);
            message = string.Join(" | ", DateTimeOffset.Now.ToString("O"), logLevel, _categoryName, message);
            if (exception is not null)
                message = string.Join(Environment.NewLine, message, exception);
            _fileLoggerProvider.Log(message);
        }
    }
}
