After three years, five months, and two days, this issue has finally been fixed. Keeping this up since it still addresses other bugs and contains additional features. It may also be useful for people that wish to [revert to the previous version](https://github.com/risk-of-thunder/RoR2VersionSelector), since the accompanying update has been received rather poorly.

## Introduction

If you've spent any significant amount of time on the run history page of the *Logbook*, then you probably have noticed a large number of missing entries. The current implementation limits the total number of run reports to thirty. However, once you exceed this threshold, the game will select a completely arbitrary entry for deletion. This plugin simply modifies the behavior to delete the oldest run instead.

A configuration file (`BepInEx/config/local.fix.history.cfg`) is provided to allow the history limit to be increased or decreased as desired. By default, ten pages of twelve reports each will be displayed on most resolutions. Furthermore, information shown in the mouse-over tooltip has been expanded and entries can now be removed via the user interface.

Additionally, a fix is included for an unrelated issue where **Eclipse** did not count towards certain achievements or victory statistics. The inventory display in run history and end-of-game reports will now also show the last **Equipment** held by the player. You can also enable an experimental feature to back up save data monthly in the directory below.

## Known Issues

- Unfortunately, there is no reliable way to restore missing run reports or **Eclipse** wins; these fixes do not apply retroactively. Using a file recovery tool to inspect `steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/RunReports` or manually editing your profile in `userdata/*/632360/remote/UserProfiles` may be an option if this is important to you.
- Note that history limit is only enforced upon completing a run, as per usual.

Please report feedback or issues discovered [here](https://github.com/6thmoon/HistoryFix/issues). Any suggestions regarding related problems to fix with the game are welcome, as I may not have experienced them. Feel free to check out my other released [content](https://thunderstore.io/package/6thmoon/?ordering=top-rated) as well.

## Version History

#### `1.0.0`
- Compatibility with *Seekers of the Storm* DLC update.

#### `0.5.0`
- Add option for profile backup.
- Fix equipment display for non-host players.

#### `0.4.3`
- Improve code/compatibility.

#### `0.4.2`
- Update icon.
- Reset cached entries upon exiting logbook.

#### `0.4.1`
- Allow certain achievements/unlocks to be granted in **Eclipse** mode.

#### `0.4.0`
- Add configuration option for unlimited entries. Increase default value.
- Expand upon information displayed in the history entry tooltip and provide interface for manual deletion.
- Preserve run history when participating in **Prismatic Trials** or otherwise playing unmodded.

#### `0.3.0`
- Show equipment in run history and end-of-game reports. Publish source code and update documentation.

#### `0.2.0`
- Victories in **Eclipse** (from the *Alternate Game Modes* menu) will now be recorded for profile statistics.

#### `0.1.0` ***- Initial Release***
