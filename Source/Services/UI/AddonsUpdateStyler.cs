using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnikiHelper
{
    internal static class AddonsUpdateStyler
    {
        private static DispatcherTimer timer;
        private static readonly HashSet<IntPtr> hooked = new HashSet<IntPtr>();

        public static void Start()
        {
            if (timer != null)
                return;

            Application.Current.Exit += (_, __) =>
            {
                timer?.Stop();
                timer = null;
                hooked.Clear();
            };

            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };

            timer.Tick += Tick;
            timer.Start();
        }

        private static void Tick(object sender, EventArgs e)
        {
            var app = Application.Current;
            if (app == null)
                return;

            // Only apply this visual patch when the Aniki theme is active.
            if (!IsAnikiThemeActive())
                return;

            foreach (Window w in app.Windows)
            {
                if (!string.Equals(
                        w.GetType().FullName,
                        "Playnite.FullscreenApp.Windows.AddonsUpdateWindow",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var handle = new System.Windows.Interop.WindowInteropHelper(w).Handle;

                if (handle != IntPtr.Zero && !hooked.Contains(handle))
                {
                    hooked.Add(handle);
                    w.Closed += (_, __) => hooked.Remove(handle);
                }

                // Important:
                // Re-apply while the window is open because some controls from DataTemplates
                // can be generated after the first Loaded pass.
                w.Dispatcher.InvokeAsync(() =>
                {
                    ApplyToInternalBorders(w);
                    ApplyToInternalControls(w);
                }, DispatcherPriority.ContextIdle);
            }
        }

        private static bool IsAnikiThemeActive()
        {
            try
            {
                return Application.Current?.TryFindResource("Aniki_ThemeMarker") is bool enabled && enabled;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyToInternalBorders(Window win)
        {
            try
            {
                var mainGrid = FindVisualChildren<Grid>(win)
                    .FirstOrDefault(g => string.Equals(g.Name, "GridMain", StringComparison.Ordinal));

                if (mainGrid != null)
                {
                    // Convert the original centered Playnite layout into a left side panel.
                    mainGrid.ColumnDefinitions.Clear();
                    mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(600) });
                    mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });

                    // Content fills the panel, footer stays at the bottom.
                    mainGrid.RowDefinitions.Clear();
                    mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
                    mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
                }

                var topStyle = Application.Current.TryFindResource("Aniki_AddonsUpdateWindowStyle_Top") as Style;
                var bottomStyle = Application.Current.TryFindResource("Aniki_AddonsUpdateWindowStyle_Bottom") as Style;

                foreach (var border in FindVisualChildren<Border>(win))
                {
                    var row = Grid.GetRow(border);
                    var col = Grid.GetColumn(border);

                    // Original main content panel:
                    // Grid.Column=1, Grid.Row=1
                    if (col == 1 && row == 1)
                    {
                        Grid.SetColumn(border, 0);
                        Grid.SetRow(border, 1);

                        border.ClearValue(Border.BackgroundProperty);
                        border.ClearValue(Border.CornerRadiusProperty);
                        border.ClearValue(Border.BorderThicknessProperty);
                        border.ClearValue(Border.BorderBrushProperty);
                        border.ClearValue(Border.EffectProperty);
                        border.ClearValue(Border.PaddingProperty);

                        if (topStyle != null)
                            border.Style = topStyle;

                        continue;
                    }

                    // Original footer buttons panel:
                    // Grid.Column=1, Grid.Row=2
                    if (col == 1 && row == 2)
                    {
                        Grid.SetColumn(border, 0);
                        Grid.SetRow(border, 2);

                        border.ClearValue(Border.BackgroundProperty);
                        border.ClearValue(Border.CornerRadiusProperty);
                        border.ClearValue(Border.BorderThicknessProperty);
                        border.ClearValue(Border.BorderBrushProperty);
                        border.ClearValue(Border.EffectProperty);
                        border.ClearValue(Border.PaddingProperty);

                        if (bottomStyle != null)
                            border.Style = bottomStyle;

                        continue;
                    }
                }
            }
            catch
            {
                // Visual patch only. Never crash Playnite for this.
            }
        }

        private static void ApplyToInternalControls(Window win)
        {
            try
            {
                var checkBoxStyle = Application.Current.TryFindResource("Aniki_AddonsUpdateCheckBoxStyle") as Style;
                var buttonStyle = Application.Current.TryFindResource("Aniki_AddonsUpdateButtonStyle") as Style;

                foreach (var control in FindVisualChildren<Control>(win))
                {
                    var typeName = control.GetType().Name;
                    var fullName = control.GetType().FullName ?? string.Empty;

                    if (typeName.Contains("CheckBoxEx") || fullName.Contains("CheckBoxEx"))
                    {
                        if (checkBoxStyle != null && control.Style != checkBoxStyle)
                        {
                            control.ClearValue(Control.MarginProperty);
                            control.ClearValue(Control.PaddingProperty);
                            control.ClearValue(Control.BackgroundProperty);
                            control.ClearValue(Control.BorderBrushProperty);
                            control.ClearValue(Control.BorderThicknessProperty);
                            control.FocusVisualStyle = null;
                            control.Style = checkBoxStyle;
                        }

                        continue;
                    }

                    if (typeName.Contains("ButtonEx") || fullName.Contains("ButtonEx"))
                    {
                        if (buttonStyle != null && control.Style != buttonStyle)
                        {
                            control.ClearValue(Control.MarginProperty);
                            control.ClearValue(Control.PaddingProperty);
                            control.ClearValue(Control.BackgroundProperty);
                            control.ClearValue(Control.BorderBrushProperty);
                            control.ClearValue(Control.BorderThicknessProperty);
                            control.FocusVisualStyle = null;
                            control.Style = buttonStyle;
                        }

                        continue;
                    }
                }

                ApplyTitleStyle(win);
                ApplyFooterLayout(win);
                ApplyTextSpacing(win);
            }
            catch
            {
                // Visual patch only.
            }
        }

        private static void ApplyTitleStyle(Window win)
        {
            try
            {
                var title = FindVisualChildren<TextBlock>(win)
                    .FirstOrDefault(tb => DockPanel.GetDock(tb) == Dock.Top);

                if (title == null)
                    return;

                title.Margin = new Thickness(0, 10, 0, 22);
                title.HorizontalAlignment = HorizontalAlignment.Center;
                title.TextAlignment = TextAlignment.Center;
                title.FontSize = 26;
                title.FontWeight = FontWeights.SemiBold;

                var highlight = Application.Current.TryFindResource("TextHighlightBrush") as Brush;
                if (highlight != null)
                    title.Foreground = highlight;
            }
            catch
            {
                // Visual patch only.
            }
        }

        private static void ApplyFooterLayout(Window win)
        {
            try
            {
                foreach (var dock in FindVisualChildren<DockPanel>(win))
                {
                    // Original footer DockPanel has Margin="20".
                    if (dock.Margin.Left == 20 &&
                        dock.Margin.Top == 20 &&
                        dock.Margin.Right == 20 &&
                        dock.Margin.Bottom == 20)
                    {
                        dock.Margin = new Thickness(0);
                        dock.Height = 58;
                        dock.LastChildFill = false;
                        dock.HorizontalAlignment = HorizontalAlignment.Stretch;
                        dock.VerticalAlignment = VerticalAlignment.Center;
                    }
                }
            }
            catch
            {
                // Visual patch only.
            }
        }

        private static void ApplyTextSpacing(Window win)
        {
            try
            {
                foreach (var sp in FindVisualChildren<StackPanel>(win))
                {
                    // Original addon row content StackPanel has Margin="5,0,0,0".
                    if (sp.Margin.Left == 5 &&
                        sp.Margin.Top == 0 &&
                        sp.Margin.Right == 0 &&
                        sp.Margin.Bottom == 0)
                    {
                        sp.Margin = new Thickness(0);
                    }
                }

                foreach (var tb in FindVisualChildren<TextBlock>(win))
                {
                    if (tb.FontSize < 18)
                        tb.FontSize = 16;

                    tb.TextTrimming = TextTrimming.CharacterEllipsis;
                }
            }
            catch
            {
                // Visual patch only.
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T match)
                    yield return match;

                foreach (var sub in FindVisualChildren<T>(child))
                    yield return sub;
            }
        }
    }
}