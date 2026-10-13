# Office Days

Office Days marks a day as an office day when this laptop connects to your office Wi-Fi, then shows how that adds up for the week and the quarter.

It runs in the notification area. After today is logged it stops checking Wi-Fi until midnight. Nothing leaves the PC.

## What it does

- Marks today when the laptop joins the office network you chose. The first time you open it, you pick that network. Nothing is selected for you.
- Records the arrival time only.
- Shows **this quarter** as a large count, with **this week** underneath, for example `2 of 3 this week · 1 day left this week`.
- Puts the week on the tray icon as `2/3`, so you can see it without opening the window.
- Lists every office day, and shows the same days on a month calendar.
- Builds a quarterly report: office days, working days, attendance, and a monthly and weekly breakdown. You can save that report as a web page.
- Imports older office days from the Windows Wi-Fi history, which usually goes back a few weeks.
- Exports the day list to CSV.

There is no button to add or remove a day. A day is recorded only when the laptop is on the office Wi-Fi, or when you import history.

## The 39-day quarter

A quarter’s target is **39 office days**, which is **3 days a week**.

| Situation | What you see |
| --- | --- |
| Still short of 3 this week, and the week can still be finished | `1 day left this week` under the counter |
| This week reaches 3, and a weekday is still left | The counter says you can skip the rest, and you get one notice |
| Friday 4:00 PM, and the week is still under 3 | One notice that this week is not complete |
| Staying at 3 days a week would finish the quarter under 39 | A Monday notice that you may not reach 39 |
| The quarter reaches 39 | One notice that you can skip the office |

Once the quarter is at 39, the weekly “not complete” and “skip the rest of this week” notices stay quiet. The quarter message covers that.

Working days are Monday to Friday. Public holidays and leave are not removed, so the attendance percentage is slightly conservative.

Quarters are calendar quarters by default (Q1 is January–March). In Settings you can start the year in April for India’s financial year.

## Install

Double-click `install.cmd`, or run it from this folder. No administrator account is needed.

It builds `OfficeDays.exe`, puts it in `%LOCALAPPDATA%\Programs\OfficeAttendance`, adds a Start menu shortcut named **Office Days**, and starts the app. If sign-in startup was already turned on, that entry is updated to the new name.

`uninstall.cmd` removes the app and the startup entry. It leaves your history in place.

## Where your days are saved

```
%APPDATA%\OfficeAttendance\attendance.csv
```

Settings are in `settings.ini` in that same folder. You can open the CSV in Excel. The link in Settings opens the folder.

## Build

`build.cmd` compiles with the C# compiler that ships with Windows (.NET Framework 4.x). The default output is `bin\OfficeDays.exe`.

The app is a single executable. It does not need a separate installer or a NuGet restore.

## What it does not do

- It does not mark a day when you are only on Ethernet and Wi-Fi never connects.
- It does not know public holidays or leave.
- A short connection to the office network still counts as a full day.
- Windows only keeps a few weeks of Wi-Fi history, so earlier days cannot be imported.
