using Playnite.SDK;
using Playnite.SDK.Events;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AnikiHelper.Services
{
    public sealed class SteamAccountSessionInfo
    {
        public string SteamId64 { get; set; }
        public string WebApiToken { get; set; }
        public bool HasSteamSessionCookies { get; set; }
        public string FinalUrl { get; set; }
        public string Error { get; set; }

        public bool HasSteamId => !string.IsNullOrWhiteSpace(SteamId64);
        public bool IsConnected => HasSteamId && HasSteamSessionCookies;

        public static SteamAccountSessionInfo Empty(string error = null)
        {
            return new SteamAccountSessionInfo
            {
                SteamId64 = string.Empty,
                WebApiToken = string.Empty,
                HasSteamSessionCookies = false,
                FinalUrl = string.Empty,
                Error = error ?? string.Empty
            };
        }
    }

    public sealed class SteamRecommendedPageResult
    {
        public bool Success { get; set; }
        public string Html { get; set; }
        public string FinalUrl { get; set; }
        public string Error { get; set; }
        public SteamAccountSessionInfo Session { get; set; }
    }

    public sealed class SteamWishlistAppIdsResult
    {
        public bool Success { get; set; }
        public HashSet<int> AppIds { get; set; }
        public List<int> OrderedAppIds { get; set; }
        public string Html { get; set; }
        public string FinalUrl { get; set; }
        public string Error { get; set; }
        public SteamAccountSessionInfo Session { get; set; }
        public bool LoadedFromWebApi { get; set; }

        public SteamWishlistAppIdsResult()
        {
            AppIds = new HashSet<int>();
            OrderedAppIds = new List<int>();
            Html = string.Empty;
            FinalUrl = string.Empty;
            Error = string.Empty;
            Session = SteamAccountSessionInfo.Empty();
            LoadedFromWebApi = false;
        }
    }

    /// <summary>
    /// Steam Store account session based on Playnite WebView/CEF cookies.
    /// This is intentionally separate from the Steam Web API key: the key can load friends data,
    /// while the CEF session can load personalized Store pages such as /recommender/.
    /// </summary>
    public sealed class SteamAccountSessionService
    {
        private readonly IPlayniteAPI api;
        private readonly ILogger logger;
        private IWebView interactiveLoginView;
        private SteamAccountSessionInfo interactiveLoginResult;
        private readonly SemaphoreSlim storeWebViewGate = new SemaphoreSlim(1, 1);

        private const string StoreHost = "store.steampowered.com";
        private const string StoreAccountUrl = "https://store.steampowered.com/account/";
        private const string StoreRecommendedBaseUrl = "https://store.steampowered.com/recommender/";
        private const string StoreRecommendedFeedBaseUrl = "https://store.steampowered.com/recommended/";
        private const string StoreWishlistProfileBaseUrl = "https://store.steampowered.com/wishlist/profiles/";
        private const string WishlistWebApiBaseUrl = "https://api.steampowered.com/IWishlistService/GetWishlist/v1/";

        private static readonly HttpClient WishlistHttpClient = CreateWishlistHttpClient();

        private static readonly Regex SteamIdPattern =
            new Regex(@"g_steamID\s*=\s*""(?<id>[0-9]{17})""", RegexOptions.IgnoreCase);

        private static readonly Regex JsonSteamIdPattern =
            new Regex(@"""steamid""\s*:\s*""(?<id>[0-9]{17})""", RegexOptions.IgnoreCase);

        private static readonly Regex EncodedTokenPattern =
            new Regex(@"&quot;webapi_token&quot;:&quot;(?<token>[^&]+)&quot;", RegexOptions.IgnoreCase);

        private static readonly Regex JsonTokenPattern =
            new Regex(@"""webapi_token""\s*:\s*""(?<token>[^""]+)""", RegexOptions.IgnoreCase);

        public SteamAccountSessionService(IPlayniteAPI api, ILogger logger)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.logger = logger;
        }

        public async Task<SteamAccountSessionInfo> ProbeAsync(CancellationToken ct)
        {
            await storeWebViewGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await InvokeOnUiAsync(async () =>
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        using (var view = api.WebViews.CreateOffscreenView())
                        {
                            await NavigateAndWaitAsync(view, StoreAccountUrl, 15000).ConfigureAwait(true);
                            await Task.Delay(700, ct).ConfigureAwait(true);

                            var finalUrl = SafeGetCurrentAddress(view);
                            var session = ResolveFromView(view, StoreHost);
                            session.FinalUrl = finalUrl;

                            if (IsLoginPageUrl(finalUrl) || !IsStoreDomainUrl(finalUrl))
                            {
                                session.HasSteamSessionCookies = false;
                            }

                            logger?.Info(
                                $"[SteamAccount] Store probe | connected={session.IsConnected} | " +
                                $"hasStoreLoginCookie={session.HasSteamSessionCookies} | " +
                                $"hasSteamId={session.HasSteamId} | finalUrl={finalUrl}"
                            );

                            return session;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[SteamAccount] Store probe failed.");
                        return SteamAccountSessionInfo.Empty("Failed to check Steam Store session.");
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                storeWebViewGate.Release();
            }
        }

        public async Task<SteamAccountSessionInfo> AuthenticateInteractiveAsync(CancellationToken ct)
        {
            try
            {
                var existing = await ProbeAsync(ct).ConfigureAwait(false);
                if (existing?.IsConnected == true)
                {
                    return existing;
                }
            }
            catch
            {
                // Continue with interactive login.
            }

            var tcs = new TaskCompletionSource<SteamAccountSessionInfo>(TaskCreationOptions.RunContinuationsAsynchronously);

            var dispatcher = api?.MainView?.UIDispatcher;
            if (dispatcher == null)
            {
                return SteamAccountSessionInfo.Empty("Playnite UI dispatcher is not available.");
            }

            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var result = LoginInteractively();
                    tcs.TrySetResult(result ?? SteamAccountSessionInfo.Empty("Steam login was cancelled."));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }));

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(3), ct)).ConfigureAwait(false);
            if (completed != tcs.Task)
            {
                CloseInteractiveLoginView();
                return SteamAccountSessionInfo.Empty("Steam login timed out.");
            }

            return await tcs.Task.ConfigureAwait(false);
        }

        public Task<SteamRecommendedPageResult> GetRecommendedPageHtmlAsync(string language, string countryCode, CancellationToken ct)
        {
            return GetSteamStorePageHtmlAsync(BuildRecommendedUrl(language, countryCode), "Steam recommender", ct);
        }

        public Task<SteamRecommendedPageResult> GetRecommendedFeedPageHtmlAsync(string language, string countryCode, CancellationToken ct)
        {
            return GetSteamStorePageHtmlAsync(BuildRecommendedFeedUrl(language, countryCode), "Steam recommended supplement", ct);
        }

        private async Task<SteamRecommendedPageResult> GetSteamStorePageHtmlAsync(string url, string label, CancellationToken ct)
        {
            await storeWebViewGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await InvokeOnUiAsync(async () =>
                {
                    var result = new SteamRecommendedPageResult
                    {
                        Success = false,
                        Html = string.Empty,
                        FinalUrl = string.Empty,
                        Error = string.Empty,
                        Session = SteamAccountSessionInfo.Empty()
                    };

                    try
                    {
                        ct.ThrowIfCancellationRequested();

                        using (var view = api.WebViews.CreateOffscreenView())
                        {
                            await NavigateAndWaitAsync(view, url, 20000).ConfigureAwait(true);
                            await Task.Delay(2000, ct).ConfigureAwait(true);

                            result.FinalUrl = SafeGetCurrentAddress(view);
                            result.Session = ResolveFromView(view, StoreHost);
                            result.Html = await SafeGetPageSourceAsync(view).ConfigureAwait(true);

                            if (IsLoginPageUrl(result.FinalUrl) ||
                                !IsStoreDomainUrl(result.FinalUrl) ||
                                result.Session?.IsConnected != true)
                            {
                                result.Error = "Steam Store session is not connected.";
                                return result;
                            }

                            if (string.IsNullOrWhiteSpace(result.Html))
                            {
                                result.Error = label + " page returned empty HTML.";
                                return result;
                            }

                            result.Success = true;
                            return result;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[SteamAccount] Failed to load " + label + " page.");
                        result.Error = ex.Message;
                        result.Session = SteamAccountSessionInfo.Empty(result.Error);
                        return result;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                storeWebViewGate.Release();
            }
        }

        public async Task<SteamWishlistAppIdsResult> GetWishlistAppIdsAsync(
            string steamId64,
            string language,
            string countryCode,
            CancellationToken ct)
        {
            var steamId = NormalizeSteamId64(steamId64);
            if (string.IsNullOrWhiteSpace(steamId))
            {
                return new SteamWishlistAppIdsResult
                {
                    Error = "SteamID64 is missing for wishlist lookup."
                };
            }

            // Primary source: current Wishlist Web API. It avoids scraping Steam's
            // React page and replaces the obsolete /wishlistdata/ endpoint.
            var apiResult = await TryGetWishlistFromWebApiAsync(steamId, ct).ConfigureAwait(false);
            if (apiResult?.Success == true)
            {
                return apiResult;
            }

            await storeWebViewGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await InvokeOnUiAsync(async () =>
                {
                    var result = new SteamWishlistAppIdsResult();

                    try
                    {
                        ct.ThrowIfCancellationRequested();

                        using (var view = api.WebViews.CreateOffscreenView())
                        {
                            var pageUrl = BuildWishlistPageUrl(steamId, language, countryCode);
                            await NavigateAndWaitAsync(view, pageUrl, 20000).ConfigureAwait(true);
                            await Task.Delay(3000, ct).ConfigureAwait(true);

                            result.FinalUrl = SafeGetCurrentAddress(view);
                            result.Session = ResolveFromView(view, StoreHost);

                            if (result.Session?.IsConnected != true)
                            {
                                result.Error = "Steam Store session is not connected for wishlist lookup.";
                                return result;
                            }

                            if (!IsWishlistPageUrl(result.FinalUrl, steamId))
                            {
                                result.Error = IsLoginPageUrl(result.FinalUrl)
                                    ? "Steam Store session is not connected for wishlist lookup."
                                    : "Steam did not return the requested wishlist page.";
                                return result;
                            }

                            result.Html = await SafeGetPageSourceAsync(view).ConfigureAwait(true);
                            result.OrderedAppIds = ExtractWishlistAppIdsInOrder(result.Html);

                            // The wishlist is rendered dynamically. Give React one final chance
                            // to populate the page before accepting a genuinely empty list.
                            if (result.OrderedAppIds.Count == 0)
                            {
                                await Task.Delay(2000, ct).ConfigureAwait(true);
                                result.Html = await SafeGetPageSourceAsync(view).ConfigureAwait(true);
                                result.OrderedAppIds = ExtractWishlistAppIdsInOrder(result.Html);
                            }

                            result.AppIds = new HashSet<int>(result.OrderedAppIds);
                            result.Success = true;
                            return result;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "[SteamAccount] Failed to load Steam wishlist data.");
                        result.Error = ex.Message;
                        result.Session = SteamAccountSessionInfo.Empty(result.Error);
                        return result;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                storeWebViewGate.Release();
            }
        }

        private async Task<SteamWishlistAppIdsResult> TryGetWishlistFromWebApiAsync(
            string steamId64,
            CancellationToken ct)
        {
            var result = new SteamWishlistAppIdsResult();
            var url = BuildWishlistWebApiUrl(steamId64);

            try
            {
                ct.ThrowIfCancellationRequested();

                using (var response = await WishlistHttpClient.GetAsync(url, ct).ConfigureAwait(false))
                {
                    result.FinalUrl = WishlistWebApiBaseUrl;

                    if (!response.IsSuccessStatusCode)
                    {
                        result.Error = $"Steam Wishlist Web API returned HTTP {(int)response.StatusCode}.";
                        logger?.Info($"[SteamAccount] Wishlist Web API unavailable | status={(int)response.StatusCode} | falling back to Store page");
                        return result;
                    }

                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var root = JObject.Parse(json);
                    var items = root["response"]?["items"] as JArray;

                    if (items == null)
                    {
                        result.Error = "Steam Wishlist Web API returned an invalid response.";
                        logger?.Info("[SteamAccount] Wishlist Web API response has no response.items | falling back to Store page");
                        return result;
                    }

                    var ordered = new List<int>();
                    var seen = new HashSet<int>();

                    foreach (var item in items)
                    {
                        var appId = item?["appid"]?.Value<int?>() ?? 0;
                        if (appId > 0 && seen.Add(appId))
                        {
                            ordered.Add(appId);
                        }
                    }

                    if (ordered.Count == 0)
                    {
                        result.Error = "Steam Wishlist Web API returned no items; verifying with the authenticated Store page.";
                        logger?.Info("[SteamAccount] Wishlist Web API returned 0 items | verifying with Store page");
                        return result;
                    }

                    result.OrderedAppIds = ordered;
                    result.AppIds = new HashSet<int>(ordered);
                    result.LoadedFromWebApi = true;
                    result.Success = true;
                    result.Error = string.Empty;
                    result.Session = null;

                    logger?.Info($"[SteamAccount] Wishlist Web API loaded | count={ordered.Count}");
                    return result;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                logger?.Warn(ex, "[SteamAccount] Wishlist Web API failed; falling back to Store page.");
                return result;
            }
        }

        public void ClearSession()
        {
            CloseInteractiveLoginView();

            try
            {
                InvokeOnUi(() =>
                {
                    using (var view = api.WebViews.CreateOffscreenView())
                    {
                        ClearSteamCookies(view);
                    }
                });
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[SteamAccount] Failed to clear Steam cookies.");
            }
        }

        private SteamAccountSessionInfo LoginInteractively()
        {
            interactiveLoginResult = null;
            IWebView view = null;

            try
            {
                view = api.WebViews.CreateView(1000, 800);
                interactiveLoginView = view;
                view.LoadingChanged += CloseWhenLoggedIn;
                view.Navigate(StoreAccountUrl);
                view.OpenDialog();

                return interactiveLoginResult;
            }
            finally
            {
                if (view != null)
                {
                    view.LoadingChanged -= CloseWhenLoggedIn;
                    if (ReferenceEquals(interactiveLoginView, view))
                    {
                        interactiveLoginView = null;
                    }

                    view.Dispose();
                }
            }
        }

        private async void CloseWhenLoggedIn(object sender, WebViewLoadingChangedEventArgs e)
        {
            try
            {
                if (e.IsLoading)
                {
                    return;
                }

                var view = sender as IWebView;
                if (view == null)
                {
                    return;
                }

                var address = SafeGetCurrentAddress(view);
                if (IsLoginPageUrl(address))
                {
                    return;
                }

                await Task.Delay(300).ConfigureAwait(true);
                var session = ResolveFromView(view, StoreHost);
                session.FinalUrl = address;

                if (session.IsConnected && IsStoreDomainUrl(address))
                {
                    interactiveLoginResult = session;
                    view.Close();
                    return;
                }

                if (!IsLoginPageUrl(address) && !IsStoreDomainUrl(address))
                {
                    view.Navigate(StoreAccountUrl);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[SteamAccount] Failed to check interactive login status.");
            }
        }

        private SteamAccountSessionInfo ResolveFromView(IWebView view, string requiredCookieHost = null)
        {
            if (view == null)
            {
                return SteamAccountSessionInfo.Empty();
            }

            var cookies = view.GetCookies() ?? new List<HttpCookie>();
            var cookieHost = string.IsNullOrWhiteSpace(requiredCookieHost)
                ? TryGetHost(SafeGetCurrentAddress(view))
                : requiredCookieHost;

            var applicableCookies = cookies
                .Where(c => c != null && CookieAppliesToHost(c.Domain, cookieHost))
                .ToList();

            var hasCookies = HasSteamSessionCookies(applicableCookies);
            var cookieSteamId = TryExtractSteamId64FromCookies(applicableCookies);
            var source = string.Empty;

            try
            {
                source = view.GetPageSource();
            }
            catch
            {
            }

            var steamId = NormalizeSteamId64(cookieSteamId) ?? ExtractSteamId64(source);
            var token = ExtractWebApiToken(source);

            return new SteamAccountSessionInfo
            {
                SteamId64 = steamId ?? string.Empty,
                WebApiToken = token ?? string.Empty,
                HasSteamSessionCookies = hasCookies,
                FinalUrl = SafeGetCurrentAddress(view),
                Error = string.Empty
            };
        }

        private static async Task NavigateAndWaitAsync(IWebView view, string url, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object sender, WebViewLoadingChangedEventArgs e)
            {
                if (!e.IsLoading)
                {
                    view.LoadingChanged -= Handler;
                    tcs.TrySetResult(true);
                }
            }

            view.LoadingChanged += Handler;
            try
            {
                view.Navigate(url);
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(true);
                if (completed != tcs.Task)
                {
                    throw new TimeoutException("Steam WebView navigation timed out.");
                }

                await tcs.Task.ConfigureAwait(true);
            }
            finally
            {
                view.LoadingChanged -= Handler;
            }
        }

        private Task<T> InvokeOnUiAsync<T>(Func<Task<T>> action)
        {
            var dispatcher = api?.MainView?.UIDispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                return action();
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    var value = await action().ConfigureAwait(true);
                    tcs.TrySetResult(value);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }));

            return tcs.Task;
        }

        private void InvokeOnUi(Action action)
        {
            var dispatcher = api?.MainView?.UIDispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Invoke(action);
        }

        private void CloseInteractiveLoginView()
        {
            try
            {
                var view = interactiveLoginView;
                if (view == null)
                {
                    return;
                }

                var dispatcher = api?.MainView?.UIDispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    view.Close();
                }
                else
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { view.Close(); } catch { }
                    }));
                }
            }
            catch
            {
            }
        }

        private static string BuildRecommendedUrl(string language, string countryCode)
        {
            var lang = string.IsNullOrWhiteSpace(language) ? "english" : language.Trim().ToLowerInvariant();
            var cc = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode.Trim().ToUpperInvariant();

            // Do not rely on Steam's snr tracking parameter. It only tells Steam where the
            // click came from. The recommender page itself is the important source.
            return $"{StoreRecommendedBaseUrl}?l={Uri.EscapeDataString(lang)}&cc={Uri.EscapeDataString(cc)}";
        }

        private static string BuildRecommendedFeedUrl(string language, string countryCode)
        {
            var lang = string.IsNullOrWhiteSpace(language) ? "english" : language.Trim().ToLowerInvariant();
            var cc = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode.Trim().ToUpperInvariant();
            return $"{StoreRecommendedFeedBaseUrl}?l={Uri.EscapeDataString(lang)}&cc={Uri.EscapeDataString(cc)}";
        }

        private static string BuildWishlistPageUrl(string steamId64, string language, string countryCode)
        {
            var lang = string.IsNullOrWhiteSpace(language) ? "english" : language.Trim().ToLowerInvariant();
            var cc = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode.Trim().ToUpperInvariant();
            return $"{StoreWishlistProfileBaseUrl}{Uri.EscapeDataString(steamId64)}/?l={Uri.EscapeDataString(lang)}&cc={Uri.EscapeDataString(cc)}";
        }

        private static string BuildWishlistWebApiUrl(string steamId64)
        {
            return WishlistWebApiBaseUrl + "?steamid=" + Uri.EscapeDataString(steamId64);
        }

        private static HttpClient CreateWishlistHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AnikiHelper/SteamWishlist"
            );
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            return client;
        }

        private static List<int> ExtractWishlistAppIdsInOrder(string html)
        {
            var ordered = new List<int>();
            var seen = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return ordered;
            }

            var raw = WebUtility.HtmlDecode(html ?? string.Empty) ?? string.Empty;

            Action<int> add = id =>
            {
                if (id > 0 && seen.Add(id))
                {
                    ordered.Add(id);
                }
            };

            // The normal wishlist page keeps the app list inside scripts (g_rgWishlistData / appid fields).
            // Do not strip scripts before parsing, otherwise the real wishlist becomes invisible.
            foreach (Match match in Regex.Matches(raw, "\\\"appid\\\"\\s*:\\s*\\\"?(?<id>\\d{2,10})\\\"?", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups["id"].Value, out var id))
                {
                    add(id);
                }
            }

            foreach (Match match in Regex.Matches(raw, "\\\"app_id\\\"\\s*:\\s*\\\"?(?<id>\\d{2,10})\\\"?", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups["id"].Value, out var id))
                {
                    add(id);
                }
            }

            foreach (Match match in Regex.Matches(raw, @"data-(?:ds-)?appid=[""'](?<id>\d{2,10})[""']", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups["id"].Value, out var id))
                {
                    add(id);
                }
            }

            foreach (Match match in Regex.Matches(raw, @"data-app-id=[""'](?<id>\d{2,10})[""']", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups["id"].Value, out var id))
                {
                    add(id);
                }
            }

            foreach (Match match in Regex.Matches(raw, @"(?:https?:)?//store\.steampowered\.com/app/(?<id>\d{2,10})(?:/|\b)", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups["id"].Value, out var id))
                {
                    add(id);
                }
            }

            foreach (Match match in Regex.Matches(raw, @"/app/(?<id>\d{2,10})(?:/|\b)", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups["id"].Value, out var id))
                {
                    add(id);
                }
            }

            return ordered;
        }

        private static HashSet<int> ExtractWishlistAppIds(string html)
        {
            return new HashSet<int>(ExtractWishlistAppIdsInOrder(html));
        }

        private static bool IsStoreDomainUrl(string url)
        {
            var host = TryGetHost(url);
            return !string.IsNullOrWhiteSpace(host) &&
                   (host.Equals(StoreHost, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + StoreHost, StringComparison.OrdinalIgnoreCase));
        }

        private static string TryGetHost(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                ? uri.Host ?? string.Empty
                : string.Empty;
        }

        private static bool CookieAppliesToHost(string cookieDomain, string host)
        {
            if (string.IsNullOrWhiteSpace(cookieDomain) || string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            var normalizedDomain = cookieDomain.Trim().TrimStart('.');
            var normalizedHost = host.Trim().TrimEnd('.');

            return normalizedHost.Equals(normalizedDomain, StringComparison.OrdinalIgnoreCase) ||
                   normalizedHost.EndsWith("." + normalizedDomain, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWishlistPageUrl(string url, string steamId64)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var host = uri.Host ?? string.Empty;
            if (!host.Equals("store.steampowered.com", StringComparison.OrdinalIgnoreCase) &&
                !host.EndsWith(".steampowered.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var path = (uri.AbsolutePath ?? string.Empty).Replace('\\', '/');

            if (path.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            // Steam can redirect:
            // /wishlist/profiles/{steamId64}/
            // to:
            // /wishlist/id/{customProfileName}/
            if (path.IndexOf("/wishlist/id/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(steamId64))
            {
                return false;
            }

            return path.IndexOf("/wishlist/profiles/" + steamId64, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (path.IndexOf("/wishlist/profiles/", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 path.IndexOf(steamId64, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsLoginPageUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("openid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("login.steampowered.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSteamDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return false;
            var d = domain.Trim().TrimStart('.');
            return d.EndsWith("steamcommunity.com", StringComparison.OrdinalIgnoreCase) ||
                   d.EndsWith("steampowered.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSteamSessionCookies(IEnumerable<HttpCookie> cookies)
        {
            // sessionid also exists for anonymous visitors and is not proof of authentication.
            // Only steamLoginSecure on the requested Steam host counts as connected.
            return cookies?.Any(c =>
                c != null &&
                !string.IsNullOrWhiteSpace(c.Domain) &&
                IsSteamDomain(c.Domain) &&
                string.Equals(c.Name, "steamLoginSecure", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.Value)) == true;
        }

        private static string TryExtractSteamId64FromCookies(IEnumerable<HttpCookie> cookies)
        {
            var authCookie = cookies?.FirstOrDefault(c =>
                c != null &&
                !string.IsNullOrWhiteSpace(c.Domain) &&
                IsSteamDomain(c.Domain) &&
                string.Equals(c.Name, "steamLoginSecure", StringComparison.OrdinalIgnoreCase));

            return authCookie == null ? null : TryExtractSteamId64FromSteamLoginSecure(authCookie.Value);
        }

        private static string TryExtractSteamId64FromSteamLoginSecure(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(value);
            }
            catch
            {
                decoded = value;
            }

            var match = Regex.Match(decoded, @"(?<id>\d{17})");
            return match.Success ? match.Groups["id"].Value : null;
        }

        private static string ExtractSteamId64(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;

            var match = SteamIdPattern.Match(source);
            if (!match.Success)
            {
                match = JsonSteamIdPattern.Match(source);
            }

            return match.Success ? NormalizeSteamId64(match.Groups["id"].Value) : null;
        }

        private static string ExtractWebApiToken(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;

            var match = EncodedTokenPattern.Match(source);
            if (!match.Success)
            {
                match = JsonTokenPattern.Match(source);
            }

            var token = match.Success ? WebUtility.HtmlDecode(match.Groups["token"].Value).Trim() : null;
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }

        private static string NormalizeSteamId64(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            return !string.IsNullOrWhiteSpace(normalized) && Regex.IsMatch(normalized, @"^\d{17}$")
                ? normalized
                : null;
        }

        private static string SafeGetCurrentAddress(IWebView view)
        {
            try { return view?.GetCurrentAddress() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static async Task<string> SafeGetPageSourceAsync(IWebView view)
        {
            try { return await view.GetPageSourceAsync().ConfigureAwait(true); }
            catch { return string.Empty; }
        }

        private static void ClearSteamCookies(IWebView view)
        {
            view.DeleteDomainCookies(".steamcommunity.com");
            view.DeleteDomainCookies("steamcommunity.com");
            view.DeleteDomainCookies(".store.steampowered.com");
            view.DeleteDomainCookies("store.steampowered.com");
            view.DeleteDomainCookies(".steampowered.com");
            view.DeleteDomainCookies("steampowered.com");
            view.DeleteDomainCookies(".login.steampowered.com");
            view.DeleteDomainCookies("login.steampowered.com");
            view.DeleteDomainCookies(".help.steampowered.com");
            view.DeleteDomainCookies("help.steampowered.com");
        }
    }
}
