import assert from "node:assert/strict";
import { createRequire } from "node:module";
import test from "node:test";
const require = createRequire(import.meta.url);
const calibration = require("../../src/OpenWiimote.App/wwwroot/tilt-calibration.js");
const s = Math.SQRT1_2;
function poses() {
  return { neutral: { x: 0, y: 0, z: 1 }, pitchUp: { x: 0, y: s, z: s },
    pitchDown: { x: 0, y: -s, z: s }, rollLeft: { x: -s, y: 0, z: s }, rollRight: { x: s, y: 0, z: s } };
}
const identity = { x: { x: 1, y: 0, z: 0 }, y: { x: 0, y: 1, z: 0 }, z: { x: 0, y: 0, z: 1 } };
function near(actual, expected, epsilon = 1e-9) {
  for (const key of Object.keys(expected)) assert.ok(Math.abs(actual[key] - expected[key]) <= epsilon, `${key}: ${actual[key]} != ${expected[key]}`);
}
function held(overrides = {}) {
  return [0, 250, 500].map((t) => ({ t, x: 0, y: 0, z: 1, ...overrides }));
}
function gravityAt(roll, pitch) {
  const r = roll * Math.PI / 180, p = pitch * Math.PI / 180;
  return { x: Math.sin(r) * Math.cos(p), y: Math.sin(p), z: Math.cos(r) * Math.cos(p) };
}

test("five moderate holds recover the standard sensor basis", () => {
  const basis = calibration.basisFromPoses(poses());
  for (const axis of ["x", "y", "z"]) near(basis[axis], identity[axis]);
});
test("five holds recover a rotated sensor basis", () => {
  const rotate = (v) => ({ x: v.z, y: v.x, z: v.y });
  const input = Object.fromEntries(Object.entries(poses()).map(([key, value]) => [key, rotate(value)]));
  const basis = calibration.basisFromPoses(input);
  for (const axis of ["x", "y", "z"]) near(basis[axis], rotate(identity[axis]));
});
test("unequal positive and negative tilt angles still recover the axes", () => {
  const input = poses();input.pitchUp = gravityAt(0, 20);input.pitchDown = gravityAt(0, -65);
  input.rollLeft = gravityAt(-25, 0);input.rollRight = gravityAt(60, 0);
  const basis = calibration.basisFromPoses(input);
  for (const axis of ["x", "y", "z"]) near(basis[axis], identity[axis]);
});
test("repeated neutral holds and yaw-only turns cannot define tilt axes", () => {
  const input = poses();for (const key of Object.keys(input)) input[key] = input.neutral;
  assert.equal(calibration.basisFromPoses(input), null);
});
test("reversing one pair rejects a mirrored basis", () => {
  const input = poses();[input.rollLeft, input.rollRight] = [input.rollRight, input.rollLeft];
  assert.equal(calibration.basisFromPoses(input), null);
});
test("crossed pitch and roll directions are rejected", () => {
  const input = poses();input.rollLeft = input.pitchDown;input.rollRight = input.pitchUp;
  assert.equal(calibration.basisFromPoses(input), null);
});
test("both members of an opposite pair must point in opposite directions", () => {
  const input = poses();input.pitchDown = input.pitchUp;
  assert.equal(calibration.basisFromPoses(input), null);
});
test("upright and barely tilted holds are rejected", () => {
  for (const angle of [0, 5, 90, 150]) {
    const input = poses();input.pitchUp = gravityAt(0, angle);
    assert.equal(calibration.basisFromPoses(input), null);
  }
});
test("invalid, missing and non-gravity pose values are rejected", () => {
  for (const bad of [null, {}, [], { x: NaN, y: 0, z: 1 }, { x: 0, y: 0, z: Infinity }, { x: 0, y: 0, z: 0.6 }, { x: 0, y: 0, z: 1.4 }, { x: "0", y: 0, z: 1 }]) {
    const input = poses();input.neutral = bad;assert.equal(calibration.basisFromPoses(input), null);
  }
  assert.equal(calibration.basisFromPoses(undefined), null);
});
test("a quiet recent half-second hold returns its average without gyro claims", () => {
  const result = calibration.stableSample(held(), 600);
  assert.deepEqual(result, { accel: { x: 0, y: 0, z: 1 } });
});
test("small still-hold noise is accepted", () => {
  const input = held();input[0].x = -0.01;input[2].x = 0.01;
  near(calibration.stableSample(input, 600).accel, { x: 0, y: 0, z: 1 });
});
test("motion and a transient acceleration are rejected", () => {
  const input = held();input[1].x = 0.2;
  assert.equal(calibration.stableSample(input, 600), null);
  input[1] = { ...input[1], x: 0, z: 1.5 };
  assert.equal(calibration.stableSample(input, 600), null);
});
test("stale, insufficient and short-duration samples cannot be captured", () => {
  assert.equal(calibration.stableSample(held(), 900), null);
  assert.equal(calibration.stableSample(held().slice(1), 600), null);
  assert.equal(calibration.stableSample(held().map((v, i) => ({ ...v, t: i * 100 })), 200), null);
  assert.equal(calibration.stableSample(held().map((v) => ({ ...v, t: 500 })), 600), null);
});
test("old samples do not extend the required current hold", () => {
  assert.equal(calibration.stableSample([{ t: 0, x: 0, y: 0, z: 1 }, { t: 1500, x: 0, y: 0, z: 1 }, { t: 1750, x: 0, y: 0, z: 1 }], 1800), null);
});
test("malformed, non-finite and future samples are rejected", () => {
  for (const input of [null, {}, held({ x: NaN }), held({ z: Infinity }), held({ x: "0" }), held({ t: 1000 }), held({ z: 0.1 })]) {
    assert.equal(calibration.stableSample(input, 600), null);
  }
  assert.equal(calibration.stableSample(held(), NaN), null);
});
test("level capture preserves one gravity unit at neutral", () => {
  near(calibration.levelVector({ x: 0, y: 0, z: 1 }, { x: 0, y: 0, z: 1 }), { x: 0, y: 0, z: 1 });
});
test("a level reference preserves positive and negative pitch and roll", () => {
  for (const [roll, pitch] of [[30, 0], [-30, 0], [0, 30], [0, -30], [30, 30], [-30, -30]]) {
    const raw = gravityAt(roll, pitch);near(calibration.levelVector(raw, identity.z), raw);
    const angles = calibration.tiltAngles(raw);near(angles, { roll, pitch });
  }
});
test("levelling a tilted reference rotates it upright instead of subtracting gravity", () => {
  const rest = gravityAt(35, 20);const result = calibration.levelVector(rest, rest);
  near(result, { x: 0, y: 0, z: 1 });
  const raw = { x: 0.2, y: -0.3, z: 0.9 };const rotated = calibration.levelVector(raw, rest);
  assert.ok(Math.abs(Math.hypot(rotated.x, rotated.y, rotated.z) - Math.hypot(raw.x, raw.y, raw.z)) < 1e-9);
});
test("the upside-down reference has a finite upright result", () => {
  near(calibration.levelVector({ x: 0, y: 0, z: -1 }, { x: 0, y: 0, z: -1 }), { x: 0, y: 0, z: 1 });
});
test("simultaneous physical pitch and roll produce both correct angles", () => {
  const angles = calibration.tiltAngles(gravityAt(30, 30));
  near(angles, { roll: 30, pitch: 30, total: 41.40962210927086 });
});
test("zero and malformed angle inputs and level references are rejected", () => {
  for (const bad of [null, {}, [], { x: 0, y: 0, z: 0 }, { x: NaN, y: 0, z: 1 }, { x: 0, y: Infinity, z: 1 }]) {
    assert.equal(calibration.tiltAngles(bad), null);
    assert.equal(calibration.levelVector(identity.z, bad), null);
  }
  assert.equal(calibration.levelVector({ x: Infinity, y: 0, z: 0 }, identity.z), null);
});

