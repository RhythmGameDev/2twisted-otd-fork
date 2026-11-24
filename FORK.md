# 2Twisted fork of OpenTabletDriver

OpenTabletDriver 0.6.8.1 with one change: **two (or more) tablets of the same model work at the same time**, each
as its own tablet with its own profile, areas and output mode. Stock 0.6 uses only the first device that matches a
tablet configuration and ignores the rest (upstream issue #1666).

Made for [2Twisted](https://github.com/RhythmGameDev/2twisted-otd-plugin), a rhythm game played with one pen per hand.
With the 2Twisted output mode each tablet drives its own cursor in the game. Nothing here is specific to the game:
any output mode sees each tablet separately.

## What changed

`OpenTabletDriver/Driver.cs`, `MatchAll` (was `Match`):

- Every physical device that matches a configuration becomes a tablet. Endpoints are grouped per physical device
  with HidSharp's `IsSibling` (as the 0.7 branch does), so a tablet's pen and express-key interfaces stay together.
- The first tablet keeps the configuration's name, so one tablet, and its saved profile, behave exactly as before.
  The others are named `Name (2)`, `Name (3)`, ... Profiles are kept by name, so each gets its own.
- They are ordered by device path, which follows the USB port: the same tablets in the same ports keep their names.
- For endpoint types that cannot tell siblings apart (WinUSB, plugin device hubs) only the same endpoint counts, as
  in 0.7. A single tablet still gets its auxiliary device as before.

`IDeviceEndpoint` (in the plugin library) is unchanged, so plugins built for 0.6 keep working.

## Tests

- `OpenTabletDriver.Tests/IdenticalTabletsTest.cs`: grouping, the sibling rule, renaming.
- End to end on Windows with two emulated identical tablets (two Gaomon S56K) through this daemon and the 2Twisted
  plugin into the game: two tablets detected, one profile each, two cursors moving and clicking independently. See
  `test/` in the 2twisted-otd-plugin repo (`FAKE_TABLETS_IDENTICAL=1`).
- Not yet tried on two real identical tablets, nor on macOS or Linux.

The 12 `HidSharpEndpointTest ..._OnLinux` tests fail on Windows on unmodified upstream 0.6.8.1 too.
