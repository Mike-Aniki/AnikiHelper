using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;

namespace AnikiHelper.Services.WebBrowser
{
    internal sealed class AnikiWebBrowserHomeView : UserControl
    {
        private const int MaximumFavoriteColumns = 4;
        private const double FavoriteCellWidth = 286.0;
        private const double MaximumSearchWidth = 1040.0;
        private static readonly ControlTemplate FavoriteButtonTemplate = CreateRoundedButtonTemplate(12.0);
        private static readonly ControlTemplate SearchButtonTemplate = CreateRoundedButtonTemplate(12.0);

        private readonly Action<string> searchRequested;
        private readonly Action<AnikiWebFavorite> favoriteRequested;
        private readonly Action keyboardRequested;
        private readonly TextBox searchBox;
        private readonly Button searchButton;
        private readonly Border searchBorder;
        private readonly UniformGrid favoritesGrid;
        private readonly TextBlock emptyText;
        private readonly List<Control> focusableControls = new List<Control>();

        private int favoriteColumns = 1;
        private int favoriteCount;

        public AnikiWebBrowserHomeView(
            Action<string> searchRequested,
            Action<AnikiWebFavorite> favoriteRequested,
            Action keyboardRequested,
            ImageSource logoSource)
        {
            this.searchRequested = searchRequested;
            this.favoriteRequested = favoriteRequested;
            this.keyboardRequested = keyboardRequested;

            Background = new SolidColorBrush(Color.FromRgb(10, 12, 16));
            Focusable = true;
            KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Contained);
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);

