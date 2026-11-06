// Per-remote browser state. No HID access, profile edits or controller output.
(function (root, factory) {
  const math = typeof module === 'object' && module.exports ? require('./tilt-calibration.js') : root.TiltCalibration;
  const api = factory(math);
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.AvatarCalibration = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function (math) {
  const KEY = 'openwiimote.poseCalibration';
  const STEPS = [
    { id: 'neutral', title: 'Level, buttons facing up', hint: 'Point the tip forward. Keep both side edges level and hold still for a second.' },
    { id: 'pitchUp', title: 'Pitch: raise the front tip', hint: 'Raise the tip about 45°. Keep the side edges level. Hold still; do not point straight up.' },
    { id: 'pitchDown', title: 'Pitch: lower the front tip', hint: 'Return to level, then lower the tip about 45°. Keep the side edges level and hold still.' },
    { id: 'rollLeft', title: 'Roll: raise the left edge', hint: 'Return to level. Twist around the remote’s length about 45°, raising its left edge. Keep the tip pointing forward and hold still.' },
    { id: 'rollRight', title: 'Roll: raise the right edge', hint: 'Return to level. Twist the other way, raising the right edge about 45°. Keep pointing forward and hold still.' }
  ];
  const object = (v) => !!v && typeof v === 'object' && !Array.isArray(v);
  const finite = (v) => object(v) && [v.x, v.y, v.z].every(Number.isFinite);
  const dot = (a, b) => a.x * b.x + a.y * b.y + a.z * b.z;
  const norm = (v) => Math.hypot(v.x, v.y, v.z);
  const cross = (a, b) => ({ x: a.y * b.z - a.z * b.y, y: a.z * b.x - a.x * b.z, z: a.x * b.y - a.y * b.x });
  function validBasis(b) {
    return object(b) && ['x', 'y', 'z'].every(a => finite(b[a]) && Math.abs(norm(b[a]) - 1) < 0.02) &&
      Math.abs(dot(b.x, b.y)) < 0.02 && Math.abs(dot(b.x, b.z)) < 0.02 &&
      Math.abs(dot(b.y, b.z)) < 0.02 && dot(cross(b.x, b.y), b.z) > 0.98;
  }
  function identity(device) {
    if (!device) return '';
    const address = typeof device.bluetoothAddress === 'string' ? device.bluetoothAddress.trim() : '';
    if (address) return address.toUpperCase();
    return typeof device.key === 'string' ? device.key : '';
  }
  function aliases(device) {
    const address = device && typeof device.bluetoothAddress === 'string' ? device.bluetoothAddress.trim() : '';
    return [...new Set([identity(device), address, address.toLowerCase(), device && device.key].filter(Boolean))];
  }
  function raw(state) {
    if (!state) return null;
    const v = { x: state.accelX, y: state.accelY, z: state.accelZ };
    return finite(v) ? v : null;
  }
  function project(vector, entry) {
    if (!finite(vector)) return null;
    const basis = entry && validBasis(entry.basis) ? entry.basis : null;
    const map = v => basis ? { x: dot(basis.x, v), y: dot(basis.y, v), z: dot(basis.z, v) } : { ...v };
    const result = map(vector);
    const level = entry && entry.level;
    return finite(level) && norm(level) >= 0.75 && norm(level) <= 1.25
      ? math.levelVector(result, map(level)) : result;
  }
  class Store {
    constructor(storage) { this.storage = storage; }
    read(forWrite = false) {
      let text;
      try { text = this.storage.getItem(KEY); }
      catch (error) {
        if (forWrite) throw error; // Never overwrite unseen entries after a read failure.
        return {};
      }
      try {
        const value = JSON.parse(text || '{}');
        return object(value) ? value : {};
      } catch { return {}; }
    }
    get(device) {
      const values = this.read();
      // Also accept the earlier experimental address spelling / device-key entry.
      const candidates = aliases(device);
      for (const key of candidates) {
        const entry = values[key];
        if (!key || !object(entry)) continue;
        const basis = validBasis(entry.basis) ? entry.basis : null;
        const level = finite(entry.level) && norm(entry.level) >= 0.75 && norm(entry.level) <= 1.25 ? entry.level : null;
        if (basis || level) return { basis, level };
      }
      return { basis: null, level: null };
    }
    put(device, patch) {
      const key = identity(device);
      if (!key) return false;
      try {
        const values = this.read(true);
        const existing = aliases(device).map(alias => values[alias]).find(object) || {};
        const entry = { ...existing, ...patch, version: 2 };
        this.storage.setItem(KEY, JSON.stringify({ ...values, [key]: entry }));
        return true;
      } catch { return false; }
    }
    clear(device) {
      if (!identity(device)) return false;
      try {
        const values = this.read(true);
        for (const key of aliases(device)) delete values[key];
        this.storage.setItem(KEY, JSON.stringify(values));
        return true;
      } catch { return false; }
    }
  }
  class Session {
    constructor() { this.context = ''; this.key = ''; this.reset(); }
    reset() {
      this.step = 0; this.draft = {}; this.samples = []; this.pending = null;
      this.newest = -Infinity; this.active = true;
    }
    select(device) {
      const key = identity(device);
      const context = key ? JSON.stringify([key, device.key || '']) : '';
      if (context === this.context) return false;
      this.context = context; this.key = key; this.reset();
      return true;
    }
    observe(device, now) {
      this.select(device);
      const v = raw(device && device.state);
      const report = device && device.state && Date.parse(device.state.lastReport);
      if (!this.key || !Number.isFinite(now) || !Number.isFinite(report) ||
          now - report > 2500 || report - now > 5000 || report < this.newest) {
        this.samples = []; return;
      }
      this.newest = report;
      if (!v) { this.samples = []; return; }
      const previous = this.samples[this.samples.length - 1];
      if (previous && now <= previous.t) return;
      this.samples.push({ ...v, t: now, report });
      this.samples = this.samples.filter(s => s.t >= now - 1200);
    }
    still(now) {
      const recent = this.samples.filter(s => s.t >= now - 1000);
      // Polls measure stillness; advancing report time establishes connection liveness.
      // Requiring changed gravity would reject a correctly stationary remote.
      if (new Set(recent.map(s => s.report)).size < 2) return null;
      return math.stableSample(recent, now);
    }
    capture(now) {
      if (!this.key) return { kind: 'error', message: 'Connect and select a remote first.' };
      if (!this.active) { this.reset(); return { kind: 'started' }; }
      if (this.pending) return this.pending;
      const sample = this.still(now);
      if (!sample) return { kind: 'error', message: 'Hold still for a second, then try again. Fresh, steady readings are needed.' };
      this.draft[STEPS[this.step].id] = sample.accel;
      this.samples = []; this.step++;
      if (this.step < STEPS.length) return { kind: 'next' };
      const basis = math.basisFromPoses(this.draft);
      if (!basis) {
        this.reset();
        return { kind: 'error', message: 'Those holds did not separate pitch and roll. Start again from level, using opposite tilts for each pair.' };
      }
      this.pending = { kind: 'complete', key: this.key, basis };
      return this.pending;
    }
    finish() { this.reset(); this.active = false; }
  }
  return { KEY, STEPS, Session, Store, identity, raw, validBasis, project };
});
