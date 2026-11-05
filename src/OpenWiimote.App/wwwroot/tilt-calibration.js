// Browser-only accelerometer axis setup. Gravity determines pitch and roll;
// it cannot determine heading or a twist around gravity.
(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  if (root) root.TiltCalibration = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  const dot = (a, b) => a.x * b.x + a.y * b.y + a.z * b.z;
  const scale = (a, n) => ({ x: a.x * n, y: a.y * n, z: a.z * n });
  const sub = (a, b) => ({ x: a.x - b.x, y: a.y - b.y, z: a.z - b.z });
  const add = (a, b) => ({ x: a.x + b.x, y: a.y + b.y, z: a.z + b.z });
  const cross = (a, b) => ({ x: a.y * b.z - a.z * b.y, y: a.z * b.x - a.x * b.z, z: a.x * b.y - a.y * b.x });
  const length = (a) => Math.hypot(a.x, a.y, a.z);
  const valid = (a) => !!a && typeof a === "object" && !Array.isArray(a) && [a.x, a.y, a.z].every(Number.isFinite);
  const unit = (a) => {
    if (!valid(a)) return null;
    const magnitude = length(a);
    return Number.isFinite(magnitude) && magnitude > 1e-9 ? scale(a, 1 / magnitude) : null;
  };
  const gravity = (a) => valid(a) && length(a) >= 0.75 && length(a) <= 1.25;
  const project = (a, normal) => sub(a, scale(normal, dot(a, normal)));

  function basisFromPoses(samples) {
    if (!samples || typeof samples !== "object" || Array.isArray(samples)) return null;
    const names = ["neutral", "pitchUp", "pitchDown", "rollLeft", "rollRight"];
    if (!names.every((name) => gravity(samples[name]))) return null;
    const z = unit(samples.neutral);
    const projected = {};
    for (const name of names.slice(1)) {
      const direction = unit(samples[name]);
      const cosine = Math.min(1, Math.max(-1, dot(direction, z)));
      const angle = Math.acos(cosine) * 180 / Math.PI;
      // Moderate tilts remain observable and keep the buttons facing upward.
      if (angle < 15 - 1e-8 || angle > 70 + 1e-8) return null;
      projected[name] = unit(project(direction, z));
      if (!projected[name]) return null;
    }
    if (dot(projected.pitchUp, projected.pitchDown) > -0.8 ||
        dot(projected.rollLeft, projected.rollRight) > -0.8) return null;
    const y = unit(sub(projected.pitchUp, projected.pitchDown));
    const right = unit(sub(projected.rollRight, projected.rollLeft));
    if (!y || !right || Math.abs(dot(right, y)) > 0.35) return null;
    // A reversed hold is an error, not permission to silently mirror an axis.
    if (dot(cross(right, y), z) <= 0) return null;
    const x = unit(sub(right, scale(y, dot(right, y))));
    return x ? { x, y, z } : null;
  }

  function stableSample(samples, now) {
    if (!Array.isArray(samples) || !Number.isFinite(now)) return null;
    const recent = [];
    let lastTimestamp = -Infinity;
    for (const sample of samples) {
      // Replayed or reordered readings cannot establish a fresh, stable hold.
      if (!sample || !Number.isFinite(sample.t) || sample.t <= lastTimestamp ||
          sample.t > now || !gravity(sample)) return null;
      lastTimestamp = sample.t;
      if (sample.t >= now - 1000) recent.push(sample);
    }
    if (recent.length < 3) return null;
    const times = recent.map((sample) => sample.t);
    const first = times.reduce((a, b) => Math.min(a, b), Infinity);
    const last = times.reduce((a, b) => Math.max(a, b), -Infinity);
    if (new Set(times).size < 3 || last - first < 500 || now - last > 350) return null;
    const mean = scale(recent.reduce((sum, sample) => add(sum, sample), { x: 0, y: 0, z: 0 }), 1 / recent.length);
    if (!gravity(mean) || recent.some((sample) => length(sub(sample, mean)) > 0.08)) return null;
    return { accel: mean };
  }

  function levelVector(raw, rest) {
    if (!valid(raw)) return null;
    const from = unit(rest);
    if (!from) return null;
    const cosine = Math.min(1, Math.max(-1, from.z));
    if (cosine > 1 - 1e-12) return { x: raw.x, y: raw.y, z: raw.z };
    if (cosine < -1 + 1e-12) return { x: raw.x, y: -raw.y, z: -raw.z };
    // Rodrigues' minimal rotation from the reference gravity direction to +Z.
    const axis = cross(from, { x: 0, y: 0, z: 1 });
    const first = cross(axis, raw);
    const second = cross(axis, first);
    return add(add(raw, first), scale(second, 1 / (1 + cosine)));
  }

  function tiltAngles(vector) {
    if (!unit(vector)) return null;
    const degrees = 180 / Math.PI;
    return {
      roll: Math.atan2(vector.x, vector.z) * degrees,
      pitch: Math.atan2(vector.y, Math.hypot(vector.x, vector.z)) * degrees,
      total: Math.atan2(Math.hypot(vector.x, vector.y), vector.z) * degrees
    };
  }

  return { basisFromPoses, stableSample, levelVector, tiltAngles };
});
