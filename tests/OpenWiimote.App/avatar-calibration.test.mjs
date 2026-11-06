import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const api = require('../../src/OpenWiimote.App/wwwroot/avatar-calibration.js');
const basis = { x: { x: 1, y: 0, z: 0 }, y: { x: 0, y: 1, z: 0 }, z: { x: 0, y: 0, z: 1 } };
const device = (key = 'one', address = '02:00:00:00:00:01') => ({ key, bluetoothAddress: address });
const at = (d, xyz, report) => ({ ...d, state: { accelX: xyz[0], accelY: xyz[1], accelZ: xyz[2], lastReport: new Date(report).toISOString() } });
const holds = [[0,0,1], [0,Math.SQRT1_2,Math.SQRT1_2], [0,-Math.SQRT1_2,Math.SQRT1_2], [-Math.SQRT1_2,0,Math.SQRT1_2], [Math.SQRT1_2,0,Math.SQRT1_2]];
function hold(session, xyz, start = 10000, d = device(), report = t => t) {
  for (let dt = 0; dt <= 720; dt += 180) session.observe(at(d, xyz, report(start + dt)), start + dt);
  return session.capture(start + 720);
}
function memory(initial = {}) {
  const values = new Map([[api.KEY, JSON.stringify(initial)]]);
  return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value) };
}
test('five moderate holds complete a basis without changing raw readings', () => {
  const session = new api.Session();
  let result;
  holds.forEach((v,i) => { result = hold(session, v, 10000+i*1200); });
  assert.equal(result.kind, 'complete');
  assert.equal(result.key, device().bluetoothAddress);
  assert.ok(api.validBasis(result.basis));
  assert.deepEqual(holds[0], [0,0,1]);
});
test('a completed basis can be retried after storage failure without new holds', () => {
  const s = new api.Session();
  holds.forEach((v,i) => hold(s,v,10000+i*1200));
  const pending = s.capture(20000);
  assert.equal(pending.kind,'complete');
  assert.equal(s.capture(30000),pending);
  s.finish();
  assert.equal(s.capture(40000).kind,'started');
  assert.equal(s.step,0);
});
test('constant gravity with slow advancing controller reports can be captured', () => {
  const s = new api.Session();
  for (const dt of [0,180,360,540,720,900,1080,1260]) s.observe(at(device(),holds[0],10000+Math.floor(dt/1000)*1000),10000+dt);
  assert.equal(s.capture(11260).kind,'next');
});
test('one frozen but still-recent report never becomes multiple source reports', () => {
  const s = new api.Session();
  assert.equal(hold(s,holds[0],10000,device(),()=>10000).kind,'error');
  assert.equal(s.step,0);
});
test('a stale source or expired browser sample window is not captured', () => {
  const s = new api.Session();
  assert.equal(hold(s,holds[0],10000,device(),t=>t-3000).kind,'error');
  hold(s,holds[0],14000);
  assert.equal(s.still(17000),null);
});
test('out-of-order source reports discard the current hold', () => {
  const s = new api.Session();
  for (const dt of [0,180,360,540]) s.observe(at(device(),holds[0],10000+dt),10000+dt);
  s.observe(at(device(),holds[0],10000),10720);
  assert.equal(s.capture(10720).kind,'error');
});
test('changing remotes cannot combine holds or samples', () => {
  const s = new api.Session();hold(s,holds[0]);
  assert.equal(s.step,1);
  s.select(device('two','02:00:00:00:00:02'));
  assert.equal(s.step,0);assert.deepEqual(s.draft,{});assert.equal(s.still(11000),null);
});
test('disconnect and a replacement session for the same address cancel drafts', () => {
  const s = new api.Session();hold(s,holds[0]);s.select(null);assert.equal(s.key,'');assert.equal(s.capture(11000).kind,'error');
  hold(s,holds[0],12000);s.select(device('replacement'));assert.equal(s.step,0);assert.deepEqual(s.draft,{});
});
test('invalid and moving gravity cannot establish a hold', () => {
  for (const xyz of [[0,0,0],[0,0,3],[NaN,0,1],[undefined,0,1]]) {
    assert.equal(hold(new api.Session(),xyz).kind,'error');
  }
  const s=new api.Session();
  for (let dt=0;dt<=720;dt+=180) s.observe(at(device(),[dt%360 ? .8 : -.8,0,.6],10000+dt),10000+dt);
  assert.equal(s.capture(10720).kind,'error');
});
test('five identical directions fail without yielding a saved basis', () => {
  const s=new api.Session();let result;
  holds.forEach((_,i)=>{result=hold(s,holds[0],10000+i*1200);});
  assert.equal(result.kind,'error');assert.equal(s.step,0);assert.equal(s.pending,null);
});
test('new per-remote writes retain another remote and unknown entry metadata', () => {
  const a=device(),b=device('two','02:00:00:00:00:02');
  const storage=memory({[b.bluetoothAddress]:{basis},[a.bluetoothAddress]:{future:{kept:true}}});const store=new api.Store(storage);
  assert.ok(store.put(a,{basis}));assert.deepEqual(store.get(b).basis,basis);
  assert.deepEqual(JSON.parse(storage.getItem(api.KEY))[a.bluetoothAddress].future,{kept:true});
});
test('legacy device-key bases survive canonical address writes and selective clear', () => {
  const a=device(),b=device('two','02:00:00:00:00:02');const storage=memory({[a.key]:{basis,gyroBias:{x:1}},[b.bluetoothAddress]:{basis}});const store=new api.Store(storage);
  assert.deepEqual(store.get(a).basis,basis);store.put(a,{level:{x:0,y:0,z:1}});
  assert.deepEqual(store.get(a).basis,basis);assert.ok(store.clear(a));assert.equal(store.get(a).basis,null);assert.deepEqual(store.get(b).basis,basis);
});
test('malformed storage and invalid bases cannot poison the view', () => {
  for(const value of ['[]','null','"text"','{bad']) {
    const storage=memory();storage.setItem(api.KEY,value);const store=new api.Store(storage);
    assert.equal(store.get(device()).basis,null);assert.ok(store.put(device(),{basis}));assert.deepEqual(store.get(device()).basis,basis);
  }
  for(const invalid of [{...basis,x:{x:2,y:0,z:0}},{...basis,x:{x:-1,y:0,z:0}},{...basis,y:basis.x}]) assert.equal(api.validBasis(invalid),false);
});
test('storage denial is reported and cannot replace an existing setup', () => {
  const storage=memory({[device().bluetoothAddress]:{basis}});storage.setItem=()=>{throw new Error('quota');};const store=new api.Store(storage);
  assert.equal(store.put(device(),{basis:null}),false);assert.equal(store.clear(device()),false);assert.deepEqual(store.get(device()).basis,basis);
});
test('per-remote levelling preserves gravity and leaves other remotes unchanged', () => {
  const v={x:.5,y:0,z:Math.sqrt(.75)};const result=api.project(v,{level:v});
  assert.ok(Math.abs(result.z-1)<1e-10);assert.ok(Math.hypot(result.x,result.y)<1e-10);
  assert.deepEqual(api.project(v,{}),v);assert.equal(api.project({x:NaN,y:0,z:1},{}),null);
});
test('guided basis and one-point level compose in the same coordinate frame', () => {
  const rotated={x:{x:0,y:1,z:0},y:{x:0,y:0,z:1},z:{x:1,y:0,z:0}};
  const raw={x:Math.SQRT1_2,y:0,z:Math.SQRT1_2};
  const result=api.project(raw,{basis:rotated,level:raw});
  assert.ok(Math.abs(result.z-1)<1e-10);assert.ok(Math.hypot(result.x,result.y)<1e-10);
});
test('a denied storage read cannot cause a destructive write or clear', () => {
  let writes=0;
  const store=new api.Store({getItem:()=>{throw new Error('denied');},setItem:()=>{writes++;}});
  assert.equal(store.get(device()).basis,null);
  assert.equal(store.put(device(),{basis}),false);assert.equal(store.clear(device()),false);assert.equal(writes,0);
});
test('a newer report with missing gravity cannot be followed by an older valid capture', () => {
  const s=new api.Session();
  for(const dt of [0,180,360]) s.observe(at(device(),holds[0],10000+dt),10000+dt);
  s.observe(at(device(),[undefined,0,1],10720),10720);
  s.observe(at(device(),holds[0],10540),10900);
  assert.equal(s.capture(10900).kind,'error');
});
test('legacy lowercase addresses survive load, write and clear from uppercase discovery', () => {
  const current=device('one','02:AB:CD:EF:00:01');
  const key=current.bluetoothAddress.toLowerCase();
  const storage=memory({[key]:{basis,extra:'keep'}});const store=new api.Store(storage);
  assert.deepEqual(store.get(current).basis,basis);
  assert.ok(store.put(current,{level:{x:0,y:0,z:1}}));
  assert.deepEqual(store.get(current).basis,basis);
  assert.equal(JSON.parse(storage.getItem(api.KEY))[current.bluetoothAddress].extra,'keep');
  assert.ok(store.clear(current));assert.deepEqual(JSON.parse(storage.getItem(api.KEY)),{});
});
