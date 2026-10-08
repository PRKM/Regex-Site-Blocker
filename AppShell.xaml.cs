namespace Regex_Site_Blocker_App
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage)
        {
            InitializeComponent();
            SettingsContent.Content = mainPage;
        }
    }
}
