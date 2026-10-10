using Microsoft.Win32;
using System.Security.Principal;
using System.Xml.Linq;

namespace Regex_Site_Blocker_App.WinUI
{
    public sealed class StartupRegistrationService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "RegexSiteBlocker";
        private readonly IStartupTaskScheduler taskScheduler;
        private readonly Func<string> getUserSid;
        private readonly Func<string> getExecutablePath;
        private readonly Action removeLegacyRegistration;

        public StartupRegistrationService()
            : this(new WindowsStartupTaskScheduler(), GetCurrentUserSid,
                () => Environment.ProcessPath
                    ?? throw new InvalidOperationException("프로그램 실행 파일 경로를 확인하지 못했습니다."),
                RemoveLegacyRegistration)
        {
        }

        internal StartupRegistrationService(IStartupTaskScheduler taskScheduler, Func<string> getUserSid,
            Func<string> getExecutablePath, Action removeLegacyRegistration)
        {
            this.taskScheduler = taskScheduler;
            this.getUserSid = getUserSid;
            this.getExecutablePath = getExecutablePath;
            this.removeLegacyRegistration = removeLegacyRegistration;
        }

        public void SetEnabled(bool enabled)
        {
            var userSid = getUserSid();
            // Task names are machine-wide, so keep each user's registration separate.
            var taskName = $"{ValueName}-{userSid}";
            var previousTaskXml = taskScheduler.Read(taskName);
            if (enabled)
            {
                var executable = getExecutablePath();
                taskScheduler.Register(taskName, CreateTaskXml(userSid, executable), userSid);
            }
            else
            {
                taskScheduler.Delete(taskName);
            }
            // Only remove the old Run entry after the scheduler operation succeeds.
            try { removeLegacyRegistration(); }
            catch (Exception cleanupError)
            {
                try
                {
                    if (previousTaskXml is null) taskScheduler.Delete(taskName);
                    else taskScheduler.Register(taskName, previousTaskXml, userSid);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("기존 시작프로그램 항목 제거와 예약 작업 복원에 실패했습니다.", cleanupError, rollbackError);
                }
                throw;
            }
        }

        internal static string CreateTaskXml(string userSid, string executable)
        {
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            return new XElement(ns + "Task", new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "Regex Site Blocker 로그인 시 트레이 실행")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", true),
                        new XElement(ns + "UserId", userSid))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal", new XAttribute("id", "CurrentUser"),
                        new XElement(ns + "UserId", userSid),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        // Match the application's requireAdministrator manifest.
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", false),
                    new XElement(ns + "StopIfGoingOnBatteries", false),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Enabled", true)),
                new XElement(ns + "Actions", new XAttribute("Context", "CurrentUser"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", executable),
                        new XElement(ns + "Arguments", "--startup"),
                        new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(executable)))))
                .ToString();
        }

        private static string GetCurrentUserSid()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value
                ?? throw new InvalidOperationException("현재 Windows 사용자 정보를 확인하지 못했습니다.");
        }

        private static void RemoveLegacyRegistration()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
