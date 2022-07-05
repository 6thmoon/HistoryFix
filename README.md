If you've spent any significant amount of time on the run history page of the *Logbook*, then you've probably noticed a large number of missing entries. The current implementation limits the number of run reports to thirty. However, once you exceed this threshold, the game will select a completely arbitrary entry for deletion.

This plugin simply modifies the behavior to delete the oldest run report instead. A configuration file (`BepInEx/config/local.fix.history.cfg`) is also provided to allow the history limit to be increased or decreased as desired. By default, the maximum number of entries will be doubled.

Additionally, a fix is included for an unrelated issue where **Eclipse** victories did not count towards character wins on the statistics page of the *Logbook*. Unfortunately, this does not apply retroactively - there is no reliable way to recover information lost as a result of either of these problems.

## Version History

#### `0.2.0`
- Victories in **Eclipse** (from the *Alternate Game Modes* menu) will now be recorded for profile statistics.

#### `0.1.0` ***- Initial Release***
- Prevent arbitrary deletion of run history.
