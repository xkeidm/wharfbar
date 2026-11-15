using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WharfBar.Resources;
using WharfBar.Services;
using WharfBar.Views.Onboarding;
using Wpf.Ui.Appearance;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace WharfBar.Views;

/// <summary>
/// First-run setup wizard. Owned and shown by <see cref="App.ShowOnboarding"/> — modally on a
/// fresh install (<c>config.onboardingCompleted</c> false), or on demand from Settings → General.
///
/// The shell is already running when this window opens: each step edits the live config and calls
/// the matching <see cref="App"/> apply method, so the real top bar, dock and desktop behind the
/// window preview every choice. The window itself is only the rail + header + footer shell; one
/// <see cref="UserControl"/> per step lives in <c>Views/Onboarding/</c> and is recreated per visit.
/// </summary>
public partial class OnboardingWindow : Wpf.Ui.Controls.FluentWindow
{
    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;

    /// <summary>
    /// The step order. Titles/subtitles come from the resx; <see cref="StepDefinition.RailKey"/>
    /// is the short label in the left rail.
    /// </summary>
    private static readonly StepDefinition[] Steps =
    [
        new(
            Strings.Onboarding_StepWelcome_Title,
            Strings.Onboarding_StepWelcome_Subtitle,
            Strings.Onboarding_Rail_Welcome,
            SymbolRegular.Home24,
            static () => new WelcomeStep()),
        new(
            Strings.Onboarding_StepAppearance_Title,
            Strings.Onboarding_StepAppearance_Subtitle,
            Strings.Onboarding_Rail_Appearance,
            SymbolRegular.DarkTheme24,
            static () => new AppearanceStep()),
        new(
            Strings.Onboarding_StepTopBar_Title,
            Strings.Onboarding_StepTopBar_Subtitle,
            Strings.Onboarding_Rail_TopBar,
            SymbolRegular.Navigation24,
            static () => new TopBarStep()),
        new(
            Strings.Onboarding_StepDock_Title,
            Strings.Onboarding_StepDock_Subtitle,
            Strings.Onboarding_Rail_Dock,
            SymbolRegular.Apps24,
            static () => new DockStep()),
        new(
            Strings.Onboarding_StepWallpaper_Title,
            Strings.Onboarding_StepWallpaper_Subtitle,
            Strings.Onboarding_Rail_Wallpaper,
            SymbolRegular.Image24,
            static () => new WallpaperStep()),
        new(
            Strings.Onboarding_StepTour_Title,
            Strings.Onboarding_StepTour_Subtitle,
            Strings.Onboarding_Rail_Tour,
            SymbolRegular.Info24,
            static () => new TourStep()),
        new(
            Strings.Onboarding_StepFinish_Title,
            Strings.Onboarding_StepFinish_Subtitle,
            Strings.Onboarding_Rail_Finish,
            SymbolRegular.Checkmark24,
            static () => new FinishStep()),
    ];

    private readonly bool _replay;
    private readonly List<RailItem> _railItems = [];
    private int _index = -1;
    private int _stepTransitionGeneration;
    private DispatcherTimer? _wallpaperPrewarm;

    public OnboardingWindow()
        : this(replay: false)
    {
    }

    /// <param name="replay">
    /// Reopened from Settings instead of a fresh install: the final button says "Done" rather than
    /// "Start WharfBar", because the shell is already in use.
    /// </param>
    internal OnboardingWindow(bool replay)
    {
        InitializeComponent();
        _replay = replay;

        // The top bar is a 30 DIP AppBar, so WorkArea is already the usable screen. Keep the window
        // inside it on small displays instead of letting CenterScreen push the title bar under the
        // bar (or the footer under the dock).
        double maxHeight = SystemParameters.WorkArea.Height - 48;
        if (Height > maxHeight)
        {
            Height = Math.Max(MinHeight, maxHeight);
        }

        double maxWidth = SystemParameters.WorkArea.Width - 64;
        if (Width > maxWidth)
        {
            Width = Math.Max(MinWidth, maxWidth);
        }

        // Same policy as SettingsWindow: the library watcher would force the raw system theme;
        // AppThemeWatcher keeps config.theme authoritative and re-asserts the shell afterwards.
        AppThemeWatcher.Watch(this, Backdrop, () => (Application.Current as App)?.ApplyThemeChanges());
        // The wizard previews the live shell, so it crossfades with it on a light/dark change.
        ThemeCrossfadeRegistry.Register(this, () => Content as FrameworkElement);
        Closed += (_, _) => ThemeCrossfadeRegistry.Unregister(this);

        VersionText.Text = AppVersion.IsPrerelease
            ? string.Format(CultureInfo.CurrentCulture, Strings.About_VersionBetaFormat, AppVersion.Current)
            : string.Format(CultureInfo.CurrentCulture, Strings.About_VersionFormat, AppVersion.Current);

        BuildRail();
        Loaded += OnLoaded;

        // Skipped setup must not keep decoding gallery thumbnails for a step nobody will see.
        Closed += (_, _) => _wallpaperPrewarm?.Stop();
    }

