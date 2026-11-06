# Linux setup and recovery

The remote and virtual controller need separate permissions. Start with:

```sh
./scripts/check-linux-setup.sh
```

This command only inspects the system. It does not load a module, open a physical remote,
change permissions, or pair devices. An OK result is a prerequisite, not proof of a
working controller; the app probes controller creation separately.

## Enable the virtual controller

If `/dev/uinput` is missing, an administrator can load the module:

```sh
sudo modprobe uinput
```

Install the repository's permission rules and reload them:

```sh
sudo install -m 0644 packaging/60-openwiimote.rules /etc/udev/rules.d/60-openwiimote.rules
sudo udevadm control --reload-rules
sudo udevadm trigger --subsystem-match=misc --sysname-match=uinput
sudo udevadm trigger --subsystem-match=hidraw
sudo udevadm trigger --subsystem-match=leds
```

The rule grants hidraw and LED access to `plugdev` and uinput access to `input`, with
active-seat access where available. For a headless account, an administrator can add
that account to the required existing groups; log out and back in afterward. Membership
in `input` gives access to other input devices too, so use the account intended to run
OpenWiimote. Run the app as that account.

## Connect and check a remote

1. Pair and trust the remote in Bluetooth settings once. On the first connection, press
   the red SYNC button while the system searches. After that, leave Auto-connect on and
   press 1 + 2 together to wake the paired remote. Allow up to 15 seconds for it to join.
   Each remote remembers its player light, including when they return in reverse order.
2. Open the dashboard. If the paired remote stays absent, check that Bluetooth is on and
   the remote has charged batteries. Scan retries opening HID devices already connected
   by Linux. If access is denied,
   fix the rule or account membership, then Scan again; a restart is usually unnecessary.
3. In Profiles, enable the virtual controller and choose a profile. The setup card
   distinguishes a connected remote from a virtual controller that is ready for games.
4. Check the buttons and tilt in the live readings and in the game's controller settings.
   A Wii Remote has tilt without MotionPlus. Gyro support is experimental and unavailable.

When controller output fails, the app reports it and releases that virtual controller.
Disable and re-enable emulation to retry. If the page loses contact with the service,
its setup status reports that loss and retries automatically.

## Automatic reconnection

On Linux, Auto-connect also retries connections to paired, trusted, unblocked Nintendo
Wii Remotes known to BlueZ. It requires `bluetoothctl`, normally supplied by BlueZ.
It does not discover devices, pair them again, delete bonds, change trust, turn the
adapter on, or interrupt another Bluetooth application's scan. An unreachable remote
has bounded, spaced retries and cannot block another remote's connection. Disabling
Auto-connect cancels pending attempts; existing controllers stay connected.

The remembered player slots are shared by the desktop and headless app. Manual LED
buttons can change a light pattern temporarily; reconnecting restores the remembered
player light. A fifth simultaneous remote has no player light rather than duplicating
one of the four slots. On other platforms the watcher opens remotes already connected
by the operating system; this Linux reconnect worker does not run there.

## Hardware acceptance

With the updated build, check each real remote separately, then both together:

- Every button and intended tilt direction reaches the expected game control.
- Rumble, player LEDs and battery information behave as expected.
- Switching the remote off releases held buttons and centres the virtual sticks.
- Waking and reconnecting the remote restores its pinned profile and controller.
- Turning emulation off releases output; turning it back on restores it.
- A second remote gets its own controller and retains its assigned profile.

Record hardware failures as failures even when synthetic tests pass. Installation,
real game feel and these interactive checks require a physical test session.

## Both remotes show player light 1

The app may request separate player lights while Linux keeps both default patterns if
LED brightness is not writable. Reinstall the current rule and reload/retrigger the LED
devices using the commands above. LED brightness is a sysfs attribute: ordinary udev
MODE/GROUP rules for device nodes do not change its permissions. The scoped LED rules
grant `plugdev` write access to the Nintendo player LED attributes, including after reconnect.
Then reconnect to restore the remembered player light, or choose a temporary LED pattern in the dashboard.
