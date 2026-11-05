// Turns the three axis-setup holds into the 3D remote's orientation.
//
// Each hold records the accelerometer direction that pointed at the ceiling:
// tip up, right edge up, buttons up. While the same side points up again, the
// model matches that hold. A twist that keeps the same side up is invisible
// to the accelerometer, so the holds leave that heading fixed: buttons toward
// the viewer when the tip is up, and the tip pointing away when the buttons
// or the right edge are up. Two sensor-bar lights twist the remote along its
// length. That length is the model's X axis. On the accelerometer it is Y,
// and it lines up with the screen's Z when the tip points at the screen.
// The camera's left/right is mirrored, so lights on the left of the picture
// roll the remote clockwise as you look at the screen.
(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  if (root) root.PoseView = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  const WORLD_UP = [0, 1, 0];
  const FRONT_UP = [0, 0, 1];
  const IR_WIDTH = 1024;
  // Dolphin measured 42° across the camera (jordan-woyak, 2020). The older
  // WiiBrew figure was 33°. Half of 42° is the aim at either edge.
  const IR_FOV_X = 42 * Math.PI / 180;
  // Remote axes in the flat hold: right, tip (away), buttons (up).
  const FRONT = [
    [1, 0, 0],
    [0, 0, -1],
    [0, 1, 0]
  ];

  function dot(a, b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
  function scale(a, s) { return [a[0] * s, a[1] * s, a[2] * s]; }
  function sub(a, b) { return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]; }
  function add(a, b) { return [a[0] + b[0], a[1] + b[1], a[2] + b[2]]; }
  function cross(a, b) {
    return [
      a[1] * b[2] - a[2] * b[1],
      a[2] * b[0] - a[0] * b[2],
      a[0] * b[1] - a[1] * b[0]
    ];
  }
  function norm(a) {
    if (!a) return null;
    const m = Math.hypot(a[0], a[1], a[2]);
    return !Number.isFinite(m) || m < 1e-6 ? null : scale(a, 1 / m);
  }
  function vec(v) {
    if (!v || typeof v !== "object") return null;
    const coords = Array.isArray(v) ? (v.length === 3 ? v : null) : [v.x, v.y, v.z];
    if (!coords || !coords.every((n) => typeof n === "number" && Number.isFinite(n))) return null;
    return coords.slice();
  }
  function apply(cols, v) {
    return add(add(scale(cols[0], v[0]), scale(cols[1], v[1])), scale(cols[2], v[2]));
  }
  function mulCols(a, b) {
    return [apply(a, b[0]), apply(a, b[1]), apply(a, b[2])];
  }
  function transpose(cols) {
    return [0, 1, 2].map((row) => [cols[0][row], cols[1][row], cols[2][row]]);
  }

  function rotate(q, v) {
    const tx = 2 * (q.y * v[2] - q.z * v[1]);
    const ty = 2 * (q.z * v[0] - q.x * v[2]);
    const tz = 2 * (q.x * v[1] - q.y * v[0]);
    return [
      v[0] + q.w * tx + (q.y * tz - q.z * ty),
      v[1] + q.w * ty + (q.z * tx - q.x * tz),
      v[2] + q.w * tz + (q.x * ty - q.y * tx)
    ];
  }

  function mul(a, b) {
    return {
      x: a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
      y: a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
      z: a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
      w: a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z
    };
  }

  function quatFromTo(a, b) {
    const d = dot(a, b);
    if (d > 0.999999) return { x: 0, y: 0, z: 0, w: 1 };
    if (d < -0.999999) {
      let axis = cross(a, [1, 0, 0]);
      if (Math.hypot(axis[0], axis[1], axis[2]) < 1e-6) axis = cross(a, [0, 1, 0]);
      axis = norm(axis);
      return { x: axis[0], y: axis[1], z: axis[2], w: 0 };
    }
    const c = cross(a, b);
    const q = { x: c[0], y: c[1], z: c[2], w: 1 + d };
    const n = Math.hypot(q.x, q.y, q.z, q.w) || 1;
    return { x: q.x / n, y: q.y / n, z: q.z / n, w: q.w / n };
  }

  function quatToCols(q) {
    const x = q.x, y = q.y, z = q.z, w = q.w;
    return [
      [1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w)],
      [2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w)],
      [2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y)]
    ];
  }

  function colsToQuat(c) {
    const m00 = c[0][0], m11 = c[1][1], m22 = c[2][2];
    const trace = m00 + m11 + m22;
    let q;
    if (trace > 0) {
      const s = Math.sqrt(trace + 1) * 2;
      q = {
        w: 0.25 * s,
        x: (c[1][2] - c[2][1]) / s,
        y: (c[2][0] - c[0][2]) / s,
        z: (c[0][1] - c[1][0]) / s
      };
    } else if (m00 > m11 && m00 > m22) {
      const s = Math.sqrt(1 + m00 - m11 - m22) * 2;
      q = {
        w: (c[1][2] - c[2][1]) / s,
        x: 0.25 * s,
        y: (c[0][1] + c[1][0]) / s,
        z: (c[2][0] + c[0][2]) / s
      };
    } else if (m11 > m22) {
      const s = Math.sqrt(1 + m11 - m00 - m22) * 2;
      q = {
        w: (c[2][0] - c[0][2]) / s,
        x: (c[0][1] + c[1][0]) / s,
        y: 0.25 * s,
        z: (c[1][2] + c[2][1]) / s
      };
    } else {
      const s = Math.sqrt(1 + m22 - m00 - m11) * 2;
      q = {
        w: (c[0][1] - c[1][0]) / s,
        x: (c[2][0] + c[0][2]) / s,
        y: (c[1][2] + c[2][1]) / s,
        z: 0.25 * s
      };
    }
    const n = Math.hypot(q.x, q.y, q.z, q.w) || 1;
    return { x: q.x / n, y: q.y / n, z: q.z / n, w: q.w / n };
  }

  // Mesh directions for the three holds. The Wiimote tip is the D-pad end.
  function modelAxes(theme) {
    if (theme === "screwdriver") {
      return [
        [0, -1, 0],
        [1, 0, 0],
        [0, 0, 1]
      ];
    }
    return [
      [0, 1, 0],
      [-1, 0, 0],
      [0, 0, 1]
    ];
  }

  function orthonormal(basis) {
    const tip = norm(vec(basis && basis.y));
    const face0 = norm(vec(basis && basis.z));
    const right0 = norm(vec(basis && basis.x));
    if (!tip || !face0 || !right0) return null;
    const face = norm(sub(face0, scale(tip, dot(face0, tip))));
    if (!face) return null;
    let right = norm(sub(sub(right0, scale(tip, dot(right0, tip))), scale(face, dot(right0, face))));
    if (!right) return null;
    if (dot(cross(right, tip), face) < 0) right = scale(right, -1);
    return [right, tip, face];
  }

  function remoteUp(accel, axes) {
    const a = norm(vec(accel));
    if (!a) return null;
    return norm([dot(axes[0], a), dot(axes[1], a), dot(axes[2], a)]);
  }

  function worldAxes(up) {
    const sens = quatToCols(quatFromTo(FRONT_UP, up));
    return mulCols(FRONT, transpose(sens));
  }

  function orientation(accel, basis, theme) {
    const axes = orthonormal(basis);
    const up = axes && remoteUp(accel, axes);
    if (!up) return null;
    const model = modelAxes(theme);
    const world = worldAxes(up);
    return colsToQuat(mulCols(world, transpose(model)));
  }

  function pointX(point) {
    if (!point || typeof point !== "object" ||
        (point.isValid !== true && point.IsValid !== true)) return null;
    const x = point.x != null ? point.x : point.X;
    return typeof x === "number" && Number.isFinite(x) &&
      x >= 0 && x < IR_WIDTH ? x : null;
  }

  // Radians of roll along the remote. Positive puts the lights on the left of
  // the camera picture, which is the mirrored aim, and rolls clockwise.
  function irHeading(points) {
    if (!points || !points.length) return null;
    let sum = 0;
    let count = 0;
    for (let i = 0; i < points.length && count < 2; i++) {
      const x = pointX(points[i]);
      if (x == null) continue;
      sum += x;
      count++;
    }
    if (count < 2) return null;
    const across = Math.min(1, Math.max(0, (sum / count) / (IR_WIDTH - 1)));
    return (0.5 - across) * IR_FOV_X;
  }

  function quatFromAxis(axis, angle) {
    const s = Math.sin(angle / 2);
    return { x: axis[0] * s, y: axis[1] * s, z: axis[2] * s, w: Math.cos(angle / 2) };
  }

  function withIrHeading(orientationQuat, points, theme) {
    if (!orientationQuat) return orientationQuat;
    const angle = irHeading(points);
    if (angle == null || Math.abs(angle) < 1e-6) return orientationQuat;
    const tip = norm(rotate(orientationQuat, theme === "screwdriver" ? [1, 0, 0] : [-1, 0, 0]));
    if (!tip) return orientationQuat;
    return mul(quatFromAxis(tip, angle), orientationQuat);
  }

  function holdName(accel, basis) {
    const axes = orthonormal(basis);
    const up = axes && remoteUp(accel, axes);
    if (!up) return "";
    const parts = [["right", Math.abs(up[0])], ["up", Math.abs(up[1])], ["flat", Math.abs(up[2])]];
    parts.sort((a, b) => b[1] - a[1]);
    return parts[0][1] >= 0.75 ? parts[0][0] : "tilted";
  }

  function extent(q) {
    let maxX = 0.01;
    let maxY = 0.01;
    const hx = 5.35, hy = 1.35, hz = 1.05;
    for (const x of [-hx, hx]) {
      for (const y of [-hy, hy]) {
        for (const z of [-hz, hz]) {
          const p = rotate(q, [x, y, z]);
          maxX = Math.max(maxX, Math.abs(p[0]));
          maxY = Math.max(maxY, Math.abs(p[1]));
        }
      }
    }
    return [maxX, maxY];
  }

  return {
    rotate,
    mul,
    orientation,
    irHeading,
    withIrHeading,
    holdName,
    extent,
    modelAxes,
    WORLD_UP
  };
});
