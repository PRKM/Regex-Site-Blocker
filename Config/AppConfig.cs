using YamlDotNet.Serialization;

using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Regex_Site_Blocker_App.Config
{
    public sealed class AppConfig : INotifyPropertyChanged
    {
        private bool startProgram;
        public event PropertyChangedEventHandler? PropertyChanged;
        // Windows 로그인 시 창 없이 트레이로 시작합니다.
        [YamlMember(Alias = "start-program", ApplyNamingConventions = false)]
        public bool StartProgram
        {
            get => startProgram;
            set
            {
                if (startProgram == value) return;
                startProgram = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartProgram)));
            }
        }

        [YamlMember(Alias = "blacklist", ApplyNamingConventions = false)]
        public ObservableCollection<BlacklistRule> Blacklist { get; set; } = [];
    }
}
