# Hardware bring-up checklist

The protocol code is covered by unit tests and the replay harness, but pairing, LEDs,
rumble and the extension handshake only happen on a real remote. This checklist walks
through the path in the order the connection runs it, so a failure can be pinned to one
step instead of the whole app.

Run the app with the log panel open (`LogEntries`) or the headless page's activity log; both
show the same lines. Every run is also written to disk, in the `Logs` folder of the app's
own directory (`%APPDATA%\OpenWiimote` on Windows, `$XDG_CONFIG_HOME/OpenWiimote`
otherwise, usually `~/.config/OpenWiimote`). The desktop window writes
`session_<timestamp>.log` and the headless service writes `headless_<timestamp>.log`.

## 0. Before the remote

- [ ] The remote is paired in the operating system (Bluetooth settings or `bluetoothctl`),
      not by this app. The app only opens HID devices that the OS already knows.
- [ ] On Linux, the udev rules from `packaging/60-openwiimote.rules` are installed, or the
      app runs as a user that can open `/dev/hidraw*` and `/sys/class/leds/*`.
- [ ] With the in-kernel `hid-wiimote` driver bound, the app can read the remote but cannot
      send output reports. Expect the log line
      `The kernel wiimote driver owns <name>; its reporting mode is left to the driver.`
      Unbind the driver (`echo <hid-id> | sudo tee /sys/bus/hid/drivers/wiimote/unbind`)
      to exercise the user space path below.

## 1. Connection

- [ ] The remote appears in the device list and the log says
      `data reporting mode 0x31 in use.` while no extension is plugged in.
- [ ] Pressing a button changes `PressedButtonText`/`detailButtons` immediately.
- [ ] Tilting the remote changes the accelerometer and tilt values. With tilt steering on,
      the virtual controller's left stick follows the roll.
- [ ] Lying perfectly still for longer than the stale timeout (6 s by default) does **not**
      disconnect the remote. That proves continuous reporting is on; without it the watcher
      would drop the connection.

## 2. LEDs and rumble

- [ ] The first connected remote lights player LED 1 and the next free slot lights the next
      remote, so a second remote shows LED 2. The LED buttons on the headless page change
      the selected remote and log nothing. With the kernel driver bound, the app writes the
      LED class devices instead and logs `Could not switch the player LEDs of <name>` when
      `packaging/60-openwiimote.rules` is not installed. Until that write succeeds the kernel
      leaves every remote on player 1.
- [ ] Enabling rumble keeps the motor running; the log must not contain
      `Write failed for <name>`.
- [ ] With "rumble on button press" the remote buzzes for about 150 ms per press.

## 3. Extension handshake

Plug a Nunchuk or a Classic Controller into the remote.

- [ ] The status report arrives and the log says
      `extension initialisation started (encryption off, identifier read).`
- [ ] Two `0x16` writes and one `0x17` read are sent. A rejected write shows up as
      `<name> rejected output report 0x16 with error 0x..` (the error code comes from the
      `0x22` result report).
- [ ] The identifier reply (`0x21`, offset `0x00FA`) is decoded and the log says
      `<name>: Nunchuk extension detected.` (or Classic Controller / Classic Controller Pro).
      An identifier that is not one of the known ones is logged with its six bytes and the
      extension is shown as unknown.
- [ ] The data reporting mode moves to `0x35` (accelerometer plus extension) and the log says
      `data reporting mode 0x35 in use.`
- [ ] The detail card says `Nunchuk connected` / `Classic Controller connected`, and the
      extension card draws the controller with the pressed buttons highlighted.
- MotionPlus gyro acquisition and mapping are experimental and unavailable. No live gyro
  readings are expected. Extension pass-through decoding is retained but requires its own
  hardware acceptance when the accessory becomes available.
- [ ] If the identifier reply is lost, the app retries the handshake every two seconds while
      extension bytes keep arriving. An identifier that is answered but unknown stops the
      retries on purpose.

## 4. Extension input

- [ ] Nunchuk: the stick moves the virtual left stick, `Z` presses LT and `C` presses RB
      with the default profile.
- [ ] Classic Controller: sticks, d-pad, A/B/X/Y, L/R, ZL/ZR, +/- and Home all reach the
      virtual controller. Every source can be changed in the profile editor (the Wiimote,
      Nunchuk and Classic columns of the mapping table, plus the stick targets below it).
- [ ] The same controller behind a Motion Plus behaves the same way, and the gyro frames in
      between do not make the sticks jump or the buttons flicker.
- [ ] Unplugging the extension logs nothing more than the status report and the mode going
      back to `0x31`; the sticks return to the accelerometer or stay neutral. Moving a
      Classic Controller from one remote to another clears it on the first remote and shows
      it, with its own 3D view, only inside the second remote's card.

## 5. IR camera

Switch on "Switch on the IR camera" in the app (or `irEnabled` on the headless settings).

- [ ] The log says `IR camera switched on (sensitivity blocks written).` The sequence is
      `0x13`, `0x1a`, mode register `0xB00030 = 0x08`, the two sensitivity blocks, the
      format register `0xB00033`, and `0xB00030 = 0x08` again, 50 ms apart.
- [ ] The data reporting mode becomes `0x33` without an extension, which needs the extended
      format (`0xB00033 = 0x03`, twelve IR bytes), and `0x37` with one, which only has ten
      bytes and therefore switches the camera to the basic format (`0xB00033 = 0x01`).
      Plugging or unplugging the extension while the camera runs must switch the format.
- [ ] With a sensor bar (or any two infrared sources) in front of the remote, the IR pointer
      panel shows up to four points, and the detail card lists their coordinates. An empty
      slot of the camera reads `0x3FF, 0x3FF` and must be skipped.
- [ ] With no IR source the panel says `No IR points` and nothing else changes.
- [ ] Switching the camera off returns the mode to `0x31`/`0x35` and stops the IR points.

## 6. Replay a captured log

If a step fails, the raw reports are worth more than the decoded state:

```bash
dotnet run --project src/OpenWiimote.App -- --replay capture.txt
```

One report per line, hex bytes separated by spaces (commas, colons, semicolons and dashes
work too). The number in front of each line is the report's place in the capture, counting
only the lines that hold a report. The output names read replies, result reports and
extension payloads, so the handshake can be checked offline. These lines are what
`tools/sample-reports.txt` prints, verbatim apart from the reports left out in the middle:

```
#1   id=0x20 buttons=One battery=100% extension=no
#3   id=0x31 buttons=B, A accel=(0.00, 0.00, 1.00)g tilt=(0deg, 0deg)
#6   id=0x21 buttons=None read=0x00FA error=0x00 data=0000A4200000
#7   id=0x35 buttons=None accel=(0.00, 0.00, 1.00)g tilt=(0deg, 0deg) extension=80808080800300000000000000000000
#8   id=0x22 buttons=None ack=0x16 error=0x00
```

## Known limitations

- A kernel-owned remote is never initialised from user space, so the extension is only
  reported as connected and its payload is not decoded.
- `IVirtualGamepad.SetBattery` is forwarded by the app and the headless service, but the
  ViGEm and uinput backends do not publish the level to the operating system yet.
- MotionPlus gyro support is an unavailable experimental stub (ROADMAP item 10). The
  current connection does not read the unverified gyro register block or produce gyro
  samples. The research remains in draft PR #17; physical activation, freshness and
  calibration validation are future work.
