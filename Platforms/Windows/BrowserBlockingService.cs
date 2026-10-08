using System.Diagnostics;
using System.Runtime.InteropServices;
using Regex_Site_Blocker_App.Services;
using Regex_Site_Blocker_App.Services.BrowserBlocking;

namespace Regex_Site_Blocker_App.WinUI
{
    public sealed class BrowserBlockingService(IConfigSource source, string? pageDirectory = null)
    {
        private readonly SemaphoreSlim lifecycle = new(1);
        private readonly BrowserBlockPolicy policy = new();
        private readonly AutoResetEvent wake = new(false);
        private CancellationTokenSource? stopping;
        private Task? worker;
        private long revision;
        public event Action<Exception>? Failed;
        public RuleIssue[] Issues => policy.Issues;
        private void Apply(Config.AppConfig config) { policy.Apply(config); Interlocked.Increment(ref revision); wake.Set(); }

        public async Task StartAsync()
        {
            await lifecycle.WaitAsync();
            try
            {
                if (worker is { IsCompleted: false }) return;
                source.Changed += Apply;
                try
                {
                    long beforeLoad = Interlocked.Read(ref revision);
                    var loaded = await source.LoadAsync();
                    if (Interlocked.Read(ref revision) == beforeLoad) policy.Apply(loaded);
                    string page = BlockPage.Create(pageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RegexSiteBlocker", "BrowserBlocking"));
                    stopping = new CancellationTokenSource();
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var token = stopping.Token;
                    worker = Task.Factory.StartNew(() => Run(page, token, ready), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    await ready.Task;
                }
                catch { source.Changed -= Apply; stopping?.Dispose(); stopping = null; throw; }
            }
            finally { lifecycle.Release(); }
        }

        public async Task StopAsync()
        {
            await lifecycle.WaitAsync();
            try
            {
                source.Changed -= Apply;
                stopping?.Cancel(); wake.Set();
                if (worker != null) await worker;
                stopping?.Dispose(); stopping = null; worker = null;
                policy.Reset();
            }
            finally { lifecycle.Release(); }
        }

        private void Run(string page, CancellationToken token, TaskCompletionSource ready)
        {
            try
            {
                using var adapter = new BrowserAutomationAdapter(() => wake.Set());
                ready.TrySetResult();
                (BrowserRedirectVerification Verification, long Started)? pendingRedirect = null;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var observation = adapter.Read();
                        if (observation == null) { policy.Reset(); pendingRedirect = null; }
                        else
                        {
                            if (pendingRedirect is { } pending)
                            {
                                var status = pending.Verification.Observe(observation, Stopwatch.GetElapsedTime(pending.Started));
                                if (status != RedirectStatus.Pending)
                                {
                                    pendingRedirect = null;
                                }
                            }
                            var decision = policy.Evaluate(observation);
                            if (decision != null)
                            {
                                if (pendingRedirect == null)
                                {
                                    string destination = BlockPage.Destination(page, decision.Host);
                                    bool redirected = adapter.Redirect(decision, destination);
                                    if (redirected)
                                    {
                                        Debug.WriteLine($"UIA redirect submitted {decision.Host} timeout={decision.Timeout}");
                                        pendingRedirect = (new BrowserRedirectVerification(observation, destination), Stopwatch.GetTimestamp());
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Browser observation failed: {ex}");
                        // A transient UIA/COM provider failure is not a browser/tab switch.
                        // Keep the previous observation; the next successful read revalidates its identity.
                        if (ex is not COMException) policy.Reset();
                    }
                    // Event callbacks wake this thread; polling covers missed events, tab/window changes and cached loads.
                    wake.WaitOne(300);
                }
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
                try { Failed?.Invoke(ex); } catch { }
            }
            finally { source.Changed -= Apply; }
        }
    }
}

