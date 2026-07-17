using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;

namespace AnikiHelper.Services
{
    public class AnikiWindowCommandProvider
    {
        private readonly Func<string, ICommand> commandFactory;
        private readonly Dictionary<string, ICommand> cache = new Dictionary<string, ICommand>();

        public AnikiWindowCommandProvider(Func<string, ICommand> commandFactory)
        {
            this.commandFactory = commandFactory;
        }

        public ICommand this[string styleKey]
        {
            get
            {
                if (string.IsNullOrWhiteSpace(styleKey))
                    return null;

                if (!cache.TryGetValue(styleKey, out var command))
                {
                    command = commandFactory(styleKey);
                    cache[styleKey] = command;
                }

                return command;
            }
        }
    }

    public class AnikiWindowManager
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly ILogger logger;
        private readonly Stack<Window> windows = new Stack<Window>();
        private Func<bool> isOverlayOpenOrOpening;
        public event Action<bool> OpenWindowStateChanged;
        private bool lastReportedOpenWindowState;
        private const string QuickAccessWindowStyleName = "QuickAccessWindowStyle";

        public AnikiWindowManager(IPlayniteAPI playniteApi)
        {
            this.playniteApi = playniteApi;
            logger = LogManager.GetLogger();
        }

        public void SetOverlayOpenStateProvider(Func<bool> provider)
        {
            isOverlayOpenOrOpening = provider;
        }