test("an arbitrary proper rotation preserves the recovered orthonormal sensor frame", () => {
  const n = Math.sqrt(14), axis = { x: 1 / n, y: 2 / n, z: 3 / n }, c = Math.cos(0.9), s = Math.sin(0.9);
  const rotate = (v) => {
    const d = axis.x * v.x + axis.y * v.y + axis.z * v.z;
    return { x: c * v.x + s * (axis.y * v.z - axis.z * v.y) + (1 - c) * d * axis.x,
      y: c * v.y + s * (axis.z * v.x - axis.x * v.z) + (1 - c) * d * axis.y,
      z: c * v.z + s * (axis.x * v.y - axis.y * v.x) + (1 - c) * d * axis.z };
  };
  const input = Object.fromEntries(Object.entries(poses()).map(([key, value]) => [key, rotate(value)]));
  const basis = calibration.basisFromPoses(input);
  for (const name of ["x", "y", "z"]) near(basis[name], rotate(identity[name]));
});
test("an overflowing reference norm cannot produce a fake valid orientation", () => {
  const huge = { x: 1.7e308, y: 1.7e308, z: 1.7e308 };
  assert.equal(calibration.levelVector(identity.z, huge), null);
  assert.equal(calibration.tiltAngles(huge), null);
});

test("out-of-order or repeated timestamps cannot be accepted as a stable hold", () => {
  const ordered = held();
  assert.deepEqual(calibration.stableSample(ordered, 600), { accel: { x: 0, y: 0, z: 1 } });
  const reordered = [ordered[1], ordered[0], ordered[2]];
  assert.equal(calibration.stableSample(reordered, 600), null);
  const repeated = [ordered[0], ordered[1], { ...ordered[1] }, ordered[2]];
  assert.equal(calibration.stableSample(repeated, 600), null);
});
