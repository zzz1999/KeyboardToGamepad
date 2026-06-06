# KeyboardToGamepad

[![build](https://github.com/zzz1999/KeyboardToGamepad/actions/workflows/build.yml/badge.svg)](https://github.com/zzz1999/KeyboardToGamepad/actions/workflows/build.yml)

Turn a region of your keyboard into a **virtual game controller** so a second person can join
local co-op games (like **Cuphead**) on the same PC — no extra hardware, no paid software.

The game sees a real controller and assigns it to **Player 2**, while Player 1 keeps using the
keyboard normally. It's a small, free, open-source take on what reWASD does for this one use case.

```
[keyboard] --(Interception driver)--> [KeyboardToGamepad] --(ViGEm)--> [virtual Xbox360 / DS4 pad] --> game = Player 2
```

## Download

Grab the latest **`KeyboardToGamepad-win-x64.zip`** from the
[Releases](https://github.com/zzz1999/KeyboardToGamepad/releases) page (built automatically by
GitHub Actions). It contains a self-contained `KeyboardToGamepad.exe` — **no .NET install
needed**. Unzip and run the exe: the **two required drivers are bundled inside it**, and the
first run offers to install whichever is missing (a Windows admin prompt appears — click Yes).
Installing the Interception driver needs **one reboot**; run the exe again afterwards. Prefer
building from source, or want to install the drivers manually? See **Setup** below.

## Why a driver?

Many games (Cuphead uses Rewired) read the keyboard via **Raw Input**, which a normal
low-level keyboard hook cannot block. KeyboardToGamepad therefore captures keys with the **Interception**
filter driver (which sits *below* Raw Input): Player 2's keys are consumed before the game sees
them, so they only drive the virtual pad and never disturb Player 1.

## Requirements

- Windows x64, .NET 8 SDK (to build).
- **ViGEmBus** driver — creates the virtual controller — https://github.com/nefarius/ViGEmBus/releases
- **Interception** driver + `interception.dll` — captures the keyboard — https://github.com/oblitum/Interception

## Setup

1. Install **ViGEmBus** (run its installer).
2. Get **Interception**:
   ```powershell
   ./scripts/get-interception.ps1                 # downloads interception.dll into native\
   ./native/install-interception.exe /install     # run as Administrator, then REBOOT
   ```
3. Build & run:
   ```powershell
   dotnet build -c Release
   ./bin/Release/net8.0-windows/KeyboardToGamepad.exe
   ```

Keep the window open while playing. Press **F9** (or Ctrl+C) to quit — this also removes the
virtual controller.

## Using it with Cuphead

1. Start KeyboardToGamepad, then launch Cuphead.
2. Player 1 plays on the keyboard as usual.
3. On the world map, Player 2 presses their jump key (`J` in the default layout) — Mugman drops in.
4. Because KeyboardToGamepad consumes P2's keys, they don't affect Player 1.

## The TUI

With `"ui": "dashboard"` KeyboardToGamepad shows a live, three-section terminal UI:

```
╭─KeyboardToGamepad──────────────────────────────────────────────────────────────────────────────╮
│ Player 2 (KeyboardToGamepad)  (virtual Xbox 360 pad = Player 2)                                │
│ ViGEm ●   Interception ●   block: ON   ● live                                                  │
│                                                                                                │
│              ╭────────╮                 ╭────────╮               ╭───┬─────┬───────┬─────────╮ │
│             ╱   LB     ╰───────────────╯     RB   ╲              │   │ KEY │ PAD   │ PRESSED │ │
│           ╱              Bk   (*)   St              ╲            ├───┼─────┼───────┼─────────┤ │
│         ╱                                     Y       ╲          │ › │ W   │ Up    │ ·       │ │
│        ╱      ( o )                         X   B      ╲         │   │ A   │ Left  │ ·       │ │
│       │                   ^                   A         │        │   │ S   │ Down  │ ·       │ │
│       │                 < + >     ( o )                 │        │   │ D   │ Right │ ·       │ │
│       │                   v                             │        │   │ H   │ X     │ ·       │ │
│       ╲                 ╱─────────────╲                 ╱        │   │ J   │ A     │ ·       │ │
│      │                ╱                 ╲                │       │   │ K   │ B     │ ·       │ │
│     │               ╱                     ╲               │      │   │ L   │ Y     │ ·       │ │
│      ╲            ╱                         ╲            ╱       │   │ Y   │ LB    │ ·       │ │
│        ╲         ╱                           ╲         ╱         │   │ U   │ LT    │ ·       │ │
│          ╰──────╯                             ╰──────╯           │   │ I   │ RT    │ ·       │ │
│                                                                  │   │ O   │ RB    │ ·       │ │
│                                                                  ╰───┴─────┴───────┴─────────╯ │
│                                                                                                │
│ ↑/↓ select   F1 rebind   F2 add   F3 remove   F4 defaults                                      │
│ F5 block   F6 name   F7 driver page   F8 save   F9 quit                                        │
╰────────────────────────────────────────────────────────────────────────────────────────────────╯
```

- **top** — status: controller label, ViGEm / Interception state, block on/off, and a
  breathing-LED "live" indicator;
- **middle** — the **mapping table** (key → pad input, with live press dots) next to the
  **gamepad monitor**: a controller diagram whose buttons flash, then settle to dim, as you
  press the mapped keys;
- **bottom** — the hotkeys, a fading status line, and the rebind prompt.

Hotkeys use **function keys** so they never clash with letter / WASD mappings:

| Key | Action |
|-----|--------|
| ↑ / ↓ | select a mapping |
| F1 | rebind the selected mapping (then press the new key) |
| F2 | add a new mapping (pick a pad input, then press a key) |
| F3 | remove the selected mapping |
| F4 | restore the default layout |
| F5 | toggle key blocking |
| F6 | set the controller label |
| F7 | open the Interception driver download page |
| F8 | save config |
| F9 | quit |
| Esc | cancel an in-progress rebind / add |

## Configuration (`config.json`)

```jsonc
{
  "backend": "interception",            // "interception" (recommended) or "hook"
  "controllerType": "xbox360",          // "xbox360" or "ds4" (PlayStation)
  "ui": "dashboard",                    // "dashboard" or "plain"
  "controllerName": "Player 2 (KeyboardToGamepad)",
  "blockMappedKeys": true,              // hide P2's keys from the game
  "mappings": { "W": "Up", "J": "A", "K": "B", "H": "X", "L": "Y" }   // ...
}
```

The default layout is **WASD** to move, home row **H J K L** = the four face buttons, and top
row **Y U I O** = shoulders/triggers (a fighting-game / KOF-style 8-button layout). Change it in
`config.json` or live in the TUI.

### Controller types

- `xbox360` — virtual Xbox 360 pad (XInput). Most compatible.
- `ds4` — virtual DualShock 4 (PlayStation). Note: a true PS5 DualSense cannot be emulated;
  `ds4` presents a PS4 pad, which games like Cuphead accept as a PlayStation controller.

### Key names (left side)

Any .NET `Keys` name: `A`–`Z`, `D0`–`D9`, `NumPad0`–`NumPad9`, `Decimal`, `Add`, `Subtract`,
`Multiply`, `Divide`, `Up`/`Down`/`Left`/`Right`, `Space`, `Enter`, …

### Targets (right side)

| Category | Values |
|---|---|
| Face buttons | `A` `B` `X` `Y` |
| Shoulders | `LB` `RB` |
| Triggers | `LT` `RT` |
| D-pad | `Up` `Down` `Left` `Right` |
| Center | `Back` `Start` `Guide` |
| Stick clicks | `LS` `RS` |
| Left stick | `LStickUp` `LStickDown` `LStickLeft` `LStickRight` |
| Right stick | `RStickUp` `RStickDown` `RStickLeft` `RStickRight` |

Face buttons are positional across controllers: on DS4, `A`=Cross, `B`=Circle, `X`=Square, `Y`=Triangle.

## Backends

- **interception** (default) — captures below Raw Input; works with Cuphead and other
  Rewired / Raw-Input games. Requires the Interception driver.
- **hook** — a `WH_KEYBOARD_LL` hook; needs no driver, but **does not block Raw Input games**.
  Useful only for games that read the keyboard the normal way.

## Troubleshooting

- **Both characters move** → you're on the `hook` backend, or the Interception driver isn't
  installed. Use `interception` and make sure the driver is installed and you rebooted.
- **"Could not create the virtual controller"** → install ViGEmBus.
- **"interception.dll not found"** → run `scripts/get-interception.ps1`.
- **TUI doesn't render** → run it in a real console window (not a redirected/piped shell).

## Roadmap

- System-tray icon, config hot-reload, multiple pads (3–4 player co-op),
- Raw Input to tell two physical keyboards apart, PlayStation-styled diagram.

## License

[MIT](LICENSE). Third-party components and their licenses (including the dual-licensed
Interception driver) are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
