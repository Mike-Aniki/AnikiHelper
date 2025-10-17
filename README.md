# Aniki Helper

**Aniki Helper** is a Playnite plugin designed to enhance the **Fullscreen** experience by providing advanced library statistics and dynamic data that can be displayed directly in your custom themes.

Originally created for [Aniki Themes](https://github.com/Mike-Aniki), this plugin can also be integrated by **any theme creator** who wants to display player statistics, in their Fullscreen Themes.

## Features

**Global library statistics**
- Total and average playtime  
- Installed / Uninstalled / Hidden / Favorite games  
- Completion progress (New / Completed  / Playing)  
- Source breakdown (Steam, Epic, Xbox Game Pass, etc.)

**Top played games**
- Displays your most played games with total playtime and percentage  
- Dynamic updates based on library data

**Monthly summary**
- Total playtime this month  
- Number of games played this month  
- Most played game of the month  

**Recently played / added / never played**
- Lists of recently active, newly added, and untouched games

## Integration for Theme Creators

*How it works*

**Aniki Helper** reads your Playnite database and exposes a set of **bindable properties** accessible from your Fullscreen theme through the `PluginSettings` markup extension.

There are **two types of bindings** you can use:

- **Direct bindings** → simple values (numbers, strings, paths) that can be displayed in a `TextBlock` or `Image`.
- **Collection bindings** → lists of structured items that must be used inside an `ItemsControl` or `ListBox`.

Example of direct bindings:

```
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=TotalPlaytimeString}" />
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=ThisMonthTopGameName}" />
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=FavoriteCount}" />
```
Example of collection binding:
```
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

It can be combined with other plugins like MoData, ThemeOptions, or SuccessStoryFullscreenHelper to display complete dashboards and statistics panels inside your Fullscreen theme.

## Exposed Bindings

Below is the list of all **bindable properties** exposed by Aniki Helper, divided into two categories:

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

---

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

---

### UI triggers 
These properties are useful for animating or conditionally displaying elements in your theme.

| Property | Description |
| ---------------------------------------------- | ---------------------------------- |
| SessionHasNewAchievements | Boolean indicating whether new achievements were unlocked. Use this to show or hide the achievement line. |
| SessionNotificationFlip | Boolean that toggles every time a game session ends. Use two DataTriggers (True/False) to start or replay your Storyboard animation. |


## Recommended Plugins

- [ThemeOptions](https://github.com/ashpynov/ThemeOptions)
- [MoData](https://github.com/jonosellier/MoData)
- [SuccessStory](https://github.com/Lacro59/playnite-successstory-plugin)
- [SuccessStory Fullscreen Helper](https://github.com/saVantCZ/SuccessStoryFullscreenHelper)

---

> **Note:** These bindings work only in **Fullscreen themes**.  
> Ensure the **Aniki Helper** plugin is installed and enabled for data to appear correctly.
