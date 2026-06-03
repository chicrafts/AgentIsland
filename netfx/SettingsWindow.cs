using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AgentIsland
{
    public sealed class SettingsWindow : Window
    {
        private readonly AppSettings settings;
        private CheckBox autoHideBox;
        private Slider delaySlider;
        private TextBlock delayValue;
        private ComboBox paletteCombo;
        private CheckBox calmMotionBox;
        private CheckBox multiCountBox;
        private CheckBox keepCompletedBox;
        private CheckBox fastProbeBox;
        private CheckBox compactModeBox;
        private Slider compactDelaySlider;
        private TextBlock compactDelayValue;
        private Slider compactTitleSlider;
        private TextBlock compactTitleValue;
        private bool loading;

        public SettingsWindow(AppSettings settings)
        {
            this.settings = settings;
            Title = "Agent Island 设置";
            Width = 430;
            Height = Math.Min(690, Math.Max(620, SystemParameters.WorkArea.Height - 80));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(13, 17, 25));
            Foreground = new SolidColorBrush(Color.FromRgb(238, 244, 255));
            ShowInTaskbar = false;
            Topmost = true;
            Content = BuildUi();
            LoadValues();
        }

        private UIElement BuildUi()
        {
            var root = new Border();
            root.Padding = new Thickness(22);
            root.Background = new SolidColorBrush(Color.FromRgb(13, 17, 25));

            var scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.CanContentScroll = false;
            root.Child = scroll;

            var stack = new StackPanel();
            scroll.Content = stack;

            var title = new TextBlock();
            title.Text = "Agent Island";
            title.FontSize = 22;
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(title);

            var subtitle = new TextBlock();
            subtitle.Text = "状态、外观和收缩动效";
            subtitle.Foreground = new SolidColorBrush(Color.FromRgb(148, 160, 180));
            subtitle.FontSize = 12;
            subtitle.Margin = new Thickness(0, 0, 0, 18);
            stack.Children.Add(subtitle);

            stack.Children.Add(Label("状态读取"));
            fastProbeBox = Check("极速状态探测");
            fastProbeBox.Margin = new Thickness(0, 7, 0, 14);
            fastProbeBox.Checked += delegate { SaveFromUi(); };
            fastProbeBox.Unchecked += delegate { SaveFromUi(); };
            stack.Children.Add(fastProbeBox);

            multiCountBox = Check("显示多个对话数量");
            multiCountBox.Margin = new Thickness(0, 0, 0, 10);
            multiCountBox.Checked += delegate { SaveFromUi(); };
            multiCountBox.Unchecked += delegate { SaveFromUi(); };
            stack.Children.Add(multiCountBox);

            keepCompletedBox = Check("完成会话常驻列表");
            keepCompletedBox.Margin = new Thickness(0, 0, 0, 18);
            keepCompletedBox.Checked += delegate { SaveFromUi(); };
            keepCompletedBox.Unchecked += delegate { SaveFromUi(); };
            stack.Children.Add(keepCompletedBox);

            stack.Children.Add(Label("悬浮窗"));
            compactModeBox = Check("自动收缩成小胶囊");
            compactModeBox.Margin = new Thickness(0, 7, 0, 10);
            compactModeBox.Checked += delegate { SaveFromUi(); };
            compactModeBox.Unchecked += delegate { SaveFromUi(); };
            stack.Children.Add(compactModeBox);

            var compactRow = SliderRow(1, 12, delegate
            {
                compactDelayValue.Text = ((int)compactDelaySlider.Value).ToString() + " 秒";
                SaveFromUi();
            }, out compactDelaySlider, out compactDelayValue);
            compactRow.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(compactRow);

            stack.Children.Add(Label("小窗文字数量（3-8字）"));
            var compactTitleHint = new TextBlock();
            compactTitleHint.Text = "控制收缩小胶囊里显示的会话标题长度";
            compactTitleHint.Foreground = new SolidColorBrush(Color.FromRgb(112, 126, 148));
            compactTitleHint.FontSize = 11;
            compactTitleHint.Margin = new Thickness(0, 3, 0, 0);
            stack.Children.Add(compactTitleHint);
            var compactTitleRow = SliderRow(3, 8, delegate
            {
                compactTitleValue.Text = ((int)compactTitleSlider.Value).ToString() + " 字";
                SaveFromUi();
            }, out compactTitleSlider, out compactTitleValue);
            compactTitleRow.Margin = new Thickness(0, 8, 0, 18);
            stack.Children.Add(compactTitleRow);

            autoHideBox = Check("空闲后自动隐藏");
            autoHideBox.Checked += delegate { SaveFromUi(); };
            autoHideBox.Unchecked += delegate { SaveFromUi(); };
            stack.Children.Add(autoHideBox);

            var delayRow = SliderRow(3, 60, delegate
            {
                delayValue.Text = ((int)delaySlider.Value).ToString() + " 秒";
                SaveFromUi();
            }, out delaySlider, out delayValue);
            delayRow.Margin = new Thickness(0, 10, 0, 18);
            stack.Children.Add(delayRow);

            stack.Children.Add(Label("配色"));
            paletteCombo = new ComboBox();
            paletteCombo.Margin = new Thickness(0, 7, 0, 18);
            paletteCombo.Items.Add(PaletteItem("极光", "Aurora"));
            paletteCombo.Items.Add(PaletteItem("海洋", "Ocean"));
            paletteCombo.Items.Add(PaletteItem("糖果", "Candy"));
            paletteCombo.Items.Add(PaletteItem("单色", "Mono"));
            paletteCombo.SelectionChanged += delegate { SaveFromUi(); };
            stack.Children.Add(paletteCombo);

            calmMotionBox = Check("柔和动画");
            calmMotionBox.Margin = new Thickness(0, 0, 0, 18);
            calmMotionBox.Checked += delegate { SaveFromUi(); };
            calmMotionBox.Unchecked += delegate { SaveFromUi(); };
            stack.Children.Add(calmMotionBox);

            var buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 8, 0, 0);

            var reset = Button("重置");
            reset.Margin = new Thickness(0, 0, 10, 0);
            reset.Click += delegate
            {
                settings.Reset();
                LoadValues();
            };
            buttons.Children.Add(reset);

            var close = Button("完成");
            close.Click += delegate { Close(); };
            buttons.Children.Add(close);
            stack.Children.Add(buttons);

            return root;
        }

        private void LoadValues()
        {
            loading = true;
            fastProbeBox.IsChecked = settings.FastStatusProbeEnabled;
            multiCountBox.IsChecked = settings.ShowMultiConversationCount;
            keepCompletedBox.IsChecked = settings.KeepCompletedConversations;
            compactModeBox.IsChecked = settings.CompactModeEnabled;
            compactDelaySlider.Value = settings.CompactDelaySeconds;
            compactDelayValue.Text = settings.CompactDelaySeconds + " 秒";
            compactTitleSlider.Value = settings.CompactTitleChars;
            compactTitleValue.Text = settings.CompactTitleChars + " 字";
            autoHideBox.IsChecked = settings.AutoHideEnabled;
            delaySlider.Value = settings.AutoHideDelaySeconds;
            delayValue.Text = settings.AutoHideDelaySeconds + " 秒";
            SelectPalette(settings.PaletteName);
            if (paletteCombo.SelectedIndex < 0)
            {
                SelectPalette("Aurora");
            }
            calmMotionBox.IsChecked = settings.CalmMotion;
            loading = false;
        }

        private void SaveFromUi()
        {
            if (loading)
            {
                return;
            }

            settings.FastStatusProbeEnabled = fastProbeBox.IsChecked == true;
            settings.ShowMultiConversationCount = multiCountBox.IsChecked == true;
            settings.KeepCompletedConversations = keepCompletedBox.IsChecked == true;
            settings.CompactModeEnabled = compactModeBox.IsChecked == true;
            settings.CompactDelaySeconds = (int)compactDelaySlider.Value;
            settings.CompactTitleChars = (int)compactTitleSlider.Value;
            settings.AutoHideEnabled = autoHideBox.IsChecked == true;
            settings.AutoHideDelaySeconds = (int)delaySlider.Value;
            settings.PaletteName = SelectedPaletteValue();
            settings.CalmMotion = calmMotionBox.IsChecked == true;
            settings.Save();
        }

        private void SelectPalette(string value)
        {
            for (var i = 0; i < paletteCombo.Items.Count; i++)
            {
                var item = paletteCombo.Items[i] as ComboBoxItem;
                if (item == null)
                {
                    continue;
                }

                var tag = item.Tag == null ? null : item.Tag.ToString();
                var content = item.Content == null ? null : item.Content.ToString();
                if (string.Equals(tag, value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(content, value, StringComparison.OrdinalIgnoreCase))
                {
                    paletteCombo.SelectedIndex = i;
                    return;
                }
            }
        }

        private string SelectedPaletteValue()
        {
            var item = paletteCombo.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag != null)
            {
                return item.Tag.ToString();
            }

            return "Aurora";
        }

        private static Grid SliderRow(double min, double max, RoutedPropertyChangedEventHandler<double> changed, out Slider slider, out TextBlock value)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            slider = new Slider();
            slider.Minimum = min;
            slider.Maximum = max;
            slider.TickFrequency = 1;
            slider.IsSnapToTickEnabled = true;
            slider.ValueChanged += changed;
            row.Children.Add(slider);

            value = new TextBlock();
            value.Width = 54;
            value.TextAlignment = TextAlignment.Right;
            value.Foreground = new SolidColorBrush(Color.FromRgb(190, 203, 220));
            value.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            return row;
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 160, 180))
            };
        }

        private static CheckBox Check(string text)
        {
            return new CheckBox
            {
                Content = text,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(232, 240, 255)),
                Margin = new Thickness(0, 0, 0, 0)
            };
        }

        private static ComboBoxItem PaletteItem(string text, string value)
        {
            var item = new ComboBoxItem();
            item.Content = text;
            item.Tag = value;
            return item;
        }

        private static Button Button(string text)
        {
            var button = new Button();
            button.Content = text;
            button.MinWidth = 86;
            button.Height = 32;
            button.Padding = new Thickness(14, 0, 14, 0);
            return button;
        }
    }
}
