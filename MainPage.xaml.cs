using System.Text.RegularExpressions;
using Regex_Site_Blocker_App.Config;
using System.Collections.Specialized;
using System.ComponentModel;
using Regex_Site_Blocker_App.Services;

namespace Regex_Site_Blocker_App
{
    public partial class MainPage : ContentPage
    {
        private BlacklistRule? menuRule;
        private bool editing;

        private readonly ConfigService configService;
#if WINDOWS
        private readonly WinUI.StartupRegistrationService startupRegistration;
#endif
        private readonly HashSet<BlacklistRule> observedRules = [];
        private AppConfig? currentConfig; // 화면에 바인딩된 Config 객체
        private AppConfig? savedConfig; // 서비스에 마지막으로 저장된 Config 상태
        private bool loaded;
        private bool loading;
        private bool saving;
        private Task pendingSave = Task.CompletedTask;

        public Task WaitForPendingSaveAsync() => pendingSave;

        public MainPage(ConfigService configService
#if WINDOWS
            , WinUI.StartupRegistrationService startupRegistration
#endif
            )
        {
            this.configService = configService;
#if WINDOWS
            this.startupRegistration = startupRegistration;
#endif
            InitializeComponent();
            PageRoot.IsEnabled = false;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (loaded || loading) return;
            loading = true;
            try
            {
                var config = await configService.LoadAsync();
                savedConfig = CopyConfig(config);
                BindConfig(config);
                loaded = true;
                PageRoot.IsEnabled = true;
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("설정 불러오기 실패",
                    $"{configService.ConfigPath}\n{ex.Message}\n기존 파일은 변경하지 않았습니다.", "확인");
            }
            finally { loading = false; }
        }

        private static AppConfig CopyConfig(AppConfig source) => new()
        {
            StartProgram = source.StartProgram,
            Blacklist = new(source.Blacklist.Select(rule => new BlacklistRule
            {
                Pattern = rule.Pattern,
                Enable = rule.Enable
            }))
        };

        private void BindConfig(AppConfig config)
        {
            if (currentConfig is not null)
            {
                currentConfig.PropertyChanged -= OnConfigChanged;
                currentConfig.Blacklist.CollectionChanged -= OnRulesChanged;
            }
            foreach (var rule in observedRules) rule.PropertyChanged -= OnRuleChanged;
            observedRules.Clear();
            currentConfig = config;
            BindingContext = config;
            config.PropertyChanged += OnConfigChanged;
            config.Blacklist.CollectionChanged += OnRulesChanged;
            ObserveRules();
        }

        private void ObserveRules()
        {
            foreach (var rule in observedRules) rule.PropertyChanged -= OnRuleChanged;
            observedRules.Clear();
            if (currentConfig is null) return;
            foreach (var rule in currentConfig.Blacklist)
                if (observedRules.Add(rule)) rule.PropertyChanged += OnRuleChanged;
        }

