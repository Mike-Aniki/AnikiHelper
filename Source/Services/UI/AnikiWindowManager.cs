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
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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
        private sealed class TrackedWindow
        {
            public Window Window { get; set; }
            public string StyleKey { get; set; }
            public Window Parent { get; set; }
            public bool IsChild { get; set; }
            public bool IsClosing { get; set; }
        }

        private readonly IPlayniteAPI playniteApi;
        private readonly ILogger logger;
        private readonly Stack<TrackedWindow> windows = new Stack<TrackedWindow>();
        private readonly HashSet<Window> secondaryMusicWindows = new HashSet<Window>();
        private readonly HashSet<Window> suppressFinalFocusRestoreWindows = new HashSet<Window>();
        private Func<bool> isOverlayOpenOrOpening;
        private Func<string, bool> cancelRequestHandler;
        public event Action<bool> OpenWindowStateChanged;
        public event Action<bool> SecondaryMusicStateChanged;
        private bool lastReportedOpenWindowState;
        private bool lastReportedSecondaryMusicState;
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

        public void SetCancelRequestHandler(Func<string, bool> handler)
        {
            cancelRequestHandler = handler;
        }

        public void FocusTopWindowElement(string elementName)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        CleanupClosedWindows();
                        var window = windows.Any() ? windows.Peek()?.Window : null;
                        if (window == null || !window.IsVisible)
                        {
                            return;
                        }

                        ApplyInitialFocus(window, elementName, false);
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, $"[AnikiHelper][WindowManager] Failed to focus top window element: {elementName}");
                    }
                }), DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[AnikiHelper][WindowManager] Failed to queue top window focus: {elementName}");
            }
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

        public bool HasSecondaryMusicWindow
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
                            return HasVisibleSecondaryMusicWindow();
                        }));
                    }

                    CleanupClosedWindows();
                    return HasVisibleSecondaryMusicWindow();
                }
                catch
                {
                    // Fallback without touching WPF visibility from a non-UI thread.
                    return secondaryMusicWindows.Any(window =>
                        window != null &&
                        windows.Any(entry => ReferenceEquals(entry.Window, window)));
                }
            }
        }

        public void OpenWindow(string parameter)
        {
            ParseOpenParameter(parameter, out var styleKey, out var focusTargetName, out var focusFirst, out var refocusAfterClick, out var noDim, out var secondaryMusic);
            Open(styleKey, false, focusTargetName, focusFirst, refocusAfterClick, noDim, secondaryMusic);
        }

        public void OpenWindow(string styleKey, string focusTargetName)
        {
            Open(styleKey, false, focusTargetName, false, false, false, false);
        }

        public void OpenChildWindow(string parameter)
        {
            ParseOpenParameter(parameter, out var styleKey, out var focusTargetName, out var focusFirst, out var refocusAfterClick, out var noDim, out var secondaryMusic);
            Open(styleKey, true, focusTargetName, focusFirst, refocusAfterClick, noDim, secondaryMusic);
        }

        private void ParseOpenParameter(string parameter, out string styleKey, out string focusTargetName, out bool focusFirst, out bool refocusAfterClick, out bool noDim, out bool secondaryMusic)
        {
            styleKey = parameter;
            focusTargetName = null;
            focusFirst = false;
            refocusAfterClick = false;
            noDim = false;
            secondaryMusic = false;

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

                if (IsSecondaryMusicOption(option))
                {
                    secondaryMusic = true;
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

        private static bool IsSecondaryMusicOption(string option)
        {
            return string.Equals(option, "SecondaryMusic", StringComparison.OrdinalIgnoreCase);
        }

        public void RegisterExternalWindow(Window window, string styleKey, bool isChild = true, bool secondaryMusic = false)
        {
            if (window == null)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => RegisterExternalWindow(window, styleKey, isChild, secondaryMusic));
                return;
            }

            CleanupClosedWindows();

            if (windows.Any(tracked => tracked?.Window != null && ReferenceEquals(tracked.Window, window)))
            {
                logger?.Debug(
                    $"[AnikiHelper][WindowManager] External window already tracked. " +
                    $"Style={styleKey ?? "<none>"}, {DescribeWindow(window)}, StackCount={windows.Count}");
                return;
            }

            var parent = window.Owner ?? playniteApi.Dialogs.GetCurrentAppWindow();

            if (window.Tag == null && !string.IsNullOrWhiteSpace(styleKey))
            {
                window.Tag = styleKey;
            }

            var trackedEntry = new TrackedWindow
            {
                Window = window,
                StyleKey = styleKey,
                Parent = parent,
                IsChild = isChild,
                IsClosing = false
            };

            windows.Push(trackedEntry);

            if (secondaryMusic)
            {
                secondaryMusicWindows.Add(window);
            }

            window.Closed += (s, e) =>
            {
                logger?.Debug(
                    $"[AnikiHelper][WindowManager] CLOSED event received for external window. " +
                    $"{DescribeWindow(window)}, StackCountBeforeRemove={windows.Count}");

                RemoveWindow(window);
            };

            logger?.Debug(
                $"[AnikiHelper][WindowManager] External window registered. {DescribeTrackedWindow(trackedEntry)}, " +
                $"StackCount={windows.Count}");

            NotifyOpenWindowStateChanged();
        }

        public bool HandleCancelRequest(string source = null)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                return dispatcher.Invoke(new Func<bool>(() => HandleCancelRequest(source)));
            }

            CleanupClosedWindows();

            logger?.Debug(
                $"[AnikiHelper][WindowManager] CANCEL requested. Source={source ?? "<unknown>"}, " +
                $"StackCount={windows.Count}, Top={DescribeTrackedWindow(windows.Any() ? windows.Peek() : null)}");

            if (!windows.Any())
            {
                return false;
            }

            var topStyleKey = windows.Peek()?.StyleKey;

            try
            {
                if (cancelRequestHandler?.Invoke(topStyleKey) == true)
                {
                    logger?.Debug(
                        $"[AnikiHelper][WindowManager] CANCEL consumed by specialized handler. Style={topStyleKey ?? "<none>"}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex,
                    $"[AnikiHelper][WindowManager] Specialized cancel handler failed. Style={topStyleKey ?? "<none>"}");
            }

            // If no specialized view consumed B/Escape, close the most recent Aniki window,
            // even when WPF temporarily reports IsActive=false during a focus transition.
            return CloseTopWindow();
        }

        public bool CloseTopWindowForExternalHandoff(Action afterClosed)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                return dispatcher.Invoke(new Func<bool>(() => CloseTopWindowForExternalHandoff(afterClosed)));
            }

            CleanupClosedWindows();

            if (!windows.Any())
            {
                logger?.Debug(
                    "[AnikiHelper][WindowManager] EXTERNAL HANDOFF requested but the window stack is empty.");

                return false;
            }

            var topEntry = windows.Pop();
            var top = topEntry?.Window;

            if (topEntry != null)
            {
                topEntry.IsClosing = true;
            }

            secondaryMusicWindows.Remove(top);

            logger?.Debug(
                $"[AnikiHelper][WindowManager] EXTERNAL HANDOFF close requested. {DescribeTrackedWindow(topEntry)}, " +
                $"StackCountAfterPop={windows.Count}");

            if (top == null)
            {
                NotifyOpenWindowStateChanged();
                afterClosed?.Invoke();
                return true;
            }

            // The normal Closed handler would restore focus to Playnite when the last Aniki window
            // disappears. During an external handoff that restoration races with the native window
            // we are about to open, so suppress it for this one close only.
            suppressFinalFocusRestoreWindows.Add(top);

            EventHandler closedHandler = null;
            closedHandler = (sender, args) =>
            {
                top.Closed -= closedHandler;

                logger?.Debug(
                    $"[AnikiHelper][WindowManager] EXTERNAL HANDOFF source window fully closed. {DescribeWindow(top)}");

                var targetDispatcher = Application.Current?.Dispatcher ?? top.Dispatcher;
                if (targetDispatcher == null)
                {
                    afterClosed?.Invoke();
                    return;
                }

                targetDispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        afterClosed?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[AnikiHelper][WindowManager] External handoff callback failed.");
                    }
                }), DispatcherPriority.Normal);
            };

            top.Closed += closedHandler;

            if (top.IsVisible)
            {
                top.Hide();

                logger?.Debug(
                    $"[AnikiHelper][WindowManager] EXTERNAL HANDOFF source hidden before deferred close. {DescribeWindow(top)}");
            }

            NotifyOpenWindowStateChanged();

            top.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    logger?.Debug(
                        $"[AnikiHelper][WindowManager] EXTERNAL HANDOFF deferred close executing. {DescribeWindow(top)}");
                    top.Close();
                }
                catch (Exception ex)
                {
                    suppressFinalFocusRestoreWindows.Remove(top);
                    top.Closed -= closedHandler;
                    logger?.Warn(ex, "[AnikiHelper][WindowManager] External handoff close failed.");
                    afterClosed?.Invoke();
                }
            }), DispatcherPriority.Background);

            // Deliberately do not call FocusAfterClosing here. The callback opens and focuses the
            // external Playnite window after the Aniki source window has fully closed.
            return true;
        }

        public bool CloseTopWindow()
        {
            CleanupClosedWindows();

            if (!windows.Any())
            {
                logger?.Debug("[AnikiHelper][WindowManager] CLOSE requested but the window stack is empty.");
                return false;
            }

            var topEntry = windows.Pop();
            var top = topEntry?.Window;

            if (topEntry != null)
            {
                topEntry.IsClosing = true;
            }

            secondaryMusicWindows.Remove(top);

            logger?.Debug(
                $"[AnikiHelper][WindowManager] CLOSE requested. {DescribeTrackedWindow(topEntry)}, " +
                $"StackCountAfterPop={windows.Count}");

            if (top != null)
            {
                try
                {
                    if (top.IsVisible)
                    {
                        top.Hide();

                        logger?.Debug(
                            $"[AnikiHelper][WindowManager] Window hidden before deferred close. {DescribeWindow(top)}");
                    }
                    else
                    {
                        // IsVisible=false does not mean that the WPF window is closed. A hidden
                        // Playnite dialog can still keep its owner dimmed until Close() is called.
                        logger?.Debug(
                            $"[AnikiHelper][WindowManager] Popped window was already hidden; forcing its deferred close. {DescribeWindow(top)}");
                    }

                    top.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            logger?.Debug(
                                $"[AnikiHelper][WindowManager] Deferred close executing. {DescribeWindow(top)}");

                            top.Close();
                        }
                        catch (InvalidOperationException)
                        {
                            // The window was already closed between the request and this callback.
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn(ex,
                                $"[AnikiHelper][WindowManager] Deferred close failed. {DescribeWindow(top)}");
                        }
                    }), DispatcherPriority.Background);
                }
                catch (InvalidOperationException)
                {
                    // The window was already closed while the close request was being prepared.
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex,
                        $"[AnikiHelper][WindowManager] Failed to hide or schedule the top window close. {DescribeWindow(top)}");

                    try
                    {
                        top.Close();
                    }
                    catch
                    {
                    }
                }
            }
            else
            {
                logger?.Debug(
                    "[AnikiHelper][WindowManager] Popped tracked entry had no window instance.");
            }

            NotifyOpenWindowStateChanged();

            // Restore the real parent immediately after Hide(), including when this was the
            // final Aniki window. This matches the former no-desktop-flash behavior.
            // The actual Close() now runs at Background priority instead of ApplicationIdle,
            // keeping the hidden owned window alive for the shortest practical time.
            FocusAfterClosing(topEntry);

            return true;
        }


        public async Task<bool> CloseAllWindowsAndWaitAsync(string source = null)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return false;
            }

            var closeTasks = new List<Task>();
            var hadWindows = false;

            await dispatcher.InvokeAsync(() =>
            {
                // Include both tracked visible windows and hidden manager-created windows that
                // are still alive in Application.Current.Windows. CleanupClosedWindows normally drops
                // hidden entries from the stack before their deferred Close executes; those orphaned
                // owners are exactly what can leave Playnite dimmed after reopening the setup.
                var trackedEntries = windows
                    .Where(entry => entry?.Window != null)
                    .ToList();

                var managedWindows = trackedEntries
                    .Select(entry => entry.Window)
                    .Concat(
                        Application.Current.Windows
                            .OfType<Window>()
                            .Where(window =>
                            {
                                var styleKey = window?.Tag as string;
                                return !string.IsNullOrWhiteSpace(styleKey) &&
                                       window.Content is Viewbox &&
                                       Application.Current.TryFindResource(styleKey) is Style;
                            }))
                    .Where(window => window != null)
                    .Distinct()
                    .ToList();

                hadWindows = managedWindows.Count > 0;

                logger?.Debug(
                    $"[AnikiHelper][WindowManager] CLOSE ALL AND WAIT requested. " +
                    $"Source={source ?? "<unknown>"}, TrackedCount={trackedEntries.Count}, " +
                    $"ManagedWindowCount={managedWindows.Count}");

                if (!hadWindows)
                {
                    NotifyOpenWindowStateChanged();
                    return;
                }

                foreach (var entry in trackedEntries)
                {
                    entry.IsClosing = true;
                }

                // Publish the final closed state immediately. The actual WPF Window.Close calls
                // are still awaited below so hidden owned windows cannot keep Playnite dimmed.
                windows.Clear();
                secondaryMusicWindows.Clear();
                NotifyOpenWindowStateChanged();

                foreach (var window in managedWindows)
                {
                    var closeCompletion = new TaskCompletionSource<bool>();

                    EventHandler closedHandler = null;
                    closedHandler = (sender, args) =>
                    {
                        try
                        {
                            window.Closed -= closedHandler;
                        }
                        catch
                        {
                        }

                        closeCompletion.TrySetResult(true);
                    };

                    window.Closed += closedHandler;
                    suppressFinalFocusRestoreWindows.Add(window);
                    closeTasks.Add(closeCompletion.Task);

                    try
                    {
                        if (window.IsVisible)
                        {
                            window.Hide();
                        }

                        window.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                window.Close();
                            }
                            catch (InvalidOperationException)
                            {
                                closeCompletion.TrySetResult(true);
                            }
                            catch (Exception ex)
                            {
                                logger?.Warn(ex,
                                    $"[AnikiHelper][WindowManager] Awaited close failed for {DescribeWindow(window)}");
                                closeCompletion.TrySetResult(true);
                            }
                        }), DispatcherPriority.Background);
                    }
                    catch (InvalidOperationException)
                    {
                        closeCompletion.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex,
                            $"[AnikiHelper][WindowManager] Awaited hide/close scheduling failed for {DescribeWindow(window)}");
                        closeCompletion.TrySetResult(true);
                    }
                }
            }, DispatcherPriority.Send);

            if (!hadWindows)
            {
                return false;
            }

            if (closeTasks.Count > 0)
            {
                var allClosed = Task.WhenAll(closeTasks);
                var completed = await Task.WhenAny(allClosed, Task.Delay(1800));

                if (!ReferenceEquals(completed, allClosed))
                {
                    logger?.Warn(
                        $"[AnikiHelper][WindowManager] Timed out while waiting for all Aniki windows to close. " +
                        $"Source={source ?? "<unknown>"}");
                }
            }

            await dispatcher.InvokeAsync(() =>
            {
                CleanupClosedWindows();
                NotifyOpenWindowStateChanged();
                RestorePlayniteForegroundAfterFinalClose();
            }, DispatcherPriority.ApplicationIdle);

            return true;
        }

        public bool CloseAllWindowsAndRestorePlayniteFocus(string source = null)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                return dispatcher.Invoke(new Func<bool>(() => CloseAllWindowsAndRestorePlayniteFocus(source)));
            }

            CleanupClosedWindows();

            var entries = windows
                .Where(entry => entry?.Window != null)
                .ToList();

            logger?.Warn(
                $"[AnikiHelper][WindowManager] EMERGENCY CLOSE ALL requested. " +
                $"Source={source ?? "<unknown>"}, TrackedCount={entries.Count}");

            // Never steal the foreground when there is nothing managed to close.
            // This is especially important while a game is running: a missed B-button
            // release must not bring Playnite to the front after the overlay has already
            // been closed by its own input handler.
            if (entries.Count == 0)
            {
                logger?.Debug(
                    "[AnikiHelper][WindowManager] Emergency close ignored because no tracked window is open; foreground left unchanged.");
                return false;
            }

            windows.Clear();
            secondaryMusicWindows.Clear();

            foreach (var entry in entries)
            {
                entry.IsClosing = true;
                var window = entry.Window;

                try
                {
                    if (window.IsVisible)
                    {
                        window.Hide();
                    }

                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            window.Close();
                        }
                        catch (InvalidOperationException)
                        {
                            // The window was already closed between the request and this callback.
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn(ex,
                                $"[AnikiHelper][WindowManager] Emergency close failed for {DescribeWindow(window)}");
                        }
                    }), DispatcherPriority.Background);
                }
                catch (InvalidOperationException)
                {
                    // Already closed.
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex,
                        $"[AnikiHelper][WindowManager] Emergency hide/close scheduling failed for {DescribeWindow(window)}");
                }
            }

            NotifyOpenWindowStateChanged();

            var playniteWindow = playniteApi.Dialogs.GetCurrentAppWindow();
            if (playniteWindow != null && playniteWindow.IsVisible)
            {
                playniteWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        playniteWindow.Activate();
                        playniteWindow.Focus();

                        logger?.Debug(
                            "[AnikiHelper][WindowManager] Emergency close completed; Playnite focus restored.");
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex,
                            "[AnikiHelper][WindowManager] Failed to restore Playnite focus after emergency close.");
                    }
                }), DispatcherPriority.ApplicationIdle);
            }

            return entries.Count > 0;
        }

        public bool IsTopWindowActive()
        {
            CleanupClosedWindows();

            if (!windows.Any())
                return false;

            var top = windows.Peek()?.Window;
            return top != null && top.IsActive;
        }

        private void Open(string styleKey, bool forceChild, string focusTargetName, bool focusFirst, bool refocusAfterClick, bool noDim, bool secondaryMusic)
        {
            if (string.IsNullOrWhiteSpace(styleKey))
                return;

            logger?.Debug(
                $"[AnikiHelper][WindowManager] OPEN requested. Style={styleKey}, " +
                $"Type={(forceChild ? "Child" : "Main")}, FocusTarget={focusTargetName ?? "<none>"}, " +
                $"FocusFirst={focusFirst}, RefocusAfterClick={refocusAfterClick}, NoDim={noDim}, " +
                $"SecondaryMusic={secondaryMusic}, StackCount={windows.Count}");

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

                var existingEntry = windows.FirstOrDefault(entry =>
                    entry?.Window != null &&
                    entry.Window.IsVisible &&
                    string.Equals(entry.StyleKey, styleKey, StringComparison.OrdinalIgnoreCase));
                var existingWindow = existingEntry?.Window;

                if (existingWindow != null)
                {
                    logger?.Debug(
                        $"[AnikiHelper][WindowManager] Existing visible window reused. {DescribeTrackedWindow(existingEntry)}, " +
                        $"StackCount={windows.Count}");
                    if (secondaryMusic)
                    {
                        secondaryMusicWindows.Add(existingWindow);
                    }
                    else
                    {
                        secondaryMusicWindows.Remove(existingWindow);
                    }

                    NotifyOpenWindowStateChanged();
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

                logger?.Debug(
                    $"[AnikiHelper][WindowManager] Window instance created. Style={styleKey}, " +
                    $"Type={(forceChild ? "Child" : "Main")}, RawWindowType={window?.GetType().FullName ?? "<null>"}");

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
                if (style == null)
                {
                    logger?.Error(
                        $"[AnikiHelper][WindowManager] Window style not found. Opening cancelled. " +
                        $"Style={styleKey}, Type={(forceChild ? "Child" : "Main")}, StackCount={windows.Count}");

                    try
                    {
                        window.Close();
                    }
                    catch (Exception ex)
                    {
                        logger?.Debug(ex,
                            $"[AnikiHelper][WindowManager] Failed to close unopened window after missing style. Style={styleKey}");
                    }

                    return;
                }

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

                if (forceChild)
                {
                    // A child window can be opened from a window owned by another plugin
                    // (for example PlayniteAchievements' game-details window). In that case,
                    // the top Aniki-managed window is not the real visual parent.
                    var activeWindow = Application.Current.Windows
                        .OfType<Window>()
                        .FirstOrDefault(candidate =>
                            !ReferenceEquals(candidate, window) &&
                            candidate.IsVisible &&
                            candidate.IsActive);

                    if (activeWindow != null)
                    {
                        window.Owner = activeWindow;
                    }
                    else if (windows.Any())
                    {
                        window.Owner = windows.Peek().Window;
                    }
                    else if (parent != null)
                    {
                        window.Owner = parent;
                    }
                }
                else if (parent != null)
                {
                    window.Owner = parent;
                }

                logger?.Debug(
                    $"[AnikiHelper][WindowManager] Owner selected. Window={styleKey}, " +
                    $"Owner={DescribeWindow(window.Owner)}, StackCount={windows.Count}");

                window.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape || e.Key == Key.Back)
                    {
                        if (HandleCancelRequest("Window.PreviewKeyDown"))
                        {
                            e.Handled = true;
                        }
                    }
                };

                window.Closed += (s, e) =>
                {
                    logger?.Debug(
                        $"[AnikiHelper][WindowManager] CLOSED event received. {DescribeWindow(window)}, " +
                        $"StackCountBeforeRemove={windows.Count}");

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

                var trackedWindow = new TrackedWindow
                {
                    Window = window,
                    StyleKey = styleKey,
                    Parent = window.Owner,
                    IsChild = forceChild,
                    IsClosing = false
                };

                windows.Push(trackedWindow);

                logger?.Debug(
                    $"[AnikiHelper][WindowManager] Window pushed to stack. {DescribeTrackedWindow(trackedWindow)}, " +
                    $"StackCount={windows.Count}");

                if (secondaryMusic)
                {
                    secondaryMusicWindows.Add(window);
                }

                // Publish the opening state before Show() can deactivate Playnite.
                // Without this, Main.xaml briefly sees IsActive=False while both
                // IsAnikiWindowOpen and IsSecondaryMusicWindowOpen are still false,
                // which can pause the Hub music before its fade-out starts.
                NotifyWindowOpeningState(secondaryMusic);

                window.Show();
                logger?.Debug(
                    $"[AnikiHelper][WindowManager] Window shown. {DescribeWindow(window)}, StackCount={windows.Count}");

                window.Activate();
                window.Focus();

                logger?.Debug(
                    $"[AnikiHelper][WindowManager] Window activation requested. {DescribeWindow(window)}, " +
                    $"KeyboardFocus={DescribeFocusedElement()}");

                NotifyOpenWindowStateChanged();

                if (refocusAfterClick)
                {
                    AttachRefocusAfterClick(window);
                }

                if (!string.IsNullOrWhiteSpace(focusTargetName) || focusFirst)
                {
                    logger?.Debug(
                        $"[AnikiHelper][WindowManager] Initial focus queued. Window={styleKey}, " +
                        $"Target={focusTargetName ?? "<first focusable>"}, FocusFirst={focusFirst}");

                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ApplyInitialFocus(window, focusTargetName, focusFirst);

                        logger?.Debug(
                            $"[AnikiHelper][WindowManager] Initial focus pass completed. Window={styleKey}, " +
                            $"KeyboardFocus={DescribeFocusedElement()}");
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

            if (!target.Focusable || (target is Control targetControl && !targetControl.IsTabStop))
            {
                target = FindSelectedFocusableElement(target) ?? FindFirstFocusableElement(target);
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


        private static FrameworkElement FindSelectedFocusableElement(DependencyObject parent)
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
                    var canReceiveFocus = control == null || control.IsTabStop;

                    if (canReceiveFocus && IsSelectedDataContext(element.DataContext))
                    {
                        return element;
                    }
                }

                var result = FindSelectedFocusableElement(child);

                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static bool IsSelectedDataContext(object dataContext)
        {
            if (dataContext == null)
            {
                return false;
            }

            try
            {
                var property = dataContext.GetType().GetProperty("IsSelected");

                return property != null &&
                       property.PropertyType == typeof(bool) &&
                       property.GetValue(dataContext, null) is bool selected &&
                       selected;
            }
            catch
            {
                return false;
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
                .Where(entry =>
                    entry?.Window != null &&
                    string.Equals(entry.StyleKey, styleKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var entry in windowsToClose)
            {
                var window = entry.Window;
                entry.IsClosing = true;

                try
                {
                    // Hide() removes the old page immediately so the destination can appear without
                    // waiting. Close() must still run quickly: a hidden Playnite dialog can keep the
                    // main window dimmed for as long as the dialog object remains alive.
                    if (window.IsVisible)
                    {
                        window.Hide();
                    }

                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            window.Close();
                        }
                        catch (InvalidOperationException)
                        {
                            // Already closed.
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn(ex,
                                $"[AnikiHelper][WindowManager] Style window deferred close failed. Style={styleKey}, {DescribeWindow(window)}");
                        }
                    }), DispatcherPriority.Background);
                }
                catch (InvalidOperationException)
                {
                    // Already closed.
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex,
                        $"[AnikiHelper][WindowManager] Failed to hide or schedule style window close. Style={styleKey}, {DescribeWindow(window)}");

                    try
                    {
                        window.Close();
                    }
                    catch
                    {
                    }
                }
            }

            NotifyOpenWindowStateChanged();
        }

        private void RemoveWindow(Window window)
        {
            if (window == null)
                return;

            logger?.Debug(
                $"[AnikiHelper][WindowManager] Removing window from tracking. {DescribeWindow(window)}, " +
                $"StackCountBefore={windows.Count}");

            secondaryMusicWindows.Remove(window);
            var suppressFinalFocusRestore = suppressFinalFocusRestoreWindows.Remove(window);

            var wasStillTracked = windows.Any(entry => ReferenceEquals(entry.Window, window));

            if (wasStillTracked)
            {
                var rebuilt = windows.Reverse()
                    .Where(entry => !ReferenceEquals(entry.Window, window))
                    .ToList();

                windows.Clear();

                foreach (var item in rebuilt)
                    windows.Push(item);

                logger?.Debug(
                    $"[AnikiHelper][WindowManager] Window tracking updated. Removed={window.Tag as string ?? "<no tag>"}, " +
                    $"StackCountAfter={windows.Count}, NewTop={DescribeTrackedWindow(windows.Any() ? windows.Peek() : null)}");
            }
            else
            {
                // CloseTopWindow removes the entry before calling Close(). The Closed event still has to
                // perform the final Playnite restoration; returning here was the Alt+F4 regression.
                logger?.Debug(
                    $"[AnikiHelper][WindowManager] Closed window was already popped from tracking. " +
                    $"StackCount={windows.Count}, Window={DescribeWindow(window)}");
            }

            NotifyOpenWindowStateChanged();

            if (!windows.Any())
            {
                if (suppressFinalFocusRestore)
                {
                    logger?.Debug(
                        $"[AnikiHelper][WindowManager] Final Playnite focus restoration suppressed for external handoff. " +
                        $"Window={DescribeWindow(window)}");
                }
                else
                {
                    RestorePlayniteForegroundAfterFinalClose();
                }
            }
        }

        private void RestorePlayniteForegroundAfterFinalClose()
        {
            var playniteWindow = playniteApi.Dialogs.GetCurrentAppWindow();

            if (playniteWindow == null || !playniteWindow.IsVisible)
            {
                return;
            }

            playniteWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    logger?.Debug(
                        $"[AnikiHelper][WindowManager] Final window fully closed; restoring Playnite foreground. " +
                        $"Target={DescribeWindow(playniteWindow)}");

                    if (playniteWindow.WindowState == WindowState.Minimized)
                    {
                        playniteWindow.WindowState = WindowState.Normal;
                    }

                    playniteWindow.Activate();
                    playniteWindow.Focus();

                    var handle = new WindowInteropHelper(playniteWindow).Handle;
                    if (handle != IntPtr.Zero)
                    {
                        SetForegroundWindow(handle);
                    }

                    logger?.Debug(
                        $"[AnikiHelper][WindowManager] Playnite foreground restoration completed. " +
                        $"Target={DescribeWindow(playniteWindow)}, KeyboardFocus={DescribeFocusedElement()}");
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex,
                        "[AnikiHelper][WindowManager] Failed to restore Playnite foreground after final close.");
                }
            }), DispatcherPriority.ApplicationIdle);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void CleanupClosedWindows()
        {
            var opened = windows.Reverse()
                .Where(entry => entry?.Window != null && entry.Window.IsVisible)
                .ToList();

            windows.Clear();

            foreach (var item in opened)
                windows.Push(item);

            secondaryMusicWindows.RemoveWhere(window =>
                window == null ||
                !window.IsVisible ||
                !windows.Any(entry => ReferenceEquals(entry.Window, window)));
        }

        private bool HasVisibleSecondaryMusicWindow()
        {
            return windows.Any(entry =>
                entry?.Window != null &&
                entry.Window.IsVisible &&
                secondaryMusicWindows.Contains(entry.Window));
        }

        private void NotifyWindowOpeningState(bool secondaryMusic)
        {
            try
            {
                if (!lastReportedOpenWindowState)
                {
                    lastReportedOpenWindowState = true;
                    OpenWindowStateChanged?.Invoke(true);
                }

                if (secondaryMusic && !lastReportedSecondaryMusicState)
                {
                    lastReportedSecondaryMusicState = true;
                    SecondaryMusicStateChanged?.Invoke(true);
                }
            }
            catch (Exception ex)
            {
                // Informational state only: never prevent a window from opening.
                logger?.Debug(ex, "[AnikiHelper][WindowManager] Failed to publish pre-show window state.");
            }
        }

        private void NotifyOpenWindowStateChanged()
        {
            try
            {
                bool hasVisibleWindow = windows.Any(entry =>
                    entry?.Window != null &&
                    entry.Window.IsVisible);

                if (lastReportedOpenWindowState != hasVisibleWindow)
                {
                    lastReportedOpenWindowState = hasVisibleWindow;
                    OpenWindowStateChanged?.Invoke(hasVisibleWindow);
                }

                bool hasSecondaryMusicWindow = HasVisibleSecondaryMusicWindow();

                if (lastReportedSecondaryMusicState != hasSecondaryMusicWindow)
                {
                    lastReportedSecondaryMusicState = hasSecondaryMusicWindow;
                    SecondaryMusicStateChanged?.Invoke(hasSecondaryMusicWindow);
                }
            }
            catch
            {
                // État informatif uniquement : ne jamais casser une fenêtre.
            }
        }


        private void FocusAfterClosing(TrackedWindow closingEntry)
        {
            var owner = closingEntry?.Parent;

            if (owner != null && owner.IsVisible)
            {
                logger?.Debug(
                    $"[AnikiHelper][WindowManager] Restoring focus to the actual owner of the closing window. " +
                    $"Closing={DescribeTrackedWindow(closingEntry)}, Owner={DescribeWindow(owner)}");

                owner.Activate();
                owner.Focus();
                return;
            }

            FocusTopWindow();
        }

        private void FocusTopWindow()
        {
            CleanupClosedWindows();

            if (windows.Any())
            {
                var topEntry = windows.Peek();
                var top = topEntry.Window;

                logger?.Debug(
                    $"[AnikiHelper][WindowManager] Restoring focus to top tracked window. {DescribeTrackedWindow(topEntry)}, " +
                    $"StackCount={windows.Count}");

                top.Activate();
                top.Focus();
            }
            else
            {
                var playniteWindow = playniteApi.Dialogs.GetCurrentAppWindow();

                logger?.Debug(
                    $"[AnikiHelper][WindowManager] No tracked parent remains; returning focus to Playnite. " +
                    $"PlayniteWindow={DescribeWindow(playniteWindow)}");

                playniteWindow?.Activate();
            }
        }

        private static string DescribeTrackedWindow(TrackedWindow entry)
        {
            if (entry == null)
            {
                return "TrackedWindow=<null>";
            }

            return $"Style={entry.StyleKey ?? "<none>"}, IsChild={entry.IsChild}, " +
                   $"IsClosing={entry.IsClosing}, Parent={DescribeWindow(entry.Parent)}, " +
                   DescribeWindow(entry.Window);
        }

        private static string DescribeWindow(Window window)
        {
            if (window == null)
            {
                return "Window=<null>";
            }

            string tag;

            try
            {
                tag = window.Tag as string ?? "<no tag>";
            }
            catch
            {
                tag = "<unavailable>";
            }

            try
            {
                return $"Window={tag}, Type={window.GetType().Name}, " +
                       $"Visible={window.IsVisible}, Active={window.IsActive}, Loaded={window.IsLoaded}";
            }
            catch
            {
                return $"Window={tag}, Type={window.GetType().Name}, State=<unavailable>";
            }
        }

        private static string DescribeFocusedElement()
        {
            try
            {
                var focused = Keyboard.FocusedElement;

                if (focused == null)
                {
                    return "<none>";
                }

                var frameworkElement = focused as FrameworkElement;
                var name = frameworkElement?.Name;

                return string.IsNullOrWhiteSpace(name)
                    ? focused.GetType().Name
                    : $"{focused.GetType().Name}:{name}";
            }
            catch
            {
                return "<unavailable>";
            }
        }
    }
}