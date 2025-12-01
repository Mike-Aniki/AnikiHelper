<div align="center">

# ANIKI HELPER  
![Made for Playnite Fullscreen Themes](https://img.shields.io/badge/Made%20for-Playnite%20Fullscreen%20Themes-A600FF?style=for-the-badge)

</div>



**Aniki Helper** is a Playnite plugin created to enhance the Fullscreen experience by adding advanced player statistics, dynamic visual effects, and other interactive features that can be displayed directly inside Fullscreen themes.

⚠️ Note for theme creators

This plugin was originally developed for my own [Aniki Themes](https://github.com/Mike-Aniki), to add features that can’t be achieved with XAML alone in Playnite Fullscreen mode — such as dynamic color adaptation, session notifications, and advanced statistics.
It is documented here only for reference, in case another theme creator wishes to integrate or adapt its features into their own Fullscreen theme.

## Global library statistics

**Aniki Helper** reads your Playnite database and exposes a set of **bindable properties** accessible from your Fullscreen theme through the `PluginSettings` markup extension.

There are **two types of bindings** you can use:

- **Direct bindings** → simple values (numbers, strings, paths) that can be displayed in a `TextBlock` or `Image`.
- **Collection bindings** → lists of structured items that must be used inside an `ItemsControl` or `ListBox`.

Example of direct bindings:

```xml
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=TotalPlaytimeString}" />
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=ThisMonthTopGameName}" />
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=FavoriteCount}" />
```
Example of collection binding:
```xml
<GroupBox Header="Top Played Games">
  <ItemsControl ItemsSource="{PluginSettings Plugin=AnikiHelper, Path=TopPlayed}">
    <ItemsControl.ItemTemplate>
      <DataTemplate>
        <StackPanel Orientation="Horizontal">
          <TextBlock Text="{Binding Name}" Margin="0,0,10,0" />
          <TextBlock Text="{Binding PlaytimeString}" Foreground="Gray" />
        </StackPanel>
      </DataTemplate>
    </ItemsControl.ItemTemplate>
  </ItemsControl>
</GroupBox>
```
### Direct Value Bindings
These properties can be used directly in elements like `<TextBlock>` or `<Image>`.

| Property | Description |
| ---------------------------------------------- | ---------------------------------- |
| TotalCount | Total number of games in the library. |
| InstalledCount / NotInstalledCount | Number of installed / uninstalled games. |
| HiddenCount / FavoriteCount | Number of hidden games / number of favorites. |
| TotalPlaytimeString / AveragePlaytimeString | Total and average playtime across the entire library (formatted strings). |
| ThisMonthPlayedTotalString | Total playtime accumulated this month. |
| ThisMonthPlayedCount | Number of games played at least once this month. |
| ThisMonthTopGameName | Name of the most played game this month. |
| ThisMonthTopGamePlaytime | Playtime spent on that game this month (formatted). |
| ThisMonthTopGameCoverPath | Cover image path of the most played game this month. |
| ThisMonthTopGameBackgroundPath | Background image path of the most played game this month. |
| SessionGameName | Name of the game whose session has just ended. |
| SessionDurationString | Duration of the last ended session (formatted). |
| SessionTotalPlaytimeString | Total accumulated playtime after the last session (formatted). |
| SessionNewAchievementsCount | Number of achievements unlocked during the last session. |
| SuggestedGameName | Name of the recommended game (“Suggested for You”). |
| SuggestedGameCoverPath | Cover image path of the suggested game. |
| SuggestedGameBackgroundPath | Background image path of the suggested game. |
| SuggestedGameSourceName | Reference game used for the suggestion (“Because you played X”). |
| SuggestedGameReasonKey | Internal suggestion reason key (SameGenre, SimilarTags, etc.). |
| SuggestedGameBannerText | Localized banner text (e.g. “Same genre as Elden Ring”). |

Example of Suggested Game Integration:
```xml
    <!-- Cover -->
    <Image Source="{PluginSettings Plugin=AnikiHelper, Path=SuggestedGameCoverPath}"
           Height="180"
           Stretch="UniformToFill" />

    <!-- Game Name -->
    <TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=SuggestedGameName}"
               FontSize="20"
               FontWeight="SemiBold"
               Foreground="White"
               Margin="0,6,0,0" />

    <!-- Reason (localized key) -->
    <TextBlock  FontSize="13"
                Foreground="White"
                TextTrimming="CharacterEllipsis"
                Text="{PluginSettings Plugin=AnikiHelper, Path=SuggestedGameBannerText}">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Visibility" Value="Visible" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Text, RelativeSource={RelativeSource Self}}" Value="">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Text, RelativeSource={RelativeSource Self}}" Value="{x:Null}">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
```

### Localization Keys for Suggested Game Banner

To localize the `SuggestedGameBannerText` generated by Aniki Helper, your theme must provide the following resource keys in its language files (`en_EN.xaml`, `fr_FR.xaml`, etc.):

| Resource Key | Example (English) | Description |
|--------------|-------------------|-------------|
| SuggestBanner_SameGenre | Same genre as {0} | Displayed when the suggested game shares one or more genres with the reference game. |
| SuggestBanner_SimilarTags | Similar tags to {0} | Displayed when similar tags were detected. |
| SuggestBanner_SameDeveloper | Same developer as {0} | Displayed when both games share the same developer. |
| SuggestBanner_SamePublisher | Same publisher as {0} | Displayed when both games share the same publisher. |

**Fallback Behavior**

If your theme does not provide these localization keys, Aniki Helper will automatically fall back to the English version.  
This ensures the suggested-game banner always displays readable text even without translation.

### Collection Bindings (use with `ItemsControl`)
These properties are **lists** and must be displayed using an `<ItemsControl>` with an `ItemTemplate`.

| Property | Structure | Description |
| ---------------------------------------------- | --------------------------- | ---------------------------------- |
| TopPlayed | {Name, PlaytimeString, PercentageString} | List of most played games |
| CompletionStates | {Name, Value, PercentageString} | Completion status breakdown |
| GameProviders | {Name, Value, PercentageString} | Breakdown by source/platform |
| RecentPlayed | {Name, Value} | Recently played games (Value = last activity date) |
| RecentAdded | {Name, Value} | Recently added games (Value = added date) |
| NeverPlayed | {Name, Value} | Games never played |
| RecentAchievements | {Game, Title, Desc, UnlockedString, IconPath} | 3 most recent unlocked achievements |
| RareTop | {Game, Title, PercentString, IconPath} | 3 rarest unlocked achievements this year |

### UI triggers
These properties are useful for animating or conditionally displaying elements in your theme.

| Property | Description |
| ---------------------------------------------- | ---------------------------------- |
| SessionHasNewAchievements | **True when new achievements were unlocked during the last ended session.** Use this to show or hide the achievement line inside the session-end notification. |
| SessionNotificationArmed | **True immediately after a game session ends.** Works as a safety flag to allow the session-end notification to appear. Automatically resets to **False** when Playnite starts. |
| SessionNotificationFlip | **Toggles between True and False each time a session ends.** Use together with `SessionNotificationArmed` to replay your Storyboard animation (one trigger for each state). |
| SessionNotificationStamp | **Unique GUID refreshed at every session end.** Useful if you prefer triggering animations based on a value change instead of booleans. |

---

## Dynamic Color System

The DynamicAuto module adds a real-time color adaptation system to Fullscreen themes.
When enabled, it automatically extracts the dominant color from the game’s background (or cover as a fallback) and applies it to the key color resources of your theme.

Activation

To enable this feature, make sure the following line is present in one of your constant.xaml files :

```xml
<sys:Boolean x:Key="DynamicAutoEnabled">True</sys:Boolean>
```

Once this key is set to True, Aniki Helper will continuously monitor the currently selected game and dynamically recolor your interface based on its dominant hue.

When active, the system updates the following color and brush resources in real time :

```xml
GlyphColor
GlowFocusColor
DynamicGlowBackgroundPrimary
OverlayMenu
ButtonPlayColor
FocusGameBorderBrush
MenuBorderBrush
NoFocusBorderButtonBrush
SuccessMenu
DynamicGlowBackgroundSuccess
ShadeBackground
```
These keys are refreshed automatically with smooth animated transitions (≈200 ms) each time the user selects a new game.
If DynamicAutoEnabled is later set to False, the theme immediately restores its original static colors.

**Notes**

> This system uses the exact same resource keys as those defined in the Aniki Themes color model.
> Because this plugin was originally created for Aniki Themes, it directly targets their key structure.
> However, it is fully documented here in case another theme creator wants to reuse or adapt it to their own Fullscreen theme.

--- 

## Addons Update Window Styling

AnikiHelper gives theme creators full control over the appearance of Playnite’s Add-ons Update window in Fullscreen mode.

When the update window appears, Aniki Helper automatically locates its inner panels and applies your custom styles if they exist in your theme resources.

To use this feature, define two styles in your constants or theme resource files:

```xml
<Style x:Key="Aniki_AddonsUpdateWindowStyle_Top" TargetType="Border">
  <Setter Property="Background" Value="{DynamicResource OverlayMenu}" />
  <Setter Property="CornerRadius" Value="12" />
  <Setter Property="BorderThickness" Value="0" />
</Style>

<Style x:Key="Aniki_AddonsUpdateWindowStyle_Bottom" TargetType="Border">
  <Setter Property="Background" Value="{DynamicResource OverlayMenu}" />
  <Setter Property="CornerRadius" Value="0,0,12,12" />
  <Setter Property="BorderThickness" Value="0" />
</Style>
```

These will be applied automatically:

The top border (update content area) uses the Aniki_AddonsUpdateWindowStyle_Top style.

The bottom border (buttons footer) uses the Aniki_AddonsUpdateWindowStyle_Bottom style.

No additional setup is required — the system detects and applies your styles automatically each time the window opens.

--- 

## SuccessStory Refresh Command

Aniki Helper exposes a command that allows Fullscreen themes to trigger a manual refresh of SuccessStory data for the currently selected game.  
This is useful to update achievements after unlocking trophies without switching back to Desktop Mode.

| Property | Description |
|---------|-------------|
| RefreshSuccessStoryCommand | Command that refreshes SuccessStory data for the selected game (equivalent to “Download plugin data” in Desktop mode). |

**Usage**
```xml
<Button Content="Refresh"
        Command="{PluginSettings Plugin=AnikiHelper, Path=RefreshSuccessStoryCommand}" />
```

This command only works when at least one game is selected in Fullscreen mode.  
SuccessStory must be installed and its data provider enabled.

---

## Steam Features

### Steam Patch Notes Bindings
These properties expose the latest Steam update for the selected game.  
Useful for displaying a “Last Update Patch Note” panel and an “Update Available” badge.

> **How the badge "Update Available" works**  
> Aniki Helper keeps a small local cache inside the plugin data folder, storing the last known Steam update title for each game.  
> When switching games, the plugin compares the current Steam update title with the cached one.  
> If they differ, the badge appears (`SteamUpdateIsNew = True`), and the cache is updated automatically.

| Property              | Description |
|-----------------------|-------------|
| SteamUpdateTitle      | Title of the latest Steam news/update for the selected game. |
| SteamUpdateDate       | Formatted date/time of the update. |
| SteamUpdateHtml       | Raw HTML content of the update (for HTML-capable controls). |
| SteamUpdateAvailable  | True when a Steam update/news was successfully found. |
| SteamUpdateError      | Error or status message if no update is available. |
| SteamUpdateIsNew      | True only when the update is new compared to the local cache (useful for badges). |

**Example**
```xml
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=SteamUpdateTitle}" />
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=SteamUpdateDate}" />
<HtmlTextView Html="{PluginSettings Plugin=AnikiHelper, Path=SteamUpdateHtml}" />
```

---

### Steam Recent Updates (Global – Top 10)
Aniki Helper also provides a global list of the 10 most recent Steam updates across your library.
This is designed for dedicated “Latest Updates” views inside fullscreen themes.

> **How it works**  
> Unlike the per-game update badge, this feature builds a **global list** of the most recently patched Steam games.  
> Every time a Steam update is detected for any game, the cache entry for that game is refreshed.  
> The plugin then sorts all cached entries by date and exposes only the **10 newest updates**.


| Field        | Description                                                   |
|--------------|---------------------------------------------------------------|
| **SteamRecentUpdates** | A list of 10 `SteamRecentUpdateItem` objects (sorted by newest).            |
| **GameName** | Display name of the game.                                     |
| **Title**    | Title of the Steam update/news.                               |
| **DateString** | Localized short date/time string.                           |
| **CoverPath** | Auto–detected path to the game’s cover image.                |
| **IconPath**  | Auto–detected path to the game’s icon.                       |
| **IsRecent**  | True when the update is less than 48 hours old (for badges). |

**Example**
```xml
<ItemsControl ItemsSource="{PluginSettings Plugin=AnikiHelper, Path=SteamRecentUpdates}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal">

                <!-- Cover -->
                <Image Source="{Binding CoverPath}"
                       Width="80" Height="80" />

                <!-- Info -->
                <StackPanel Margin="10,0,0,0">
                    <TextBlock Text="{Binding GameName}" FontWeight="SemiBold"/>
                    <TextBlock Text="{Binding DateString}" Opacity="0.7"/>
                    <TextBlock Text="{Binding Title}" />
                </StackPanel>

            </StackPanel>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```
---

### Steam Player Count Bindings

| Property                     | Description |
|------------------------------|-------------|
| SteamCurrentPlayersString  | Formatted player count string (e.g. "34,521 players online"). |
| SteamCurrentPlayersAvailable | True when a valid Steam player count was retrieved. |
| SteamCurrentPlayersError   | Error or status message if the player count could not be retrieved. |

**Example**
```xml
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=SteamCurrentPlayersString}" />
```

**Visibility trigger**
```xml
<DataTrigger Binding="{PluginSettings Plugin=AnikiHelper, Path=SteamCurrentPlayersAvailable}" Value="True">
    <Setter Property="Visibility" Value="Visible"/>
</DataTrigger>
```

---

## Global News System

Aniki Helper includes a complete News module designed for Fullscreen themes.  
It aggregates three different feeds:

1. **Global News** (custom RSS / IGN / etc.)  
2. **Playnite News** (GitHub releases for Playnite + add-ons)  
3. **Steam Deals** (game-deals.app – EU region)

All lists are exposed as **ObservableCollections**, ready to use inside `ItemsControl`.

### Global News (SteamGlobalNews)

A list of global gaming news pulled from a user-defined RSS feed.  
If no RSS is configured, the default source is **IGN** (filtered to remove ads, discounts, Black Friday spam, etc.).

| Field | Description |
|-------|------------|
| **Title** | Title of the news article. |
| **DateString** | Localized short date/time string. |
| **Summary** | Short summary extracted from the RSS feed. |
| **ImagePath** | Local cached preview image (if extracted from the feed). |
| **Url** | Full article URL (openable via a browser). |

**Binding Example**
```xml
<ItemsControl ItemsSource="{PluginSettings Plugin=AnikiHelper, Path=SteamGlobalNews}">
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <StackPanel Margin="0,0,0,16">
        <TextBlock Text="{Binding Title}" FontWeight="SemiBold"/>
        <TextBlock Text="{Binding DateString}" Opacity="0.7"/>
      </StackPanel>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```
### Playnite News (via GitHub feeds)

A curated list of updates for:

- Playnite releases  
- Add-on releases (ThemeOptions, MoData, SuccessStory, etc.)  

It shows **only the 10 most recent entries**.

| Field | Description |
|-------|-------------|
| **Title** | Title of the release/update. |
| **DateString** | Localized date. |
| **Summary** | Short description extracted from GitHub Atom feed. |
| **Url** | Link to the GitHub release. |

**Badge indicator**
| Property | Description |
|---------|-------------|
| **PlayniteNewsHasNew** | `True` when a new Playnite/add-on update has appeared since last refresh (useful for notification badges). |

**Binding Example**
```xml
<ItemsControl ItemsSource="{PluginSettings Plugin=AnikiHelper, Path=PlayniteNews}">
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <StackPanel>
        <TextBlock Text="{Binding Title}" FontWeight="SemiBold"/>
        <TextBlock Text="{Binding DateString}" Opacity="0.7"/>
      </StackPanel>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```
### Steam Deals (`Deals`)

A rotating list of the **15 newest Steam deals** (EU region), updated from game-deals.app.  
Deals older than **7 days** are automatically ignored.

| Field | Description |
|--------|-------------|
| **Title** | Deal title (usually the game name). |
| **DateString** | Date the deal was posted. |
| **Summary** | Includes price, discount, and platform. |
| **ImagePath** | Cached image for the deal/game. |
| **Url** | Link to the deal page. |

**Binding Example**
```xml
<ItemsControl ItemsSource="{PluginSettings Plugin=AnikiHelper, Path=Deals}">
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <StackPanel Margin="0,0,0,12">
        <TextBlock Text="{Binding Title}" FontWeight="SemiBold"/>
        <TextBlock Text="{Binding Summary}" Opacity="0.7"/>
      </StackPanel>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```
### Error & Status Bindings

These bindings help themes show messages when a feed is unavailable or invalid.

| Property | Description |
|----------|-------------|
| **SteamNewsCustomFeedInvalid** | `True` when the user RSS feed is not compatible. Shows an error message or a “Feed not supported” UI. |
| **NewsScanEnabled** | `True` when the news scanner is active. If `False`, you can grey out or hide the News tab. |


### Notes

- All images from RSS feeds, GitHub releases, and deals are downloaded and cached locally by the plugin.  
- The news system uses automatic spam filtering for keywords like “Black Friday”, “discount”, “sponsored”, etc.  
- Invalid RSS links never crash Playnite — instead, `SteamNewsCustomFeedInvalid = True` is set so the theme can display an informative message.

---

## Global Toast System

Aniki Helper provides a lightweight, theme-friendly toast system that allows Fullscreen themes to display temporary notifications (similar to PlayStation/Xbox banners).  
These toasts are triggered by several modules, including:

- Steam updates  
- Playnite add-on updates  
- Global news feed  
- Deals feed  
- Internal plugin events (optional future use)

The system exposes **four bindable properties** that themes can use to animate toast panels.

---

### Toast Bindings

| Property | Description |
|----------|-------------|
| **GlobalToastMessage** | The text content of the last generated toast (already localized). |
| **GlobalToastType** | A string describing the toast category (`"news"`, `"playniteNews"`, `"steamUpdate"`, `"deal"`, etc.). |
| **GlobalToastStamp** | A unique timestamp value updated on every toast (useful for change-triggered animations). |
| **GlobalToastFlip** | A boolean that toggles between `True` and `False` every time a toast appears (useful for storyboard replays). |

These bindings allow themes to display toast notifications even when the same message is triggered twice consecutively.

---

### Example: Minimal Toast Panel (XAML)

```xml
<Border x:Name="ToastPanel"
        Background="#AA000000"
        CornerRadius="8"
        Padding="16,8"
        HorizontalAlignment="Center"
        VerticalAlignment="Top"
        Visibility="Collapsed">

    <TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=GlobalToastMessage}"
               FontSize="16"
               Foreground="White"
               TextWrapping="Wrap" />

    <Border.Style>
        <Style TargetType="Border">
            <Style.Triggers>
                <!-- Trigger on type OR on flip -->
                <DataTrigger Binding="{PluginSettings Plugin=AnikiHelper, Path=GlobalToastFlip}" Value="True">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
                <DataTrigger Binding="{PluginSettings Plugin=AnikiHelper, Path=GlobalToastFlip}" Value="False">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
</Border>
```

Your theme can then animate `Opacity`, `TranslateTransform.Y`, or any animation you want.

---

### Toast Categories (`GlobalToastType`)

The plugin currently uses the following types:

- `"steamUpdate"` → When a new Steam update is detected  
- `"playniteNews"` → When new Playnite add-on updates are found  
- `"news"` → When a new global news article appears  
- `"deal"` → When new Steam deals are detected  

Themes can use this to customize colors or icons:

```xml
<DataTrigger Binding="{PluginSettings Plugin=AnikiHelper, Path=GlobalToastType}" Value="steamUpdate">
    <Setter Property="Background" Value="#FF1B7CFF"/>
</DataTrigger>
```

---

### Localization for Toast Messages

Toast messages shown inside `GlobalToastMessage` are **fully localizable** using the theme’s language files.  
These are the resource keys used by Aniki Helper when composing toast notifications:

| Resource Key | Example Text (English) | Used For |
|--------------|-------------------------|----------|
| **Toast_SteamUpdate** | New Steam update for {0} | Steam update detected |
| **Toast_PlayniteNews** | New Playnite add-on update available | Playnite add-on releases |
| **Toast_GlobalNews** | New article: {0} | Global news feed |
| **Toast_Deal** | New deal available: {0} | Steam deals |

**Example in `en_EN.xaml`:**
```xml
<sys:String x:Key="Toast_SteamUpdate">New Steam update for {0}</sys:String>
<sys:String x:Key="Toast_PlayniteNews">New Playnite add-on update available</sys:String>
<sys:String x:Key="Toast_GlobalNews">New article: {0}</sys:String>
<sys:String x:Key="Toast_Deal">New deal available: {0}</sys:String>
```

**Fallback behavior:**  
If a localization key is missing, Aniki Helper automatically falls back to English.

---

### Notes

- `GlobalToastMessage` is always short and ready for UI use.  
- `GlobalToastFlip` and `GlobalToastStamp` ensure animations always play, even if the same event happens twice.  
- Toasts never stack: launching a new one replaces the old text (no overlay issues).

---

## Disk Usage Bindings

Aniki Helper exposes a complete storage overview system that allows Fullscreen themes to display real-time disk usage information for all drives detected by Playnite.

The plugin automatically scans all disks and produces a list of `DiskUsageItem` objects that you can bind inside an `ItemsControl`.

---

### DiskUsages (Collection)

Use this binding to display a list of all drives and their usage.

**Binding:**  
`{PluginSettings Plugin=AnikiHelper, Path=DiskUsages}`

Each item in the list includes:

| Field | Description |
|-------|-------------|
| **Label** | Drive label or path (e.g., “C:\”, “D:\ Games”). |
| **TotalSpaceString** | Total space (e.g., “931 GB”). |
| **FreeSpaceString** | Free space remaining. |
| **UsedPercentage** | Percentage used as a number between 0–100. |
| **UsedTenthsInt** | Integer from 0 to 10 for simple bar indicators (e.g., 7 = 70%). |

`UsedTenthsInt` is ideal for progress bars where you want a fixed number of steps.

---

## Recommended Plugins

- [ThemeOptions](https://github.com/ashpynov/ThemeOptions)
- [MoData](https://github.com/jonosellier/MoData)
- [SuccessStory](https://github.com/Lacro59/playnite-successstory-plugin)
- [SuccessStory Fullscreen Helper](https://github.com/saVantCZ/SuccessStoryFullscreenHelper)

---

> **Note:** These bindings work only in **Fullscreen themes**.  
> Ensure the **Aniki Helper** plugin is installed and enabled for data to appear correctly.
