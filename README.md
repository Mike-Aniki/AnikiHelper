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
| TotalCount | Total number of games |
| InstalledCount / NotInstalledCount | Installed / Uninstalled games |
| HiddenCount / FavoriteCount | Hidden and favorite games |
| TotalPlaytimeString / AveragePlaytimeString | Global and average playtime (formatted) |
| ThisMonthPlayedTotalString | Total playtime this month |
| ThisMonthPlayedCount | Number of games played this month |
| ThisMonthTopGameName | Most played game name this month |
| ThisMonthTopGamePlaytime | Time spent on that game this month |
| ThisMonthTopGameCoverPath | Cover image path of the most played game |
| SessionGameName | Name of the game whose session just ended |
| SessionDurationString | Duration of the last session |
| SessionTotalPlaytimeString | Total accumulated playtime on that game after the session |
| SessionNewAchievementsCount | Number of achievements earned during the last session |

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

This system uses the exact same resource keys as those defined in the Aniki Themes color model.
Because this plugin was originally created for Aniki Themes, it directly targets their key structure.
However, it is fully documented here in case another theme creator wants to reuse or adapt it to their own Fullscreen theme.

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

> **How the badge works**  
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

## Recommended Plugins

- [ThemeOptions](https://github.com/ashpynov/ThemeOptions)
- [MoData](https://github.com/jonosellier/MoData)
- [SuccessStory](https://github.com/Lacro59/playnite-successstory-plugin)
- [SuccessStory Fullscreen Helper](https://github.com/saVantCZ/SuccessStoryFullscreenHelper)

---

> **Note:** These bindings work only in **Fullscreen themes**.  
> Ensure the **Aniki Helper** plugin is installed and enabled for data to appear correctly.
