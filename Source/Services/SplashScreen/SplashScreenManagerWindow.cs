using Microsoft.Win32;
using Playnite.SDK.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnikiHelper.Services.SplashScreen
{
    internal class SplashScreenManagerWindow : UserControl
    {
        private readonly SplashScreenTarget target;
        private readonly string targetFolder;

        private readonly ListBox filesList;
        private readonly Image imagePreview;
        private readonly MediaElement videoPreview;
        private readonly TextBlock infoText;

        private string MainSplashFilePath =>
            Path.Combine(targetFolder, "main_splash.txt");

        public SplashScreenManagerWindow(Game game, SplashScreenFolderService folderService)
            : this(SplashScreenTarget.FromGame(game, folderService))
        {
        }

        public SplashScreenManagerWindow(SplashScreenTarget target)
        {
            this.target = target;
            targetFolder = target.FolderPath;

            Directory.CreateDirectory(targetFolder);

            var root = new Grid
            {
                Margin = new Thickness(16)
            };

            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            filesList = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                Margin = new Thickness(0, 0, 16, 0)
            };

            filesList.SelectionChanged += (_, __) => UpdatePreview();

            Grid.SetColumn(filesList, 0);
            Grid.SetRow(filesList, 0);
            root.Children.Add(filesList);

            var previewGrid = new Grid
            {
                Background = Brushes.Black
            };

            imagePreview = new Image
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(16)
            };

            videoPreview = new MediaElement
            {
                Stretch = Stretch.Uniform,
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Volume = 0,
                IsMuted = true,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(16)
            };

            infoText = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(30)
            };

            previewGrid.Children.Add(imagePreview);
            previewGrid.Children.Add(videoPreview);
            previewGrid.Children.Add(infoText);

            Grid.SetColumn(previewGrid, 1);
            Grid.SetRow(previewGrid, 0);
            root.Children.Add(previewGrid);

            var buttonsPanel = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 0)
            };

            buttonsPanel.Children.Add(CreateButton(Loc("SplashManager_AddLocalFiles", "Add local files"), AddLocalFiles));
            if (target.Scope == SplashScreenScope.Game)
            {
                buttonsPanel.Children.Add(CreateButton(Loc("SplashManager_SearchImages", "Search Images"), SearchImages));
            }
            buttonsPanel.Children.Add(CreateButton(Loc("SplashManager_SetAsMain", "Set as main"), SetAsMainSplash));
            buttonsPanel.Children.Add(CreateButton(Loc("SplashManager_ClearMain", "Clear main"), ClearMainSplash));
            buttonsPanel.Children.Add(CreateButton(Loc("SplashManager_Refresh", "Refresh"), LoadFiles));
            buttonsPanel.Children.Add(CreateButton(Loc("SplashManager_OpenFolder", "Open folder"), OpenFolder));
            buttonsPanel.Children.Add(CreateButton(Loc("SplashManager_DeleteSelected", "Delete selected"), DeleteSelected));

            Grid.SetColumnSpan(buttonsPanel, 2);
            Grid.SetRow(buttonsPanel, 1);
            root.Children.Add(buttonsPanel);

            Content = root;

            Loaded += (_, __) => LoadFiles();
            Unloaded += (_, __) => StopVideoPreview();
        }

        private Button CreateButton(string text, Action action)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 135,
                Height = 36,
                Margin = new Thickness(6, 0, 0, 6),
                Padding = new Thickness(14, 4, 14, 4)
            };

            button.Click += (_, __) => action();
            return button;
        }

        private string Loc(string key, string fallback)
        {
            return Application.Current?.TryFindResource(key) as string ?? fallback;
        }

        private void LoadFiles()
        {
            filesList.Items.Clear();

            Directory.CreateDirectory(targetFolder);

            var files = SplashScreenMediaScanner.GetSupportedFiles(targetFolder);

            string mainSplash = null;

            if (File.Exists(MainSplashFilePath))
            {
                mainSplash = File.ReadAllText(MainSplashFilePath)?.Trim();
            }

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);

                var displayName = fileName;

                if (!string.IsNullOrWhiteSpace(mainSplash)
                    && string.Equals(mainSplash, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    displayName = "★ " + fileName;
                }

                filesList.Items.Add(new ListBoxItem
                {
                    Content = displayName,
                    Tag = file
                });
            }

            if (filesList.Items.Count > 0)
            {
                filesList.SelectedIndex = 0;
            }
            else
            {
                imagePreview.Source = null;
                imagePreview.Visibility = Visibility.Collapsed;
                videoPreview.Visibility = Visibility.Collapsed;
                infoText.Text = Loc(target.EmptyMessageKey, "No splash screen found.");
            }
        }

        private void UpdatePreview()
        {
            StopVideoPreview();

            imagePreview.Source = null;
            imagePreview.Visibility = Visibility.Collapsed;
            videoPreview.Visibility = Visibility.Collapsed;
            infoText.Text = string.Empty;

            var selectedItem = filesList.SelectedItem as ListBoxItem;
            var path = selectedItem?.Tag as string;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            if (SplashScreenMediaScanner.IsVideoFile(path))
            {
                try
                {
                    videoPreview.Source = new Uri(path, UriKind.Absolute);
                    videoPreview.Position = TimeSpan.Zero;
                    videoPreview.Visibility = Visibility.Visible;
                    videoPreview.Play();
                }
                catch
                {
                    infoText.Text = Loc("SplashManager_UnablePreviewVideo", "Unable to preview this video.");
                }

                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                imagePreview.Source = bitmap;
                imagePreview.Visibility = Visibility.Visible;
            }
            catch
            {
                infoText.Text = Loc("SplashManager_UnablePreviewFile", "Unable to preview this file.");
            }
        }

        private void StopVideoPreview()
        {
            try
            {
                videoPreview?.Stop();
                videoPreview?.Close();
            }
            catch
            {
            }
        }

        private void SetAsMainSplash()
        {
            var selectedItem = filesList.SelectedItem as ListBoxItem;
            var path = selectedItem?.Tag as string;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.WriteAllText(MainSplashFilePath, Path.GetFileName(path));
                LoadFiles();

                Playnite.SDK.API.Instance.Dialogs.ShowMessage(
                    Loc("SplashManager_MainSplashSet", "This image will now always be used for this folder."),
                    "Aniki Helper");
            }
            catch (Exception ex)
            {
                Playnite.SDK.API.Instance.Dialogs.ShowErrorMessage(ex.Message, "Aniki Helper");
            }
        }

        private void ClearMainSplash()
        {
            try
            {
                if (File.Exists(MainSplashFilePath))
                {
                    File.Delete(MainSplashFilePath);
                }

                LoadFiles();

                Playnite.SDK.API.Instance.Dialogs.ShowMessage(
                    Loc("SplashManager_RandomRestored", "Random choice has been re-enabled for this folder."),
                    "Aniki Helper");
            }
            catch (Exception ex)
            {
                Playnite.SDK.API.Instance.Dialogs.ShowErrorMessage(ex.Message, "Aniki Helper");
            }
        }

        private void AddLocalFiles()
        {
            var dialog = new OpenFileDialog
            {
                Title = Loc("SplashManager_SelectSplashFiles", "Select splash files"),
                Filter = SplashScreenMediaScanner.DialogFilter,
                Multiselect = true,
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            Directory.CreateDirectory(targetFolder);

            foreach (var source in dialog.FileNames)
            {
                if (!File.Exists(source))
                {
                    continue;
                }

                var destination = GetUniqueDestinationPath(source);
                File.Copy(source, destination, false);
            }

            LoadFiles();
        }

        private string GetUniqueDestinationPath(string sourcePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            var extension = Path.GetExtension(sourcePath);
            var destination = Path.Combine(targetFolder, fileName + extension);

            var index = 1;
            while (File.Exists(destination))
            {
                destination = Path.Combine(targetFolder, $"{fileName} ({index}){extension}");
                index++;
            }

            return destination;
        }

        private void DeleteSelected()
        {
            var selectedItem = filesList.SelectedItem as ListBoxItem;
            var path = selectedItem?.Tag as string;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            var result = Playnite.SDK.API.Instance.Dialogs.ShowMessage(
                string.Format(
                    Loc("SplashManager_DeleteConfirm", "Delete this splash file?{0}{1}"),
                    Environment.NewLine,
                    Path.GetFileName(path)),
                "Aniki Helper",
                MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                StopVideoPreview();
                File.Delete(path);
                LoadFiles();
            }
            catch (Exception ex)
            {
                Playnite.SDK.API.Instance.Dialogs.ShowErrorMessage(ex.Message, "Aniki Helper");
            }
        }

        private void SearchImages()
        {
            var gameName = target?.DisplayName;

            if (string.IsNullOrWhiteSpace(gameName)
                || string.Equals(gameName, "Unknown game", StringComparison.OrdinalIgnoreCase))
            {
                Playnite.SDK.API.Instance.Dialogs.ShowMessage(
                    Loc("SplashManager_NoGameNameForSearch", "No game name is available for this search."),
                    "Aniki Helper");

                return;
            }

            var query = $"{gameName} wallpaper imagesize:3840x2160";
            var url = "https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(query);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Playnite.SDK.API.Instance.Dialogs.ShowErrorMessage(ex.Message, "Aniki Helper");
            }
        }

        private void OpenFolder()
        {
            Directory.CreateDirectory(targetFolder);

            Process.Start(new ProcessStartInfo
            {
                FileName = targetFolder,
                UseShellExecute = true
            });
        }

    }
}