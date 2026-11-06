# Sensor-bar pointer

Use a powered **Wii infrared sensor bar**, centred above or below the monitor and facing
you. An ordinary RGB light bar is not a substitute. Original Wii Remotes have the IR
camera; MotionPlus is unnecessary. Keep the bar visible from your usual seated position. If aiming at an ultrawide edge loses
the bar, increase your distance or adjust its placement. Calibration cannot extend the
camera's field of view.

## Windows desktop app

1. Connect the remote in Windows Bluetooth settings. Open **Sensor bar pointer setup**.
2. Choose either connected remote and your ultrawide from **Display**, then **Use selected
   remote**. This enables its IR camera. Only the chosen remote controls the desktop cursor.
3. Choose **Calibrate screen**. A full-screen target and live IR inset open on the selected monitor. The inset shows raw dots, tracking and readiness; the cursor stays still during calibration.
4. Aim at each green target, hold still for about half a second, then press and release
   **A**. The order is top left, top right, bottom right, bottom left. Four valid targets
   save a separate calibration for that remote and display. Esc or Home cancels.
5. Aim at the bar, release A/B, and switch **Mouse mode** on in the main header (or choose **Enable pointer**). **A** is left click,
   **B** is right click, and **Home** stops the pointer.

To use the other remote, choose it and **Use selected remote**. Calibrate it once, then
use its saved calibration. Switching releases the old remote's mouse buttons and leaves
pointer output off until you enable it. Pointer mode also pauses gamepad output for its
selected remote; the other remote's virtual controller remains independent.

## Browser dashboard / Linux

Use the **Sensor bar pointer** card. Move the browser to the selected display before
choosing **Calibrate screen**; allow full-screen mode. Targets cover the screen regardless
of aspect ratio or desktop scaling. Stay in full-screen until all four targets are saved.
Esc cancels. A dashboard on another computer cannot calibrate the host's physical screen.

The Linux backend currently maps onto the whole X11 desktop through uinput. A single
ultrawide works as one surface. Selecting individual Linux monitors or native Wayland
pointer support needs further work. Windows enumerates individual monitors and supports
negative monitor positions and different display scaling.

## Tracking and recovery

The remote must see exactly two plausible IR points. More points can mean reflections
or another IR source; move those sources before retrying. The app releases mouse buttons
when tracking disappears, the selected remote disconnects, output fails, or the app stops.
After tracking returns, release A/B before clicking again. Other button/status reports
cannot keep an old IR reading fresh.

Calibration is saved on the host, per remote and display layout. A monitor layout or
resolution change invalidates it. Cancellation and failed calibration preserve the last
valid calibration. Pointer output always starts off when the app restarts. Moving the bar,
changing your seated position, or changing monitor geometry requires recalibration.

Keep the normal mouse available during the first physical test. Verify all four edges,
centre, stationary hold, click/release, loss of tracking, Home, disconnect and switching
remotes before relying on pointer mode in a game or presentation.
