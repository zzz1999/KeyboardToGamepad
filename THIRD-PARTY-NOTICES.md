# Third-Party Notices

KeyboardToGamepad depends on the following third-party components.

## ViGEmBus / Nefarius.ViGEm.Client
- Virtual gamepad bus driver + .NET client by Nefarius Software Solutions.
- License: BSD-3-Clause (client); the driver under its own license.
- https://github.com/nefarius/ViGEmBus  •  https://github.com/nefarius/ViGEm.NET

## Spectre.Console
- Terminal UI library.
- License: MIT.
- https://github.com/spectreconsole/spectre.console

## Interception
- Keyboard/mouse filter driver + library by Francisco Lopes (oblitum).
- License: dual-licensed — LGPL for non-commercial use; a commercial license otherwise
  (contact francisco@oblita.com).
- interception.dll and the driver are **not** bundled in this repository; users fetch them
  with `scripts/get-interception.ps1`.
- https://github.com/oblitum/Interception

KeyboardToGamepad talks to the Interception driver solely through its published API (interception.dll),
which is the condition under which the LGPL library and its binary assets may be redistributed
for non-commercial use. If you ship a build that bundles interception.dll, review the
Interception license first.
