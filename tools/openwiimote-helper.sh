#!/usr/bin/env bash
# Run as the normal account with BlueZ permissions. Linux ignores setuid on scripts.
# Install with: sudo install -m 0755 tools/openwiimote-helper.sh /usr/local/bin/openwiimote-helper
set -u
ACTION="${1:-}"
if (( $# )); then shift; fi
connected() {
  bluetoothctl info "$ADDR" 2>/dev/null | grep -Eq '^[[:space:]]*Connected:[[:space:]]+yes[[:space:]]*$'
}
case "$ACTION" in
  list-paired)
    bluetoothctl devices Paired 2>&1
    ;;
  scan)
    bluetoothctl --timeout 5 scan on 2>&1
    bluetoothctl devices 2>&1
    ;;
  pair|connect)
    ADDR="${1:-}"
    if [[ ! "$ADDR" =~ ^([[:xdigit:]]{2}:){5}[[:xdigit:]]{2}$ ]]; then
      echo 'The address must look like AA:BB:CC:DD:EE:FF.' >&2
      exit 1
    fi
    if connected; then echo 'The remote is already connected.'; exit 0; fi
    KNOWN=yes
    INFO=$(bluetoothctl info "$ADDR" 2>&1) || KNOWN=no
    bluetoothctl --timeout 15 connect "$ADDR" 2>&1 || true
    if connected; then echo 'Connected to the remote.'; exit 0; fi
    if [[ "$ACTION" == connect ]] || grep -Eq '^[[:space:]]*Paired:[[:space:]]+yes[[:space:]]*$' <<< "$INFO"; then
      echo 'The remote is not connected. Its saved pairing was kept; wake it and retry.' >&2
      exit 1
    fi
    if [[ "$KNOWN" != yes ]]; then
      echo 'Unable to confirm pairing state. Press SYNC, scan, then retry.' >&2
      exit 1
    fi
    bluetoothctl --timeout 20 pair "$ADDR" 2>&1 || true
    bluetoothctl trust "$ADDR" 2>&1 || true
    bluetoothctl --timeout 15 connect "$ADDR" 2>&1 || true
    if connected; then echo 'Connected to the remote.'; exit 0; fi
    echo 'The remote is not connected. Press SYNC and retry.' >&2
    exit 1
    ;;
  *)
    echo "Usage: $0 {list-paired|scan|pair|connect} [address]" >&2
    exit 1
    ;;
esac
