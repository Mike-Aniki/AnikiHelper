using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AnikiHelper.Services.SplashScreen
{
    internal class SplashScreenTargetPickerWindow : UserControl
    {
        private readonly ListBox targetsList;
        private readonly Action<SplashScreenTarget> openTargetAction;

        public SplashScreenTargetPickerWindow(
            string title,
            string description,
            IEnumerable<SplashScreenTarget> targets,
            Action<SplashScreenTarget> openTargetAction)
        {
            this.openTargetAction = openTargetAction;

            var root = new Grid
            {
                Margin = new Thickness(16)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            };

            Grid.SetRow(titleText, 0);
            root.Children.Add(titleText);

            var descText = new TextBlock
            {
                Text = description,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };

            Grid.SetRow(descText, 1);
            root.Children.Add(descText);

            targetsList = new ListBox
            {
                ItemsSource = targets?.OrderBy(x => x.DisplayName).ToList(),
                DisplayMemberPath = "DisplayName",
                Margin = new Thickness(0, 0, 0, 0)
            };

            targetsList.MouseDoubleClick += (_, __) => OpenSelectedTarget();

            targetsList.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    OpenSelectedTarget();
                    e.Handled = true;
                }
            };

            Grid.SetRow(targetsList, 2);
            root.Children.Add(targetsList);

            Content = root;
        }

        private void OpenSelectedTarget()
        {
            var target = targetsList.SelectedItem as SplashScreenTarget;
            if (target == null)
            {
                return;
            }

            openTargetAction?.Invoke(target);
        }
    }
}