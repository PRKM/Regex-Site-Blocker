using System.ComponentModel;
using YamlDotNet.Serialization;

namespace Regex_Site_Blocker_App.Config
{
    public sealed class BlacklistRule : INotifyPropertyChanged
    {
        private string pattern = string.Empty;
        private bool enable;

        public event PropertyChangedEventHandler? PropertyChanged;

        [YamlMember(Alias = "pattern", ApplyNamingConventions = false)]
        public string Pattern
        {
            get => pattern;
            set
            {
                if (pattern == value) return;
                pattern = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pattern)));
            }
        }

        [YamlMember(Alias = "enable", ApplyNamingConventions = false)]
        public bool Enable
        {
            get => enable;
            set
            {
                if (enable == value) return;
                enable = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enable)));
            }
        }
    }
}
