/* One host cursor, one selected remote. Calibration is separate from avatar/tilt setup. */
(() => {
  const card = document.createElement('section');
  card.className = 'card'; card.id = 'pointerSetup';
  card.innerHTML = `<div class="section">SENSOR BAR POINTER</div>
    <p class="note">Centre a powered Wii infrared sensor bar above or below the screen. Choose either remote. A clicks, B right-clicks, Home stops the pointer.</p>
    <label class="field"><span>Remote</span><select id="pointerRemote" aria-label="Pointer remote"></select></label>
    <label class="field"><span>Display</span><select id="pointerDisplay" aria-label="Pointer display"></select></label>
    <button class="btn" id="pointerSelect">Use this remote</button>
    <button class="btn" id="pointerCalibrate">Calibrate screen</button>
    <button class="btn primary" id="pointerEnable">Enable pointer</button>
    <button class="btn danger" id="pointerStop">Stop pointer</button>
    <p class="note" id="pointerStatus" role="status">Checking pointer support…</p>
    <p class="note small">Move this browser onto the chosen display before calibration. It uses the full screen, including ultrawide edges. Calibrate each remote from your normal position. Changing display layout requires calibration again. Pointer starts off each time the app starts.</p>`;
  document.querySelector('[data-tab-panel="dashboard"] main .col').append(card);
  const overlay = document.createElement('div'); overlay.id = 'pointerCalibration'; overlay.hidden = true;
  overlay.innerHTML = '<div id="pointerTarget"></div><div id="pointerHint" role="status"></div><button id="pointerCancel">Cancel calibration</button>';
  document.body.append(overlay);
  const css = document.createElement('style'); css.textContent = `
    #pointerCalibration:not([hidden]){display:block;position:fixed;inset:0;background:#101820;color:white;z-index:10000}
    #pointerTarget{position:absolute;width:36px;height:36px;transform:translate(-50%,-50%);border-radius:50%;background:#53ee7f;border:3px solid white}
    #pointerHint{position:absolute;top:42%;left:15%;right:15%;text-align:center;font:24px/1.5 sans-serif}
    #pointerCancel{position:absolute;top:65%;left:50%;transform:translateX(-50%);padding:12px 20px}
    #pointerSetup select{min-width:0;width:100%}`;
  document.head.append(css);
  const el = id => document.getElementById(id);
  let signature = '', starting = false, last = null, generation = 0;
  async function action(name) { return post('/api/pointer', { action: name }); }
  async function select() {
    return post('/api/pointer', { action: 'select', deviceKey: el('pointerRemote').value, displayId: el('pointerDisplay').value });
  }
  async function close(cancel) {
    generation++; overlay.hidden = true;
    if (document.fullscreenElement === overlay) await document.exitFullscreen().catch(() => {});
    if (cancel) await action('cancel');
  }
  el('pointerRemote').onchange = el('pointerDisplay').onchange = () => { el('pointerEnable').disabled = true; };
  el('pointerSelect').onclick = select;
  el('pointerEnable').onclick = () => action('enable');
  el('pointerStop').onclick = () => action('disable');
  el('pointerCancel').onclick = () => close(true);
  el('pointerCalibrate').onclick = async () => {
    const current = ++generation; starting = true; overlay.hidden = false;
    try {
      if (!overlay.requestFullscreen) throw new Error('Use a browser with full-screen support for screen calibration.');
      await overlay.requestFullscreen();
      if (!await select() || current !== generation) { await close(true); return; }
      if (!await action('calibrate') || current !== generation) await close(true);
    } catch (error) { await close(false); el('pointerStatus').textContent = error.message; }
    finally { starting = false; }
  };
  document.addEventListener('fullscreenchange', () => {
    if (!document.fullscreenElement && !overlay.hidden) close(true);
  });
  window.addEventListener('pagehide', () => { if (last?.calibrating) action('cancel'); });
  window.PointerSetup = { update(snapshot) {
    const state = snapshot.pointer;
    if (!state) { card.hidden = true; return; }
    card.hidden = false; last = state;
    const devices = snapshot.devices || [], displays = state.displays || [];
    const key = JSON.stringify([devices.map(d => [d.key, d.name]), displays]);
    if (key !== signature) {
      signature = key;
      const previousRemote = el('pointerRemote').value || state.deviceKey;
      const previousDisplay = el('pointerDisplay').value || state.displayId;
      for (const [id, values, value, label] of [
        ['pointerRemote', devices, d => d.key, d => `${d.playerNumber ? "Player " + d.playerNumber + " · " : ""}${d.name}`],
        ['pointerDisplay', displays, d => d.id, d => `${d.name} — ${d.width} × ${d.height}`]
      ]) {
        el(id).replaceChildren(...values.map(d => new Option(label(d), value(d))));
      }
      if (devices.some(d => d.key === previousRemote)) el('pointerRemote').value = previousRemote;
      if (displays.some(d => d.id === previousDisplay)) el('pointerDisplay').value = previousDisplay;
    }
    for (const id of ['pointerSelect', 'pointerCalibrate']) el(id).disabled = !state.available || !devices.length || !displays.length;
    el('pointerEnable').disabled = !state.calibrated || !state.irVisible || state.calibrating
      || el('pointerRemote').value !== state.deviceKey || el('pointerDisplay').value !== state.displayId;
    el('pointerStatus').textContent = state.error || (!state.available ? 'Pointer output is unavailable on this desktop.'
      : state.enabled ? 'Pointer enabled. Press Home to stop.'
      : state.calibrated ? 'Calibration saved. Aim at the bar and enable the pointer.'
      : 'Pointer is off. Select a remote and calibrate its screen.');
    if (state.calibrating && !overlay.hidden) {
      const step = Math.max(0, Math.min(3, state.step));
      el('pointerTarget').style.left = [5, 95, 95, 5][step] + '%';
      el('pointerTarget').style.top = [5, 5, 95, 95][step] + '%';
      el('pointerHint').textContent = `Target ${step + 1} of 4. ` + (state.error || (state.stable ? 'Steady — press A to record.' : 'Point at the green dot and hold still.')) + ' Esc or Home cancels.';
    } else if (!starting && !overlay.hidden) close(false);
  }};
})();
