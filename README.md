# Cycling Training Planner

A local desktop app for **Windows and macOS** (C#, .NET 8, Avalonia UI) that turns your race calendar,
weekly hours and FTP into a day-by-day structured plan, sends each workout to your Garmin Edge as a FIT file
(the Edge runs it in ERG on the trainer) or to Zwift as a `.zwo` file, and imports your rides to track
fitness and adapt the plan.

- **Local only.** One SQLite file in your user data folder, no accounts, no cloud.
- **Rules, not AI.** The plan engine is deterministic and unit-tested; it works with no network.
- **Garmin does the riding.** The app never talks to the trainer. Workouts go to the Edge over USB.

## Download

Every push builds ready-to-run apps on GitHub: open the repository's **Actions** tab, pick the latest
green **build** run and download from **Artifacts**:

| Artifact | For |
| --- | --- |
| `Trainer-windows-x64` | Windows 10/11: unzip and run `Trainer.exe` |
| `Trainer-mac-apple-silicon` | Mac with an M1/M2/M3/M4 chip (most Mac minis since 2020) |
| `Trainer-mac-intel` | older Intel Macs |

Nothing else needs installing: .NET is built in.

### Mac: first launch

1. Download `Trainer-mac-apple-silicon`, double-click the zip in Downloads (it may unzip twice), and drag
   **Trainer.app** into **Applications**.
2. The app isn't notarised by Apple, so the first time **right-click Trainer.app → Open → Open**.
   If macOS still says it "is damaged" or "can't be opened", open Terminal and run
   `xattr -dr com.apple.quarantine /Applications/Trainer.app`, then open it again.
3. After that it opens normally from Launchpad or the Dock.

### Windows: first launch

Unzip, run `Trainer.exe`; if SmartScreen appears, click **More info → Run anyway**.

## Build from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows or macOS). Then:

```bash
dotnet test tests/Trainer.Tests                # engine, maths, database, FIT and ZWO tests
dotnet run --project src/Trainer.Desktop       # start the app
```

On Windows you can also open `Trainer.sln` in Visual Studio 2022 and press F5 (set **Trainer.Desktop**
as the startup project); on a Mac, JetBrains Rider or VS Code work.

Packaging:

- **Mac:** `./build/package-mac.sh` (Apple Silicon) or `./build/package-mac.sh osx-x64` (Intel) makes
  `publish/mac/Trainer.app`, signed ad hoc so it runs on your own Mac.
- **Windows:** `powershell -ExecutionPolicy Bypass -File build\publish.ps1` makes a single
  `publish\Trainer.exe`; compile `build\Trainer.iss` with [Inno Setup](https://jrsoftware.org/isinfo.php)
  for an installer.

## First run

The app opens on **Settings**:

1. Set weekly hours, training days, long-ride day, level and the no-race goal (FTP or fitness).
2. Enter an FTP you know. If you don't know it, type a rough guess and choose **Start with a ramp test**.
   The plan opens with a ramp test, and when you import that ride the app sets your FTP to 75 % of your
   best minute.
3. Add your races under **Races**. The calendar now shows the plan.

## Using it

| Screen | What you do there |
| --- | --- |
| Calendar | Month or week view of workouts, races, blocked days and rides, with each week's prescribed and done hours/TSS on the right. Drag a workout to another day, or right-click it to **Move to** a day, **Remove** it or **Restore** it. Right-click a day to block it. Double-click to open. Green is done, amber is partial, red is missed. **Send week to Edge** exports the next 7 days. |
| Workout detail | Interval graph in watts and % FTP (hover for detail), TSS, IF, the step list. Toggle indoor or outdoor, lock, change the length, swap for another workout, export to the Edge or save the FIT file. |
| Races | Add or edit races: date, type, duration, intensity 1–10, priority A/B/C. An A race less than 3 months from another is refused. |
| Plan overview | Phase bands (base, build, specialty, taper), weekly target hours and TSS, planned CTL against actual CTL. |
| Activities | Imported rides. Import from the Edge, from FIT files or from Strava. Link a ride to a planned workout by hand. |
| Analysis | PMC (CTL, ATL, TSB) with races marked, power curve for the last 6 weeks against all time, weekly compliance, FTP estimate from rides. |
| Settings | Athlete, FTP history, Edge and watched folder, Strava, workout library editor, backup and restore. |

### Light and dark mode

The ☀/☾ button at the bottom of the sidebar switches between light and dark. The choice is remembered
(`ui-settings.json` in the data folder); until you pick one, the app follows your Mac or Windows setting.

### Garmin Edge

- **Workouts → Edge:** the app writes FIT workout files with power targets in watts into
  `Garmin\NewFiles`. Unplug the Edge; it imports them (Training → Workouts) and runs them in ERG.
- **Rides → app:** **Import from Edge** copies new files from `Garmin\Activity`.
- Newer Edges connect as **MTP** devices rather than drives. On **Windows** the app finds both (MTP through
  the MediaDevices package).
- On a **Mac**, Edges that show up in Finder as a drive (under `/Volumes`) work directly. macOS can't open
  MTP devices, so for newer Edges: use **Save FIT file…** or **Send week to Edge** (which then offers to
  save the files to a folder) and copy them into `Garmin/NewFiles` with a free tool such as
  [OpenMTP](https://openmtp.ganeshrvel.com/); for rides, download them from Garmin Connect into the
  watched folder.
- If the Edge can't be reached, the app saves the FIT files to a folder so you can copy them by hand.
- **Watched folder:** point it at the folder where you download FIT files from Garmin Connect. New files
  are imported automatically while the app is open.

### Zwift (.zwo files)

Any workout can also be saved as a Zwift `.zwo` file, the format used by Zwift, MyWhoosh, TrainerRoad,
TrainingPeaks Virtual and [zwofactory.com](https://zwofactory.com/templates/). Power stays as % FTP, so
the riding app uses its own FTP setting.

- **Workout detail → Save ZWO file…** for one workout.
- **Calendar → Week to ZWO…** for the next 7 days.
- **Settings → Workout library → Export ZWO…** for a library workout, including your own.

If Zwift is installed, the save dialog opens in `Documents\Zwift\Workouts\<your Zwift id>`. Restart Zwift
and the workouts appear under **Custom Workouts**. Two-step repeats become Zwift interval blocks, ERG-off
steps become free ride, and step labels show as on-screen messages.

### Strava

Create your own API application at <https://www.strava.com/settings/api> with **Authorization Callback
Domain** set to `localhost`. Paste its Client ID and Client Secret into Settings, then **Connect**. Your
browser opens Strava's consent page; the app listens on `http://localhost:8723/` for the reply.
**Sync Strava** then pulls power, heart rate and cadence streams for rides that aren't already imported.
The same ride from the Edge and from Strava is stored once (start time within 2 minutes), and the FIT
file wins.

### Your data

Everything is in one folder: `trainer.db` and the `fit` folder of imported rides.

- Windows: `%LOCALAPPDATA%\Trainer`
- macOS: `~/Library/Application Support/Trainer`

To move from one computer to another, use **Back up…** on one and **Restore…** on the other.
Settings → Data has **Back up…** (zip) and **Restore…**.

## How the plan is built

The engine (`src/Trainer.Core/Planning/PlanEngine.cs`) works back from each A race:

| Phase | Length | Focus |
| --- | --- | --- |
| Base | what remains, at most 12 weeks | endurance, tempo; sweet spot when the goal is FTP |
| Build | 8 weeks | threshold, over-unders, first VO2 work |
| Specialty | 6 weeks (4 if time is short) | sessions for the event type; long ride sized to the race |
| Taper | 1 week (2 if the race is over 4 h) | volume down 40–50 %, short openers |

- Load and recovery weeks: 3 + 1 (basic level: 2 + 1); recovery weeks run at about 60 % of your hours.
  Cycles are counted back from the taper, so recovery weeks don't move when the plan is regenerated.
- Between two A races: a recovery week, then build, specialty and taper. Base is skipped if the races are
  less than 16 weeks apart.
- B race: 3-day mini taper, openers the day before, recovery ride the day after; it replaces a key workout.
  C race: no taper; it counts as that week's hardest key workout.
- With no race on the calendar: rolling 8-week blocks (3 + 1 twice), always at least 8 weeks ahead.
  FTP goal: sweet spot, then threshold, then VO2. Fitness goal: endurance, tempo, sweet spot.
- Splitting a week: the long ride (30–35 % of the hours) goes on your long-ride day; key sessions (2 at
  4–5 days, up to 3 at 6+ days for advanced and pro) are never on consecutive days and avoid the day
  next to the long ride where possible; endurance rides fill the rest. Blocked days are skipped.
- Race intensity sets how hard the work is: 1–3 means one key session a week, 4–7 two, 8–10 two or three
  with VO2 and anaerobic work.
- Level sets the CTL ramp cap (+3/+5/+6/+8 a week) and the maximum time at threshold and VO2 per session.
- A ramp test goes on the first day after each recovery week, at least 24 days apart and never within 10
  days of an A race.

**Adaptation.** The plan is regenerated from today whenever a race, blocked day, workout position, FTP,
hours or days change, and after every import. Past workouts and anything you edited by hand (locked)
are frozen. A ride on the planned day (±1 day) is linked to it: 80 %+ of the planned TSS is done,
50–80 % partial, under 50 % or nothing by midnight is missed. A missed key session moves to the next
free, non-adjacent day that week, or is dropped. A bad week (3+ missed, or under 60 % of planned TSS)
makes the next week repeat its load. If TSB stays below −30 for 3 days, the next key session becomes an
endurance ride. Old plan versions are kept (the last 20).

**Moving and removing workouts.** A moved workout is locked on its new day. If it moves to another week,
the week it left isn't back-filled (its day stays free) and the week it lands in trims its other rides to
stay within its hours. A removed workout leaves a rest day; the session isn't re-planned elsewhere and the
other sessions keep their type and length. If a removed workout was a key session, or 40 %+ of the week's
target hours were removed, the next week repeats that week's load instead of stepping up. Removed
workouts stay visible (faded) and can be restored.

**Maths.** NP is the fourth root of the mean of the 30-second rolling power to the fourth power;
IF = NP / FTP; TSS = t · NP · IF / (FTP · 3600) · 100. CTL and ATL are 42- and 7-day exponentially
weighted TSS; TSB is yesterday's CTL minus yesterday's ATL. Rides with heart rate but no power use
hrTSS from your threshold heart rate. Workouts are stored as % FTP; watts are worked out at export from
the FTP on the workout's date, so a new FTP updates every future workout.

## Workout library

There are 60 built-in workouts, written in a short interval notation that the library editor also uses:

```
wu 12m 50-70; 3x(10m 95-100 "threshold", 5m 55); fill 20m 65; cd 8m 50
```

Steps are `[wu|cd|rec|fill|free] duration power% ["label"]`. `fill` is endurance that stretches to the
planned length, and `free` turns ERG off (for sprints and race simulations). Repeats can be nested. Your
own workouts are picked by the plan engine like the built-in ones.

## Solution layout

| Project | Holds |
| --- | --- |
| `Trainer.Core` | Models, zones, NP/TSS/PMC maths, workout library, plan engine, adaptation rules. No UI, no I/O, so a future Android client can reuse it. |
| `Trainer.Data` | SQLite through EF Core (10 tables, migrations), and `TrainerService`, which every screen uses. |
| `Trainer.Integrations` | FIT workout writer and activity reader (Garmin FIT SDK), Edge drive and MTP access, watched folder, Strava client. |
| `Trainer.Desktop` | Avalonia UI screens (Windows, macOS, Linux), MVVM view-models (CommunityToolkit.Mvvm), ScottPlot charts. |
| `Trainer.Tests` | xUnit: engine on fixed calendars (one A race, two A races, A + B + C, no race), maths, compliance, database, FIT round-trips, Edge folder sync. |
| `assets/` | App icon: `icon.svg` is the source; `icon.ico` (Windows), `icon.icns` (macOS), `icon.png` and `strava-icon.png` (for the Strava API app) are exported from it. |

## Status

Everything on the v1 list is built, and the core is covered by 104 tests. These items still need checking on
real hardware or accounts:

- **Milestone 2:** copying to your exact Edge model over MTP, and riding an exported workout in ERG. The
  FIT files decode correctly with the Garmin SDK, and drive-letter copying is tested.
- **Milestone 8:** a Strava sync with a real API application. Stream parsing is tested; the OAuth flow
  has only been built, not run.
- **The app on a real Mac and Windows PC.** Every screen has been rendered and checked with seeded data
  (headless, with the same Skia renderer the app uses), and the macOS and Windows packages build, but
  nobody has clicked through them on a real machine yet.

Open decisions from the build plan: which Edge model, which race is the first A race, Saturday or Sunday
for the long ride (it's a setting), an optional AI step to explain plans, and English or Latvian for the UI.
