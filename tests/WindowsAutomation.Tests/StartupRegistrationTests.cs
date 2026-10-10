using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using Regex_Site_Blocker_App.WinUI;
using Xunit;

public sealed class StartupRegistrationTests
{
    private const string UserSid = "S-1-5-21-111-222-333-1001";
    private const string Executable = @"C:\프로그램 & 도구\Regex Site Blocker.exe";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void StartupRunsInCurrentUsersSessionWithoutBatteryOrTimeLimits()
    {
        var task = XElement.Parse(StartupRegistrationService.CreateTaskXml(UserSid, Executable));
        var trigger = Assert.Single(task.Element(Ns + "Triggers")!.Elements());
        Assert.Equal(Ns + "LogonTrigger", trigger.Name);
        Assert.Equal(UserSid, trigger.Element(Ns + "UserId")!.Value);
        var principal = task.Element(Ns + "Principals")!.Element(Ns + "Principal")!;
        Assert.Equal(UserSid, principal.Element(Ns + "UserId")!.Value);
        Assert.Equal("InteractiveToken", principal.Element(Ns + "LogonType")!.Value);
        Assert.Equal("HighestAvailable", principal.Element(Ns + "RunLevel")!.Value);
        var exec = task.Element(Ns + "Actions")!.Element(Ns + "Exec")!;
        Assert.Equal(Executable, exec.Element(Ns + "Command")!.Value);
        Assert.Equal("--startup", exec.Element(Ns + "Arguments")!.Value);
        Assert.Equal(Path.GetDirectoryName(Executable), exec.Element(Ns + "WorkingDirectory")!.Value);
        var settings = task.Element(Ns + "Settings")!;
        Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value);
        Assert.Equal("false", settings.Element(Ns + "StopIfGoingOnBatteries")!.Value);
        Assert.Equal("PT0S", settings.Element(Ns + "ExecutionTimeLimit")!.Value);
        Assert.Equal("IgnoreNew", settings.Element(Ns + "MultipleInstancesPolicy")!.Value);
    }

    [Fact]
    public void EnablingUpdatesSameUserTaskBeforeRemovingLegacyRegistration()
    {
        var scheduler = new Scheduler();
        var cleaned = 0;
        var service = CreateService(scheduler, () =>
        {
            Assert.NotNull(scheduler.Xml);
            cleaned++;
        });
        Assert.Empty(scheduler.TaskNames);
        service.SetEnabled(true);
        service.SetEnabled(true);
        Assert.All(scheduler.TaskNames, name => Assert.Equal($"RegexSiteBlocker-{UserSid}", name));
        Assert.Equal(2, scheduler.RegisterCount);
        Assert.Equal(2, cleaned);
    }

    [Fact]
    public void DisablingRemovesTaskAndLegacyEntryEvenWhenTaskWasNeverRegistered()
    {
        var scheduler = new Scheduler { Xml = "previous XML" };
        var cleaned = 0;
        var service = CreateService(scheduler, () => { Assert.Null(scheduler.Xml); cleaned++; });
        service.SetEnabled(false);
        service.SetEnabled(false);
        Assert.Null(scheduler.Xml);
        Assert.Equal(0, scheduler.RegisterCount);
        Assert.Equal(2, cleaned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SchedulerFailurePreservesLegacyRegistration(bool enabled)
    {
        var scheduler = new Scheduler { FailOperation = true };
        var cleaned = false;
        var service = CreateService(scheduler, () => cleaned = true);
        Assert.Throws<InvalidOperationException>(() => service.SetEnabled(enabled));
        Assert.False(cleaned);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "previous XML")]
    [InlineData(false, "previous XML")]
    public void LegacyCleanupFailureRestoresPreviousTask(bool enabled, string? previousXml)
    {
        var scheduler = new Scheduler { Xml = previousXml };
        var service = CreateService(scheduler, () => throw new IOException("cleanup failed"));
        Assert.Throws<IOException>(() => service.SetEnabled(enabled));
        Assert.Equal(previousXml, scheduler.Xml);
    }

    [Fact]
    public void MissingWindowsTaskCanBeReadAndDeletedWithoutError()
    {
        var scheduler = new WindowsStartupTaskScheduler();
        var taskName = $"RegexSiteBlocker-MissingTest-{Guid.NewGuid():N}";
        Assert.Null(scheduler.Read(taskName));
        scheduler.Delete(taskName);
    }

    [Fact]
    public void WindowsSchedulerValidatesXmlWithoutRegisteringATask()
    {
        // TASK_VALIDATE_ONLY checks the real scheduler schema without modifying live tasks.
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        object? service = null;
        object? folder = null;
        object? task = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            ((dynamic)service!).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            task = ((dynamic)folder).RegisterTask("RegexSiteBlocker-ValidationOnly",
                StartupRegistrationService.CreateTaskXml(sid, Executable), 1, sid, null, 3, null);
        }
        finally
        {
            foreach (var value in new[] { task, folder, service })
                if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }

    private static StartupRegistrationService CreateService(Scheduler scheduler, Action cleanup) =>
        new(scheduler, () => UserSid, () => Executable, cleanup);

    private sealed class Scheduler : IStartupTaskScheduler
    {
        public string? Xml;
        public bool FailOperation;
        public int RegisterCount;
        public List<string> TaskNames { get; } = [];

        public string? Read(string taskName) { TaskNames.Add(taskName); return Xml; }
        public void Register(string taskName, string taskXml, string userSid)
        {
            if (FailOperation) throw new InvalidOperationException("registration failed");
            Assert.Equal(UserSid, userSid);
            TaskNames.Add(taskName);
            Xml = taskXml;
            RegisterCount++;
        }
        public void Delete(string taskName)
        {
            if (FailOperation) throw new InvalidOperationException("deletion failed");
            TaskNames.Add(taskName);
            Xml = null;
        }
    }
}
