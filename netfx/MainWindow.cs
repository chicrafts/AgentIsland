using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace AgentIsland
{
    public sealed partial class MainWindow : Window
    {
        private readonly AppSettings settings;
        private Image agentIconImage;
        private string agentIconKey;
        private TextBlock agentText;
        private TextBlock statusText;
        private TextBlock detailText;
        private Ellipse signalCore;
        private Ellipse signalTailA;
        private Ellipse signalTailB;
        private Ellipse pulseRing;
        private Ellipse pulseRingB;
        private Ellipse coreDot;
        private Border flowTrack;
        private Border flowBar;
        private GradientStop glowStopA;
        private GradientStop flowStop;
        private Border islandBorder;
        private ScaleTransform islandScale;
        private ScaleTransform morphScale;
        private ScaleTransform switchScale;
        private SkewTransform switchSkew;
        private TranslateTransform islandTranslate;
        private TranslateTransform switchTranslate;
        private DispatcherTimer autoHideTimer;
        private StatusSnapshot lastSnapshot;
        private DropShadowEffect islandShadow;
        private DropShadowEffect dotShadow;
        private ScaleTransform pulseScale;
        private ScaleTransform pulseScaleB;
        private TranslateTransform flowTranslate;
        private DropShadowEffect signalCoreShadow;
        private const double ShellWindowWidth = 448;
        private const double ShellWindowHeight = 126;
        private const double ConversationRowHeight = 54;
        private const double ConversationRowGap = 8;
        private const double IslandDragThreshold = 7;
        private Border stackCardA;
        private Border stackCardB;
        private Grid conversationListScroller;
        private StackPanel conversationListStack;
        private TranslateTransform conversationListTranslate;
        private DispatcherTimer outsideClickTimer;
        private bool leftButtonWasDown;
        private Point islandPressPoint;
        private bool islandPressTracking;
        private bool conversationListExpanded;
        private int activeConversationCount;
        private bool flowRunning;
        private bool flowBusy;
        private Action<string> conversationClearHandler;

        public MainWindow(AppSettings settings)
        {
            this.settings = settings;
            lastSnapshot = StatusSnapshot.Idle();
            Title = "Agent Island";
            Width = ShellWindowWidth;
            Height = ShellWindowHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Opacity = 0;
            autoHideTimer = new DispatcherTimer();
            autoHideTimer.Tick += delegate
            {
                autoHideTimer.Stop();
                HideIsland();
            };
            outsideClickTimer = new DispatcherTimer();
            outsideClickTimer.Interval = TimeSpan.FromMilliseconds(60);
            outsideClickTimer.Tick += delegate { CheckOutsideClick(); };
            PreviewMouseDown += OnWindowPreviewMouseDown;
            Deactivated += delegate { CollapseConversationList(); };
            settings.Changed += delegate { ApplySettings(); };
            Content = BuildUi();
            InitializeCompactBehavior();

            Loaded += delegate
            {
                PositionTopCenter();
                StartAnimations();
                ApplyStatus(StatusSnapshot.Idle());
                ShowIsland();
            };
        }

        public void ApplyStatus(StatusSnapshot snapshot)
        {
            var previousSnapshot = lastSnapshot;
            var shouldAnimateConversationSwitch = ShouldAnimateTopConversationSwitch(previousSnapshot, snapshot);
            lastSnapshot = snapshot;
            ShowIsland();
            PrepareExpandedStatus();

            var accent = AccentFor(snapshot.Mode);
            expandedTargetWidth = WidthFor(snapshot.Mode);
            var morphProgress = morphAnimator != null ? morphAnimator.Position : (compacted ? 0.0 : 1.0);
            ApplyMorphProgress(morphProgress, false);
            AnimateColor((SolidColorBrush)coreDot.Fill, accent);
            AnimateColor((SolidColorBrush)pulseRing.Stroke, accent);
            AnimateColor((SolidColorBrush)pulseRingB.Stroke, accent);
            var flowColor = new ColorAnimation(accent, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            flowColor.SetValue(Timeline.DesiredFrameRateProperty, 60);
            flowStop.BeginAnimation(GradientStop.ColorProperty, flowColor);

            var glowColor = new ColorAnimation(accent, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            glowColor.SetValue(Timeline.DesiredFrameRateProperty, 60);
            glowStopA.BeginAnimation(GradientStop.ColorProperty, glowColor);
            dotShadow.Color = accent;
            ApplyShellPalette();

            ApplySignal(snapshot.Mode, accent);
            UpdateCompactStatus(snapshot, accent);
            UpdateConversationList(snapshot);

            UpdateExpandedAgent(snapshot.Agent);
            CrossfadeText(statusText, snapshot.Title);
            CrossfadeText(detailText, DisplayDetail(snapshot));

            pulseRing.Opacity = snapshot.Mode == IslandMode.Idle ? 0.10 : 0.36;
            pulseRingB.Opacity = snapshot.Mode == IslandMode.Idle ? 0.08 : 0.30;
            SetFlowActive(snapshot.Mode != IslandMode.Idle && snapshot.Mode != IslandMode.Done);
            if (shouldAnimateConversationSwitch)
            {
                PlayTopConversationSwitchAnimation();
            }
            ScheduleAutoHide(snapshot.Mode);
            if (snapshot.Mode == IslandMode.Waiting)
            {
                SetCompact(false);
            }
            ScheduleCompact();
        }

        private void UpdateExpandedAgent(string agent)
        {
            if (agentText != null)
            {
                agentText.Text = agent;
            }

            if (agentIconImage == null)
            {
                return;
            }

            var key = CompactAgentKey(agent);
            if (string.IsNullOrWhiteSpace(key))
            {
                agentIconKey = null;
                agentIconImage.Source = null;
                agentIconImage.Visibility = Visibility.Collapsed;
                return;
            }

            if (agentIconKey == key && agentIconImage.Source != null)
            {
                agentIconImage.Visibility = Visibility.Visible;
                return;
            }

            var source = LoadCompactAgentIcon(key);
            if (source == null)
            {
                agentIconKey = null;
                agentIconImage.Source = null;
                agentIconImage.Visibility = Visibility.Collapsed;
                return;
            }

            agentIconKey = key;
            agentIconImage.Source = source;
            agentIconImage.Visibility = Visibility.Visible;
            var fade = new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(190));
            fade.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };
            fade.SetValue(Timeline.DesiredFrameRateProperty, 60);
            agentIconImage.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }
        public void SetConversationClearHandler(Action<string> handler)
        {
            conversationClearHandler = handler;
        }
        public void ShowIsland()
        {
            if (!IsVisible)
            {
                Show();
            }

            if (Opacity > 0.97 && islandScale.ScaleX > 0.97 && islandScale.ScaleY > 0.97)
            {
                return;
            }

            var duration = TimeSpan.FromMilliseconds(settings.CalmMotion ? 340 : 220);
            if (Opacity < 0.08)
            {
                islandScale.ScaleX = 0.18;
                islandScale.ScaleY = 0.74;
            }

            var opacityAnim = new DoubleAnimation(1, duration)
            {
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, opacityAnim, HandoffBehavior.SnapshotAndReplace);

            var scaleX = new DoubleAnimation(1, duration)
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.12 }
            };
            islandScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX, HandoffBehavior.SnapshotAndReplace);

            var scaleY = new DoubleAnimation(1, duration)
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.08 }
            };
            islandScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY, HandoffBehavior.SnapshotAndReplace);
        }

        public void HideIsland()
        {
            if (!IsVisible)
            {
                return;
            }

            autoHideTimer.Stop();
            var duration = TimeSpan.FromMilliseconds(settings.CalmMotion ? 260 : 180);
            var opacity = new DoubleAnimation(0, duration) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn } };
            opacity.SetValue(Timeline.DesiredFrameRateProperty, 60);
            opacity.Completed += delegate
            {
                if (Opacity < 0.05)
                {
                    Hide();
                }
            };
            BeginAnimation(OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);

            var scaleX = new DoubleAnimation(0.18, duration)
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
            };
            scaleX.SetValue(Timeline.DesiredFrameRateProperty, 60);
            islandScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX, HandoffBehavior.SnapshotAndReplace);

            var scaleY = new DoubleAnimation(0.74, duration)
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
            };
            scaleY.SetValue(Timeline.DesiredFrameRateProperty, 60);
            islandScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY, HandoffBehavior.SnapshotAndReplace);
        }

        private void ScheduleAutoHide(IslandMode mode)
        {
            autoHideTimer.Stop();
            if (!settings.AutoHideEnabled)
            {
                return;
            }

            if (mode == IslandMode.Idle || mode == IslandMode.Done)
            {
                autoHideTimer.Interval = TimeSpan.FromSeconds(settings.AutoHideDelaySeconds);
                autoHideTimer.Start();
            }
        }

        private void ApplySettings()
        {
            ApplyShellPalette();
            if (!settings.AutoHideEnabled && !IsVisible)
            {
                ShowIsland();
            }
            ScheduleAutoHide(lastSnapshot.Mode);
            ApplyCompactSettings();
        }

        private UIElement BuildUi()
        {
            var outer = new Grid();
            outer.ClipToBounds = false;

            var topHost = new Grid();
            topHost.Height = ShellWindowHeight;
            topHost.VerticalAlignment = VerticalAlignment.Top;
            topHost.ClipToBounds = false;
            outer.Children.Add(topHost);

            islandScale = new ScaleTransform(1, 1);
            morphScale = new ScaleTransform(1, 1);
            switchScale = new ScaleTransform(1, 1);
            switchSkew = new SkewTransform(0, 0);
            islandTranslate = new TranslateTransform(0, 0);
            switchTranslate = new TranslateTransform(0, 0);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(islandScale);
            transformGroup.Children.Add(morphScale);
            transformGroup.Children.Add(switchScale);
            transformGroup.Children.Add(switchSkew);
            transformGroup.Children.Add(islandTranslate);
            transformGroup.Children.Add(switchTranslate);

            stackCardB = CreateStackCard(24, 56, 0);
            stackCardA = CreateStackCard(14, 28, 0);
            topHost.Children.Add(stackCardB);
            topHost.Children.Add(stackCardA);

            var island = new Border();
            islandBorder = island;
            island.Width = WidthFor(lastSnapshot.Mode);
            island.Height = ExpandedHeight;
            island.HorizontalAlignment = HorizontalAlignment.Center;
            island.VerticalAlignment = VerticalAlignment.Center;
            island.Margin = new Thickness(0);
            island.Padding = new Thickness(0);
            island.CornerRadius = new CornerRadius(32);
            island.BorderThickness = new Thickness(1);
            island.BorderBrush = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255));
            island.Background = new SolidColorBrush(Color.FromArgb(235, 12, 16, 24));
            island.RenderTransformOrigin = new Point(0.5, 0.5);
            island.RenderTransform = transformGroup;
            island.MouseLeftButtonDown += OnMouseLeftButtonDown;
            island.MouseMove += OnIslandMouseMove;
            island.MouseLeftButtonUp += OnIslandMouseLeftButtonUp;
            islandShadow = new DropShadowEffect();
            islandShadow.Color = Colors.Black;
            islandShadow.BlurRadius = 24;
            islandShadow.ShadowDepth = 10;
            islandShadow.Opacity = 0.42;
            islandShadow.RenderingBias = RenderingBias.Performance;
            island.Effect = islandShadow;
            topHost.Children.Add(island);

            var layer = new RoundedClipGrid();
            layer.Radius = 31;
            island.Child = layer;

            var aurora = new Border();
            aurora.Margin = new Thickness(12, 4, 12, 4);
            aurora.VerticalAlignment = VerticalAlignment.Stretch;
            aurora.HorizontalAlignment = HorizontalAlignment.Stretch;
            aurora.Opacity = 0.16;
            aurora.IsHitTestVisible = false;
            var auroraBrush = new RadialGradientBrush();
            auroraBrush.Center = new Point(0.72, 0.46);
            auroraBrush.GradientOrigin = new Point(0.72, 0.46);
            auroraBrush.RadiusX = 0.46;
            auroraBrush.RadiusY = 0.72;
            glowStopA = new GradientStop(Color.FromRgb(54, 211, 153), 0);
            auroraBrush.GradientStops.Add(glowStopA);
            auroraBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 16, 21, 32), 1));
            aurora.Background = auroraBrush;
            layer.Children.Add(aurora);

            expandedGrid = new Grid();
            var grid = expandedGrid;
            grid.Margin = new Thickness(18, 12, 15, 12);
            grid.ClipToBounds = false;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            layer.Children.Add(grid);

            var signal = new Grid();
            signal.Width = 54;
            signal.Height = 36;
            signal.HorizontalAlignment = HorizontalAlignment.Left;
            signal.VerticalAlignment = VerticalAlignment.Center;
            signal.Margin = new Thickness(6, 0, 12, 0);

            signalTailB = SignalDot(6, Color.FromRgb(178, 132, 255));
            signalTailB.HorizontalAlignment = HorizontalAlignment.Left;
            signalTailB.VerticalAlignment = VerticalAlignment.Center;
            signalTailB.Margin = new Thickness(3, 0, 0, 0);
            signal.Children.Add(signalTailB);

            signalTailA = SignalDot(9, Color.FromRgb(76, 201, 240));
            signalTailA.HorizontalAlignment = HorizontalAlignment.Left;
            signalTailA.VerticalAlignment = VerticalAlignment.Center;
            signalTailA.Margin = new Thickness(15, 0, 0, 0);
            signal.Children.Add(signalTailA);

            signalCore = SignalDot(17, Color.FromRgb(54, 211, 153));
            signalCore.HorizontalAlignment = HorizontalAlignment.Left;
            signalCore.VerticalAlignment = VerticalAlignment.Center;
            signalCore.Margin = new Thickness(28, 0, 0, 0);
            signal.Children.Add(signalCore);

            Grid.SetColumn(signal, 0);
            grid.Children.Add(signal);

            var textGrid = new Grid();
            textGrid.VerticalAlignment = VerticalAlignment.Center;
            textGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            textGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(textGrid, 1);
            grid.Children.Add(textGrid);

            var titleLine = new StackPanel();
            titleLine.Orientation = Orientation.Horizontal;
            titleLine.VerticalAlignment = VerticalAlignment.Center;

            var badge = new Border();
            badge.CornerRadius = new CornerRadius(9);
            badge.Padding = new Thickness(7, 2, 7, 2);
            badge.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
            badge.BorderBrush = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
            badge.BorderThickness = new Thickness(1);
            badge.Margin = new Thickness(0, 0, 9, 0);
            var badgeContent = new StackPanel();
            badgeContent.Orientation = Orientation.Horizontal;
            badgeContent.VerticalAlignment = VerticalAlignment.Center;
            var agentIconFrame = new Border();
            agentIconFrame.Width = 18;
            agentIconFrame.Height = 18;
            agentIconFrame.CornerRadius = new CornerRadius(9);
            agentIconFrame.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            agentIconFrame.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            agentIconFrame.BorderThickness = new Thickness(1);
            agentIconFrame.Margin = new Thickness(0, 0, 5, 0);
            agentIconImage = new Image();
            agentIconImage.Width = 14;
            agentIconImage.Height = 14;
            agentIconImage.Stretch = Stretch.Uniform;
            agentIconImage.SnapsToDevicePixels = true;
            agentIconImage.Clip = new EllipseGeometry(new Point(7, 7), 7, 7);
            RenderOptions.SetBitmapScalingMode(agentIconImage, BitmapScalingMode.HighQuality);
            agentIconFrame.Child = agentIconImage;
            badgeContent.Children.Add(agentIconFrame);
            agentText = new TextBlock();
            agentText.Text = "AGENT";
            agentText.FontFamily = new FontFamily("Consolas");
            agentText.FontSize = 10;
            agentText.FontWeight = FontWeights.Bold;
            agentText.Foreground = new SolidColorBrush(Color.FromRgb(184, 195, 214));
            agentText.VerticalAlignment = VerticalAlignment.Center;
            badgeContent.Children.Add(agentText);
            badge.Child = badgeContent;
            titleLine.Children.Add(badge);

            statusText = new TextBlock();
            statusText.Text = "待命";
            statusText.FontFamily = new FontFamily("Microsoft YaHei UI");
            statusText.FontSize = 17;
            statusText.FontWeight = FontWeights.SemiBold;
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(247, 250, 255));
            titleLine.Children.Add(statusText);
            textGrid.Children.Add(titleLine);

            detailText = new TextBlock();
            detailText.Text = "等待 Codex / Claude Code 的下一次任务";
            detailText.Margin = new Thickness(0, 5, 0, 0);
            detailText.FontFamily = new FontFamily("Microsoft YaHei UI");
            detailText.FontSize = 11.5;
            detailText.Foreground = new SolidColorBrush(Color.FromRgb(169, 180, 199));
            detailText.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetRow(detailText, 1);
            textGrid.Children.Add(detailText);

            var pulseGrid = new Grid();
            pulseGrid.Width = 42;
            pulseGrid.Height = 42;
            pulseGrid.HorizontalAlignment = HorizontalAlignment.Center;
            pulseGrid.VerticalAlignment = VerticalAlignment.Center;
            pulseGrid.Clip = new EllipseGeometry(new Point(21, 21), 20, 20);
            pulseGrid.CacheMode = new BitmapCache();
            pulseGrid.SnapsToDevicePixels = true;
            Grid.SetColumn(pulseGrid, 2);
            grid.Children.Add(pulseGrid);

            pulseScale = new ScaleTransform(0.26, 0.26);
            pulseRing = new Ellipse();
            pulseRing.Width = 15;
            pulseRing.Height = 15;
            pulseRing.HorizontalAlignment = HorizontalAlignment.Center;
            pulseRing.VerticalAlignment = VerticalAlignment.Center;
            pulseRing.Stroke = new SolidColorBrush(Color.FromRgb(54, 211, 153));
            pulseRing.StrokeThickness = 1.55;
            pulseRing.Opacity = 0;
            pulseRing.RenderTransformOrigin = new Point(0.5, 0.5);
            pulseRing.RenderTransform = pulseScale;
            pulseRing.CacheMode = new BitmapCache();
            pulseRing.IsHitTestVisible = false;
            pulseGrid.Children.Add(pulseRing);

            pulseScaleB = new ScaleTransform(0.26, 0.26);
            pulseRingB = new Ellipse();
            pulseRingB.Width = 15;
            pulseRingB.Height = 15;
            pulseRingB.HorizontalAlignment = HorizontalAlignment.Center;
            pulseRingB.VerticalAlignment = VerticalAlignment.Center;
            pulseRingB.Stroke = new SolidColorBrush(Color.FromRgb(54, 211, 153));
            pulseRingB.StrokeThickness = 1.55;
            pulseRingB.Opacity = 0;
            pulseRingB.RenderTransformOrigin = new Point(0.5, 0.5);
            pulseRingB.RenderTransform = pulseScaleB;
            pulseRingB.CacheMode = new BitmapCache();
            pulseRingB.IsHitTestVisible = false;
            pulseGrid.Children.Add(pulseRingB);

            dotShadow = new DropShadowEffect();
            dotShadow.Color = Color.FromRgb(54, 211, 153);
            dotShadow.BlurRadius = 10;
            dotShadow.ShadowDepth = 0;
            dotShadow.Opacity = 0.58;
            dotShadow.RenderingBias = RenderingBias.Performance;
            coreDot = new Ellipse();
            coreDot.Width = 14;
            coreDot.Height = 14;
            coreDot.Fill = new SolidColorBrush(Color.FromRgb(54, 211, 153));
            coreDot.HorizontalAlignment = HorizontalAlignment.Center;
            coreDot.VerticalAlignment = VerticalAlignment.Center;
            coreDot.Effect = dotShadow;
            pulseGrid.Children.Add(coreDot);

            flowTrack = new Border();
            flowTrack.Height = 2;
            flowTrack.CornerRadius = new CornerRadius(1);
            flowTrack.VerticalAlignment = VerticalAlignment.Bottom;
            flowTrack.Margin = new Thickness(58, 0, 58, 0);
            flowTrack.Opacity = 0.18;
            flowTrack.ClipToBounds = true;
            flowTrack.SnapsToDevicePixels = true;
            flowTrack.CacheMode = new BitmapCache();
            flowTrack.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            Grid.SetColumnSpan(flowTrack, 3);
            grid.Children.Add(flowTrack);

            flowBar = new Border();
            flowBar.Width = 104;
            flowBar.Height = 2;
            flowBar.HorizontalAlignment = HorizontalAlignment.Left;
            flowBar.CornerRadius = new CornerRadius(1);
            flowBar.CacheMode = new BitmapCache();
            flowTranslate = new TranslateTransform(-128, 0);
            flowBar.RenderTransform = flowTranslate;
            var flowBrush = new LinearGradientBrush();
            flowBrush.StartPoint = new Point(0, 0);
            flowBrush.EndPoint = new Point(1, 0);
            flowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 54, 211, 153), 0));
            flowStop = new GradientStop(Color.FromRgb(54, 211, 153), 0.5);
            flowBrush.GradientStops.Add(flowStop);
            flowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 54, 211, 153), 1));
            flowBar.Background = flowBrush;
            flowTrack.Child = flowBar;

            compactGrid = BuildCompactUi();
            compactGrid.Opacity = 0;
            layer.Children.Add(compactGrid);

            outer.Children.Add(BuildConversationListLayer());

            return outer;
        }

        private Border CreateStackCard(double yOffset, double widthInset, double opacity)
        {
            var card = new Border();
            card.Width = WidthFor(IslandMode.Idle) - widthInset;
            card.Height = ExpandedHeight - 6;
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            card.Margin = new Thickness(0, yOffset, 0, 0);
            card.CornerRadius = new CornerRadius(30);
            card.Background = new SolidColorBrush(Color.FromArgb(220, 18, 23, 34));
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
            card.BorderThickness = new Thickness(1);
            card.Opacity = 0;
            card.IsHitTestVisible = false;
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            return card;
        }

        private UIElement BuildConversationListLayer()
        {
            conversationListTranslate = new TranslateTransform(0, -12);
            conversationListStack = new StackPanel();
            conversationListStack.Orientation = Orientation.Vertical;

            conversationListScroller = new Grid();
            conversationListScroller.Width = 414;
            conversationListScroller.MaxHeight = 430;
            conversationListScroller.Margin = new Thickness(0, ShellWindowHeight - 2, 0, 0);
            conversationListScroller.HorizontalAlignment = HorizontalAlignment.Center;
            conversationListScroller.VerticalAlignment = VerticalAlignment.Top;
            conversationListScroller.Background = Brushes.Transparent;
            conversationListScroller.ClipToBounds = false;
            conversationListScroller.Opacity = 0;
            conversationListScroller.Visibility = Visibility.Collapsed;
            conversationListScroller.RenderTransform = conversationListTranslate;
            conversationListScroller.Children.Add(conversationListStack);
            return conversationListScroller;
        }

        private void UpdateConversationList(StatusSnapshot snapshot)
        {
            activeConversationCount = snapshot.ActiveConversationCount;
            UpdateStackCards(activeConversationCount);
            RebuildConversationRows(snapshot);

            if (conversationListExpanded && activeConversationCount <= 1)
            {
                SetConversationListExpanded(false);
            }
            else if (conversationListExpanded)
            {
                Height = CalculateConversationWindowHeight();
            }
        }

        private void UpdateStackCards(int count)
        {
            var show = count > 1 && !conversationListExpanded;
            AnimateElementOpacity(stackCardA, show ? 0.88 : 0.0, 180);
            AnimateElementOpacity(stackCardB, show && count > 2 ? 0.62 : 0.0, 180);
        }

        private bool ShouldAnimateTopConversationSwitch(StatusSnapshot previous, StatusSnapshot next)
        {
            if (previous == null || next == null || islandBorder == null)
            {
                return false;
            }

            if (previous.Mode == IslandMode.Idle || next.Mode == IslandMode.Idle)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(previous.SessionKey) || string.IsNullOrWhiteSpace(next.SessionKey) ||
                string.Equals(previous.SessionKey, next.SessionKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return previous.ActiveConversationCount > 1 ||
                   next.ActiveConversationCount > 1 ||
                   (previous.Conversations != null && previous.Conversations.Length > 1) ||
                   (next.Conversations != null && next.Conversations.Length > 1);
        }

        private void PlayTopConversationSwitchAnimation()
        {
            if (switchTranslate == null || switchScale == null || switchSkew == null)
            {
                return;
            }

            StopSwitchTransformAnimations();
            switchTranslate.Y = 16;
            switchScale.ScaleX = 0.99;
            switchScale.ScaleY = 0.9;
            switchSkew.AngleX = -1.2;

            AnimateTransformDouble(switchTranslate, TranslateTransform.YProperty, 0, 420,
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 }, 0);
            AnimateTransformDouble(switchScale, ScaleTransform.ScaleXProperty, 1, 380,
                new QuarticEase { EasingMode = EasingMode.EaseOut }, 0);
            AnimateTransformDouble(switchScale, ScaleTransform.ScaleYProperty, 1, 440,
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.16 }, 0);
            AnimateTransformDouble(switchSkew, SkewTransform.AngleXProperty, 0, 380,
                new QuarticEase { EasingMode = EasingMode.EaseOut }, 0);

            AnimateStackCardLift(stackCardA, 10, 0.9, 24);
            AnimateStackCardLift(stackCardB, 16, 0.85, 52);
        }

        private void StopSwitchTransformAnimations()
        {
            switchTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            switchScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            switchScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            switchSkew.BeginAnimation(SkewTransform.AngleXProperty, null);
        }

        private static void AnimateTransformDouble(Animatable target, DependencyProperty property, double value, int milliseconds, IEasingFunction easing, int delay)
        {
            if (target == null)
            {
                return;
            }

            var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds));
            animation.EasingFunction = easing;
            animation.SetValue(Timeline.DesiredFrameRateProperty, 60);
            if (delay > 0)
            {
                animation.BeginTime = TimeSpan.FromMilliseconds(delay);
            }
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static void AnimateStackCardLift(Border card, double startY, double startScaleY, int delay)
        {
            if (card == null)
            {
                return;
            }

            var scale = new ScaleTransform(1, startScaleY);
            var translate = new TranslateTransform(0, startY);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);
            card.RenderTransform = group;

            AnimateTransformDouble(translate, TranslateTransform.YProperty, 0, 420,
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.16 }, delay);
            AnimateTransformDouble(scale, ScaleTransform.ScaleYProperty, 1, 420,
                new QuarticEase { EasingMode = EasingMode.EaseOut }, delay);
        }
        private void RebuildConversationRows(StatusSnapshot snapshot)
        {
            if (conversationListStack == null)
            {
                return;
            }

            conversationListStack.Children.Clear();
            var conversations = snapshot.Conversations ?? new StatusSnapshot[0];
            var shown = 0;
            for (var i = 0; i < conversations.Length; i++)
            {
                if (string.Equals(conversations[i].SessionKey, snapshot.SessionKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (shown >= 6)
                {
                    break;
                }

                var row = BuildConversationRow(conversations[i], shown);
                conversationListStack.Children.Add(row);
                if (conversationListExpanded)
                {
                    AnimateListItemIn(row, shown);
                }
                shown++;
            }

            if (conversations.Length > shown + 1)
            {
                var overflow = BuildOverflowRow(conversations.Length - shown - 1);
                conversationListStack.Children.Add(overflow);
                if (conversationListExpanded)
                {
                    AnimateListItemIn(overflow, shown);
                }
            }
        }

        private UIElement BuildConversationRow(StatusSnapshot item, int index)
        {
            var accent = AccentFor(item.Mode);
            var host = new Grid();
            host.Height = ConversationRowHeight;
            host.Margin = new Thickness(0, index == 0 ? 0 : ConversationRowGap, 0, 0);
            host.ClipToBounds = false;

            var clearHint = new Border();
            clearHint.CornerRadius = new CornerRadius(27);
            clearHint.Background = new SolidColorBrush(Color.FromArgb(84, 248, 113, 113));
            clearHint.Opacity = 0;
            var clearText = new TextBlock();
            clearText.Text = "清除";
            clearText.HorizontalAlignment = HorizontalAlignment.Center;
            clearText.VerticalAlignment = VerticalAlignment.Center;
            clearText.FontFamily = new FontFamily("Microsoft YaHei UI");
            clearText.FontSize = 12;
            clearText.FontWeight = FontWeights.SemiBold;
            clearText.Foreground = new SolidColorBrush(Color.FromRgb(255, 230, 232));
            clearHint.Child = clearText;
            host.Children.Add(clearHint);

            var row = new Border();
            row.Height = ConversationRowHeight;
            row.CornerRadius = new CornerRadius(27);
            row.Background = new SolidColorBrush(Color.FromArgb(226, 13, 18, 28));
            row.BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
            row.BorderThickness = new Thickness(1);
            row.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 14,
                ShadowDepth = 6,
                Opacity = 0.28,
                RenderingBias = RenderingBias.Performance
            };

            var grid = new Grid();
            grid.Margin = new Thickness(15, 0, 14, 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Child = grid;

            var dot = new Ellipse();
            dot.Width = 10;
            dot.Height = 10;
            dot.Fill = new SolidColorBrush(accent);
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Effect = new DropShadowEffect { Color = accent, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.58, RenderingBias = RenderingBias.Performance };
            grid.Children.Add(dot);

            var agentBadge = BuildConversationAgentBadge(item.Agent, accent);
            Grid.SetColumn(agentBadge, 1);
            grid.Children.Add(agentBadge);

            var textPanel = new StackPanel();
            textPanel.VerticalAlignment = VerticalAlignment.Center;
            textPanel.Margin = new Thickness(6, 0, 8, 0);
            Grid.SetColumn(textPanel, 2);
            grid.Children.Add(textPanel);

            var conversationName = DisplayConversationName(item);

            var title = new TextBlock();
            title.Text = string.IsNullOrWhiteSpace(conversationName)
                ? item.Agent + " · " + item.Title
                : conversationName;
            title.FontFamily = new FontFamily("Microsoft YaHei UI");
            title.FontSize = 13.5;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = new SolidColorBrush(Color.FromRgb(246, 249, 255));
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            textPanel.Children.Add(title);

            var detail = new TextBlock();
            detail.Text = string.IsNullOrWhiteSpace(conversationName)
                ? item.Detail
                : item.Agent + " · " + item.Title + " · " + item.Detail;
            detail.Margin = new Thickness(0, 3, 0, 0);
            detail.FontFamily = new FontFamily("Microsoft YaHei UI");
            detail.FontSize = 10.5;
            detail.Foreground = new SolidColorBrush(Color.FromRgb(158, 170, 190));
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            textPanel.Children.Add(detail);

            var rightPanel = new StackPanel();
            rightPanel.Orientation = Orientation.Horizontal;
            rightPanel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(rightPanel, 3);
            grid.Children.Add(rightPanel);

            var age = new TextBlock();
            age.Text = AgeLabel(item.Timestamp);
            age.VerticalAlignment = VerticalAlignment.Center;
            age.FontFamily = new FontFamily("Consolas");
            age.FontSize = 10.5;
            age.Foreground = new SolidColorBrush(Color.FromRgb(132, 144, 164));
            age.Margin = new Thickness(0, 0, 8, 0);
            rightPanel.Children.Add(age);

            var closeFill = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            var closeStroke = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
            var closeButton = new Border();
            closeButton.Width = 22;
            closeButton.Height = 22;
            closeButton.CornerRadius = new CornerRadius(11);
            closeButton.Background = closeFill;
            closeButton.BorderBrush = closeStroke;
            closeButton.BorderThickness = new Thickness(1);
            closeButton.Cursor = Cursors.Hand;
            closeButton.ToolTip = "清除这个会话";
            closeButton.VerticalAlignment = VerticalAlignment.Center;

            var closeText = new TextBlock();
            closeText.Text = "×";
            closeText.HorizontalAlignment = HorizontalAlignment.Center;
            closeText.VerticalAlignment = VerticalAlignment.Center;
            closeText.FontFamily = new FontFamily("Segoe UI Symbol");
            closeText.FontSize = 12;
            closeText.FontWeight = FontWeights.Bold;
            closeText.Foreground = new SolidColorBrush(Color.FromRgb(174, 184, 202));
            closeButton.Child = closeText;
            rightPanel.Children.Add(closeButton);

            closeButton.MouseEnter += delegate
            {
                AnimateColor(closeFill, Color.FromArgb(74, 248, 113, 113));
                AnimateColor(closeStroke, Color.FromArgb(86, 248, 113, 113));
                AnimateColor((SolidColorBrush)closeText.Foreground, Color.FromRgb(255, 238, 240));
            };
            closeButton.MouseLeave += delegate
            {
                AnimateColor(closeFill, Color.FromArgb(20, 255, 255, 255));
                AnimateColor(closeStroke, Color.FromArgb(34, 255, 255, 255));
                AnimateColor((SolidColorBrush)closeText.Foreground, Color.FromRgb(174, 184, 202));
            };
            closeButton.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (string.IsNullOrWhiteSpace(item.SessionKey))
                {
                    return;
                }

                AnimateSwipeClear(host, row, clearHint, 420, delegate
                {
                    if (conversationClearHandler != null)
                    {
                        conversationClearHandler(item.SessionKey);
                    }
                });
            };

            host.Children.Add(row);
            AttachSwipeToClear(host, row, clearHint, item.SessionKey);
            return host;
        }

        private UIElement BuildConversationAgentBadge(string agent, Color accent)
        {
            var badge = new Border();
            badge.Width = 24;
            badge.Height = 24;
            badge.CornerRadius = new CornerRadius(12);
            badge.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            badge.BorderBrush = new SolidColorBrush(Color.FromArgb(52, accent.R, accent.G, accent.B));
            badge.BorderThickness = new Thickness(1);
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.HorizontalAlignment = HorizontalAlignment.Left;

            var key = CompactAgentKey(agent);
            var source = string.IsNullOrWhiteSpace(key) ? null : LoadCompactAgentIcon(key);
            if (source == null)
            {
                var fallback = new TextBlock();
                fallback.Text = string.IsNullOrWhiteSpace(agent) ? "?" : agent.Trim().Substring(0, 1).ToUpperInvariant();
                fallback.FontFamily = new FontFamily("Consolas");
                fallback.FontSize = 10;
                fallback.FontWeight = FontWeights.Bold;
                fallback.Foreground = new SolidColorBrush(Color.FromRgb(182, 193, 211));
                fallback.HorizontalAlignment = HorizontalAlignment.Center;
                fallback.VerticalAlignment = VerticalAlignment.Center;
                badge.Child = fallback;
                return badge;
            }

            var image = new Image();
            image.Width = 16;
            image.Height = 16;
            image.Stretch = Stretch.Uniform;
            image.SnapsToDevicePixels = true;
            image.Clip = new EllipseGeometry(new Point(8, 8), 8, 8);
            image.HorizontalAlignment = HorizontalAlignment.Center;
            image.VerticalAlignment = VerticalAlignment.Center;
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            image.Source = source;
            badge.Child = image;
            return badge;
        }
        private void AttachSwipeToClear(Grid host, Border row, Border clearHint, string sessionKey)
        {
            var translate = new TranslateTransform(0, 0);
            row.RenderTransform = translate;

            Point start = new Point();
            var tracking = false;
            var swiping = false;

            row.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (string.IsNullOrWhiteSpace(sessionKey))
                {
                    return;
                }

                tracking = true;
                swiping = false;
                start = e.GetPosition(this);
                row.CaptureMouse();
                e.Handled = true;
            };

            row.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!tracking || e.LeftButton != MouseButtonState.Pressed)
                {
                    return;
                }

                var point = e.GetPosition(this);
                var dx = point.X - start.X;
                var dy = point.Y - start.Y;
                if (!swiping && Math.Abs(dx) > 8 && Math.Abs(dx) > Math.Abs(dy) * 1.25)
                {
                    swiping = true;
                }

                if (!swiping)
                {
                    return;
                }

                var limited = Clamp(dx, -132, 132);
                translate.X = limited;
                clearHint.Opacity = Clamp01(Math.Abs(limited) / 82.0);
                row.Opacity = 1.0 - Math.Min(0.34, Math.Abs(limited) / 300.0);
                e.Handled = true;
            };

            row.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!tracking)
                {
                    return;
                }

                tracking = false;
                row.ReleaseMouseCapture();
                var dismiss = Math.Abs(translate.X) >= 86;
                if (dismiss)
                {
                    AnimateSwipeClear(host, row, clearHint, translate.X < 0 ? -420 : 420, delegate
                    {
                        if (conversationClearHandler != null)
                        {
                            conversationClearHandler(sessionKey);
                        }
                    });
                }
                else
                {
                    AnimateSwipeBack(row, clearHint, translate);
                }

                e.Handled = swiping;
            };

            row.MouseLeave += delegate
            {
                if (tracking && Mouse.LeftButton != MouseButtonState.Pressed)
                {
                    tracking = false;
                    row.ReleaseMouseCapture();
                    AnimateSwipeBack(row, clearHint, translate);
                }
            };
        }

        private static void AnimateSwipeBack(Border row, Border clearHint, TranslateTransform translate)
        {
            var back = new DoubleAnimation(0, TimeSpan.FromMilliseconds(320));
            back.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.10 };
            back.SetValue(Timeline.DesiredFrameRateProperty, 60);
            translate.BeginAnimation(TranslateTransform.XProperty, back, HandoffBehavior.SnapshotAndReplace);

            var rowOpacity = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            rowOpacity.SetValue(Timeline.DesiredFrameRateProperty, 60);
            row.BeginAnimation(OpacityProperty, rowOpacity, HandoffBehavior.SnapshotAndReplace);

            var hintOpacity = new DoubleAnimation(0, TimeSpan.FromMilliseconds(210))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
            };
            hintOpacity.SetValue(Timeline.DesiredFrameRateProperty, 60);
            clearHint.BeginAnimation(OpacityProperty, hintOpacity, HandoffBehavior.SnapshotAndReplace);
        }

        private static void AnimateSwipeClear(Grid host, Border row, Border clearHint, double targetX, EventHandler completed)
        {
            var translate = row.RenderTransform as TranslateTransform;
            if (translate == null)
            {
                translate = new TranslateTransform(0, 0);
                row.RenderTransform = translate;
            }

            var slide = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(260));
            slide.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn };
            slide.SetValue(Timeline.DesiredFrameRateProperty, 60);
            translate.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);

            var rowOpacity = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
            };
            rowOpacity.SetValue(Timeline.DesiredFrameRateProperty, 60);
            row.BeginAnimation(OpacityProperty, rowOpacity, HandoffBehavior.SnapshotAndReplace);

            var hintOpacity = new DoubleAnimation(0.18, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            hintOpacity.SetValue(Timeline.DesiredFrameRateProperty, 60);
            clearHint.BeginAnimation(OpacityProperty, hintOpacity, HandoffBehavior.SnapshotAndReplace);

            var collapse = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            collapse.BeginTime = TimeSpan.FromMilliseconds(120);
            collapse.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut };
            collapse.SetValue(Timeline.DesiredFrameRateProperty, 60);
            collapse.Completed += completed;
            host.BeginAnimation(HeightProperty, collapse, HandoffBehavior.SnapshotAndReplace);
        }
        private Border BuildOverflowRow(int hiddenCount)
        {
            var row = new Border();
            row.Height = 42;
            row.Margin = new Thickness(0, ConversationRowGap, 0, 0);
            row.CornerRadius = new CornerRadius(21);
            row.Background = new SolidColorBrush(Color.FromArgb(170, 16, 21, 31));
            row.BorderBrush = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
            row.BorderThickness = new Thickness(1);

            var textBlock = new TextBlock();
            textBlock.Text = "还有 " + hiddenCount + " 个对话";
            textBlock.HorizontalAlignment = HorizontalAlignment.Center;
            textBlock.VerticalAlignment = VerticalAlignment.Center;
            textBlock.FontFamily = new FontFamily("Microsoft YaHei UI");
            textBlock.FontSize = 12;
            textBlock.FontWeight = FontWeights.SemiBold;
            textBlock.Foreground = new SolidColorBrush(Color.FromRgb(178, 188, 205));
            row.Child = textBlock;
            return row;
        }

        public void CollapseConversationList()
        {
            if (conversationListExpanded)
            {
                SetConversationListExpanded(false);
            }
        }

        private void ToggleConversationList()
        {
            SetConversationListExpanded(!conversationListExpanded);
        }

        private void SetConversationListExpanded(bool expanded)
        {
            if (expanded && activeConversationCount <= 1)
            {
                return;
            }

            if (conversationListExpanded == expanded && conversationListScroller != null)
            {
                return;
            }

            conversationListExpanded = expanded;
            UpdateStackCards(activeConversationCount);
            if (expanded)
            {
                if (compactTimer != null)
                {
                    compactTimer.Stop();
                }
                SetCompact(false);
                leftButtonWasDown = (Forms.Control.MouseButtons & Forms.MouseButtons.Left) == Forms.MouseButtons.Left;
                outsideClickTimer.Start();
                conversationListScroller.Visibility = Visibility.Visible;
                conversationListTranslate.Y = -18;
                AnimateWindowHeight(CalculateConversationWindowHeight(), 260, null);
                AnimateElementOpacity(conversationListScroller, 1.0, 190);
                AnimateTranslateY(conversationListTranslate, 0, 260);
                return;
            }

            outsideClickTimer.Stop();
            leftButtonWasDown = false;
            AnimateElementOpacity(conversationListScroller, 0.0, 140, delegate
            {
                if (!conversationListExpanded && conversationListScroller != null)
                {
                    conversationListScroller.Visibility = Visibility.Collapsed;
                }
            });
            AnimateTranslateY(conversationListTranslate, -16, 170);
            AnimateWindowHeight(ShellWindowHeight, 210, delegate
            {
                if (!conversationListExpanded)
                {
                    Height = ShellWindowHeight;
                }
            });
        }

        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!conversationListExpanded)
            {
                return;
            }

            if ((islandBorder == null || !islandBorder.IsMouseOver) &&
                (conversationListScroller == null || !conversationListScroller.IsMouseOver))
            {
                SetConversationListExpanded(false);
                e.Handled = true;
            }
        }

        private void CheckOutsideClick()
        {
            if (!conversationListExpanded)
            {
                outsideClickTimer.Stop();
                return;
            }

            var leftDown = (Forms.Control.MouseButtons & Forms.MouseButtons.Left) == Forms.MouseButtons.Left;
            if (leftDown && !leftButtonWasDown && !IsCursorInsideWindow())
            {
                SetConversationListExpanded(false);
            }
            leftButtonWasDown = leftDown;
        }

        private bool IsCursorInsideWindow()
        {
            try
            {
                var cursor = Forms.Cursor.Position;
                var local = PointFromScreen(new Point(cursor.X, cursor.Y));
                var width = ActualWidth > 0 ? ActualWidth : Width;
                var height = ActualHeight > 0 ? ActualHeight : Height;
                return local.X >= 0 && local.Y >= 0 && local.X <= width && local.Y <= height;
            }
            catch
            {
                return false;
            }
        }

        private double CalculateConversationWindowHeight()
        {
            var secondaryCount = Math.Max(activeConversationCount - 1, 0);
            var visibleRows = Math.Min(secondaryCount, 6);
            var contentHeight = visibleRows * ConversationRowHeight + Math.Max(visibleRows - 1, 0) * ConversationRowGap;
            if (secondaryCount > visibleRows)
            {
                contentHeight += ConversationRowGap + 42;
            }

            var target = ShellWindowHeight + 12 + contentHeight;
            var max = SystemParameters.WorkArea.Height - 46;
            return Math.Max(ShellWindowHeight, Math.Min(target, max));
        }

        private void AnimateWindowHeight(double target, int milliseconds, EventHandler completed)
        {
            BeginAnimation(HeightProperty, null);
            var start = ActualHeight > 0 ? ActualHeight : Height;
            var animation = new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(milliseconds));
            animation.EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut };
            animation.SetValue(Timeline.DesiredFrameRateProperty, 60);
            animation.Completed += delegate
            {
                BeginAnimation(HeightProperty, null);
                Height = target;
                if (completed != null)
                {
                    completed(this, EventArgs.Empty);
                }
            };
            BeginAnimation(HeightProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static void AnimateListItemIn(UIElement element, int index)
        {
            if (element == null)
            {
                return;
            }

            var translate = new TranslateTransform(0, -8);
            element.RenderTransform = translate;
            element.Opacity = 0;

            var delay = TimeSpan.FromMilliseconds(Math.Min(index * 28, 140));
            var opacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240));
            opacity.BeginTime = delay;
            opacity.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };
            opacity.SetValue(Timeline.DesiredFrameRateProperty, 60);
            element.BeginAnimation(OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);

            var y = new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(300));
            y.BeginTime = delay;
            y.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.08 };
            y.SetValue(Timeline.DesiredFrameRateProperty, 60);
            translate.BeginAnimation(TranslateTransform.YProperty, y, HandoffBehavior.SnapshotAndReplace);
        }
        private static void AnimateElementOpacity(UIElement element, double target, int milliseconds)
        {
            AnimateElementOpacity(element, target, milliseconds, null);
        }

        private static void AnimateElementOpacity(UIElement element, double target, int milliseconds, EventHandler completed)
        {
            if (element == null)
            {
                return;
            }

            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds));
            animation.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };
            animation.SetValue(Timeline.DesiredFrameRateProperty, 60);
            if (completed != null)
            {
                animation.Completed += completed;
            }
            element.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static void AnimateTranslateY(TranslateTransform transform, double target, int milliseconds)
        {
            if (transform == null)
            {
                return;
            }

            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds));
            animation.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.10 };
            animation.SetValue(Timeline.DesiredFrameRateProperty, 60);
            transform.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static string AgeLabel(DateTimeOffset timestamp)
        {
            var seconds = Math.Max(0, (int)(DateTimeOffset.Now - timestamp).TotalSeconds);
            if (seconds < 5)
            {
                return "now";
            }
            if (seconds < 60)
            {
                return seconds + "s";
            }
            return Math.Min(99, seconds / 60) + "m";
        }

        private static string DisplayDetail(StatusSnapshot snapshot)
        {
            var conversationName = DisplayConversationName(snapshot);
            if (string.IsNullOrWhiteSpace(conversationName))
            {
                return snapshot.Detail;
            }

            if (string.IsNullOrWhiteSpace(snapshot.Detail) ||
                string.Equals(conversationName, snapshot.Detail, StringComparison.OrdinalIgnoreCase))
            {
                return conversationName;
            }

            return conversationName + " · " + snapshot.Detail;
        }

        private static string DisplayConversationName(StatusSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ConversationName))
            {
                return null;
            }

            return snapshot.ConversationName.Trim();
        }
        private static Ellipse SignalDot(double size, Color color)
        {
            var ellipse = new Ellipse();
            ellipse.Width = size;
            ellipse.Height = size;
            ellipse.Fill = new SolidColorBrush(color);
            ellipse.Opacity = 0.9;
            return ellipse;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            islandPressPoint = e.GetPosition(this);
            islandPressTracking = true;
            if (islandBorder != null)
            {
                islandBorder.CaptureMouse();
            }
            e.Handled = true;
        }

        private void OnIslandMouseMove(object sender, MouseEventArgs e)
        {
            if (!islandPressTracking || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var current = e.GetPosition(this);
            var deltaX = current.X - islandPressPoint.X;
            var deltaY = current.Y - islandPressPoint.Y;
            if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < IslandDragThreshold)
            {
                return;
            }

            islandPressTracking = false;
            if (islandBorder != null)
            {
                islandBorder.ReleaseMouseCapture();
            }

            var restore = SuppressVisualsForDrag();
            try
            {
                DragMove();
            }
            catch
            {
            }
            finally
            {
                restore();
            }
            e.Handled = true;
        }

        private Action SuppressVisualsForDrag()
        {
            Effect savedEffect = null;
            CacheMode savedCacheMode = null;
            if (islandBorder != null)
            {
                savedEffect = islandBorder.Effect;
                savedCacheMode = islandBorder.CacheMode;
                islandBorder.Effect = null;
                islandBorder.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
            }

            var pulseAVis = pulseRing != null ? pulseRing.Visibility : Visibility.Visible;
            var pulseBVis = pulseRingB != null ? pulseRingB.Visibility : Visibility.Visible;
            var flowVis = flowTrack != null ? flowTrack.Visibility : Visibility.Visible;
            var stackAVis = stackCardA != null ? stackCardA.Visibility : Visibility.Visible;
            var stackBVis = stackCardB != null ? stackCardB.Visibility : Visibility.Visible;

            if (pulseRing != null) pulseRing.Visibility = Visibility.Hidden;
            if (pulseRingB != null) pulseRingB.Visibility = Visibility.Hidden;
            if (flowTrack != null) flowTrack.Visibility = Visibility.Hidden;
            if (stackCardA != null) stackCardA.Visibility = Visibility.Hidden;
            if (stackCardB != null) stackCardB.Visibility = Visibility.Hidden;

            return delegate
            {
                if (islandBorder != null)
                {
                    islandBorder.CacheMode = savedCacheMode;
                    islandBorder.Effect = savedEffect;
                }
                if (pulseRing != null) pulseRing.Visibility = pulseAVis;
                if (pulseRingB != null) pulseRingB.Visibility = pulseBVis;
                if (flowTrack != null) flowTrack.Visibility = flowVis;
                if (stackCardA != null) stackCardA.Visibility = stackAVis;
                if (stackCardB != null) stackCardB.Visibility = stackBVis;
            };
        }

        private void OnIslandMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!islandPressTracking)
            {
                return;
            }

            islandPressTracking = false;
            if (islandBorder != null)
            {
                islandBorder.ReleaseMouseCapture();
            }

            if (activeConversationCount > 1)
            {
                ToggleConversationList();
            }
            e.Handled = true;
        }

        private void PositionTopCenter()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2.0;
            Top = workArea.Top + 8;
        }

        private void StartAnimations()
        {
            var rippleDuration = TimeSpan.FromMilliseconds(settings.CalmMotion ? 3600 : 3000);
            var rippleStagger = TimeSpan.FromMilliseconds(rippleDuration.TotalMilliseconds / 2.0);

            StartPulseWave(pulseRing, pulseScale, rippleDuration, TimeSpan.Zero, 0.46);
            StartPulseWave(pulseRingB, pulseScaleB, rippleDuration, rippleStagger, 0.32);
        }

        private static void StartPulseWave(Ellipse ring, ScaleTransform scale, TimeSpan duration, TimeSpan delay, double opacityStart)
        {
            if (ring == null || scale == null)
            {
                return;
            }

            scale.ScaleX = 0.30;
            scale.ScaleY = 0.30;
            ring.Opacity = 0;
            ring.StrokeThickness = 1.55;

            var scaleX = new DoubleAnimation(0.30, 2.46, duration);
            scaleX.BeginTime = delay;
            scaleX.RepeatBehavior = RepeatBehavior.Forever;
            scaleX.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            scaleX.SetValue(Timeline.DesiredFrameRateProperty, 60);

            var scaleY = new DoubleAnimation(0.30, 2.46, duration);
            scaleY.BeginTime = delay;
            scaleY.RepeatBehavior = RepeatBehavior.Forever;
            scaleY.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            scaleY.SetValue(Timeline.DesiredFrameRateProperty, 60);

            var opacity = new DoubleAnimationUsingKeyFrames();
            opacity.BeginTime = delay;
            opacity.RepeatBehavior = RepeatBehavior.Forever;
            opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            opacity.KeyFrames.Add(new SplineDoubleKeyFrame(opacityStart, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)), new KeySpline(0.18, 0.0, 0.18, 1.0)));
            opacity.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration), new KeySpline(0.16, 0.0, 0.26, 1.0)));
            opacity.SetValue(Timeline.DesiredFrameRateProperty, 60);

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY, HandoffBehavior.SnapshotAndReplace);
            ring.BeginAnimation(OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
        }

        private void SetFlowActive(bool active)
        {
            if (flowRunning && active == flowBusy)
            {
                return;
            }

            flowRunning = true;
            flowBusy = active;
            if (active)
            {
                AnimateElementOpacity(flowTrack, 0.74, 200);
                StartFlowAnimation(true);
            }
            else
            {
                AnimateElementOpacity(flowTrack, 0.18, 280);
                StartFlowAnimation(false);
            }
        }

        private void StartFlowAnimation(bool busy)
        {
            var start = -128.0;
            var end = 440.0;
            var duration = TimeSpan.FromMilliseconds(busy ? (settings.CalmMotion ? 2600 : 2200) : (settings.CalmMotion ? 6200 : 5400));

            flowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            flowTranslate.X = start;

            var flow = new DoubleAnimation(start, end, duration);
            flow.RepeatBehavior = RepeatBehavior.Forever;
            flow.SetValue(Timeline.DesiredFrameRateProperty, 60);
            flowTranslate.BeginAnimation(TranslateTransform.XProperty, flow, HandoffBehavior.SnapshotAndReplace);
        }

        private static void AnimateColor(SolidColorBrush brush, Color color)
        {
            var animation = new ColorAnimation(color, TimeSpan.FromMilliseconds(280));
            animation.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };
            animation.SetValue(Timeline.DesiredFrameRateProperty, 60);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static void CrossfadeText(TextBlock block, string text)
        {
            if (block.Text == text)
            {
                return;
            }

            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
            fadeOut.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            fadeOut.SetValue(Timeline.DesiredFrameRateProperty, 60);
            fadeOut.Completed += delegate
            {
                block.Text = text;
                var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(190));
                fadeIn.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };
                fadeIn.SetValue(Timeline.DesiredFrameRateProperty, 60);
                block.BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
            };
            block.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        }


        private static double ShadowOpacityFor(IslandMode mode)
        {
            return (mode == IslandMode.Waiting || mode == IslandMode.Error) ? 0.58 : 0.42;
        }
        private void ApplySignal(IslandMode mode, Color accent)
        {
            AnimateColor((SolidColorBrush)signalCore.Fill, accent);
            AnimateColor((SolidColorBrush)signalTailA.Fill, TrailColorA(mode));
            AnimateColor((SolidColorBrush)signalTailB.Fill, TrailColorB(mode));

            AnimateElementOpacity(signalCore, 1.0, 220);
            AnimateElementOpacity(signalTailA, 0.72, 220);
            AnimateElementOpacity(signalTailB, 0.42, 220);

            if (signalCoreShadow == null)
            {
                signalCoreShadow = new DropShadowEffect
                {
                    ShadowDepth = 0,
                    RenderingBias = RenderingBias.Performance
                };
                signalCore.Effect = signalCoreShadow;
            }
            signalCoreShadow.Color = accent;
            signalCoreShadow.BlurRadius = mode == IslandMode.Idle ? 5 : 7;
            signalCoreShadow.Opacity = mode == IslandMode.Idle ? 0.35 : 0.55;
            signalTailA.Effect = null;
            signalTailB.Effect = null;
        }

        private void ApplyShellPalette()
        {
            if (islandBorder == null)
            {
                return;
            }

            AnimateColor((SolidColorBrush)islandBorder.Background, SurfaceColor());
        }

        private Color TrailColorA(IslandMode mode)
        {
            if (PaletteIs("Mono"))
            {
                return Color.FromRgb(188, 195, 205);
            }
            if (PaletteIs("Ocean"))
            {
                return mode == IslandMode.Compacting ? Color.FromRgb(129, 140, 248) : Color.FromRgb(45, 212, 191);
            }
            if (PaletteIs("Candy"))
            {
                return mode == IslandMode.Thinking ? Color.FromRgb(34, 211, 238) : Color.FromRgb(249, 168, 212);
            }

            switch (mode)
            {
                case IslandMode.Thinking:
                    return Color.FromRgb(76, 201, 240);
                case IslandMode.Editing:
                case IslandMode.Running:
                    return Color.FromRgb(255, 189, 46);
                case IslandMode.Waiting:
                case IslandMode.Error:
                    return Color.FromRgb(255, 189, 46);
                case IslandMode.Compacting:
                    return Color.FromRgb(99, 179, 237);
                default:
                    return Color.FromRgb(76, 201, 240);
            }
        }

        private Color TrailColorB(IslandMode mode)
        {
            if (PaletteIs("Mono"))
            {
                return Color.FromRgb(116, 126, 142);
            }
            if (PaletteIs("Ocean"))
            {
                return Color.FromRgb(96, 165, 250);
            }
            if (PaletteIs("Candy"))
            {
                return Color.FromRgb(251, 191, 36);
            }

            switch (mode)
            {
                case IslandMode.Thinking:
                    return Color.FromRgb(178, 132, 255);
                case IslandMode.Editing:
                case IslandMode.Running:
                    return Color.FromRgb(54, 211, 153);
                case IslandMode.Waiting:
                case IslandMode.Error:
                    return Color.FromRgb(76, 201, 240);
                case IslandMode.Compacting:
                    return Color.FromRgb(255, 189, 46);
                default:
                    return Color.FromRgb(178, 132, 255);
            }
        }
        private static double WidthFor(IslandMode mode)
        {
            switch (mode)
            {
                case IslandMode.Idle:
                    return 314;
                case IslandMode.Done:
                    return 338;
                case IslandMode.Waiting:
                    return 414;
                case IslandMode.Error:
                    return 398;
                case IslandMode.Editing:
                    return 386;
                case IslandMode.Running:
                    return 382;
                case IslandMode.Compacting:
                    return 376;
                default:
                    return 356;
            }
        }

        private Color AccentFor(IslandMode mode)
        {
            if (PaletteIs("Mono"))
            {
                if (mode == IslandMode.Waiting || mode == IslandMode.Error)
                {
                    return Color.FromRgb(248, 113, 113);
                }
                return mode == IslandMode.Idle || mode == IslandMode.Done
                    ? Color.FromRgb(163, 163, 163)
                    : Color.FromRgb(229, 231, 235);
            }

            if (PaletteIs("Ocean"))
            {
                switch (mode)
                {
                    case IslandMode.Waiting:
                    case IslandMode.Error:
                        return Color.FromRgb(251, 113, 133);
                    case IslandMode.Thinking:
                        return Color.FromRgb(45, 212, 191);
                    case IslandMode.Editing:
                        return Color.FromRgb(56, 189, 248);
                    case IslandMode.Running:
                        return Color.FromRgb(96, 165, 250);
                    case IslandMode.Compacting:
                        return Color.FromRgb(129, 140, 248);
                    default:
                        return Color.FromRgb(52, 211, 153);
                }
            }

            if (PaletteIs("Candy"))
            {
                switch (mode)
                {
                    case IslandMode.Waiting:
                    case IslandMode.Error:
                        return Color.FromRgb(251, 113, 133);
                    case IslandMode.Thinking:
                        return Color.FromRgb(249, 168, 212);
                    case IslandMode.Editing:
                        return Color.FromRgb(34, 211, 238);
                    case IslandMode.Running:
                        return Color.FromRgb(192, 132, 252);
                    case IslandMode.Compacting:
                        return Color.FromRgb(251, 191, 36);
                    default:
                        return Color.FromRgb(167, 243, 208);
                }
            }

            switch (mode)
            {
                case IslandMode.Waiting:
                    return Color.FromRgb(255, 95, 87);
                case IslandMode.Error:
                    return Color.FromRgb(255, 75, 92);
                case IslandMode.Thinking:
                    return Color.FromRgb(255, 189, 46);
                case IslandMode.Editing:
                    return Color.FromRgb(76, 201, 240);
                case IslandMode.Running:
                    return Color.FromRgb(99, 179, 237);
                case IslandMode.Compacting:
                    return Color.FromRgb(178, 132, 255);
                default:
                    return Color.FromRgb(54, 211, 153);
            }
        }

        private Color SurfaceColor()
        {
            if (PaletteIs("Ocean"))
            {
                return Color.FromRgb(8, 18, 30);
            }
            if (PaletteIs("Candy"))
            {
                return Color.FromRgb(22, 13, 28);
            }
            if (PaletteIs("Mono"))
            {
                return Color.FromRgb(15, 15, 17);
            }
            return Color.FromRgb(12, 16, 24);
        }

        private bool PaletteIs(string name)
        {
            return string.Equals(settings.PaletteName, name, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class RoundedClipGrid : Grid
    {
        public double Radius { get; set; }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            Clip = new RectangleGeometry(new Rect(0, 0, arrangeSize.Width, arrangeSize.Height), Radius, Radius);
            return base.ArrangeOverride(arrangeSize);
        }
    }
}
