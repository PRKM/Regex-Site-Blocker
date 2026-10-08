using Microsoft.Extensions.DependencyInjection;

namespace Regex_Site_Blocker_App
{
    public partial class App : Application
    {
        private readonly AppShell shell;
        private readonly MainPage mainPage;
#if WINDOWS
        private WinUI.TrayIconService? trayIcon;
        private WinUI.BrowserBlockingService? browserBlocker;
#endif

        public App(AppShell shell, MainPage mainPage)
        {
            InitializeComponent();
            this.shell = shell;
            this.mainPage = mainPage;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(shell);
#if WINDOWS
            window.Created += async (_, _) =>
            {
                try
                {
                    if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
                    {
                        trayIcon = new WinUI.TrayIconService(nativeWindow, async () =>
                        {
                            await mainPage.WaitForPendingSaveAsync();
                            if (browserBlocker != null) await browserBlocker.StopAsync();
                        });
                        if (Environment.GetCommandLineArgs().Contains("--startup", StringComparer.OrdinalIgnoreCase))
                        {
                            // 시작프로그램으로써 자동 실행될 경우, 설정 창은 띄우지 않고 트레이 아이콘만 띄워서 실행
                            nativeWindow.AppWindow.Hide();
                            void HideOnFirstActivation(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
                            {
                                nativeWindow.Activated -= HideOnFirstActivation;
                                nativeWindow.AppWindow.Hide();
                            }
                            nativeWindow.Activated += HideOnFirstActivation;
                            nativeWindow.DispatcherQueue.TryEnqueue(() =>
                            {
                                nativeWindow.Activated -= HideOnFirstActivation;
                                nativeWindow.AppWindow.Hide();
                            });
                        }
                    }
                    browserBlocker = Handler!.MauiContext!.Services.GetRequiredService<WinUI.BrowserBlockingService>();
                    browserBlocker.Failed += ex => MainThread.BeginInvokeOnMainThread(async () =>
                        await shell.DisplayAlertAsync("브라우저 차단 중단", ex.Message, "확인"));
                    await browserBlocker.StartAsync();
                    if (browserBlocker.Issues.Length > 0)
                        await shell.DisplayAlertAsync("일부 규칙 제외", string.Join("\n", browserBlocker.Issues.Select(issue => issue.Pattern)), "확인");
                }
                catch (Exception ex)
                {
                    await shell.DisplayAlertAsync("차단 초기화 실패", ex.Message, "확인");
                }
            };
            window.Destroying += async (_, _) =>
            {
                try
                {
                    if (browserBlocker != null) await browserBlocker.StopAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
                finally
                {
                    trayIcon?.Dispose();
                    trayIcon = null;
                }
            };
#endif
            return window;
        }
    }
}
