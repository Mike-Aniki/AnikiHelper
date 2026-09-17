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

        public bool TryGetParameter(ICommand command, out string parameter)
        {
            parameter = null;

            if (command == null)
            {
                return false;
            }

            foreach (var pair in cache)
            {
                if (ReferenceEquals(pair.Value, command))
                {
                    parameter = pair.Key;
                    return true;
                }
            }

            return false;
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
            public bool PlayAdditionalViewSound { get; set; }
        }

        private readonly IPlayniteAPI playniteApi;
        private readonly ILogger logger;
        private readonly Action playOpenPanelSound;
        private readonly Action playOpenAdditionalViewSound;
        private readonly Action playCloseAdditionalViewSound;
        private readonly Stack<TrackedWindow> windows = new Stack<TrackedWindow>();
        private readonly HashSet<Window> secondaryMusicWindows = new HashSet<Window>();
        private readonly HashSet<Window> suppressFinalFocusRestoreWindows = new HashSet<Window>();
        private Func<bool> isOverlayOpenOrOpening;
        private Func<bool> blockWindowOpenProvider;
        private Func<string, bool> cancelRequestHandler;
        public event Action<bool> OpenWindowStateChanged;
        public event Action<bool> SecondaryMusicStateChanged;
        private bool lastReportedOpenWindowState;
        private bool lastReportedSecondaryMusicState;
        private const string QuickAccessWindowStyleName = "QuickAccessWindowStyle";
        private const string ControlCenterWindowStyleName = "ControlCenterWindowStyle";
        private const string FirstSetupWindowStyleName = "FirstSetupWindowStyle";
        private const string VideoPlayerWindowStyleName = "VideoPlayerWindowStyle";
        private const string GamepadTesterWindowStyleName = "GamepadTesterWindowStyle";
        private const string AudioSwitcherWindowStyleName = "AudioSwitcherWindowStyle";
        private const int SecondaryLoadingIndicatorDelayMs = 300;
        private const int MediaCenterLoadingIndicatorDelayMs = 150;

        // Disabled globally after fullscreen secondary-view testing showed that attaching the
        // real ControlTemplate after the first frame can introduce visible black frames and UI
        // thread stutter on heavier pages. Keep the deferred-loading implementation below so it
        // can be reintroduced later as an explicit opt-in for a specific view if it proves useful.
        private static readonly bool DeferredSecondaryContentEnabled = false;

        public AnikiWindowManager(
            IPlayniteAPI playniteApi,
            Action playOpenPanelSound = null,
            Action playOpenAdditionalViewSound = null,
            Action playCloseAdditionalViewSound = null)
        {
            this.playniteApi = playniteApi;
            this.playOpenPanelSound = playOpenPanelSound;
            this.playOpenAdditionalViewSound = playOpenAdditionalViewSound;
            this.playCloseAdditionalViewSound = playCloseAdditionalViewSound;
            logger = LogManager.GetLogger();
        }

        public void SetOverlayOpenStateProvider(Func<bool> provider)
        {
            isOverlayOpenOrOpening = provider;
        }

        public void SetWindowOpenBlockProvider(Func<bool> provider)
        {
            blockWindowOpenProvider = provider;
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
            ParseOpenParameter(parameter, out var styleKey, out var focusTargetName, out var focusFirst, out var refocusAfterClick, out var noDim, out var secondaryMusic, out var openPanelSound, out var additionalViewSound);
            Open(styleKey, false, focusTargetName, focusFirst, refocusAfterClick, noDim, secondaryMusic, openPanelSound, additionalViewSound);
        }

        public void OpenWindow(string styleKey, string focusTargetName)
        {
            Open(styleKey, false, focusTargetName, false, false, false, false, false, false);
        }

        public void OpenChildWindow(string parameter)
        {
            ParseOpenParameter(parameter, out var styleKey, out var focusTargetName, out var focusFirst, out var refocusAfterClick, out var noDim, out var secondaryMusic, out var openPanelSound, out var additionalViewSound);
            Open(styleKey, true, focusTargetName, focusFirst, refocusAfterClick, noDim, secondaryMusic, openPanelSound, additionalViewSound);
        }

        private void ParseOpenParameter(string parameter, out string styleKey, out string focusTargetName, out bool focusFirst, out bool refocusAfterClick, out bool noDim, out bool secondaryMusic, out bool openPanelSound, out bool additionalViewSound)
        {
            styleKey = parameter;
            focusTargetName = null;
            focusFirst = false;
            refocusAfterClick = false;
            noDim = false;
            secondaryMusic = false;
            openPanelSound = false;
            additionalViewSound = false;

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

                if (IsOpenPanelSoundOption(option))
                {
                    openPanelSound = true;
                    continue;
                }

                if (IsAdditionalViewSoundOption(option))
                {
                    additionalViewSound = true;
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

        private static bool IsOpenPanelSoundOption(string option)
        {
            return string.Equals(option, "OpenPanelSound", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAdditionalViewSoundOption(string option)
        {
            return string.Equals(option, "AdditionalViewSound", StringComparison.OrdinalIgnoreCase);
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
                global::AnikiHelper.AnikiLog.Debug(logger,
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
                IsClosing = false,
                PlayAdditionalViewSound = false
            };

            windows.Push(trackedEntry);

            if (secondaryMusic)
            {
                secondaryMusicWindows.Add(window);
            }

            window.Closed += (s, e) =>
            {
                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] CLOSED event received for external window. " +
                    $"{DescribeWindow(window)}, StackCountBeforeRemove={windows.Count}");

                RemoveWindow(window);
            };

            global::AnikiHelper.AnikiLog.Debug(logger,
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

            global::AnikiHelper.AnikiLog.Debug(logger,
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
                    global::AnikiHelper.AnikiLog.Debug(logger,
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
                global::AnikiHelper.AnikiLog.Debug(logger,
                    "[AnikiHelper][WindowManager] EXTERNAL HANDOFF requested but the window stack is empty.");
                return false;
            }

            var topEntry = windows.Pop();
            var top = topEntry?.Window;

            if (topEntry != null)
            {
                topEntry.IsClosing = true;
            }

            var playAdditionalTransitionSound = topEntry?.PlayAdditionalViewSound == true;
            secondaryMusicWindows.Remove(top);

            global::AnikiHelper.AnikiLog.Debug(logger,
                $"[AnikiHelper][WindowManager] EXTERNAL HANDOFF synchronous close requested. " +
                $"{DescribeTrackedWindow(topEntry)}, StackCountAfterPop={windows.Count}");

            if (top != null)
            {
                global::AnikiHelper.NativeUiSoundSuppressor.Suppress(
                    playniteApi,
                    navigation: true,
                    activation: false,
                    durationMs: 320,
                    reason: "SecondaryWindowExternalHandoff");

                if (playAdditionalTransitionSound)
                {
                    TryPlayAdditionalViewSound("close-external-handoff", topEntry?.StyleKey);
                }

                suppressFinalFocusRestoreWindows.Add(top);

                try
                {
                    // Close directly in the same dispatcher pass; this lets
                    // Playnite finish its dialog/dim cleanup without creating a visible gap.
                    top.Close();
                }
                catch (InvalidOperationException)
                {
                    suppressFinalFocusRestoreWindows.Remove(top);
                }
                catch (Exception ex)
                {
                    suppressFinalFocusRestoreWindows.Remove(top);
                    logger?.Warn(ex, "[AnikiHelper][WindowManager] External handoff synchronous close failed.");
                }
            }

            NotifyOpenWindowStateChanged();

            try
            {
                afterClosed?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][WindowManager] External handoff callback failed.");
            }

            return true;
        }

        public bool CloseTopWindow()
        {
            CleanupClosedWindows();

            if (!windows.Any())
            {
                global::AnikiHelper.AnikiLog.Debug(logger, "[AnikiHelper][WindowManager] CLOSE requested but the window stack is empty.");
                return false;
            }

            var topEntry = windows.Pop();
            var top = topEntry?.Window;

            if (topEntry != null)
            {
                topEntry.IsClosing = true;
            }

            var playAdditionalTransitionSound = topEntry?.PlayAdditionalViewSound == true;
            secondaryMusicWindows.Remove(top);

            global::AnikiHelper.AnikiLog.Debug(logger,
                $"[AnikiHelper][WindowManager] CLOSE requested. {DescribeTrackedWindow(topEntry)}, " +
                $"StackCountAfterPop={windows.Count}");

            if (top != null)
            {
                global::AnikiHelper.NativeUiSoundSuppressor.Suppress(
                    playniteApi,
                    navigation: true,
                    activation: false,
                    durationMs: 320,
                    reason: "SecondaryWindowClose.FocusReturn");

                if (playAdditionalTransitionSound)
                {
                    TryPlayAdditionalViewSound("close", topEntry?.StyleKey);
                }

                try
                {
                    if (top.IsVisible)
                    {
                        top.Hide();

                        global::AnikiHelper.AnikiLog.Debug(logger,
                            $"[AnikiHelper][WindowManager] Window hidden before deferred close. {DescribeWindow(top)}");
                    }
                    else
                    {
                        // IsVisible=false does not mean that the WPF window is closed. A hidden
                        // Playnite dialog can still keep its owner dimmed until Close() is called.
                        global::AnikiHelper.AnikiLog.Debug(logger,
                            $"[AnikiHelper][WindowManager] Popped window was already hidden; forcing its deferred close. {DescribeWindow(top)}");
                    }

                    top.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            global::AnikiHelper.AnikiLog.Debug(logger,
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
                    }), DispatcherPriority.ApplicationIdle);
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
                global::AnikiHelper.AnikiLog.Debug(logger,
                    "[AnikiHelper][WindowManager] Popped tracked entry had no window instance.");
            }

            NotifyOpenWindowStateChanged();

            // Restore the real parent immediately after Hide(), including when this was the
            // final Aniki window. Keep the hidden owned window alive until ApplicationIdle,
            // matching the previous behavior and preventing the Windows desktop from flashing
            // before Playnite has fully returned to the foreground.
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

                global::AnikiHelper.AnikiLog.Debug(logger,
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
                global::AnikiHelper.AnikiLog.Debug(logger,
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

                        global::AnikiHelper.AnikiLog.Debug(logger,
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

        private bool IsWindowOpenBlockedByGameForeground()
        {
            try
            {
                return blockWindowOpenProvider?.Invoke() == true;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][ControllerGuard] Window-open guard provider failed. Allowing request.");
                return false;
            }
        }

        private void LogGameForegroundWindowBlock(string styleKey, string phase)
        {
            global::AnikiHelper.AnikiLog.Debug(logger,
                $"[AnikiHelper][ControllerGuard] WINDOW BLOCKED | Style={styleKey ?? "<null>"} | " +
                $"Phase={phase} | Reason=Game is running/launching and Playnite/Aniki does not own foreground.");
        }

        private void Open(string styleKey, bool forceChild, string focusTargetName, bool focusFirst, bool refocusAfterClick, bool noDim, bool secondaryMusic, bool openPanelSound, bool additionalViewSound, bool allowQuickAccessHandoff = false)
        {
            if (string.IsNullOrWhiteSpace(styleKey))
                return;

            global::AnikiHelper.AnikiLog.Debug(logger,
                $"[AnikiHelper][WindowManager] OPEN requested. Style={styleKey}, " +
                $"Type={(forceChild ? "Child" : "Main")}, FocusTarget={focusTargetName ?? "<none>"}, " +
                $"FocusFirst={focusFirst}, RefocusAfterClick={refocusAfterClick}, NoDim={noDim}, " +
                $"SecondaryMusic={secondaryMusic}, OpenPanelSound={openPanelSound}, AdditionalViewSound={additionalViewSound}, StackCount={windows.Count}");

            // Second line of defence: even if a theme command or delayed RelayCommand somehow
            // fires while the game owns foreground, do not create/activate a Playnite/Aniki window.
            // Exception: a Quick Access -> destination handoff has already passed this guard on
            // the original user request. Closing Quick Access can make Xbox/Windows briefly move
            // foreground away from Playnite for one dispatcher turn, so the internal continuation
            // must not be rejected because of that transient focus change.
            if (!allowQuickAccessHandoff && IsWindowOpenBlockedByGameForeground())
            {
                LogGameForegroundWindowBlock(styleKey, "request");
                return;
            }

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

                // Re-check on the UI thread because foreground ownership can change between
                // the controller callback / command execution and the queued WPF open.
                // The sole exception is the internal continuation of a Quick Access handoff;
                // the initiating request already passed the foreground guard before Quick Access closed.
                if (!allowQuickAccessHandoff && IsWindowOpenBlockedByGameForeground())
                {
                    LogGameForegroundWindowBlock(styleKey, "UI dispatch");
                    return;
                }

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
                    global::AnikiHelper.AnikiLog.Debug(logger,
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
                    global::AnikiHelper.NativeUiSoundSuppressor.Suppress(
                        playniteApi,
                        navigation: true,
                        activation: false,
                        durationMs: 280,
                        reason: "SecondaryWindowReactivate");
                    existingWindow.Activate();
                    existingWindow.Focus();
                    return;
                }

                if (!string.Equals(styleKey, QuickAccessWindowStyleName, StringComparison.OrdinalIgnoreCase))
                {
                    var quickAccessWasOpen = windows.Any(entry =>
                        entry?.Window != null &&
                        entry.Window.IsVisible &&
                        string.Equals(entry.StyleKey, QuickAccessWindowStyleName, StringComparison.OrdinalIgnoreCase));

                    if (quickAccessWasOpen)
                    {
                        // Close Quick Access, wait one dispatcher turn, then open the destination window.
                        // Keep final focus restore suppressed during the handoff.
                        CloseWindowByStyleKey(
                            QuickAccessWindowStyleName,
                            closeImmediately: true,
                            suppressFinalFocusRestore: true);
                        CleanupClosedWindows();

                        global::AnikiHelper.AnikiLog.Debug(logger,
                            $"[AnikiHelper][WindowManager] Quick Access handoff waiting one dispatcher turn before opening destination. " +
                            $"Destination={styleKey}");

                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            global::AnikiHelper.AnikiLog.Debug(logger,
                                $"[AnikiHelper][WindowManager] Quick Access handoff continuation authorized. Destination={styleKey}");

                            Open(
                                styleKey,
                                forceChild,
                                focusTargetName,
                                focusFirst,
                                refocusAfterClick,
                                noDim,
                                secondaryMusic,
                                openPanelSound,
                                additionalViewSound,
                                allowQuickAccessHandoff: true);
                        }), DispatcherPriority.ApplicationIdle);

                        return;
                    }
                }

                // NoDim is used for theme child windows that draw their own dim/gradient in XAML.
                // Using a raw transparent WPF window avoids Playnite's dialog chrome/background dim for this window only.
                var window = noDim
                    ? new Window()
                    : playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                    {
                        ShowMinimizeButton = false
                    });

                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] Window instance created. Style={styleKey}, " +
                    $"Type={(forceChild ? "Child" : "Main")}, RawWindowType={window?.GetType().FullName ?? "<null>"}");

                window.Tag = styleKey;

                // A raw WPF Window defaults to the system Window brush (normally white).
                // NoDim views can briefly expose that native window background while their
                // root content is fading in/out or while the template is being created. That
                // produced the full-screen white flashes seen in First Setup and Gamepad Tester.
                // Paint every non-child NoDim window black before any content is shown. Child
                // windows are switched back to Transparent below, so overlays remain unaffected.
                if (noDim)
                {
                    window.Background = Brushes.Black;
                }

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
                        global::AnikiHelper.AnikiLog.Debug(logger, ex,
                            $"[AnikiHelper][WindowManager] Failed to close unopened window after missing style. Style={styleKey}");
                    }

                    return;
                }

                // Full secondary pages are opened in two phases. Showing a lightweight shell first
                // gives WPF/Windows a real frame to present immediately instead of blocking the
                // button press while a large ControlTemplate (Video Center, Media Gallery, etc.) is
                // being instantiated. The actual theme Style is attached only after that first frame.
                var useDeferredContent = ShouldUseDeferredSecondaryContent(styleKey, forceChild);
                var contentHost = new ContentControl
                {
                    Focusable = false,
                    Visibility = useDeferredContent ? Visibility.Collapsed : Visibility.Visible
                };

                if (!useDeferredContent)
                {
                    contentHost.Style = style;
                }

                var rootGrid = new Grid
                {
                    Width = 1920,
                    Height = 1080
                };

                rootGrid.Children.Add(contentHost);

                FrameworkElement openingShell = null;
                if (useDeferredContent)
                {
                    openingShell = CreateSecondaryViewOpeningShell(styleKey);
                    Panel.SetZIndex(openingShell, 10000);
                    rootGrid.Children.Add(openingShell);
                }

                window.Content = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    Child = rootGrid
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

                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] Owner selected. Window={styleKey}, " +
                    $"Owner={DescribeWindow(window.Owner)}, StackCount={windows.Count}");

                window.PreviewKeyDown += (s, e) =>
                {
                    // Backspace must remain a text-editing key while an editable text field
                    // owns keyboard focus. Without this guard, Backspace is interpreted as the
                    // global Back/Cancel action and closes modal surfaces such as the Video
                    // Center artwork picker instead of deleting the previous character.
                    if (e.Key == Key.Back &&
                        Keyboard.FocusedElement is TextBoxBase focusedTextBox &&
                        !focusedTextBox.IsReadOnly)
                    {
                        return;
                    }

                    // Audio Switcher's nested ScrollViewer consumes Up/Down at the list
                    // boundary before WPF can move focus back to the controls above it.
                    // Handle navigation ourselves while focus is inside the output-device list.
                    if (string.Equals(styleKey, AudioSwitcherWindowStyleName, StringComparison.OrdinalIgnoreCase) &&
                        (e.Key == Key.Up || e.Key == Key.Down) &&
                        HandleAudioSwitcherDeviceNavigation(window, e.Key))
                    {
                        e.Handled = true;
                        return;
                    }

                    if (e.Key == Key.Escape || e.Key == Key.Back)
                    {
                        // The Gamepad Tester must be allowed to see B/Back while one of its
                        // live capture tests is running. Otherwise the WindowManager consumes
                        // the same input as a global Cancel and closes the whole tester window.
                        if (string.Equals(styleKey, GamepadTesterWindowStyleName, StringComparison.OrdinalIgnoreCase) &&
                            IsGamepadTesterCaptureRunning(window))
                        {
                            global::AnikiHelper.AnikiLog.Debug(logger,
                                "[AnikiHelper][WindowManager] Gamepad Tester capture active; forwarding Back/Escape to the tester.");
                            return;
                        }

                        if (HandleCancelRequest("Window.PreviewKeyDown"))
                        {
                            e.Handled = true;
                        }
                    }
                };

                window.Closed += (s, e) =>
                {
                    global::AnikiHelper.AnikiLog.Debug(logger,
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
                    IsClosing = false,
                    PlayAdditionalViewSound = additionalViewSound
                };

                windows.Push(trackedWindow);

                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] Window pushed to stack. {DescribeTrackedWindow(trackedWindow)}, " +
                    $"StackCount={windows.Count}");

                if (secondaryMusic)
                {
                    secondaryMusicWindows.Add(window);
                }

                // The destination window will move keyboard focus and Playnite normally plays
                // navigation.wav for that focus change. Keep activation.wav intact here: source
                // controls that already play an Aniki transition sound suppress activation earlier
                // in PreProcessInput, while ordinary Quick Access buttons keep their normal click sound.
                global::AnikiHelper.NativeUiSoundSuppressor.Suppress(
                    playniteApi,
                    navigation: true,
                    activation: false,
                    durationMs: 300,
                    reason: "SecondaryWindowOpen.Focus");

                // Publish the opening state before Show() can deactivate Playnite.
                // Without this, Main.xaml briefly sees IsActive=False while both
                // IsAnikiWindowOpen and IsSecondaryMusicWindowOpen are still false,
                // which can pause the Hub music before its fade-out starts.
                NotifyWindowOpeningState(secondaryMusic);

                if (openPanelSound)
                {
                    TryPlayOpenPanelSound(styleKey);
                }

                if (trackedWindow.PlayAdditionalViewSound)
                {
                    TryPlayAdditionalViewSound("open", styleKey);
                }

                if (useDeferredContent)
                {
                    EventHandler firstFrameRendered = null;
                    firstFrameRendered = (sender, args) =>
                    {
                        window.ContentRendered -= firstFrameRendered;

                        // Do not build a page that was already cancelled while the lightweight
                        // opening shell was on screen.
                        if (trackedWindow.IsClosing || !window.IsVisible)
                        {
                            return;
                        }

                        window.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (trackedWindow.IsClosing || !window.IsVisible)
                            {
                                return;
                            }

                            try
                            {
                                contentHost.Style = style;
                                contentHost.Visibility = Visibility.Visible;

                                global::AnikiHelper.AnikiLog.Debug(logger,
                                    $"[AnikiHelper][WindowManager] Deferred secondary content creation started. Style={styleKey}");

                                // Keep the lightweight shell above the real page while WPF creates,
                                // measures and renders the heavy template behind it. ContextIdle runs
                                // after the pending Render/Layout work, so removing the shell here does
                                // not expose an empty/black frame.
                                window.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    if (trackedWindow.IsClosing || !window.IsVisible)
                                    {
                                        return;
                                    }

                                    if (openingShell != null)
                                    {
                                        openingShell.Visibility = Visibility.Collapsed;
                                    }

                                    QueueInitialFocus(window, styleKey, focusTargetName, focusFirst);

                                    global::AnikiHelper.AnikiLog.Debug(logger,
                                        $"[AnikiHelper][WindowManager] Deferred secondary content revealed. Style={styleKey}");
                                }), DispatcherPriority.ContextIdle);
                            }
                            catch (Exception ex)
                            {
                                logger?.Error(ex,
                                    $"[AnikiHelper][WindowManager] Deferred secondary content creation failed. Style={styleKey}");

                                // Never leave the user trapped behind the generic loading shell.
                                if (openingShell != null)
                                {
                                    openingShell.Visibility = Visibility.Collapsed;
                                }
                            }
                        }), DispatcherPriority.Background);
                    };

                    window.ContentRendered += firstFrameRendered;
                }

                window.Show();
                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] Window shown. {DescribeWindow(window)}, StackCount={windows.Count}");

                window.Activate();
                window.Focus();

                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] Window activation requested. {DescribeWindow(window)}, " +
                    $"KeyboardFocus={DescribeFocusedElement()}");

                NotifyOpenWindowStateChanged();

                if (refocusAfterClick)
                {
                    AttachRefocusAfterClick(window);
                }

                if (!useDeferredContent)
                {
                    QueueInitialFocus(window, styleKey, focusTargetName, focusFirst);
                }
            });
        }

        private static bool ShouldUseDeferredSecondaryContent(string styleKey, bool forceChild)
        {
            if (string.IsNullOrWhiteSpace(styleKey))
            {
                return false;
            }

            // Secondary views now open their real content directly by default. This avoids the
            // intermediate shell/frame and prevents the deferred template construction from
            // competing with the theme's opening animation on the UI thread.
            if (!DeferredSecondaryContentEnabled)
            {
                return false;
            }

            // Heavy media/profile/store pages are deliberately NOT deferred behind the generic
            // opening shell. Building their real template while the shell is animating can stall
            // the UI thread and make the opening/loading animation visibly stutter.
            if (string.Equals(styleKey, "MediaGalleryGamesWindowStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "PlayerProfileWindowStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "ScreenShotsThumbsWindowStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "LastCapturesWindowStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, VideoPlayerWindowStyleName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "AchievementsWindow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "FriendsStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "FriendsActivityWindow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "FriendsWindowStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "SteamStoreStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "LastUpdatesWindowStyle", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Do not put the full-screen opening shell on quick panels, option popups and other
            // small modal surfaces. Some full pages are intentionally opened as child windows
            // (Game News from Game Details, Friends/Achievements from the in-game overlay), so
            // forceChild alone cannot be used to decide whether a view is a real secondary page.
            if (string.Equals(styleKey, QuickAccessWindowStyleName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, ControlCenterWindowStyleName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, FirstSetupWindowStyleName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, AudioSwitcherWindowStyleName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "TopBarManagerWindowStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "SteamStatusMenuWindowStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "NotificationMenuWindowStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "QuickAccessNotificationWindowStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "AchievementActionsWindowStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "AchievementCaptureViewerWindowStyle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "AchievementsDetailsOptionsWindow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "AchievementsOptionsWindow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "FriendAchievementsGlobalOptionsWindow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "FriendGameAchievementsOptionsWindow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleKey, "FriendsActivityOptionsWindow", StringComparison.OrdinalIgnoreCase) ||
                styleKey.IndexOf("DuplicateHiderVersions", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return true;
        }

        private static FrameworkElement CreateSecondaryViewOpeningShell(string styleKey)
        {
            // Capture Gallery is fast enough that a loading indicator becomes a distracting
            // flash. Give it a lightweight structural shell instead: the destination title
            // and footer are visible immediately, then the real page replaces them when its
            // ControlTemplate is ready.
            if (string.Equals(styleKey, "MediaGalleryGamesWindowStyle", StringComparison.OrdinalIgnoreCase))
            {
                return CreateMediaGalleryOpeningShell();
            }

            var shell = new Grid
            {
                Width = 1920,
                Height = 1080,
                IsHitTestVisible = false
            };

            // Present the destination background immediately so the button press always gets an
            // instant visual response. The loading indicator itself is intentionally delayed: fast
            // views can replace this shell before the delay expires, which avoids a distracting
            // 100-200 ms "loading flash". Slow views still reveal a real loading state after 300 ms.
            var background = new Border();
            background.SetResourceReference(Border.BackgroundProperty, "SecondaryViewBackground");
            shell.Children.Add(background);

            var loadingText = new TextBlock
            {
                FontSize = 34,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.White
            };
            loadingText.SetResourceReference(TextBlock.TextProperty, "LOCLoading");

            var baseTextStyle = Application.Current?.TryFindResource("TextBlockBaseStyle") as Style;
            if (baseTextStyle != null)
            {
                loadingText.Style = baseTextStyle;
            }

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0
            };
            stack.Children.Add(loadingText);
            stack.Children.Add(new ProgressBar
            {
                Width = 360,
                Height = 6,
                Margin = new Thickness(0, 18, 0, 0),
                IsIndeterminate = true
            });

            // Use an opacity animation with a delayed BeginTime instead of a DispatcherTimer.
            // The animation clock is committed before the heavy page template is instantiated, so
            // WPF's compositor can reveal the indicator even if the UI thread is briefly busy.
            RoutedEventHandler shellLoaded = null;
            shellLoaded = (sender, args) =>
            {
                shell.Loaded -= shellLoaded;

                var loadingDelayMs = string.Equals(styleKey, VideoPlayerWindowStyleName, StringComparison.OrdinalIgnoreCase)
                    ? MediaCenterLoadingIndicatorDelayMs
                    : SecondaryLoadingIndicatorDelayMs;

                var delayedReveal = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    BeginTime = TimeSpan.FromMilliseconds(loadingDelayMs),
                    Duration = new Duration(TimeSpan.FromMilliseconds(1)),
                    FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
                };

                stack.BeginAnimation(UIElement.OpacityProperty, delayedReveal);
            };

            shell.Loaded += shellLoaded;
            shell.Children.Add(stack);

            return shell;
        }

        private static FrameworkElement CreateMediaGalleryOpeningShell()
        {
            var shell = new Grid
            {
                Width = 1920,
                Height = 1080,
                IsHitTestVisible = false
            };

            var background = new Border();
            background.SetResourceReference(Border.BackgroundProperty, "SecondaryViewBackground");
            shell.Children.Add(background);

            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });

            var header = new Grid
            {
                Margin = new Thickness(50, 35, 50, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(header, 0);

            var title = new TextBlock
            {
                FontSize = 43,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = Brushes.White
            };
            title.SetResourceReference(TextBlock.TextProperty, "LOCMediaGalleryTitle");

            var baseTextStyle = Application.Current?.TryFindResource("TextBlockBaseStyle") as Style;
            if (baseTextStyle != null)
            {
                title.Style = baseTextStyle;
            }

            header.Children.Add(title);
            shell.Children.Add(header);

            // Keep the same 60px footer footprint as the real Capture Gallery page.
            // This avoids a black/empty intermediate screen without pretending that data
            // is loading when the page normally becomes ready in a few hundred milliseconds.
            var footer = new Border
            {
                Height = 60,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            footer.SetResourceReference(Border.BackgroundProperty, "BottomBar");
            Grid.SetRow(footer, 2);
            shell.Children.Add(footer);

            return shell;
        }

        private void QueueInitialFocus(Window window, string styleKey, string focusTargetName, bool focusFirst)
        {
            if (window == null || (string.IsNullOrWhiteSpace(focusTargetName) && !focusFirst))
            {
                return;
            }

            global::AnikiHelper.AnikiLog.Debug(logger,
                $"[AnikiHelper][WindowManager] Initial focus queued. Window={styleKey}, " +
                $"Target={focusTargetName ?? "<first focusable>"}, FocusFirst={focusFirst}");

            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!window.IsVisible)
                {
                    return;
                }

                ApplyInitialFocus(window, focusTargetName, focusFirst);

                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] Initial focus pass completed. Window={styleKey}, " +
                    $"KeyboardFocus={DescribeFocusedElement()}");
            }), DispatcherPriority.ApplicationIdle);
        }

        private void TryPlayOpenPanelSound(string styleKey)
        {
            try
            {
                playOpenPanelSound?.Invoke();
                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] OpenPanel.wav requested. Style={styleKey ?? "<none>"}");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex,
                    $"[AnikiHelper][WindowManager] Failed to request panel opening sound. Style={styleKey ?? "<none>"}");
            }
        }

        private void TryPlayAdditionalViewSound(string phase, string styleKey)
        {
            try
            {
                var isOpening = !string.IsNullOrWhiteSpace(phase) &&
                    phase.StartsWith("open", StringComparison.OrdinalIgnoreCase);

                if (isOpening)
                {
                    playOpenAdditionalViewSound?.Invoke();
                }
                else
                {
                    playCloseAdditionalViewSound?.Invoke();
                }

                var soundName = isOpening ? "OpenAdditionalView.wav" : "CloseAdditionalView.wav";
                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] {soundName} requested. Phase={phase}, Style={styleKey ?? "<none>"}");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex,
                    $"[AnikiHelper][WindowManager] Failed to request Additional View transition sound. Phase={phase}, Style={styleKey ?? "<none>"}");
            }
        }

        private bool IsOverlayBlockingCustomWindowOpen()
        {
            try
            {
                return isOverlayOpenOrOpening?.Invoke() == true;
            }
            catch (Exception ex)
            {
                global::AnikiHelper.AnikiLog.Debug(logger, ex, "[AnikiHelper][WindowManager] Failed to query overlay state.");
                return false;
            }
        }

        private void LogBlockedWindowOpen(string styleKey, string stage)
        {
            try
            {
                global::AnikiHelper.AnikiLog.Debug(logger, $"[AnikiHelper][WindowManager] Window open blocked by overlay. Style={styleKey}, Stage={stage}");
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

        private static bool HandleAudioSwitcherDeviceNavigation(Window window, Key key)
        {
            if (window == null)
            {
                return false;
            }

            var deviceList = FindVisualChildByName<ItemsControl>(window, "AudioSwitcherDeviceList");
            if (deviceList == null)
            {
                return false;
            }

            var focusedObject = Keyboard.FocusedElement as DependencyObject;
            if (focusedObject == null || !IsDescendantOf(focusedObject, deviceList))
            {
                return false;
            }

            var focusedElement = FindNearestFocusableElement(focusedObject);
            if (focusedElement == null)
            {
                return false;
            }

            var deviceButtons = new List<FrameworkElement>();
            CollectFocusableButtons(deviceList, deviceButtons);

            var currentIndex = deviceButtons.FindIndex(button => ReferenceEquals(button, focusedElement));
            if (currentIndex < 0)
            {
                return false;
            }

            FrameworkElement target = null;

            if (key == Key.Up)
            {
                if (currentIndex > 0)
                {
                    target = deviceButtons[currentIndex - 1];
                }
                else
                {
                    // Leaving the first output device should return to the control directly
                    // above the output section instead of being swallowed by ScrollViewer.
                    target = FindVisualChildByName<FrameworkElement>(window, "AudioSwitcherMuteButton");
                }
            }
            else if (key == Key.Down)
            {
                if (currentIndex < deviceButtons.Count - 1)
                {
                    target = deviceButtons[currentIndex + 1];
                }
                else
                {
                    // Output Devices is the last section on this page.
                    // Consume Down at the final device so focus stays stable.
                    return true;
                }
            }

            if (target == null || !IsValidFocusableTarget(target))
            {
                return false;
            }

            target.Focus();
            Keyboard.Focus(target);

            var focusScope = FocusManager.GetFocusScope(target);
            if (focusScope != null)
            {
                FocusManager.SetFocusedElement(focusScope, target);
            }

            target.BringIntoView();

            target.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    target.BringIntoView();
                }
                catch
                {
                }
            }), DispatcherPriority.Input);

            return true;
        }

        private static void CollectFocusableButtons(DependencyObject parent, List<FrameworkElement> result)
        {
            if (parent == null || result == null)
            {
                return;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is ButtonBase button &&
                    button.Focusable &&
                    button.IsEnabled &&
                    button.IsVisible &&
                    button.IsTabStop)
                {
                    result.Add(button);
                }

                CollectFocusableButtons(child, result);
            }
        }

        private static bool IsGamepadTesterCaptureRunning(Window window)
        {
            if (window == null)
            {
                return false;
            }

            return IsBooleanPropertyTrue(
                       GetContentDataContext(FindVisualChildByName<ContentControl>(window, "GamepadTester_ButtonMap")),
                       "IsButtonCaptureRunning") ||
                   IsBooleanPropertyTrue(
                       GetContentDataContext(FindVisualChildByName<ContentControl>(window, "GamepadTester_StickCheck")),
                       "IsStickCaptureRunning") ||
                   IsBooleanPropertyTrue(
                       GetContentDataContext(FindVisualChildByName<ContentControl>(window, "GamepadTester_LatencyMini")),
                       "IsLatencyTestRunning");
        }

        private static object GetContentDataContext(ContentControl control)
        {
            if (control?.Content is FrameworkElement contentElement)
            {
                return contentElement.DataContext;
            }

            return control?.DataContext;
        }

        private static bool IsBooleanPropertyTrue(object source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            try
            {
                var property = source.GetType().GetProperty(propertyName);

                return property != null &&
                       property.PropertyType == typeof(bool) &&
                       property.GetValue(source, null) is bool value &&
                       value;
            }
            catch
            {
                return false;
            }
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

        private void CloseWindowByStyleKey(
            string styleKey,
            bool closeImmediately = false,
            bool suppressFinalFocusRestore = false)
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

                if (entry.PlayAdditionalViewSound)
                {
                    TryPlayAdditionalViewSound("close-by-style", entry.StyleKey);
                }

                try
                {
                    if (suppressFinalFocusRestore)
                    {
                        suppressFinalFocusRestoreWindows.Add(window);
                    }

                    if (closeImmediately)
                    {
                        global::AnikiHelper.AnikiLog.Debug(logger,
                            $"[AnikiHelper][WindowManager] Closing style window synchronously for handoff. " +
                            $"Style={styleKey}, SuppressFinalFocusRestore={suppressFinalFocusRestore}, {DescribeWindow(window)}");

                        // Do not Hide() first. Keeping the old dialog visible until Close() completes
                        // avoids exposing the desktop/owner surface for an intermediate frame.
                        window.Close();
                        continue;
                    }

                    // Deferred closes still hide immediately, then release the Playnite dialog at idle.
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
                    }), DispatcherPriority.ApplicationIdle);
                }
                catch (InvalidOperationException)
                {
                    // Already closed.
                    if (suppressFinalFocusRestore)
                    {
                        suppressFinalFocusRestoreWindows.Remove(window);
                    }
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
                        if (suppressFinalFocusRestore)
                        {
                            suppressFinalFocusRestoreWindows.Remove(window);
                        }
                    }
                }
            }

            NotifyOpenWindowStateChanged();
        }

        private void RemoveWindow(Window window)
        {
            if (window == null)
                return;

            global::AnikiHelper.AnikiLog.Debug(logger,
                $"[AnikiHelper][WindowManager] Removing window from tracking. {DescribeWindow(window)}, " +
                $"StackCountBefore={windows.Count}");

            var trackedEntry = windows.FirstOrDefault(entry => ReferenceEquals(entry.Window, window));
            var playAdditionalTransitionSound = trackedEntry?.PlayAdditionalViewSound == true && trackedEntry.IsClosing == false;

            secondaryMusicWindows.Remove(window);
            var suppressFinalFocusRestore = suppressFinalFocusRestoreWindows.Remove(window);

            var wasStillTracked = trackedEntry != null;

            if (playAdditionalTransitionSound)
            {
                TryPlayAdditionalViewSound("close-external", trackedEntry?.StyleKey ?? window.Tag as string);
            }

            if (wasStillTracked)
            {
                var rebuilt = windows.Reverse()
                    .Where(entry => !ReferenceEquals(entry.Window, window))
                    .ToList();

                windows.Clear();

                foreach (var item in rebuilt)
                    windows.Push(item);

                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] Window tracking updated. Removed={window.Tag as string ?? "<no tag>"}, " +
                    $"StackCountAfter={windows.Count}, NewTop={DescribeTrackedWindow(windows.Any() ? windows.Peek() : null)}");
            }
            else
            {
                // CloseTopWindow removes the entry before calling Close(). The Closed event still has to
                // perform the final Playnite restoration; returning here was the Alt+F4 regression.
                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] Closed window was already popped from tracking. " +
                    $"StackCount={windows.Count}, Window={DescribeWindow(window)}");
            }

            NotifyOpenWindowStateChanged();

            if (!windows.Any())
            {
                if (suppressFinalFocusRestore)
                {
                    global::AnikiHelper.AnikiLog.Debug(logger,
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
                    global::AnikiHelper.AnikiLog.Debug(logger,
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

                    global::AnikiHelper.AnikiLog.Debug(logger,
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
                global::AnikiHelper.AnikiLog.Debug(logger, ex, "[AnikiHelper][WindowManager] Failed to publish pre-show window state.");
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
                global::AnikiHelper.AnikiLog.Debug(logger,
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

                global::AnikiHelper.AnikiLog.Debug(logger,
                    $"[AnikiHelper][WindowManager] Restoring focus to top tracked window. {DescribeTrackedWindow(topEntry)}, " +
                    $"StackCount={windows.Count}");

                top.Activate();
                top.Focus();
            }
            else
            {
                var playniteWindow = playniteApi.Dialogs.GetCurrentAppWindow();

                global::AnikiHelper.AnikiLog.Debug(logger,
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
