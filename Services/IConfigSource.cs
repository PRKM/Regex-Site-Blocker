using Regex_Site_Blocker_App.Config;

namespace Regex_Site_Blocker_App.Services
{
    public interface IConfigSource
    {
        event Action<AppConfig>? Changed;
        Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default);
    }
}
