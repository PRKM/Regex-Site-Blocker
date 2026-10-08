using Microsoft.Win32;

namespace Regex_Site_Blocker_App.WinUI
{
    public sealed class StartupRegistrationService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "RegexSiteBlocker";

        public void SetEnabled(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                ?? throw new InvalidOperationException("Windows 시작프로그램 설정을 열지 못했습니다.");
            if (enabled)
            {
                var executable = Environment.ProcessPath
                    ?? throw new InvalidOperationException("프로그램 실행 파일 경로를 확인하지 못했습니다.");
                key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }
}
