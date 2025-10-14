# Aniki Helper

**Aniki Helper** is a Playnite plugin designed to enhance the **Fullscreen** experience by providing advanced library statistics and dynamic data that can be displayed directly in your custom themes.

Originally created for [Aniki Themes](https://github.com/Mike-Aniki), this plugin can also be integrated by **any theme creator** who wants to display player statistics, in their Fullscreen Themes.

---

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

**Storage overview (via MoData)**
- Disk usage and available space for all drives  
- Color-coded bars with used percentage indicators  

---

## How it works

Aniki Helper reads your Playnite database and exposes **bindable properties** you can use in XAML via the `PluginSettings` markup extension:

```
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=TotalPlaytimeString}" />
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=ThisMonthTopGameName}" />
<TextBlock Text="{PluginSettings Plugin=AnikiHelper, Path=FavoriteCount}" />
```
It can be combined with other plugins (like MoData, ThemeOptions, or SuccessStoryFullscreenHelper) to display complete dashboards inside your Fullscreen theme.

## Integration for Theme Creators

To integrate Aniki Helper into your Fullscreen theme:
Bind the exposed properties using PluginSettings (see examples above).
You can use it to build :

- Statistics panels
- Player dashboards
- Library insights
- Monthly summaries

Example panel:
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

## Exposed Bindings

| Property                                       | Description                        |
| ---------------------------------------------- | ---------------------------------- |
| `TotalCount`                                   | Total number of games              |
| `InstalledCount`, `NotInstalledCount`          | Installed / Uninstalled games      |
| `TotalPlaytimeString`, `AveragePlaytimeString` | Global and average playtime        |
| `TopPlayed`                                    | List of most played games          |
| `CompletionStates`                             | Dictionary of completion stats     |
| `GameProviders`                                | Breakdown by source/platform       |
| `ThisMonthPlayedTotalString`                   | Total playtime this month          |
| `ThisMonthPlayedCount`                         | Number of games played this month  |
| `ThisMonthTopGameName`                         | Most played game name this month   |
| `ThisMonthTopGamePlaytime`                     | Time spent on that game this month |



