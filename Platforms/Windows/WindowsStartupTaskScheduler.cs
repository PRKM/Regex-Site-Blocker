using System.Runtime.InteropServices;

namespace Regex_Site_Blocker_App.WinUI
{
    internal interface IStartupTaskScheduler
    {
        string? Read(string taskName);
        void Register(string taskName, string taskXml, string userSid);
        void Delete(string taskName);
    }

    internal sealed class WindowsStartupTaskScheduler : IStartupTaskScheduler
    {
        private const int TaskCreateOrUpdate = 6;
        private const int TaskLogonInteractiveToken = 3;
        private const int FileNotFound = unchecked((int)0x80070002);

        public string? Read(string taskName)
        {
            string? xml = null;
            WithTaskFolder(folder =>
            {
                object? task = null;
                try
                {
                    task = folder.GetTask(taskName);
                    xml = ((dynamic)task).Xml;
                }
                catch (Exception ex) when (ex.HResult == FileNotFound)
                {
                    // No previous registration to restore.
                }
                finally { Release(task); }
            });
            return xml;
        }

        public void Register(string taskName, string taskXml, string userSid)
        {
            WithTaskFolder(folder =>
            {
                object? task = null;
                try
                {
                    task = folder.RegisterTask(taskName, taskXml, TaskCreateOrUpdate,
                        userSid, null, TaskLogonInteractiveToken, null);
                }
                finally { Release(task); }
            });
        }

        public void Delete(string taskName)
        {
            WithTaskFolder(folder =>
            {
                try { folder.DeleteTask(taskName, 0); }
                catch (Exception ex) when (ex.HResult == FileNotFound)
                {
                    // Turning startup off is also valid when no task has been registered.
                }
            });
        }

        private static void WithTaskFolder(Action<dynamic> operation)
        {
            object? service = null;
            object? folder = null;
            try
            {
                var schedulerType = Type.GetTypeFromProgID("Schedule.Service")
                    ?? throw new InvalidOperationException("Windows 작업 스케줄러를 찾지 못했습니다.");
                service = Activator.CreateInstance(schedulerType)
                    ?? throw new InvalidOperationException("Windows 작업 스케줄러를 열지 못했습니다.");
                ((dynamic)service).Connect();
                folder = ((dynamic)service).GetFolder(@"\");
                operation(folder);
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Windows 작업 스케줄러의 시작프로그램 설정을 변경하지 못했습니다. {ex.Message}", ex);
            }
            finally
            {
                Release(folder);
                Release(service);
            }
        }

        private static void Release(object? value)
        {
            if (value is not null && Marshal.IsComObject(value))
                Marshal.ReleaseComObject(value);
        }
    }
}
