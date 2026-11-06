# Windows Bluetooth troubleshooting

Use [Windows setup](Windows-Setup.md) for the current pairing flow. The app has two paths:
the Wiimote-specific native helper and **Open Windows pairing wizard**. In the verified
Windows 11 ARM session, the native helper timed out with code 258 while the built-in wizard
successfully paired an original remote. A timeout alone does not establish its cause.

- Close an old PIN dialog before starting another pairing flow. Press and release red SYNC
  while discovery runs. Use a no-code option only if Windows offers one; do not guess a PIN.
- A saved pairing and actual HID streaming are separate checks. Confirm the remote appears
  in OpenWiimote and that live button/accelerometer values update.
- The native helper enables only the HID service, preserves existing bonds and does not
  install a Bluetooth driver. It stops/reaps its own scan before opening the system wizard.
- Mouse mode uses the built-in Windows input API and needs no ViGEmBus driver. The native
  ARM64 app supports mouse mode; the x64 requirement concerns virtual Xbox gamepads only.
- Check that Bluetooth is on, the remote is charged and nearby, and another host is not
  holding its connection. Keep logs/device identifiers private when investigating failures.
- Do not add unrelated serial-port services, automatically erase bonds, weaken Windows
  protections or run the whole application as administrator to work around a pairing issue.

Older service-activation errors were not reproduced and are not a current setup recipe.
