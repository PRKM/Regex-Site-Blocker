using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace Regex_Site_Blocker_App.WinUI
{
    /// <summary>Owns the Windows notification icon and the main window's close-to-tray lifecycle.</summary>
    internal sealed class TrayIconService : IDisposable
    {
        private const uint CallbackMessage = 0x8001;
        private const uint IconId = 1;
        private const uint SettingsCommand = 1;
        private const uint ExitCommand = 2;
        private readonly Microsoft.UI.Xaml.Window window;
        private readonly Func<Task> beforeExit;
        private readonly nint hwnd;
        private readonly Native.SubclassProc windowProcedure;
        private readonly uint taskbarCreated;
        private Native.NotifyIconData iconData;
        private bool ownsIcon;
        private bool registered;
        private bool exiting;
        private bool disposed;

        public TrayIconService(Microsoft.UI.Xaml.Window window, Func<Task> beforeExit)
        {
            this.window = window;
            this.beforeExit = beforeExit;
            hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            windowProcedure = HandleWindowMessage;
            taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            if (!Native.SetWindowSubclass(hwnd, windowProcedure, 1, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "트레이 아이콘 메뉴를 등록하지 못했습니다.");

            try
            {
                // Allow Explorer notifications when the app runs elevated.
                Native.ChangeWindowMessageFilterEx(hwnd, CallbackMessage, 1, 0);
                if (taskbarCreated != 0)
                    Native.ChangeWindowMessageFilterEx(hwnd, taskbarCreated, 1, 0);

                nint icon = 0;
                if (Environment.ProcessPath is { } executable)
                {
                    Native.ExtractIconEx(executable, 0, out var large, out var small, 1);
                    icon = small != 0 ? small : large;
                    if (small != 0 && large != 0) Native.DestroyIcon(large);
                    ownsIcon = icon != 0;
                }
                if (icon == 0) icon = Native.LoadIcon(0, (nint)32512);
                iconData = new Native.NotifyIconData
                {
                    Size = (uint)Marshal.SizeOf<Native.NotifyIconData>(),
                    Window = hwnd,
                    Id = IconId,
                    Flags = 0x1 | 0x2 | 0x4 | 0x80, // message, icon, tooltip, show tooltip with v4
                    Callback = CallbackMessage,
                    Icon = icon,
                    Tip = "Regex Site Blocker",
                    Info = string.Empty,
                    InfoTitle = string.Empty
                };
                if (!AddIcon())
                    throw new InvalidOperationException("Windows 트레이 아이콘을 등록하지 못했습니다.");
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                window.AppWindow.Closing += OnClosing;
                window.Closed += OnClosed;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private bool AddIcon()
        {
            registered = Native.ShellNotifyIcon(0, ref iconData); // NIM_ADD
            if (registered)
            {
                iconData.Version = 4;
                Native.ShellNotifyIcon(4, ref iconData); // NIM_SETVERSION
            }
            return registered;
        }

        private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (disposed) return;
            // 트레이 아이콘의 메뉴를 통해서만 종료가 가능
            // 설정 창을 닫아도 프로그램 종료 처리하지 않음
            if (exiting)
            {
                args.Cancel = true;
                sender.Hide();
                return;
            }
            // Never hide the only way to reach the app if icon registration failed.
            if (!registered && !AddIcon()) return;
            args.Cancel = true;
            sender.Hide();
        }

        public void ShowSettings()
        {
            if (disposed || exiting) return;
            window.AppWindow.Show();
            if (window.AppWindow.Presenter is OverlappedPresenter presenter &&
                presenter.State == OverlappedPresenterState.Minimized)
                presenter.Restore();
            window.Activate();
            Native.SetForegroundWindow(hwnd);
        }

        private nint HandleWindowMessage(nint handle, uint message, nuint wParam, nint lParam,
            nuint subclassId, nuint referenceData)
        {
            // A managed exception must not cross the native callback boundary.
            try
            {
                if (message == taskbarCreated && taskbarCreated != 0 && !disposed)
                {
                    registered = false;
                    if (!AddIcon()) window.DispatcherQueue.TryEnqueue(ShowSettings);
                }
                if (message == CallbackMessage && !disposed)
                {
                    var notification = (uint)((long)lParam & 0xffff);
                    if (notification is 0x203 or 0x401) // double-click or keyboard activation
                        window.DispatcherQueue.TryEnqueue(ShowSettings);
                    else if (notification is 0x7b or 0x205) // WM_CONTEXTMENU or legacy right-button up
                        window.DispatcherQueue.TryEnqueue(ShowMenu);
                    return 0;
                }
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
            return Native.DefSubclassProc(handle, message, wParam, lParam);
        }

        private void ShowMenu()
        {
            if (disposed || exiting) return;
            var menu = Native.CreatePopupMenu();
            if (menu == 0) return;
            uint command;
            try
            {
                Native.AppendMenu(menu, 0, SettingsCommand, "설정");
                Native.AppendMenu(menu, 0, ExitCommand, "서비스 종료");
                Native.GetCursorPos(out var point);
                Native.SetForegroundWindow(hwnd);
                command = Native.TrackPopupMenuEx(menu, 0x100 | 0x2, point.X, point.Y, hwnd, 0);
                Native.PostMessage(hwnd, 0, 0, 0);
            }
            finally { Native.DestroyMenu(menu); }

            if (command == SettingsCommand) ShowSettings();
            else if (command == ExitCommand) ExitApplication();
        }

        private async void ExitApplication()
        {
            if (exiting || disposed) return;
            exiting = true;
            try
            {
                await beforeExit();
                Dispose();
                window.Close();
            }
            catch (Exception ex)
            {
                exiting = false;
                ShowSettings();
                Debug.WriteLine(ex);
            }
        }

        private void OnClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args) => Dispose();

        private void OnProcessExit(object? sender, EventArgs args) => RemoveIcon();

        private void RemoveIcon()
        {
            if (!registered) return;
            Native.ShellNotifyIcon(2, ref iconData); // NIM_DELETE
            registered = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            window.AppWindow.Closing -= OnClosing;
            window.Closed -= OnClosed;
            RemoveIcon();
            Native.RemoveWindowSubclass(hwnd, windowProcedure, 1);
            if (ownsIcon && iconData.Icon != 0) Native.DestroyIcon(iconData.Icon);
            GC.KeepAlive(windowProcedure);
        }

        private static class Native
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct NotifyIconData
            {
                public uint Size;
                public nint Window;
                public uint Id, Flags, Callback;
                public nint Icon;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
                public uint State, StateMask;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
                public uint Version;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
                public uint InfoFlags;
                public Guid Guid;
                public nint BalloonIcon;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct Point { public int X, Y; }

            [UnmanagedFunctionPointer(CallingConvention.Winapi)]
            internal delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam,
                nuint subclassId, nuint referenceData);

            [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
            [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
            internal static extern uint ExtractIconEx(string file, int index, out nint large, out nint small, uint count);
            [DllImport("comctl32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
            [DllImport("comctl32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
            [DllImport("comctl32.dll")]
            internal static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
            [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
            internal static extern uint RegisterWindowMessage(string message);
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint filter);
            [DllImport("user32.dll", EntryPoint = "LoadIconW")]
            internal static extern nint LoadIcon(nint instance, nint name);
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool DestroyIcon(nint icon);
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetForegroundWindow(nint hwnd);
            [DllImport("user32.dll")]
            internal static extern nint CreatePopupMenu();
            [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetCursorPos(out Point point);
            [DllImport("user32.dll")]
            internal static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool DestroyMenu(nint menu);
            [DllImport("user32.dll", EntryPoint = "PostMessageW")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
        }
    }
}
