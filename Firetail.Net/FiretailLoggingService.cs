using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Firetail;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;

namespace Firetail;

internal class FiretailLoggingService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FiretailOptions _options;
    private readonly SemaphoreSlim _flushSignal = new(0);
    private readonly ConcurrentQueue<string> _logQueue = new();
    private readonly ILogger<FiretailLoggingService> _logger;
    
    private const int BYTES_PER_CHAR = 4; // UTF-32 encoding size

    private int _signalPending = 0;
    private long _logSize;

    public FiretailLoggingService(
        FiretailOptions options, 
        IHttpClientFactory httpClientFactory,
        ILogger<FiretailLoggingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    private bool ShouldFlush()
    {
        return Interlocked.Read(ref _logSize) >= _options.LogMaxSize ||
               _logQueue.Count >= _options.LogMaxItems;
    }

    public void AddLogAsync(HttpContext httpContext, FiretailContext firetailContext)
    {
        try
        {
            var logEntry = FiretailLog.CreateLogEntry(httpContext, firetailContext, _options);
            var entrySize = (long)logEntry.Length * BYTES_PER_CHAR;
            var newSize = Interlocked.Add(ref _logSize, entrySize);
            _logQueue.Enqueue(logEntry);

            var pendingSignal = Interlocked.Exchange(ref _signalPending, 1);
            if (pendingSignal == 0 && ShouldFlush())
            {
                _flushSignal.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding log entry");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        _logger.LogInformation("Firetail logging service started");

        while (!token.IsCancellationRequested)
        {
            try
            {
                await _flushSignal.WaitAsync(TimeSpan.FromMilliseconds(_options.LogMaxTimeMs), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            await FlushLogsAsync(token);
            Interlocked.Exchange(ref _signalPending, 0);
        }

        await FlushLogsAsync(token);
        _logger.LogInformation("Firetail logging service stopped");
    }

    private async Task FlushLogsAsync(CancellationToken token)
    {
        try
        {

            var batch = new List<string>();
            var batchSize = 0L;

            while (_logQueue.TryDequeue(out var entry))
            {
                batch.Add(entry);
                batchSize += (long)entry.Length * BYTES_PER_CHAR;
            }

            if (batch.Count == 0) return;

            _logger.LogDebug("Flushing {Count} log entries", batch.Count);

            var newSize = Interlocked.Add(ref _logSize, -batchSize);

            if (newSize < 0)
            {
                Interlocked.Exchange(ref _logSize, 0);
            }

            using var stream = new MemoryStream();
            using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    var entry = batch[i];
                    await writer.WriteAsync(entry);
                    if (i != batch.Count - 1)
                    {
                        await writer.WriteAsync('\n');
                    }
                }
                await writer.FlushAsync(token);
            }
            stream.Position = 0;

            var httpContent = new StreamContent(stream);
            httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson") { CharSet = Encoding.UTF8.WebName };

            var client = _httpClientFactory.CreateClient("Firetail");
            var response = await client.PostAsync(string.Empty, httpContent, token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully flushed {Count} log entries", batch.Count);
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync(token);
                _logger.LogError("Failed to flush logs. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode, responseBody);
                
                foreach (var entry in batch)
                {
                    _logQueue.Enqueue(entry);
                    Interlocked.Add(ref _logSize, (long)entry.Length * BYTES_PER_CHAR);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing logs");
        }
    }

    public override void Dispose()
    {
        _flushSignal.Dispose();
        base.Dispose();
    }
}