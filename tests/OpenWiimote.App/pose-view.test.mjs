import assert from "node:assert/strict";
import { createRequire } from "node:module";
import test from "node:test";

const require = createRequire(import.meta.url);
const pose = require("../../src/OpenWiimote.App/wwwroot/pose-view.js");

const identity = {
  x: { x: 1, y: 0, z: 0 },
  y: { x: 0, y: 1, z: 0 },
  z: { x: 0, y: 0, z: 1 }
};

function near(actual, expected, label) {
  for (let i = 0; i < 3; i++) {
    assert.ok(Math.abs(actual[i] - expected[i]) < 0.02, label + " got " + actual.join(","));
  }
}

function placed(theme, accel, modelVector) {
  const q = pose.orientation(accel, identity, theme);
  assert.ok(q, "orientation");
  return pose.rotate(q, modelVector);
}

test("tip up stands the Wiimote with the buttons toward the viewer", () => {
  near(placed("wiimote", [0, 1, 0], [-1, 0, 0]), [0, 1, 0], "tip");
  near(placed("wiimote", [0, 1, 0], [0, 0, 1]), [0, 0, 1], "face");
  near(placed("wiimote", [0, 1, 0], [0, 1, 0]), [1, 0, 0], "right");
});

test("buttons up lays the Wiimote flat with the tip pointing away", () => {
  near(placed("wiimote", [0, 0, 1], [0, 0, 1]), [0, 1, 0], "face");
  near(placed("wiimote", [0, 0, 1], [-1, 0, 0]), [0, 0, -1], "tip");
  near(placed("wiimote", [0, 0, 1], [0, 1, 0]), [1, 0, 0], "right");
});

test("right edge up rolls the Wiimote onto its side", () => {
  near(placed("wiimote", [1, 0, 0], [0, 1, 0]), [0, 1, 0], "right");
  near(placed("wiimote", [1, 0, 0], [-1, 0, 0]), [0, 0, -1], "tip");
  near(placed("wiimote", [1, 0, 0], [0, 0, 1]), [-1, 0, 0], "face");
});

test("a remote whose tip-up reading is sensor Z still stands up", () => {
  const basis = {
    x: { x: 0, y: 1, z: 0 },
    y: { x: 0, y: 0, z: 1 },
    z: { x: 1, y: 0, z: 0 }
  };
  const q = pose.orientation([0, 0, 1], basis, "wiimote");
  near(pose.rotate(q, [-1, 0, 0]), [0, 1, 0], "tip");
  near(pose.rotate(q, [0, 0, 1]), [0, 0, 1], "face");
});

test("lights on the left of the picture twist a flat remote clockwise", () => {
  const flat = pose.orientation([0, 0, 1], identity, "wiimote");
  const half = 21 * Math.PI / 180;
  const turned = pose.withIrHeading(flat, [
    { isValid: true, x: 0, y: 100 },
    { isValid: true, x: 0, y: 600 }
  ], "wiimote");
  near(pose.rotate(turned, [-1, 0, 0]), [0, 0, -1], "tip");
  near(pose.rotate(turned, [0, 1, 0]), [Math.cos(half), -Math.sin(half), 0], "right");
});

test("lights on the right of the picture roll the other way", () => {
  const flat = pose.orientation([0, 0, 1], identity, "wiimote");
  const half = 21 * Math.PI / 180;
  const turned = pose.withIrHeading(flat, [
    { isValid: true, x: 1023, y: 200 },
    { isValid: true, x: 1023, y: 500 }
  ], "wiimote");
  near(pose.rotate(turned, [-1, 0, 0]), [0, 0, -1], "tip");
  near(pose.rotate(turned, [0, 1, 0]), [Math.cos(half), Math.sin(half), 0], "right");
});

test("one light does not invent a heading", () => {
  const flat = pose.orientation([0, 0, 1], identity, "wiimote");
  const same = pose.withIrHeading(flat, [{ isValid: true, x: 0, y: 400 }]);
  near(pose.rotate(same, [-1, 0, 0]), [0, 0, -1], "tip");
  assert.equal(pose.irHeading([{ isValid: true, x: 0, y: 1 }]), null);
});

test("the twist stays on the long axis when the remote is on its side", () => {
  const side = pose.orientation([1, 0, 0], identity, "wiimote");
  const turned = pose.withIrHeading(side, [
    { isValid: true, x: 0, y: 10 },
    { isValid: true, x: 0, y: 20 }
  ], "wiimote");
  near(pose.rotate(turned, [-1, 0, 0]), [0, 0, -1], "tip");
  const right = pose.rotate(turned, [0, 1, 0]);
  assert.ok(right[0] > 0.2, "right edge should leave the button axis, got " + right.join(","));
});

test("the screwdriver emitter is the tip that points up", () => {
  near(placed("screwdriver", [0, 1, 0], [1, 0, 0]), [0, 1, 0], "tip");
});

test("the downward camera still reads the three holds", () => {
  const pitch = { x: Math.sin(0.275), y: 0, z: 0, w: Math.cos(0.275) };
  const shown = (accel, modelVector) => pose.rotate(pose.mul(pitch, pose.orientation(accel, identity, "wiimote")), modelVector);
  const upTip = shown([0, 1, 0], [-1, 0, 0]);
  const upFace = shown([0, 1, 0], [0, 0, 1]);
  assert.ok(upTip[1] > 0.8 && upFace[2] > 0.4, "tip up");
  const rightEdge = shown([1, 0, 0], [0, 1, 0]);
  const rightTip = shown([1, 0, 0], [-1, 0, 0]);
  assert.ok(rightEdge[1] > 0.8 && rightTip[2] < -0.7, "right edge up");
  const flatFace = shown([0, 0, 1], [0, 0, 1]);
  const flatTip = shown([0, 0, 1], [-1, 0, 0]);
  assert.ok(flatFace[2] > 0.4 && flatTip[2] < -0.7 && flatTip[1] > 0.3, "buttons up");
});

test("malformed, coerced or overflowing sensor vectors never generate a pose", () => {
  const invalidAcceleration = [
    [0, Infinity, 1],
    [0, NaN, 1],
    [0, "1", 0],
    [0, 1],
    { x: 0, y: 1, z: null },
    [1.7e308, 1.7e308, 1.7e308]
  ];
  for (const accel of invalidAcceleration) {
    assert.equal(pose.orientation(accel, identity, "wiimote"), null);
    assert.equal(pose.holdName(accel, identity), "");
  }
  const invalidBasis = {
    ...identity,
    x: { x: 1, y: "0", z: 0 }
  };
  assert.equal(pose.orientation([0, 1, 0], invalidBasis, "wiimote"), null);
});

test("invalid IR positions do not invent or change a visual heading", () => {
  const flat = pose.orientation([0, 0, 1], identity, "wiimote");
  const examples = [
    [{ isValid: true, x: -1 }, { isValid: true, x: 10 }],
    [{ isValid: true, x: 1024 }, { isValid: true, x: 20 }],
    [{ isValid: true, x: "0" }, { isValid: true, x: 1023 }],
    [{ isValid: true, x: NaN }, { isValid: true, x: 10 }]
  ];
  for (const points of examples) {
    assert.equal(pose.irHeading(points), null);
    assert.deepEqual(pose.withIrHeading(flat, points, "wiimote"), flat);
  }
});