        private async void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppConfig.StartProgram)) await (pendingSave = SaveChangesAsync());
        }

        private async void OnRuleChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(BlacklistRule.Pattern) or nameof(BlacklistRule.Enable))
                await (pendingSave = SaveChangesAsync());
        }

        private async void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ObserveRules();
            await (pendingSave = SaveChangesAsync());
        }

        private async Task SaveChangesAsync()
        {
            if (!loaded || saving || currentConfig is null) return;
            saving = true;
            PageRoot.IsEnabled = false;
            CloseRuleMenu();
            try
            {
                var snapshot = CopyConfig(currentConfig);
#if WINDOWS
                var startupChanged = savedConfig is not null && snapshot.StartProgram != savedConfig.StartProgram;
                if (startupChanged) startupRegistration.SetEnabled(snapshot.StartProgram);
                try
                {
                    await configService.SaveAsync(snapshot);
                }
                catch (Exception saveError)
                {
                    if (startupChanged)
                    {
                        try { startupRegistration.SetEnabled(savedConfig!.StartProgram); }
                        catch (Exception rollbackError)
                        {
                            throw new AggregateException("설정 저장과 시작프로그램 등록 복원에 실패했습니다.", saveError, rollbackError);
                        }
                    }
                    throw;
                }
#else
                await configService.SaveAsync(snapshot);
#endif
                savedConfig = snapshot;
            }
            catch (Exception ex)
            {
                if (savedConfig is not null) BindConfig(CopyConfig(savedConfig));
                await DisplayAlertAsync("설정 저장 실패",
                    $"{configService.ConfigPath}\n{ex.Message}\n변경 내용을 마지막 저장 상태로 되돌렸습니다.", "확인");
            }
            finally
            {
                saving = false;
                PageRoot.IsEnabled = true;
            }
        }

        private void CloseRuleMenu()
        {
            if (MenuOverlay is not null)
                MenuOverlay.IsVisible = false;
            menuRule = null;
        }

        private void OnRuleMenuClicked(object? sender, EventArgs e)
        {
            if (sender is not ImageButton { BindingContext: BlacklistRule rule } button)
                return;

            CloseRuleMenu();
            
            // 메뉴 좌표 계산
            // 공통 MAUI 좌표를 사용하며 스크롤된 거리를 제외합니다.
            double x = 0, y = 0;
            Element? element = button;
            while (element is not null && element != PageRoot)
            {
                if (element is VisualElement visual)
                {
                    x += visual.X;
                    y += visual.Y;
                }
                if (element is ScrollView scroll)
                {
                    x -= scroll.ScrollX;
                    y -= scroll.ScrollY;
                }
                element = element.Parent;
            }
            if (element != PageRoot || PageRoot.Width <= 0 || PageRoot.Height <= 0)
                return;
            var width = Math.Min(100, Math.Max(0, PageRoot.Width - 16));
            var height = 100d;
            var left = Math.Clamp(x + button.Width - width, 8, Math.Max(8, PageRoot.Width - width - 8));
            var top = y + button.Height + 4;
            if (top + height > PageRoot.Height - 8)
                top = y - height - 4;
            top = Math.Clamp(top, 8, Math.Max(8, PageRoot.Height - height - 8));
            AbsoluteLayout.SetLayoutBounds(RuleMenu, new Rect(left, top, width, height));

            // 메뉴와 매핑되는 URL 행 아이템 설정
            menuRule = rule;

            MenuOverlay.IsVisible = true;
        }

        private void OnMenuDismissed(object? sender, TappedEventArgs e) => CloseRuleMenu();
        private void OnPageSizeChanged(object? sender, EventArgs e) => CloseRuleMenu();
        private void OnRulesScrolled(object? sender, ScrolledEventArgs e) => CloseRuleMenu();

        private void OnDeleteMenuClicked(object? sender, EventArgs e)
        {
            if (menuRule is { } rule) DeleteRule(rule);
        }

        private async void OnEditMenuClicked(object? sender, EventArgs e)
        {
            if (menuRule is { } rule) await EditRuleAsync(rule);
        }

        protected override bool OnBackButtonPressed()
        {
            if (MenuOverlay.IsVisible)
            {
                CloseRuleMenu();
                return true;
            }
            return base.OnBackButtonPressed();
        }
        private void DeleteRule(BlacklistRule rule)
        {
            CloseRuleMenu();
            if (BindingContext is AppConfig config)
                config.Blacklist.Remove(rule);
        }

        private async Task EditRuleAsync(BlacklistRule rule)
        {
            if (editing)
                return;

            CloseRuleMenu();
            editing = true;
            try
            {
                var input = rule.Pattern;
                while (true)
                {
                    input = await DisplayPromptAsync("규칙 수정", "URL (도메인 정규식)",
                        accept: "저장", cancel: "취소", initialValue: input);
                    if (input is null)
                        return;
                    if (!await ValidatePatternAsync(input))
                        continue;
                    if (BindingContext is AppConfig config && config.Blacklist.Contains(rule))
                        rule.Pattern = input;
                    return;
                }
            }
            finally { editing = false; }
        }

        protected override void OnDisappearing()
        {
            CloseRuleMenu();
            base.OnDisappearing();
        }

        private async void OnAddRuleClicked(object? sender, EventArgs e)
        {
            var pattern = NewRuleEntry.Text ?? string.Empty;
            if (!await ValidatePatternAsync(pattern))
                return;
            if (BindingContext is AppConfig config)
            {
                CloseRuleMenu();
                config.Blacklist.Add(new BlacklistRule { Pattern = pattern });
                NewRuleEntry.Text = string.Empty;
            }
        }

        private async Task<bool> ValidatePatternAsync(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                await DisplayAlertAsync("입력 오류", "도메인 정규식을 입력해주세요.", "확인");
                return false;
            }
            try
            {
                _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(50));
                return true;
            }
            catch (ArgumentException ex)
            {
                await DisplayAlertAsync("정규식 오류", ex.Message, "확인");
                return false;
            }
        }
    }
}