        public bool HasOpenWindow
        {
            get
            {
                try
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.CheckAccess())
                    {
                        return dispatcher.Invoke(new Func<bool>(() =>
                        {
                            CleanupClosedWindows();
                            return windows.Any();
                        }));
                    }

                    CleanupClosedWindows();
                    return windows.Any();
                }
                catch
                {
                    return windows.Any();
                }
            }
        }

        public void OpenWindow(string parameter)
        {
            ParseOpenParameter(parameter, out var styleKey, out var focusTargetName, out var focusFirst, out var refocusAfterClick, out var noDim);
            Open(styleKey, false, focusTargetName, focusFirst, refocusAfterClick, noDim);
        }

        public void OpenWindow(string styleKey, string focusTargetName)
        {
            Open(styleKey, false, focusTargetName, false, false, false);
        }

        public void OpenChildWindow(string parameter)
        {
            ParseOpenParameter(parameter, out var styleKey, out var focusTargetName, out var focusFirst, out var refocusAfterClick, out var noDim);
            Open(styleKey, true, focusTargetName, focusFirst, refocusAfterClick, noDim);
        }

        private void ParseOpenParameter(string parameter, out string styleKey, out string focusTargetName, out bool focusFirst, out bool refocusAfterClick, out bool noDim)
        {
            styleKey = parameter;
            focusTargetName = null;
            focusFirst = false;
            refocusAfterClick = false;
            noDim = false;

            if (string.IsNullOrWhiteSpace(parameter) || !parameter.Contains("|"))
            {
                return;
            }

            var parts = parameter.Split('|');

            styleKey = parts.Length > 0 ? parts[0] : parameter;

            for (int i = 1; i < parts.Length; i++)
            {
                var option = parts[i]?.Trim();

                if (string.IsNullOrWhiteSpace(option))
                {
                    continue;
                }

                if (IsFocusFirstOption(option))
                {
                    focusFirst = true;
                    continue;
                }

                if (IsRefocusAfterClickOption(option))
                {
                    refocusAfterClick = true;
                    continue;
                }

                if (IsNoDimOption(option))
                {
                    noDim = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(focusTargetName))
                {
                    focusTargetName = option;
                }
            }
        }

        private static bool IsFocusFirstOption(string option)
        {
            return string.Equals(option, "FocusFirst", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(option, "AutoFocusFirst", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(option, "FirstFocus", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRefocusAfterClickOption(string option)
        {
            return string.Equals(option, "RefocusAfterClick", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(option, "RefocusOnClick", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(option, "RefocusAfterAction", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNoDimOption(string option)
        {
            return string.Equals(option, "NoDim", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(option, "NoOverlayDim", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(option, "TransparentWindow", StringComparison.OrdinalIgnoreCase);
        }

        public bool CloseTopWindow()
        {
            CleanupClosedWindows();

            if (!windows.Any())
                return false;

            var top = windows.Pop();

            if (top != null && top.IsVisible)
            {
                top.Hide();

                top.Dispatcher.BeginInvoke(new Action(() =>
                {
                    top.Close();
                }), DispatcherPriority.ApplicationIdle);
            }

            NotifyOpenWindowStateChanged();
            FocusTopWindow();
            return true;
        }

        public bool IsTopWindowActive()
        {
            CleanupClosedWindows();

            if (!windows.Any())
                return false;

            var top = windows.Peek();
            return top != null && top.IsActive;
        }

        private void Open(string styleKey, bool forceChild, string focusTargetName, bool focusFirst, bool refocusAfterClick, bool noDim)
        {
            if (string.IsNullOrWhiteSpace(styleKey))
                return;

            // Reserve custom pages and the in-game overlay as mutually exclusive UI layers.
            // This first check blocks immediately when the overlay shortcut has already queued an opening.
            if (IsOverlayBlockingCustomWindowOpen())
            {
                LogBlockedWindowOpen(styleKey, "request");
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            dispatcher.Invoke(() =>
            {
                CleanupClosedWindows();

                // Check again on the UI thread because an overlay request can race this queued window open.
                if (IsOverlayBlockingCustomWindowOpen())
                {
                    LogBlockedWindowOpen(styleKey, "UI dispatch");
                    return;
                }

                if (IsPlayniteSettingsWindowOpen())
                {
                    return;
                }

                var existingWindow = windows.FirstOrDefault(w =>
                    w != null &&
                    w.IsVisible &&
                    string.Equals(w.Tag as string, styleKey, StringComparison.OrdinalIgnoreCase));

                if (existingWindow != null)
                {
                    existingWindow.Activate();
                    existingWindow.Focus();
                    return;
                }

                if (!string.Equals(styleKey, QuickAccessWindowStyleName, StringComparison.OrdinalIgnoreCase))
                {
                    CloseWindowByStyleKey(QuickAccessWindowStyleName);
                    CleanupClosedWindows();
                }

                // NoDim is used for theme child windows that draw their own dim/gradient in XAML.
                // Using a raw transparent WPF window avoids Playnite's dialog chrome/background dim for this window only.
                var window = noDim
                    ? new Window()
                    : playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                    {
                        ShowMinimizeButton = false
                    });

                window.Tag = styleKey;
                window.ShowInTaskbar = false;
                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.SizeToContent = SizeToContent.Manual;
                window.WindowStartupLocation = WindowStartupLocation.Manual;

                // Important : ne pas utiliser Maximized ici.
                // On copie la vraie fenêtre Playnite pour éviter que les plugins ouverts depuis Aniki
                // récupèrent un mauvais owner / mauvais ratio / mauvaises coordonnées.
                var parent = playniteApi.Dialogs.GetCurrentAppWindow();

                if (parent != null)
                {
                    window.Owner = parent;

                    window.WindowState = WindowState.Normal;

                    window.Left = parent.Left;
                    window.Top = parent.Top;

                    window.Width = parent.ActualWidth > 0 ? parent.ActualWidth : parent.Width;
                    window.Height = parent.ActualHeight > 0 ? parent.ActualHeight : parent.Height;
                }
                else
                {
                    window.WindowState = WindowState.Normal;

                    window.Left = 0;
                    window.Top = 0;
                    window.Width = SystemParameters.PrimaryScreenWidth;
                    window.Height = SystemParameters.PrimaryScreenHeight;
                }

                if (forceChild)
                {
                    window.AllowsTransparency = true;
                    window.Background = Brushes.Transparent;
                }

                var style = Application.Current.TryFindResource(styleKey) as Style;
                if (style != null)
                {
                    window.Content = new Viewbox
                    {
                        Stretch = Stretch.Uniform,
                        Child = new Grid
                        {
                            Width = 1920,
                            Height = 1080,
                            Children =
                            {
                                new ContentControl
                                {
                                    Focusable = false,
                                    Style = style
                                }
                            }
                        }
                    };
                }

                if (forceChild && windows.Any())
                {
                    window.Owner = windows.Peek();
                }
                else if (parent != null)
                {
                    window.Owner = parent;
                }

                window.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        e.Handled = true;
                        CloseTopWindow();
                    }
                };

                window.Closed += (s, e) =>
                {
                    RemoveWindow(window);
                };

                // Final race check immediately before the window becomes part of the active stack.
                // If the overlay won while this window was being built, discard this unopened window.
                if (IsOverlayBlockingCustomWindowOpen())
                {
                    LogBlockedWindowOpen(styleKey, "before show");

                    try
                    {
                        window.Close();
                    }
                    catch
                    {
                    }

                    return;
                }

                windows.Push(window);

                window.Show();
                window.Activate();
                window.Focus();

                NotifyOpenWindowStateChanged();

                if (refocusAfterClick)
                {
                    AttachRefocusAfterClick(window);
                }

                if (!string.IsNullOrWhiteSpace(focusTargetName) || focusFirst)
                {
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ApplyInitialFocus(window, focusTargetName, focusFirst);
                    }), DispatcherPriority.ApplicationIdle);
                }
            });
        }

        private bool IsOverlayBlockingCustomWindowOpen()
        {
            try
            {
                return isOverlayOpenOrOpening?.Invoke() == true;
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "[AnikiHelper][WindowManager] Failed to query overlay state.");
                return false;
            }
        }

        private void LogBlockedWindowOpen(string styleKey, string stage)
        {
            try
            {
                logger?.Debug($"[AnikiHelper][WindowManager] Window open blocked by overlay. Style={styleKey}, Stage={stage}");
            }
            catch
            {
            }
        }

        private static bool IsPlayniteSettingsWindowOpen()
        {
            return Application.Current.Windows
                .OfType<Window>()
                .Any(w =>
                    w.IsVisible &&
                    (w.GetType().FullName ?? "").IndexOf(
                        "SettingsWindow",
                        StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null)
                return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                var element = child as T;
                if (element != null && element.Name == name)
                    return element;

                var result = FindVisualChildByName<T>(child, name);
                if (result != null)
                    return result;
            }

            return null;
        }

        private static void ApplyInitialFocus(Window window, string focusTargetName, bool focusFirst)
        {
            if (window == null || !window.IsVisible)
            {
                return;
            }

            window.UpdateLayout();

            FrameworkElement target = null;

            if (!string.IsNullOrWhiteSpace(focusTargetName))
            {
                target = FindVisualChildByName<FrameworkElement>(window, focusTargetName);
            }

            if (target == null && focusFirst)
            {
                var focusedElement = Keyboard.FocusedElement as FrameworkElement;

                if (focusedElement != null &&
                    !ReferenceEquals(focusedElement, window) &&
                    IsDescendantOf(focusedElement, window))
                {
                    return;
                }

                target = FindFirstFocusableElement(window);
            }

            if (target == null)
            {
                return;
            }

            target.Focus();
            Keyboard.Focus(target);

            var focusScope = FocusManager.GetFocusScope(target);

            if (focusScope != null)
            {
                FocusManager.SetFocusedElement(focusScope, target);
            }
        }

        private static FrameworkElement FindFirstFocusableElement(DependencyObject parent)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                var element = child as FrameworkElement;

                if (element != null &&
                    element.Focusable &&
                    element.IsEnabled &&
                    element.IsVisible)
                {
                    var control = element as Control;

                    if (control == null || control.IsTabStop)
                    {
                        return element;
                    }
                }

                var result = FindFirstFocusableElement(child);

                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
            if (child == null || parent == null)
            {
                return false;
            }

            var current = child;

            while (current != null)
            {
                if (ReferenceEquals(current, parent))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void AttachRefocusAfterClick(Window window)
        {
            if (window == null)
            {
                return;
            }

            window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((sender, e) =>
            {
                var clickedElement = e.OriginalSource as DependencyObject;
                var clickedFocusable = FindNearestFocusableElement(clickedElement);
                var clickedTag = clickedFocusable != null ? clickedFocusable.Tag : null;

                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefocusWindowIfNeeded(window, clickedFocusable, clickedTag);
                }), DispatcherPriority.ApplicationIdle);

            }), true);
        }

        private static void RefocusWindowIfNeeded(Window window, FrameworkElement preferredTarget, object preferredTag)
        {
            if (window == null || !window.IsVisible)
            {
                return;
            }

            window.UpdateLayout();

            var focusedElement = Keyboard.FocusedElement as FrameworkElement;

            if (focusedElement != null &&
                IsValidFocusableTarget(focusedElement) &&
                IsDescendantOf(focusedElement, window))
            {
                return;
            }

            FrameworkElement target = null;

            // 1. Try to find the regenerated button with the same Tag / device Id.
            if (preferredTag != null && !string.IsNullOrWhiteSpace(preferredTag.ToString()))
            {
                target = FindFocusableElementByTag(window, preferredTag);
            }

            // 2. If the original clicked button still exists, reuse it.
            if (target == null &&
                preferredTarget != null &&
                IsValidFocusableTarget(preferredTarget) &&
                IsDescendantOf(preferredTarget, window))
            {
                target = preferredTarget;
            }

            // 3. Fallback only if nothing else works.
            if (target == null)
            {
                target = FindFirstFocusableElement(window);
            }

            if (target == null)
            {
                return;
            }

            target.Focus();
            Keyboard.Focus(target);

            var focusScope = FocusManager.GetFocusScope(target);

            if (focusScope != null)
            {
                FocusManager.SetFocusedElement(focusScope, target);
            }
        }

        private static FrameworkElement FindFocusableElementByTag(DependencyObject parent, object tag)
        {
            if (parent == null || tag == null)
            {
                return null;
            }

            var tagText = tag.ToString();

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var element = child as FrameworkElement;

                if (element != null &&
                    IsValidFocusableTarget(element) &&
                    element.Tag != null &&
                    string.Equals(element.Tag.ToString(), tagText, StringComparison.OrdinalIgnoreCase))
                {
                    return element;
                }

                var result = FindFocusableElementByTag(child, tag);

                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static FrameworkElement FindNearestFocusableElement(DependencyObject element)
        {
            var current = element;

            while (current != null)
            {
                var frameworkElement = current as FrameworkElement;

                if (frameworkElement != null && IsValidFocusableTarget(frameworkElement))
                {
                    return frameworkElement;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static bool IsValidFocusableTarget(FrameworkElement element)
        {
            if (element == null ||
                !element.Focusable ||
                !element.IsEnabled ||
                !element.IsVisible)
            {
                return false;
            }

            var control = element as Control;

            if (control != null && !control.IsTabStop)
            {
                return false;
            }

            return true;
        }

        private void CloseWindowByStyleKey(string styleKey)
        {
            if (string.IsNullOrWhiteSpace(styleKey))
                return;

            var windowsToClose = windows
                .Where(w =>
                    w != null &&
                    w.IsVisible &&
                    string.Equals(w.Tag as string, styleKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var window in windowsToClose)
            {
                // Important :
                // Hide() retire la fenêtre tout de suite sans bloquer l’ouverture de la prochaine.
                // Close() est repoussé à l'idle pour éviter que WPF détruise tout le visual tree
                // du Quick Access avant d'afficher Profile / Store / etc.
                window.Hide();

                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (window != null)
                    {
                        window.Close();
                    }
                }), DispatcherPriority.ApplicationIdle);
            }

            NotifyOpenWindowStateChanged();
        }

        private void RemoveWindow(Window window)
        {
            if (window == null || !windows.Contains(window))
                return;

            var rebuilt = windows.Reverse().Where(w => !ReferenceEquals(w, window)).ToList();

            windows.Clear();

            foreach (var item in rebuilt)
                windows.Push(item);

            NotifyOpenWindowStateChanged();
        }

        private void CleanupClosedWindows()
        {
            var opened = windows.Reverse()
                .Where(w => w != null && w.IsVisible)
                .ToList();

            windows.Clear();

            foreach (var item in opened)
                windows.Push(item);
        }

        private void NotifyOpenWindowStateChanged()
        {
            try
            {
                bool hasVisibleWindow = windows.Any(window =>
                    window != null &&
                    window.IsVisible);

                if (lastReportedOpenWindowState == hasVisibleWindow)
                {
                    return;
                }

                lastReportedOpenWindowState = hasVisibleWindow;
                OpenWindowStateChanged?.Invoke(hasVisibleWindow);
            }
            catch
            {
                // État informatif uniquement : ne jamais casser une fenêtre.
            }
        }

        private void FocusTopWindow()
        {
            CleanupClosedWindows();

            if (windows.Any())
            {
                var top = windows.Peek();
                top.Activate();
                top.Focus();
            }
            else
            {
                playniteApi.Dialogs.GetCurrentAppWindow()?.Activate();
            }
        }
    }
}