    /// <summary>Re-asserts the window frame for the current app theme (in-app theme switch).</summary>
    internal void RefreshTheme() => AppThemeWatcher.RefreshWindow(this, Backdrop);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_index >= 0)
        {
            return;
        }

        Navigate(0, animate: false);

        // Decode the wallpaper gallery thumbnails in the background, but only after the opening
        // moments: the wallpaper step then appears with real previews instead of decoding ~20
        // photos inline (which used to freeze its transition for half a second), while the first
        // page and its transition are not competing with background decodes for CPU and GC.
        _wallpaperPrewarm = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _wallpaperPrewarm.Tick += (_, _) =>
        {
            _wallpaperPrewarm?.Stop();
            WallpaperStep.Prewarm(WallpaperPaths.BundledRoot, WallpaperPaths.UserRoot);
        };
        _wallpaperPrewarm.Start();
    }

    /// <summary>Switches to <paramref name="index"/> and rebuilds the step from the live config.</summary>
    private void Navigate(int index, bool animate = true)
    {
        if (index < 0 || index >= Steps.Length || index == _index)
        {
            return;
        }

        // Direction of travel drives the slide: forward enters from the right, back from the left.
        // A rail jump uses the same rule, so its direction follows the step order either way.
        OnboardingStepMotion.Direction direction = OnboardingStepMotion.DirectionFor(_index, index);
        _index = index;
        StepDefinition step = Steps[index];

        StepTitleText.Text = step.Title;
        StepSubtitleText.Text = step.Subtitle;

        // The pane is parked off its final spot (and invisible) BEFORE the new body is swapped in,
        // so the first visible frame belongs to the incoming page — never the outgoing one.
        PrepareStepTransition(direction, animate);

        // A fresh instance per visit: steps load their state in the ctor, so jumping back after
        // another step changed the config must not show stale values.
        StepHost.Content = step.Create();

        UpdateRail();
        UpdateFooter();

        if (animate)
        {
            PlayStepTransition();
        }

        // Enter always advances; the content starts unfocused so Tab walks the step controls.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => NextButton.Focus()));
    }

    private void BuildRail()
    {
        RailSteps.Children.Clear();
        _railItems.Clear();

        for (int i = 0; i < Steps.Length; i++)
        {
            StepDefinition definition = Steps[i];

            var accent = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(1.5),
                Margin = new Thickness(0, 0, 9, 0),
                Visibility = Visibility.Collapsed,
            };
            accent.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");

            var icon = new SymbolIcon
            {
                Symbol = definition.Icon,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var badge = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Margin = new Thickness(0, 0, 10, 0),
                Child = icon,
            };
            badge.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");

            var label = new TextBlock
            {
                Text = LocalizationService.Get(definition.RailKey),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(accent, 0);
            Grid.SetColumn(badge, 1);
            Grid.SetColumn(label, 2);
            row.Children.Add(accent);
            row.Children.Add(badge);
            row.Children.Add(label);

            var container = new Border
            {
                Padding = new Thickness(6, 7, 8, 7),
                Margin = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = row,
            };

            int target = i;
            container.MouseLeftButtonUp += (_, _) => Navigate(target);
            container.MouseEnter += (_, _) =>
            {
                if (target != _index)
                {
                    container.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
                }
            };
            container.MouseLeave += (_, _) =>
            {
                if (target != _index)
                {
                    container.Background = Brushes.Transparent;
                }
            };

            _railItems.Add(new RailItem(container, accent, badge, icon, label));
            RailSteps.Children.Add(container);
        }
    }

    private void UpdateRail()
    {
        for (int i = 0; i < _railItems.Count; i++)
        {
            RailItem item = _railItems[i];
            bool active = i == _index;
            bool done = i < _index;

            item.Accent.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

            if (active)
            {
                item.Container.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            }
            else
            {
                item.Container.Background = Brushes.Transparent;
            }

            item.BadgeIcon.Symbol = done ? SymbolRegular.Checkmark24 : Steps[i].Icon;
            item.Badge.SetResourceReference(
                Border.BackgroundProperty,
                active || done ? "AccentFillColorDefaultBrush" : "ControlFillColorDefaultBrush");
            item.BadgeIcon.SetResourceReference(
                SymbolIcon.ForegroundProperty,
                active || done ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");

            item.Label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            item.Label.SetResourceReference(
                TextBlock.ForegroundProperty,
                active || done ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        }
    }

    private void UpdateFooter()
    {
        bool last = _index == Steps.Length - 1;

        BackButton.Visibility = _index > 0 ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = last
            ? _replay ? Strings.Onboarding_FinishReplay : Strings.Onboarding_Finish
            : Strings.Onboarding_Next;
        StepOfText.Text = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Onboarding_StepOfFormat,
            _index + 1,
            Steps.Length);
    }

    /// <summary>
    /// Puts the whole step pane (title, subtitle, body) at its transition start: offset to the side
    /// the page comes from and transparent. With animations off the pane is simply left in place.
    /// </summary>
    private void PrepareStepTransition(OnboardingStepMotion.Direction direction, bool animate)
    {
        // Drop the previous transition's held opacity clock first: with it still attached, setting
        // Opacity (and the new animation's implicit From) would keep the pane at 1, so everything
        // after the first step would slide without fading.
        StepPane.BeginAnimation(OpacityProperty, null);

        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            StepPane.Opacity = 1;
            StepPane.RenderTransform = Transform.Identity;
            return;
        }

        StepPane.Opacity = 0;
        StepPane.RenderTransform = new TranslateTransform(
            OnboardingStepMotion.SlideOffset(direction),
            0);
    }

    /// <summary>
    /// Windows-style page transition: slide horizontally into place under an ease-out curve while
    /// fading in. Skipped (pane already in its final state) when client-area animation is off.
    /// </summary>
    private void PlayStepTransition()
    {
        if (!SystemParameters.ClientAreaAnimation || StepPane.RenderTransform is not TranslateTransform slide)
        {
            return;
        }

        IEasingFunction ease = OnboardingStepMotion.CreateEase();
        TimeSpan duration = OnboardingStepMotion.Duration;
        int generation = ++_stepTransitionGeneration;

        var fade = new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        // Release the clocks once settled, so the pane paints as a plain element again; a
        // superseded transition never reaches Completed with the current generation.
        fade.Completed += (_, _) => ReleaseStepTransition(generation, slide);

        StepPane.BeginAnimation(OpacityProperty, fade);
        slide.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(slide.X, 0, duration)
            {
                EasingFunction = ease,
                FillBehavior = FillBehavior.HoldEnd,
            });
    }

    /// <summary>Drops the held transition clocks after the page has settled (opacity 1, offset 0).</summary>
    private void ReleaseStepTransition(int generation, TranslateTransform slide)
    {
        if (generation != _stepTransitionGeneration)
        {
            return;
        }

        StepPane.Opacity = 1;
        slide.X = 0;
        StepPane.BeginAnimation(OpacityProperty, null);
        slide.BeginAnimation(TranslateTransform.XProperty, null);
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_index >= Steps.Length - 1)
        {
            Close();
            return;
        }

        Navigate(_index + 1);
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => Navigate(_index - 1);

    /// <summary>Skip (and the title-bar close button) keep the current settings and start the shell.</summary>
    private void OnSkipClick(object sender, RoutedEventArgs e) => Close();

    private sealed record StepDefinition(
        string Title,
        string Subtitle,
        string RailKey,
        SymbolRegular Icon,
        Func<UserControl> Create);

    private sealed record RailItem(
        Border Container,
        Border Accent,
        Border Badge,
        SymbolIcon BadgeIcon,
        TextBlock Label);
}
