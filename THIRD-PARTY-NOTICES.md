# Third-Party Notices

KeyboardToGamepad depends on the following third-party components.

## ViGEmBus / Nefarius.ViGEm.Client
- Virtual gamepad bus driver + .NET client by Nefarius Software Solutions.
- License: BSD-3-Clause (client); the driver under its own license.
- The official ViGEmBus installer is **embedded, unmodified, in the published exe** so the app
  can install the driver on first run (fetched by `scripts/get-vigembus.ps1`). It is not stored
  in this repository.
- https://github.com/nefarius/ViGEmBus  •  https://github.com/nefarius/ViGEm.NET

## Spectre.Console
- Terminal UI library.
- License: MIT.
- https://github.com/spectreconsole/spectre.console

## Interception
- Keyboard/mouse filter driver + library by Francisco Lopes (oblitum).
- License: dual-licensed — LGPL for non-commercial use; a commercial license otherwise
  (contact francisco@oblita.com).
- interception.dll and the driver installer are **not** stored in this repository; they are
  fetched with `scripts/get-interception.ps1`. The published build ships interception.dll next to
  the exe and embeds the unmodified `install-interception.exe` so the driver can be installed on
  first run.
- https://github.com/oblitum/Interception

KeyboardToGamepad talks to the Interception driver solely through its published API (interception.dll),
which is the condition under which the LGPL library and its binary assets may be redistributed
for non-commercial use. If you ship a build that bundles interception.dll, review the
Interception license first.
