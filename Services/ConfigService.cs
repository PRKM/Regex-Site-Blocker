using System.Text;
using System.Text.RegularExpressions;
using Regex_Site_Blocker_App.Config;
using YamlDotNet.Serialization;

namespace Regex_Site_Blocker_App.Services
{
    public sealed partial class ConfigService : IConfigSource
    {
        private readonly SemaphoreSlim fileLock = new(1, 1);
        public string ConfigPath { get; }
        public event Action<AppConfig>? Changed;

        private void Publish(AppConfig config)
        {
            foreach (Action<AppConfig> subscriber in Changed?.GetInvocationList() ?? [])
            {
                try { subscriber(CopyAndValidate(config, validatePatterns: false)); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }

        public ConfigService(string configPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
            ConfigPath = Path.GetFullPath(configPath);
        }

        public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            await fileLock.WaitAsync(cancellationToken);
            try
            {
                string yaml;
                try
                {
                    yaml = await File.ReadAllTextAsync(ConfigPath, cancellationToken);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    var defaults = new AppConfig();
                    await WriteAsync(Serialize(defaults), ConfigPath, cancellationToken);
                    Publish(defaults);
                    return defaults;
                }

                if (string.IsNullOrWhiteSpace(yaml))
                    throw new InvalidDataException("설정 파일이 비어 있습니다.");
                var config = new DeserializerBuilder().WithDuplicateKeyChecking().Build().Deserialize<AppConfig>(yaml)
                    ?? throw new InvalidDataException("설정 파일의 내용이 올바르지 않습니다.");
                // Preserve invalid patterns on disk/in the editor; filtering skips just those rules.
                var snapshot = CopyAndValidate(config, validatePatterns: false);
                Publish(snapshot);
                return snapshot;
            }
            finally { fileLock.Release(); }
        }

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
            => SaveAsync(config, ConfigPath, cancellationToken);

        public async Task SaveAsync(AppConfig config, string filePath, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            cancellationToken.ThrowIfCancellationRequested();
            // 대기 전에 스냅샷을 만들어 변경 순서대로 저장합니다.
            var snapshot = CopyAndValidate(config);
            var yaml = Serialize(snapshot);
            var destination = Path.GetFullPath(filePath);
            await fileLock.WaitAsync(cancellationToken);
            try
            {
                await WriteAsync(yaml, destination, cancellationToken);
                if (string.Equals(destination, ConfigPath, StringComparison.OrdinalIgnoreCase)) Publish(snapshot);
            }
            finally { fileLock.Release(); }
        }

        private static string Serialize(AppConfig config)
            => new SerializerBuilder().Build().Serialize(CopyAndValidate(config));

        private static AppConfig CopyAndValidate(AppConfig config, bool validatePatterns = true)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(config.Blacklist);
            var snapshot = new AppConfig { StartProgram = config.StartProgram };
            foreach (var rule in config.Blacklist)
            {
                ArgumentNullException.ThrowIfNull(rule);
                if (validatePatterns)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(rule.Pattern);
                    _ = new Regex(rule.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(50));
                }
                snapshot.Blacklist.Add(new BlacklistRule { Pattern = rule.Pattern ?? string.Empty, Enable = rule.Enable });
            }
            return snapshot;
        }

        private static async Task WriteAsync(string yaml, string destination, CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporaryPath, yaml, new UTF8Encoding(false), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, destination, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
