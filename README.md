# Racing Audio Spotter

Racing Audio Spotter tells you when another car is alongside you in a racing simulator. The goal
is to play a warning in your left or right headphone, on the side the car is, so you can hear
where the car is without looking at a mirror or overlay.

## What it does today

This is the first milestone. It is a small Windows console app that:

- **Waits for iRacing.** You can start it before or after the sim. It connects as soon as live
  telemetry is available.
- **Reads iRacing's spotter data**, the `CarLeftRight` telemetry variable, about 60 times per
  second.
- **Prints a line only when the situation changes**, for example `CLEAR`, `LEFT`, `RIGHT`,
  `BOTH SIDES`, `MULTIPLE CARS LEFT` or `MULTIPLE CARS RIGHT`. It does not repeat the same
  state.
- **Never shows old data as current.** If telemetry stops or iRacing closes, it reports
  `Disconnected` and waits for iRacing to come back. `CLEAR` is only shown when iRacing actually
  reports no cars alongside.
- **Recovers from errors and exits cleanly** when you press Ctrl+C.

It does **not** play audio yet, and it has no graphical interface. iRacing is the only supported
simulator. The code keeps iRacing-specific logic separate from the rest of the app, so other
simulators (ACC, LMU and others) can be added later without rewriting the core.

> **Status:** The app builds and its automated tests pass. It has not yet been tested in a live
> iRacing session. See [Manual tests](#manual-requires-iracing--not-yet-performed) below.

## Requirements

- Windows 10/11 (the iRacing SDK uses Windows shared memory).
- .NET 10 SDK (`dotnet --list-sdks` should show a `10.0.x` entry). Install it with
  `winget install Microsoft.DotNet.SDK.10` or from https://dotnet.microsoft.com/download.
- iRacing, for manual testing only. iRacing's telemetry shared memory must be enabled
  (`irsdkEnableMem=1` in `Documents\iRacing\app.ini`, which is iRacing's default).

## Commands

Run from the repository root:

```powershell
dotnet restore                                   # restore NuGet packages
dotnet build                                     # build app and tests
dotnet test                                      # run automated tests (iRacing not needed)
dotnet run --project src/RacingAudioSpotter      # launch the app
```

Press **Ctrl+C** to exit cleanly.

## Example output

```
Racing Audio Spotter
====================
Press Ctrl+C to exit.

Telemetry: Waiting for iRacing...
Telemetry: iRacing is running, waiting for live telemetry...
Telemetry: Connected
Car proximity: UNAVAILABLE (simulator spotter inactive, e.g. not on track)
Car proximity: CLEAR
Car proximity: LEFT
Car proximity: CLEAR
Telemetry: Disconnected
Telemetry: Waiting for iRacing...
```

Lines are printed only when something changes. A car proximity state is shown only while telemetry
is live, so `CLEAR` always means "iRacing reports nobody alongside". It never means "no data".

## Project layout

```
src/RacingAudioSpotter/
    Program.cs                          startup, Ctrl+C handling, fatal error reporting
    Application/TelemetryMonitor.cs     polls a provider and prints changes (simulator-independent)
    Telemetry/
        ITelemetryProvider.cs           abstraction the rest of the app depends on
        TelemetrySnapshot.cs            connection state + proximity state
        CarProximityState.cs            normalized, simulator-independent proximity states
        IRacingTelemetryAdapter.cs      iRacing implementation (the only code using the SDK lifecycle)
        IRacingCarLeftRightMapper.cs    iRacing CarLeftRight -> CarProximityState
tests/RacingAudioSpotter.Tests/         xUnit tests (no iRacing required)
```

To add another simulator later, write a new `ITelemetryProvider` implementation and choose it in
`Program.cs`. `TelemetryMonitor` and any future audio code do not change.

## How it works

### iRacing SDK library

The app uses [IRSDKSharper](https://github.com/mherbold/IRSDKSharper) 1.3.0 (NuGet `IRSDKSharper`).
It was chosen because:

- It explicitly handles `CarLeftRight`. iRacing declares that variable as a bitfield, and the
  library corrects it to `Int`. It also ships a matching enum,
  `IRacingSdkEnum.CarLeftRight { Off, Clear, CarLeft, CarRight, CarLeftRight, TwoCarsLeft, TwoCarsRight }`
  with values 0 to 6. These were verified by reflection against the 1.3.0 package.
- It has explicit connect, disconnect and exception events, plus a built-in reconnect loop.
- It ships a `net9.0-windows` build that runs on .NET 10, and it is actively maintained.

**Licence note:** IRSDKSharper is **GPL-3.0**. If you distribute this application, the GPL applies to
it. If that is a problem, the SDK is isolated in `IRacingTelemetryAdapter` and can be replaced, for
example with the Apache-2.0 SVappsLAB iRacingTelemetrySDK. Before switching, confirm that it exposes
`CarLeftRight`, which has not yet been checked.

### Connection lifecycle

| State | How it is detected | Output |
|---|---|---|
| iRacing not running | no live data, and no `iRacingSim64DX11` process | `Waiting for iRacing...` |
| iRacing running, no live session | no live data, but the sim process exists | `iRacing is running, waiting for live telemetry...` |
| Connected | at least one telemetry frame received | `Connected` + proximity |
| Stale | connected, but no frame for more than 1 s | `Data stopped updating` (proximity hidden) |
| Disconnected | SDK `OnDisconnected` (no data for 3 s) | `Disconnected`, then back to waiting |
| Shutting down | Ctrl+C | `Shutting down...`, SDK stopped, `Stopped.` |

- IRSDKSharper retries opening iRacing's shared memory every 500 ms on background threads, so the
  app never busy-waits. The console polls the adapter every 50 ms.
- If the SDK raises `OnException`, its thread has stopped. The adapter prints a diagnostic, calls
  `Stop()`, and restarts the SDK after 5 seconds.
- If `CarLeftRight` is missing or has an unexpected type, a diagnostic is printed once per
  connection and the proximity shows as `NOT SUPPORTED`.

### CarLeftRight mapping

| iRacing (`irsdk_CarLeftRight`) | Value | Normalized `CarProximityState` | Printed |
|---|---|---|---|
| `LROff` | 0 | `Unavailable` | UNAVAILABLE |
| `LRClear` | 1 | `Clear` | CLEAR |
| `LRCarLeft` | 2 | `Left` | LEFT |
| `LRCarRight` | 3 | `Right` | RIGHT |
| `LRCarLeftRight` | 4 | `BothSides` | BOTH SIDES |
| `LR2CarsLeft` | 5 | `MultipleLeft` | MULTIPLE CARS LEFT |
| `LR2CarsRight` | 6 | `MultipleRight` | MULTIPLE CARS RIGHT |
| anything else | – | `Unknown` | UNKNOWN (raw value N) |

## Testing

### Automated (`dotnet test`)

24 xUnit tests cover:

- every CarLeftRight value
- unknown values
- that every SDK enum value is mapped
- that repeated states are suppressed
- unavailable versus clear
- the waiting, connected, stale and disconnected transitions
- reconnecting
- the monitor's run, diagnostics and cancellation loop

None of them need iRacing. The `IRacingTelemetryAdapter` itself is **not** unit-tested, because it
needs iRacing's shared memory.

### Manual (requires iRacing) — not yet performed

1. Start the app before iRacing. Expect `Waiting for iRacing...`.
2. Launch a session, for example a Test Drive. Expect `iRacing is running, waiting...`, then
   `Connected`.
3. In the garage or a replay, expect `UNAVAILABLE`. On track alone, expect `CLEAR`.
4. Drive alongside AI cars (an AI race or practice) and confirm that `LEFT`, `RIGHT`, `BOTH SIDES`
   and, if possible, `MULTIPLE CARS ...` match iRacing's own spotter.
5. Confirm that a steady state prints only once.
6. Exit the session or close iRacing. Expect `Disconnected` and then a waiting message. Rejoin,
   and expect it to reconnect.
7. Press Ctrl+C at each stage. Expect `Shutting down...` and `Stopped.` with no error.

## Limitations

- It has not yet been tested against a running iRacing session.
- "iRacing running but no session" depends on the simulator process name `iRacingSim64DX11`. If
  iRacing renames the process, the app still works, but it shows `Waiting for iRacing...` until
  telemetry arrives.
- iRacing only reports up to "two cars" per side. The normalized `MultipleLeft` and `MultipleRight`
  states cover that.
- There is a diagnostic channel, but no separate verbose mode that prints every frame.
- Only one simulator (iRacing) is supported.
