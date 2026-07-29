using System.Diagnostics;
using System.Timers;
using AaronOS.Core.Data;
using AaronOS.Modules.Trading.Agent;
using AaronOS.Modules.Trading.Data;
using Microsoft.EntityFrameworkCore;
using Timer = System.Timers.Timer;

namespace AaronOS.Modules.Trading.Trading;

/// <summary>
/// Runs the agent on a timer.
///
/// Worth being plain about the limit: this is a desktop application, so "autonomous" means
/// autonomous while AaronOS is open. Closing the window stops the trading. Anything that needs to
/// run overnight belongs on a server, which is a different piece of work.
///
/// A single-permit gate rather than a lock, because a cycle can outlast its interval when the model
/// is slow: overlapping runs would read the same order count twice and could double a position.
/// A tick that arrives while one is in flight is dropped rather than queued.
/// </summary>
public sealed class TradingScheduler(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    TradingAgent agent,
    SnapshotRecorder recorder) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _timer;
    private Timer? _watchdog;
    private DateTime _lastTickUtc = DateTime.UtcNow;
    private int _intervalMinutes = 30;

    /// <summary>
    /// When a cycle last ran, for a health check to read.
    ///
    /// Externally visible because the failure that prompted it was invisible: the app sat running for
    /// six hours with the market open and fired nothing, and the only symptom was a decision log whose
    /// newest entry was from the previous day. A process being alive is not evidence that its work is
    /// happening.
    /// </summary>
    public DateTime LastTickUtc => _lastTickUtc;

    public bool IsRunning => _timer is { Enabled: true };

    /// <summary>Raised after every cycle, on a background thread — marshal before touching the UI.</summary>
    public event Action<CycleResult>? CycleCompleted;

    /// <summary>
    /// Arms the timer and runs a first cycle.
    ///
    /// <paramref name="firstCycleDelay"/> exists for the unattended case. Started from a button, the
    /// first cycle should run at once or the scheduler looks inert. Started at login it should wait,
    /// because the model server is coming up at the same moment and a cycle that arrives first fails
    /// and writes an error nobody needed to see.
    /// </summary>
    public async Task StartAsync(TimeSpan firstCycleDelay = default)
    {
        Stop();

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var config = await db.Set<TradingConfig>().FirstOrDefaultAsync();
        var minutes = Math.Clamp(config?.CycleIntervalMinutes ?? 30, 1, 24 * 60);

        _intervalMinutes = minutes;
        _lastTickUtc = DateTime.UtcNow;

        _timer = new Timer(TimeSpan.FromMinutes(minutes).TotalMilliseconds) { AutoReset = true };
        _timer.Elapsed += OnElapsed;
        _timer.Start();

        // A second, slower timer that exists only to notice the first one has gone quiet and restart it.
        // Belt and braces rather than a diagnosis: a schedule meant to run unattended for a week has to
        // recover from a stall on its own, because nobody is watching it to press the button.
        _watchdog = new Timer(TimeSpan.FromMinutes(Math.Max(2, minutes)).TotalMilliseconds)
        {
            AutoReset = true,
        };
        _watchdog.Elapsed += OnWatchdog;
        _watchdog.Start();

        _ = Task.Run(async () =>
        {
            if (firstCycleDelay > TimeSpan.Zero)
            {
                await Task.Delay(firstCycleDelay);
            }

            await RunOnceAsync();
        });
    }

    public void Stop()
    {
        if (_watchdog is not null)
        {
            _watchdog.Elapsed -= OnWatchdog;
            _watchdog.Stop();
            _watchdog.Dispose();
            _watchdog = null;
        }

        if (_timer is null)
        {
            return;
        }

        _timer.Elapsed -= OnElapsed;
        _timer.Stop();
        _timer.Dispose();
        _timer = null;
    }

    private void OnElapsed(object? sender, ElapsedEventArgs e)
    {
        _lastTickUtc = DateTime.UtcNow;
        _ = Task.Run(RunOnceAsync);
    }

    /// <summary>Restarts the main timer if it has missed more than two intervals.</summary>
    private void OnWatchdog(object? sender, ElapsedEventArgs e)
    {
        var silentFor = DateTime.UtcNow - _lastTickUtc;
        if (silentFor <= TimeSpan.FromMinutes(_intervalMinutes * 2.5))
        {
            return;
        }

        Debug.WriteLine($"[trading] no tick for {silentFor.TotalMinutes:F0} min — re-arming");
        _lastTickUtc = DateTime.UtcNow;

        try
        {
            _timer?.Stop();
            _timer?.Start();
        }
        catch (ObjectDisposedException)
        {
            // Stopped while the watchdog was mid-check; nothing to recover.
        }

        _ = Task.Run(RunOnceAsync);
    }

    public async Task<CycleResult> RunOnceAsync()
    {
        if (!await _gate.WaitAsync(0))
        {
            return CycleResult.Skipped("Previous cycle still running.");
        }

        try
        {
            var result = await agent.RunCycleAsync();

            // Recorded even on a skipped cycle: the equity curve should have a point for every day
            // the app was open, whether or not the agent traded.
            try
            {
                await recorder.ReconcileOpenOrdersAsync();
                await recorder.RecordTodayAsync();
            }
            catch (Exception ex)
            {
                return new CycleResult(result.Ran, result.Summary, ex.Message);
            }

            _lastTickUtc = DateTime.UtcNow;
            CycleCompleted?.Invoke(result);
            return result;
        }
        catch (Exception ex)
        {
            var failure = CycleResult.Failed(ex.Message);
            CycleCompleted?.Invoke(failure);
            return failure;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        Stop();
        _gate.Dispose();
    }
}
