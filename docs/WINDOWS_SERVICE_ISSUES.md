# Windows Bluetooth activation notes

A field note from a handoff investigation, not a finished report. It records the errors one
Windows session printed while trying to bring up the Bluetooth services for a paired remote,
and what was guessed from them. The guesses were not checked against Microsoft's
documentation, and none of it was reproduced on the Linux machine this repository is
developed on. Treat everything below the error codes as a hypothesis.

## What was observed

Activating the two Bluetooth service GUIDs failed, and so did a device restart:

| Operation | Result |
|-----------|--------|
| HID service GUID `{00001124-0000-1000-8000-00805f9b34fb}` | error 87, `ERROR_INVALID_PARAMETER` |
| SPP service GUID `{00001101-0000-1000-8000-00805f9b34fb}` | error 1060, `ERROR_SERVICE_DOES_NOT_EXIST` |
| `pnputil /restart-device` | `Access is denied` |

The command lines and the full output were not kept, so the exact call behind each code is
not recorded either.

## What it might mean (unverified)

- Error 87 is "invalid parameter", so the call itself may have been malformed rather than the
  Bluetooth stack being broken.
- Error 1060 says the SPP service does not exist. Windows only publishes that profile when
  the stack that owns the adapter offers it.
- `Access is denied` from `pnputil` is the ordinary answer for a non-elevated shell. It says
  nothing about the adapter.

## What was true of that machine

- Source tree: `C:\Users\matth\Documents\wiimote`, reported as not being a Git checkout at
  the time. This repository is a Git checkout on Linux; there is no Windows tree here to
  compare it against.
- .NET SDK 8.0.426, runtime 8.0.32. The projects in this repository target `net10.0`, and CI
  installs 10.0.x, so that machine's SDK was older than what the build now expects.
- Avalonia 11.3.22 and HidSharp 2.6.2, which still match the project files.

## What to try next

These need an elevated shell on the Windows machine, and they are the checks that would turn
the hypotheses above into a cause:

1. Confirm the Bluetooth Support Service (`bthserv`) is running and that the adapter is
   enabled in Device Manager.
2. Write down the exact command that returned 87, run it again as administrator, and keep the
   full output this time.
3. Confirm that the OEM Bluetooth stack installs the HID and SPP profiles. A generic or
   half-installed driver is the usual reason a profile GUID is missing.
4. Pair the remote from the Windows Bluetooth settings first. This app only opens HID devices
   the operating system already knows, so nothing in it can work before that step does.
