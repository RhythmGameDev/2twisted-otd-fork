OpenTabletDriver 0.6.8.1, 2Twisted fork (0.6.8.1-2twisted.1)
=============================================================

OpenTabletDriver with one change: two (or more) tablets of the SAME model work at the same time, each as its
own tablet with its own areas and output mode. Stock OpenTabletDriver uses only the first one.
The first tablet keeps its usual name (for example "Gaomon S56K"); the second is "Gaomon S56K (2)", and so on.
The same tablets in the same USB ports keep the same names.

Needs the .NET 10 Desktop Runtime (x64), the same as official OpenTabletDriver 0.6.8.1:
https://dotnet.microsoft.com/download/dotnet/10.0

PORTABLE: the "userdata" folder next to the programs holds this copy's settings and plugins, so it never
changes the settings of an OpenTabletDriver you already have installed.

To use it
1. Close any other OpenTabletDriver first (its window, and the tray icon or daemon). Only one can run at a time.
2. Unzip this folder anywhere and run OpenTabletDriver.UX.Wpf.exe.
3. Plug in both tablets. The tablet list at the bottom left shows each one.
4. For two cursors in 2Twisted: install the 2Twisted plugin (Plugins > Plugin Manager, drop TwoTwistedPen.zip
   on it, from https://github.com/RhythmGameDev/2twisted-otd-plugin/releases/latest), restart the driver, then
   set each tablet's Output Mode to "2Twisted (one cursor per tablet)".

To go back, close this one and start your usual OpenTabletDriver again.

Tested with two emulated identical tablets through this driver and the 2Twisted plugin into the game.
Not yet tried with two real identical tablets: if something is wrong, send the daemon log from
userdata\Logs (or the Console tab of the window).

What changed is described in FORK.md. OpenTabletDriver is LGPL-3.0 (LICENSE); this fork's source is the
"identical-tablets" branch of the 2Twisted OpenTabletDriver fork.
