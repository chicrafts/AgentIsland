using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AgentIsland
{
    public sealed partial class MainWindow
    {
        private const double ExpandedHeight = 88;
        private const double CompactHeight = 48;
        private const double CompactMinWidth = 196;

        private Grid expandedGrid;
        private Grid compactGrid;
        private Ellipse compactDot;
        private Border compactTitleChip;
        private TextBlock compactTitleText;
        private Border compactDivider;
        private Border compactAgentBadge;
        private Image compactAgentImage;
        private string compactAgentIconKey;
        private Border compactAccentBar;
        private TextBlock compactStatusText;
        private ScaleTransform compactDotScale;
        private DispatcherTimer compactTimer;
        private SpringAnimator morphAnimator;
        private bool compacted;
        private bool compactBlinking;
        private double expandedTargetWidth;
        private double lastMorphProgress = double.NaN;
        private double lastAppliedIslandWidth;
        private double lastAppliedIslandHeight;
        private double lastAppliedStackWidthA;
        private double lastAppliedStackWidthB;

        private void InitializeCompactBehavior()
        {
            expandedTargetWidth = WidthFor(lastSnapshot.Mode);
            morphAnimator = new SpringAnimator(1.0, 460.0, 44.0, 1.0, 0.0012);
            morphAnimator.ValueChanged += delegate(double progress)
            {
                ApplyMorphProgress(progress, true);
            };
            morphAnimator.Completed += delegate
            {
                ApplyMorphProgress(morphAnimator.Target, false);
            };

            compactTimer = new DispatcherTimer();
            compactTimer.Tick += delegate
            {
                compactTimer.Stop();
                if (settings.CompactModeEnabled && !conversationListExpanded && !IsPointerOverIsland())
                {
                    SetCompact(true);
                }
            };

            if (islandBorder != null)
            {
                islandBorder.MouseEnter += delegate
                {
                    if (settings.CompactModeEnabled)
                    {
                        compactTimer.Stop();
                        SetCompact(false);
                    }
                };
                islandBorder.MouseLeave += delegate
                {
                    ScheduleCompact();
                };
            }
        }

        private Grid BuildCompactUi()
        {
            var compact = new Grid();
            compact.Margin = new Thickness(15, 0, 13, 0);
            compact.VerticalAlignment = VerticalAlignment.Center;
            compact.IsHitTestVisible = false;
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });

            compactDotScale = new ScaleTransform(1, 1);
            compactDot = new Ellipse();
            compactDot.Width = 8;
            compactDot.Height = 8;
            compactDot.Fill = new SolidColorBrush(Color.FromRgb(54, 211, 153));
            compactDot.HorizontalAlignment = HorizontalAlignment.Left;
            compactDot.VerticalAlignment = VerticalAlignment.Center;
            compactDot.RenderTransformOrigin = new Point(0.5, 0.5);
            compactDot.RenderTransform = compactDotScale;
            compact.Children.Add(compactDot);

            compactTitleChip = new Border();
            compactTitleChip.CornerRadius = new CornerRadius(8);
            compactTitleChip.Padding = new Thickness(5, 1, 5, 2);
            compactTitleChip.MaxWidth = CompactTitleMaxWidth();
            compactTitleChip.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            compactTitleChip.BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
            compactTitleChip.BorderThickness = new Thickness(1);
            compactTitleChip.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(compactTitleChip, 1);
            compactTitleText = new TextBlock();
            compactTitleText.Text = "";
            compactTitleText.FontFamily = new FontFamily("Microsoft YaHei UI");
            compactTitleText.FontSize = 11.2;
            compactTitleText.FontWeight = FontWeights.Medium;
            compactTitleText.Foreground = new SolidColorBrush(Color.FromRgb(182, 193, 211));
            compactTitleText.TextTrimming = TextTrimming.CharacterEllipsis;
            compactTitleChip.Child = compactTitleText;
            compact.Children.Add(compactTitleChip);

            compactDivider = new Border();
            compactDivider.Width = 1.4;
            compactDivider.Height = 14;
            compactDivider.CornerRadius = new CornerRadius(0.8);
            compactDivider.Background = new SolidColorBrush(Color.FromArgb(88, 54, 211, 153));
            compactDivider.HorizontalAlignment = HorizontalAlignment.Center;
            compactDivider.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(compactDivider, 2);
            compact.Children.Add(compactDivider);

            compactAgentBadge = new Border();
            compactAgentBadge.CornerRadius = new CornerRadius(12);
            compactAgentBadge.Padding = new Thickness(0);
            compactAgentBadge.Width = 24;
            compactAgentBadge.Height = 24;
            compactAgentBadge.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            compactAgentBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(46, 54, 211, 153));
            compactAgentBadge.BorderThickness = new Thickness(1);
            compactAgentBadge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(compactAgentBadge, 3);
            compactAgentImage = new Image();
            compactAgentImage.Width = 16;
            compactAgentImage.Height = 16;
            compactAgentImage.Stretch = Stretch.Uniform;
            compactAgentImage.SnapsToDevicePixels = true;
            compactAgentImage.Clip = new EllipseGeometry(new Point(8, 8), 8, 8);
            RenderOptions.SetBitmapScalingMode(compactAgentImage, BitmapScalingMode.HighQuality);
            compactAgentBadge.Child = compactAgentImage;
            compact.Children.Add(compactAgentBadge);

            compactStatusText = new TextBlock();
            compactStatusText.Text = "待命";
            compactStatusText.FontFamily = new FontFamily("Microsoft YaHei UI");
            compactStatusText.FontSize = 13.2;
            compactStatusText.FontWeight = FontWeights.SemiBold;
            compactStatusText.Foreground = new SolidColorBrush(Color.FromRgb(247, 250, 255));
            compactStatusText.TextTrimming = TextTrimming.CharacterEllipsis;
            compactStatusText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(compactStatusText, 5);
            compact.Children.Add(compactStatusText);

            compactAccentBar = new Border();
            compactAccentBar.Width = 3;
            compactAccentBar.Height = 18;
            compactAccentBar.CornerRadius = new CornerRadius(1.5);
            compactAccentBar.Background = new SolidColorBrush(Color.FromRgb(54, 211, 153));
            compactAccentBar.Opacity = 0.72;
            compactAccentBar.HorizontalAlignment = HorizontalAlignment.Right;
            compactAccentBar.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(compactAccentBar, 6);
            compact.Children.Add(compactAccentBar);

            return compact;
        }

        private void PrepareExpandedStatus()
        {
            expandedTargetWidth = WidthFor(lastSnapshot.Mode);
            if (!settings.CompactModeEnabled)
            {
                compacted = false;
                if (morphAnimator != null)
                {
                    morphAnimator.JumpTo(1.0);
                }
                else
                {
                    ApplyMorphProgress(1.0, false);
                }
                return;
            }

            if (morphAnimator == null || !morphAnimator.IsRunning)
            {
                ApplyMorphProgress(compacted ? 0.0 : 1.0, false);
            }
        }

        private void UpdateCompactStatus(StatusSnapshot snapshot, Color accent)
        {
            if (compactStatusText != null)
            {
                CrossfadeText(compactStatusText, snapshot.Title);
            }

            var compactTitle = CompactConversationTitle(snapshot);
            var hasTitle = !string.IsNullOrWhiteSpace(compactTitle);
            if (compactTitleText != null)
            {
                CrossfadeText(compactTitleText, compactTitle);
            }
            if (compactTitleChip != null)
            {
                compactTitleChip.Visibility = hasTitle ? Visibility.Visible : Visibility.Collapsed;
                compactTitleChip.MaxWidth = CompactTitleMaxWidth();
            }
            if (compactDivider != null)
            {
                compactDivider.Visibility = hasTitle ? Visibility.Visible : Visibility.Collapsed;
                AnimateColor((SolidColorBrush)compactDivider.Background, Color.FromArgb(120, accent.R, accent.G, accent.B));
            }

            UpdateCompactAgentIcon(snapshot.Agent, accent);

            if (compactDot != null)
            {
                AnimateColor((SolidColorBrush)compactDot.Fill, accent);
            }

            if (compactAccentBar != null)
            {
                AnimateColor((SolidColorBrush)compactAccentBar.Background, accent);
                compactAccentBar.Opacity = IsBusyForCompact(snapshot.Mode) ? 0.82 : 0.48;
            }

            SetCompactBlink(IsBusyForCompact(snapshot.Mode));
        }

        private string CompactConversationTitle(StatusSnapshot snapshot)
        {
            var title = DisplayConversationName(snapshot);
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            title = title.Trim();
            var maxChars = CompactTitleLimit();
            return title.Length <= maxChars ? title : title.Substring(0, maxChars);
        }

        private int CompactTitleLimit()
        {
            var value = settings == null ? 6 : settings.CompactTitleChars;
            return Math.Max(3, Math.Min(8, value));
        }

        private double CompactTargetWidth()
        {
            return Math.Max(CompactMinWidth, Math.Min(232, 190 + CompactTitleLimit() * 4.5));
        }

        private double CompactTitleMaxWidth()
        {
            return Math.Max(50, Math.Min(88, 32 + CompactTitleLimit() * 6.4));
        }

        private void UpdateCompactAgentIcon(string agent, Color accent)
        {
            if (compactAgentBadge == null || compactAgentImage == null)
            {
                return;
            }

            var key = CompactAgentKey(agent);
            compactAgentBadge.Visibility = string.IsNullOrWhiteSpace(key) ? Visibility.Collapsed : Visibility.Visible;
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            AnimateColor((SolidColorBrush)compactAgentBadge.BorderBrush, Color.FromArgb(58, accent.R, accent.G, accent.B));
            if (compactAgentIconKey == key)
            {
                return;
            }

            compactAgentIconKey = key;
            compactAgentImage.Source = LoadCompactAgentIcon(key);

            var fade = new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(190));
            fade.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };
            fade.SetValue(Timeline.DesiredFrameRateProperty, 60);
            compactAgentImage.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }

        private static string CompactAgentKey(string agent)
        {
            if (string.IsNullOrWhiteSpace(agent))
            {
                return string.Empty;
            }

            var normalized = agent.Trim().Replace(" ", "").Replace("-", "").Replace("_", "").ToUpperInvariant();
            if (normalized == "CODEX")
            {
                return "codex";
            }
            if (normalized == "CLAUDE" || normalized == "CLAUDECODE")
            {
                return "claude";
            }

            return string.Empty;
        }

        private static ImageSource LoadCompactAgentIcon(string key)
        {
            var fileName = key == "claude" ? "claude-code.ico" : "codex-openai.ico";
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", fileName);
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            try
            {
                var decoder = BitmapDecoder.Create(
                    new Uri(path, UriKind.Absolute),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                BitmapFrame bestFrame = null;
                foreach (var frame in decoder.Frames)
                {
                    if (bestFrame == null || frame.PixelWidth > bestFrame.PixelWidth)
                    {
                        bestFrame = frame;
                    }
                }

                if (bestFrame != null && bestFrame.CanFreeze)
                {
                    bestFrame.Freeze();
                }
                return bestFrame;
            }
            catch
            {
                return null;
            }
        }
        private bool IsPointerOverIsland()
        {
            return islandBorder != null && islandBorder.IsMouseOver;
        }

        private void ScheduleCompact()
        {
            if (compactTimer == null)
            {
                return;
            }

            compactTimer.Stop();
            if (!settings.CompactModeEnabled || !IsVisible || conversationListExpanded || lastSnapshot.Mode == IslandMode.Waiting)
            {
                return;
            }

            compactTimer.Interval = TimeSpan.FromSeconds(settings.CompactDelaySeconds);
            compactTimer.Start();
        }

        private void ApplyCompactSettings()
        {
            if (compactTimer == null)
            {
                return;
            }

            if (!settings.CompactModeEnabled)
            {
                compactTimer.Stop();
                SetCompact(false);
                return;
            }

            ScheduleCompact();
        }

        private void SetCompact(bool compact)
        {
            if (compactGrid == null || expandedGrid == null || morphAnimator == null)
            {
                return;
            }

            if (compact && conversationListExpanded)
            {
                return;
            }

            expandedTargetWidth = WidthFor(lastSnapshot.Mode);
            var target = compact ? 0.0 : 1.0;
            if (compacted == compact && Math.Abs(morphAnimator.Target - target) < 0.001)
            {
                return;
            }

            compacted = compact;
            morphAnimator.SetTarget(target);
        }

        private void ApplyMorphProgress(double progress, bool fromSpring)
        {
            if (expandedGrid == null || compactGrid == null || islandBorder == null)
            {
                return;
            }

            if (double.IsNaN(progress) || double.IsInfinity(progress))
            {
                progress = compacted ? 0.0 : 1.0;
            }

            var nearTarget = Math.Abs(progress - (compacted ? 0.0 : 1.0)) < 0.0008;
            if (fromSpring && !nearTarget && Math.Abs(progress - lastMorphProgress) < 0.0015)
            {
                return;
            }
            lastMorphProgress = progress;

            var sizeProgress = Clamp(progress, -0.018, 1.035);
            var visualProgress = Clamp01(progress);
            var contentProgress = SmoothStep(visualProgress);
            var targetWidth = expandedTargetWidth <= 0 ? WidthFor(lastSnapshot.Mode) : expandedTargetWidth;
            var width = Math.Max(138, Lerp(CompactTargetWidth(), targetWidth, sizeProgress));
            var height = Math.Max(42, Lerp(CompactHeight, ExpandedHeight, sizeProgress));

            if (Math.Abs(width - lastAppliedIslandWidth) > 0.4 || nearTarget)
            {
                islandBorder.Width = width;
                lastAppliedIslandWidth = width;
            }
            if (Math.Abs(height - lastAppliedIslandHeight) > 0.4 || nearTarget)
            {
                islandBorder.Height = height;
                lastAppliedIslandHeight = height;
            }
            ApplyStackMorph(width, height, contentProgress, nearTarget);

            var cornerR = Lerp(24, 32, contentProgress);
            islandBorder.CornerRadius = new CornerRadius(cornerR);
            islandBorder.Opacity = Lerp(0.94, 1.0, contentProgress);

            var layer = islandBorder.Child as RoundedClipGrid;
            if (layer != null)
            {
                layer.Radius = cornerR - 1;
            }

            var expandedOpacity = Clamp01((visualProgress - 0.18) / 0.64);
            var compactOpacity = Clamp01((0.84 - visualProgress) / 0.64);
            expandedGrid.Opacity = SmoothStep(expandedOpacity);
            compactGrid.Opacity = SmoothStep(compactOpacity);

            var overshoot = Math.Max(0, progress - 1.0);
            var undershoot = Math.Max(0, -progress);
            if (morphScale != null)
            {
                var scale = Lerp(0.992, 1.0, contentProgress) + overshoot * 0.006 - undershoot * 0.004;
                morphScale.ScaleX = scale;
                morphScale.ScaleY = scale;
            }
            if (islandTranslate != null)
            {
                islandTranslate.X = 0;
                islandTranslate.Y = Lerp(-0.8, 0, contentProgress) + overshoot * 0.2 - undershoot * 0.16;
            }

            if (islandShadow != null)
            {
                islandShadow.Opacity = ShadowOpacityFor(lastSnapshot.Mode) * contentProgress;
            }
        }

        private void ApplyStackMorph(double islandWidth, double islandHeight, double progress, bool force)
        {
            if (stackCardA == null || stackCardB == null)
            {
                return;
            }

            var aInset = Lerp(14, 28, progress);
            var bInset = Lerp(26, 56, progress);
            var aYOffset = Lerp(7, 14, progress);
            var bYOffset = Lerp(13, 24, progress);
            var stackHeight = Math.Max(36, islandHeight - Lerp(2, 6, progress));
            var radius = Lerp(22, 30, progress);

            var widthA = Math.Max(126, islandWidth - aInset);
            if (Math.Abs(widthA - lastAppliedStackWidthA) > 0.4 || force)
            {
                stackCardA.Width = widthA;
                stackCardA.Height = stackHeight;
                stackCardA.Margin = new Thickness(0, aYOffset, 0, 0);
                stackCardA.CornerRadius = new CornerRadius(radius);
                lastAppliedStackWidthA = widthA;
            }

            var widthB = Math.Max(116, islandWidth - bInset);
            if (Math.Abs(widthB - lastAppliedStackWidthB) > 0.4 || force)
            {
                stackCardB.Width = widthB;
                stackCardB.Height = stackHeight;
                stackCardB.Margin = new Thickness(0, bYOffset, 0, 0);
                stackCardB.CornerRadius = new CornerRadius(radius);
                lastAppliedStackWidthB = widthB;
            }
        }
        private void SetLayerOpacity(bool showCompact, bool animated)
        {
            ApplyMorphProgress(showCompact ? 0.0 : 1.0, false);
        }

        private void ApplyIslandShape(bool compact, bool animated)
        {
            ApplyMorphProgress(compact ? 0.0 : 1.0, false);
        }

        private void AnimateSizeFromCenter(double targetWidth, double targetHeight, TimeSpan duration, IEasingFunction easing)
        {
            expandedTargetWidth = targetWidth;
            if (morphAnimator != null)
            {
                morphAnimator.SetTarget(compacted ? 0.0 : 1.0);
            }
            else
            {
                ApplyMorphProgress(compacted ? 0.0 : 1.0, false);
            }
        }

        private void SetCompactBlink(bool active)
        {
            if (compactDot == null || compactDotScale == null || compactBlinking == active)
            {
                return;
            }

            compactBlinking = active;
            if (!active)
            {
                compactDot.BeginAnimation(OpacityProperty, null);
                compactDot.Opacity = 1;
                compactDotScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                compactDotScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                compactDotScale.ScaleX = 1;
                compactDotScale.ScaleY = 1;
                return;
            }

            var opacity = new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(880));
            opacity.AutoReverse = true;
            opacity.RepeatBehavior = RepeatBehavior.Forever;
            opacity.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            opacity.SetValue(Timeline.DesiredFrameRateProperty, 60);
            compactDot.BeginAnimation(OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);

            var scale = new DoubleAnimation(0.86, 1.18, TimeSpan.FromMilliseconds(880));
            scale.AutoReverse = true;
            scale.RepeatBehavior = RepeatBehavior.Forever;
            scale.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            scale.SetValue(Timeline.DesiredFrameRateProperty, 60);
            compactDotScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale, HandoffBehavior.SnapshotAndReplace);
            compactDotScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale, HandoffBehavior.SnapshotAndReplace);
        }

        private static bool IsBusyForCompact(IslandMode mode)
        {
            return mode == IslandMode.Thinking ||
                   mode == IslandMode.Editing ||
                   mode == IslandMode.Running ||
                   mode == IslandMode.Waiting ||
                   mode == IslandMode.Compacting ||
                   mode == IslandMode.Error;
        }

        private static double Lerp(double from, double to, double progress)
        {
            return from + (to - from) * progress;
        }

        private static double Clamp01(double value)
        {
            return Clamp(value, 0.0, 1.0);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        private static double SmoothStep(double value)
        {
            var t = Clamp01(value);
            return t * t * (3 - 2 * t);
        }
    }
}
