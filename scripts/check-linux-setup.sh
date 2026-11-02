#!/usr/bin/env bash
# Read-only diagnostics. Does not load modules, pair devices, or change permissions.
set -u
if [[ $(uname -s) != Linux ]]; then
    echo 'This check is for Linux. See the README for your platform.'
    exit 1
fi
result=0
if [[ ! -e /dev/uinput ]]; then
    echo 'NEEDS SETUP: /dev/uinput is missing. Load the uinput kernel module; see docs/Linux-Setup.md.'
    result=1
elif [[ ! -w /dev/uinput ]]; then
    echo 'NEEDS SETUP: your account cannot write /dev/uinput. Check the udev rule and input group membership.'
    result=1
else
    echo 'OK: your account can write /dev/uinput. The application will verify controller creation.'
fi
if command -v bluetoothctl >/dev/null 2>&1; then
    echo 'OK: bluetoothctl is installed. Use Bluetooth settings to pair the remote.'
else
    echo 'NEEDS SETUP: bluetoothctl is not installed. Install BlueZ for dashboard Bluetooth support.'
    result=1
fi
if [[ -r /etc/udev/rules.d/60-openwiimote.rules ]]; then
    echo 'OK: an OpenWiimote udev rule is installed. Compare it with packaging/60-openwiimote.rules if access fails.'
else
    echo 'NEEDS SETUP: the OpenWiimote udev rule is not installed; see docs/Linux-Setup.md.'
    result=1
fi
echo 'HID access is checked when the app opens a paired remote. A readable remote and a writable virtual controller are separate requirements.'
exit "$result"
