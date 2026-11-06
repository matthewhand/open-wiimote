# Avatar pitch and roll setup

The private Linux rc.3 candidate includes this guided browser-avatar setup. It is not in
the publicly downloadable rc.2 package. Physical-controller acceptance is still required.

1. Connect a remote and select it in the dashboard or its device tab.
2. Expand **Set up avatar tilt**. Keep still for about a second before each capture.
3. Capture these five holds, returning to level between the tilted holds:
   - **Level:** buttons up, tip pointing forward, both side edges level.
   - **Pitch up:** raise the front tip about 45 degrees, without banking sideways.
   - **Pitch down:** lower the front tip about 45 degrees, without banking sideways.
   - **Roll left:** keep pointing forward and raise the left edge about 45 degrees.
   - **Roll right:** keep pointing forward and raise the right edge about 45 degrees.
4. The finished setup is saved for that remote in this browser. Exact 45-degree angles
   are unnecessary; use distinct, opposite, moderate tilts. If capture is rejected, keep
   still and retry. Repeated or ambiguous holds require restarting from level.

The accelerometer measures pitch and roll together from gravity. It cannot measure yaw.
Twisting an upright remote rotates around gravity and supplies no new tilt reference.
MotionPlus is not required for this setup. Sensor-bar image positions do not add a turn
to the avatar; IR/pointer experiments remain separate.

## What changes

Only the avatar orientation changes. Raw readings, profiles, controller mappings, physical
sensor calibration and virtual-controller output are untouched. No settings API request is
sent. Setup is associated with the remote identity, not the order in which it connected.
The same raw/basis transform works with the Wiimote and screwdriver avatar themes.

**Set up again** and **Start again** retain the old valid setup until all five new holds
are complete and successfully saved. **Clear setup** clears only the selected remote's
browser setup and level reference. Other remotes retain their settings.

Switching remotes, disconnecting, losing the service or a change to this remote's saved
setup from another tab cancels unfinished holds. Captures require steady recent observations
and advancing controller-report timestamps. That is a connection-liveness guard, not proof
of separate accelerometer samples or a solution to MotionPlus gyro freshness.

## Quick avatar levelling and existing preferences

The **Level avatar** button and **Level the avatar only** checkbox use a single steady
hold as this remote's neutral reference. They rotate gravity into the level direction;
they do not subtract gravity. They also work after guided setup. Clearing the checkbox
removes just that level reference and keeps the guided basis.

Older shared level references cannot be safely assigned to a particular remote and are
not automatically applied to every remote. A notice asks you to capture a new per-remote
reference. Existing valid per-remote experimental bases remain compatible.

Advanced axis choices remain shared fallback view preferences, used on remotes without a
guided basis. Browser-storage failures are reported, leave the last valid setup intact,
and let a finished capture retry saving without repeating the holds. A failed level clear
restores the checkbox to reflect the reference that is actually still in use.
