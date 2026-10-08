using Microsoft.Extensions.Logging;

using Regex_Site_Blocker_App.Services;

namespace Regex_Site_Blocker_App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder.Services.AddSingleton(_ => new ConfigService());
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<AppShell>();
#if WINDOWS
            builder.Services.AddSingleton<WinUI.StartupRegistrationService>();
            builder.Services.AddSingleton(provider => new WinUI.BrowserBlockingService(provider.GetRequiredService<ConfigService>()));
            // WinUI의 기본 최소 너비(154)를 해제하여 스위치 자체 크기로 측정합니다.
            Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping("CompactWidth", (handler, view) =>
            {
                handler.PlatformView.MinWidth = 0;
            });
#endif
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
