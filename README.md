<div align="center">

# ANIKI HELPER  
![Made for Playnite Fullscreen Themes](https://img.shields.io/badge/Made%20for-Playnite%20Fullscreen%20Themes-A600FF?style=for-the-badge)

</div>

**Aniki Helper** is a fullscreen companion plugin for Playnite themes.

It adds extra features that regular fullscreen themes cannot provide on their own, such as:

- library statistics
- monthly and yearly playtime tracking
- suggested games
- profile-style summary data
- dynamic color adaptation
- Steam update detection
- Steam player counts
- RSS / gaming news integration
- fullscreen startup / shutdown video support in compatible themes

Aniki Helper is mainly designed to **enhance supported fullscreen themes**.  
If your current theme supports it, the plugin can add dynamic content, profile information, and other visual improvements directly inside Playnite Fullscreen mode.

Some features are visible immediately in supported themes, while others can be enabled or adjusted in the plugin settings.

---

## Plugin Settings Overview

Aniki Helper includes three settings tabs:

- **General**
- **Steam Features**
- **Aniki News**

### General

#### Library stats
Controls how global library statistics are calculated.

- **Include hidden games in stats**  
  If enabled, hidden games are counted in totals such as total games, installed games, and similar library statistics.  
  If disabled, hidden games are ignored.

#### Monthly tracking
Used for the plugin’s monthly statistics.

- **Start fresh for this month**  
  Resets the current month tracking data.  
  Use this if your monthly numbers look wrong or if you simply want to restart the month’s tracking manually.

#### Dynamic colors
Used by compatible themes that support Aniki Helper’s dynamic color system.

- **Pre-cache colors in the background**  
  Prepares color data in advance to improve responsiveness when browsing games.  
  This can make theme color changes feel smoother, especially in larger libraries.

- **Reset color cache**  
  Clears the stored color cache so it can be recalculated.  
  Useful if colors look wrong after changing artwork or after major library edits.

#### Video
Used by compatible fullscreen themes only.

- **Enable startup intro video**  
  Plays a fullscreen intro video before showing Playnite.

- **Enable shutdown video**  
  Plays a fullscreen shutdown video before Playnite closes.

If your theme does not support these features, enabling them may have no visible effect.

---

### Steam Features

#### Updates for games
Adds Steam update / patch note support for Steam games.

- **Enable Steam update scanning for games**  
  Lets the plugin check whether your Steam games have received recent updates.

#### Games update cache
Improves update detection accuracy and prevents every game from appearing as “new” on first scan.

- **Scan library now**  
  Pre-fills the Steam update cache for your whole library.  
  This is especially useful the first time you enable the feature, or after clearing plugin data.

#### Current players for Steam games
Adds live Steam player count support for supported themes.

- **Enable Steam player count**  
  Displays the real-time player count for Steam games.  
  This may slightly increase loading time when switching games.

---

### Aniki News

Adds a news feed system that supported themes can display inside fullscreen views.

#### News
- **Enable RSS news scanning**  
  Enables automatic RSS news fetching.

#### Feed Settings
- You can enter your own RSS feed URL.
- If left empty, the plugin uses its default news source.
- If the custom feed is unsupported, the plugin will ignore it and continue using a compatible feed instead.

#### Clear news cache
- **Clear news cache**  
  Deletes the current cached news so the feed can be rebuilt from scratch.  
  Useful after changing RSS source or if old articles remain visible.

---

## Important Notes

- Aniki Helper is primarily useful with **compatible fullscreen themes**.
- Some advanced features are only available in themes that were specifically built to support them.
- If a setting seems to do nothing, it usually means your current theme does not use that feature yet.

---

## For Theme Creators

If you are a theme developer and want to integrate Aniki Helper into your own fullscreen theme, please check the full documentation here:

**[Theme Developer Wiki](https://github.com/Mike-Aniki/AnikiHelper/wiki)**

---

```markdown

❤️ Support & Community

Aniki Helper is a free plugin developed and maintained during my spare time.

If you enjoy using it and want to support its continued development, you can buy me a coffee on Ko-fi!

ANIKI ReMake Supporters Discord

Every Ko-fi supporter gets access to our private Discord community, whether through a one-time contribution or monthly support.

Follow the development of ANIKI ReMake and Aniki Helper, discover upcoming features, share feedback, and discuss improvements directly with the community.

Simply connect your Discord account through Ko-fi to claim your Supporter role and join!

Aniki Helper and ANIKI ReMake remain completely free for everyone. Supporting the projects is entirely optional. ❤️

```

<div align="center">

<a href="https://ko-fi.com/W7W1Y9DRB">
  <img height="36" src="https://storage.ko-fi.com/cdn/kofi5.png?v=3" alt="Support on Ko-fi">
</a>

</div>

---

> **Note:** Aniki Helper works in **Fullscreen themes only**.  
> Make sure the plugin is installed and enabled in Playnite.
