namespace Regex_Site_Blocker_App.Services
{
    public sealed partial class ConfigService
    {
        public ConfigService() : this(GetDefaultConfigPath())
        {
        }

        private static string GetDefaultConfigPath()
        {
#if WINDOWS
            return Path.Combine(AppContext.BaseDirectory, "config.yaml");
#else
            return Path.Combine(Microsoft.Maui.Storage.FileSystem.Current.AppDataDirectory, "config.yaml");
#endif
        }
    }
}