            var root = new Grid
            {
                Margin = new Thickness(48, 24, 48, 28)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 18)
            };

            if (logoSource != null)
            {
                header.Children.Add(new Image
                {
                    Source = logoSource,
                    Width = 112,
                    Height = 112,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            else
            {
                header.Children.Add(new Border
                {
                    Width = 92,
                    Height = 92,
                    CornerRadius = new CornerRadius(46),
                    Background = new SolidColorBrush(Color.FromRgb(45, 185, 207)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "A",
                        Foreground = Brushes.White,
                        FontSize = 44,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    }
                });
            }

            header.Children.Add(new TextBlock
            {
                Text = Loc("WebBrowser_HomeTitle", "Web Browser"),
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            });
            header.Children.Add(new TextBlock
            {
                Text = Loc("WebBrowser_HomeDescription", "Search the web or open one of your favorites."),
                Foreground = new SolidColorBrush(Color.FromRgb(180, 185, 194)),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            searchBorder = new Border
            {
                Width = MaximumSearchWidth,
                MaxWidth = MaximumSearchWidth,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(24, 28, 35)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 24)
            };

            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            searchBox = new TextBox
            {
                MinHeight = 48,
                FontSize = 19,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(12, 0, 12, 0),
                Background = new SolidColorBrush(Color.FromRgb(13, 16, 21)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                ToolTip = Loc("WebBrowser_SearchPlaceholder", "Search or enter a web address")
            };
            searchBox.KeyDown += SearchBox_KeyDown;
            searchBox.GotKeyboardFocus += delegate
            {
                searchBox.BorderBrush = Brushes.White;
                searchBox.BorderThickness = new Thickness(2);
            };
            searchBox.LostKeyboardFocus += delegate
            {
                searchBox.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
                searchBox.BorderThickness = new Thickness(1);
            };
            Grid.SetColumn(searchBox, 0);
            searchGrid.Children.Add(searchBox);

            var searchButtonNormalBackground = new SolidColorBrush(Color.FromRgb(24, 28, 35));
            var searchButtonFocusedBackground = new SolidColorBrush(Color.FromRgb(28, 75, 86));

            searchButton = new Button
            {
                Content = new TextBlock
                {
                    Text = Loc("LOCSearchLabel", "Search"),
                    Foreground = Brushes.White,
                    FontSize = 17,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    MaxWidth = 180,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                MinWidth = 140,
                MaxWidth = 220,
                MinHeight = 48,
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(18, 0, 18, 0),
                Background = searchButtonNormalBackground,
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FocusVisualStyle = null,
                Template = SearchButtonTemplate
            };
            searchButton.Click += delegate { SubmitSearch(); };
            ConfigureButtonFocusVisual(
                searchButton,
                searchButtonNormalBackground,
                searchButtonFocusedBackground);
            Grid.SetColumn(searchButton, 1);
            searchGrid.Children.Add(searchButton);

            searchBorder.Child = searchGrid;
            Grid.SetRow(searchBorder, 1);
            root.Children.Add(searchBorder);

            var favoritesPanel = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = 1240
            };
            favoritesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            favoritesPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            favoritesPanel.Children.Add(new TextBlock
            {
                Text = Loc("WebBrowser_FavoritesTitle", "Favorites"),
                Foreground = Brushes.White,
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            });

            favoritesGrid = new UniformGrid
            {
                Columns = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };

            emptyText = new TextBlock
            {
                Text = Loc("WebBrowser_NoFavorites", "No favorites yet. Add them from Aniki Helper settings in Desktop mode."),
                Foreground = new SolidColorBrush(Color.FromRgb(170, 175, 184)),
                FontSize = 17,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20),
                Visibility = Visibility.Collapsed
            };

            var favoritesContent = new Grid();
            favoritesContent.Children.Add(favoritesGrid);
            favoritesContent.Children.Add(emptyText);

            var scroll = new ScrollViewer
            {
                Content = favoritesContent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(scroll, 1);
            favoritesPanel.Children.Add(scroll);

            Grid.SetRow(favoritesPanel, 2);
            root.Children.Add(favoritesPanel);

            Content = root;
            SizeChanged += delegate { UpdateResponsiveLayout(); };
            RebuildFocusList();
        }

        public void RefreshFavorites(IEnumerable<AnikiWebFavorite> favorites)
        {
            favoritesGrid.Children.Clear();

            var safeFavorites = (favorites ?? Enumerable.Empty<AnikiWebFavorite>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url))
                .ToList();

            foreach (var favorite in safeFavorites)
            {
                favoritesGrid.Children.Add(CreateFavoriteButton(favorite));
            }

            favoriteCount = safeFavorites.Count;
            emptyText.Visibility = favoriteCount == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            UpdateResponsiveLayout();
            RebuildFocusList();
        }

        public void FocusInitial()
        {
            FocusControl(searchBox);
        }

        public void FocusSearchAndOpenKeyboard()
        {
            FocusControl(searchBox);
            keyboardRequested?.Invoke();
        }

        public void SubmitSearch()
        {
            var text = (searchBox.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                FocusControl(searchBox);
                keyboardRequested?.Invoke();
                return;
            }

            searchRequested?.Invoke(text);
        }

        public void ActivateFocused()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            var control = FindFocusableAncestor(focused);

            if (ReferenceEquals(control, searchBox))
            {
                keyboardRequested?.Invoke();
                return;
            }

            var button = control as Button;
            if (button != null)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                return;
            }

            FocusInitial();
        }

        public void MoveFocus(FocusNavigationDirection direction)
        {
            if (focusableControls.Count == 0)
            {
                return;
            }

            var focused = Keyboard.FocusedElement as DependencyObject;
            var current = FindFocusableAncestor(focused);
            var currentIndex = focusableControls.IndexOf(current);
            if (currentIndex < 0)
            {
                FocusInitial();
                return;
            }

            var targetIndex = GetTargetIndex(currentIndex, direction);
            if (targetIndex >= 0 && targetIndex < focusableControls.Count)
            {
                FocusControl(focusableControls[targetIndex]);
            }
        }

        private int GetTargetIndex(int currentIndex, FocusNavigationDirection direction)
        {
            if (currentIndex < 2)
            {
                if (direction == FocusNavigationDirection.Left || direction == FocusNavigationDirection.Right)
                {
                    return currentIndex == 0 ? 1 : 0;
                }

                if (direction == FocusNavigationDirection.Down && focusableControls.Count > 2)
                {
                    return 2;
                }

                return currentIndex;
            }

            var favoriteIndex = currentIndex - 2;
            var columns = Math.Max(1, favoriteColumns);
            var row = favoriteIndex / columns;
            var column = favoriteIndex % columns;
            var totalFavorites = focusableControls.Count - 2;
            var targetFavoriteIndex = favoriteIndex;

            switch (direction)
            {
                case FocusNavigationDirection.Left:
                    if (column > 0)
                    {
                        targetFavoriteIndex--;
                    }
                    break;

                case FocusNavigationDirection.Right:
                    if (column < columns - 1 && favoriteIndex + 1 < totalFavorites)
                    {
                        targetFavoriteIndex++;
                    }
                    break;

                case FocusNavigationDirection.Up:
                    if (row == 0)
                    {
                        return column == columns - 1 ? 1 : 0;
                    }
                    targetFavoriteIndex -= columns;
                    break;

                case FocusNavigationDirection.Down:
                    if (favoriteIndex + columns < totalFavorites)
                    {
                        targetFavoriteIndex += columns;
                    }
                    break;
            }

            return targetFavoriteIndex + 2;
        }

        private void UpdateResponsiveLayout()
        {
            var availableWidth = ActualWidth > 0
                ? Math.Max(420.0, ActualWidth - 110.0)
                : MaximumSearchWidth;

            searchBorder.Width = Math.Min(MaximumSearchWidth, availableWidth);

            var columnsAllowedByWidth = Math.Max(
                1,
                Math.Min(
                    MaximumFavoriteColumns,
                    (int)Math.Floor(availableWidth / FavoriteCellWidth)));

            favoriteColumns = favoriteCount > 0
                ? Math.Max(1, Math.Min(favoriteCount, columnsAllowedByWidth))
                : 1;

            favoritesGrid.Columns = favoriteColumns;
            favoritesGrid.Width = Math.Min(
                availableWidth,
                favoriteColumns * FavoriteCellWidth);
        }

        private Button CreateFavoriteButton(AnikiWebFavorite favorite)
        {
            var initial = new Border
            {
                Width = 58,
                Height = 58,
                CornerRadius = new CornerRadius(29),
                Background = new SolidColorBrush(Color.FromRgb(45, 185, 207)),
                Child = new TextBlock
                {
                    Text = favorite.DisplayInitial,
                    Foreground = Brushes.White,
                    FontSize = 25,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };

            var textPanel = new StackPanel
            {
                Margin = new Thickness(0, 13, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            textPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(favorite.Name) ? favorite.DisplayHost : favorite.Name,
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 220
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = favorite.DisplayHost,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 166, 176)),
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 220,
                Margin = new Thickness(0, 5, 0, 0)
            });

            var content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(initial);
            content.Children.Add(textPanel);

            var normalBackground = new SolidColorBrush(Color.FromRgb(24, 28, 35));
            var focusedBackground = new SolidColorBrush(Color.FromRgb(28, 75, 86));
            var normalBorder = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255));

            var button = new Button
            {
                Content = content,
                Tag = favorite,
                Width = 266,
                Height = 156,
                Margin = new Thickness(10),
                Padding = new Thickness(14),
                Background = normalBackground,
                Foreground = Brushes.White,
                BorderBrush = normalBorder,
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                FocusVisualStyle = null,
                Template = FavoriteButtonTemplate
            };
            button.Click += delegate { favoriteRequested?.Invoke(favorite); };
            button.GotKeyboardFocus += delegate
            {
                button.Background = focusedBackground;
                button.BorderBrush = Brushes.White;
                button.BorderThickness = new Thickness(3);
            };
            button.LostKeyboardFocus += delegate
            {
                button.Background = normalBackground;
                button.BorderBrush = normalBorder;
                button.BorderThickness = new Thickness(1);
            };
            return button;
        }

        private static ControlTemplate CreateRoundedButtonTemplate(double cornerRadius)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetBinding(
                Border.BackgroundProperty,
                new Binding("Background")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderBrushProperty,
                new Binding("BorderBrush")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderThicknessProperty,
                new Binding("BorderThickness")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(
                ContentPresenter.ContentProperty,
                new Binding("Content")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            presenter.SetBinding(
                ContentPresenter.ContentTemplateProperty,
                new Binding("ContentTemplate")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            presenter.SetBinding(
                FrameworkElement.MarginProperty,
                new Binding("Padding")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            presenter.SetBinding(
                FrameworkElement.HorizontalAlignmentProperty,
                new Binding("HorizontalContentAlignment")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });
            presenter.SetBinding(
                FrameworkElement.VerticalAlignmentProperty,
                new Binding("VerticalContentAlignment")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });

            border.AppendChild(presenter);

            return new ControlTemplate(typeof(Button))
            {
                VisualTree = border
            };
        }

        private static void ConfigureButtonFocusVisual(
            Button button,
            Brush normalBackground,
            Brush focusedBackground)
        {
            if (button == null)
            {
                return;
            }

            button.Background = normalBackground;
            button.GotKeyboardFocus += delegate
            {
                button.Background = focusedBackground;
                button.BorderBrush = Brushes.White;
                button.BorderThickness = new Thickness(3);
            };
            button.LostKeyboardFocus += delegate
            {
                button.Background = normalBackground;
                button.BorderBrush = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255));
                button.BorderThickness = new Thickness(1);
            };
        }

        private void RebuildFocusList()
        {
            focusableControls.Clear();
            focusableControls.Add(searchBox);
            focusableControls.Add(searchButton);

            foreach (var child in favoritesGrid.Children)
            {
                var control = child as Control;
                if (control != null)
                {
                    focusableControls.Add(control);
                }
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SubmitSearch();
                e.Handled = true;
            }
        }

        private static Control FindFocusableAncestor(DependencyObject source)
        {
            var current = source;
            while (current != null)
            {
                var control = current as Control;
                if (control != null && control.Focusable)
                {
                    return control;
                }

                current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }

            return null;
        }

        private static void FocusControl(Control control)
        {
            if (control == null)
            {
                return;
            }

            control.Focus();
            Keyboard.Focus(control);
            control.BringIntoView();
        }

        private static string Loc(string key, string fallback)
        {
            try
            {
                var value = Application.Current?.TryFindResource(key) as string;
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
