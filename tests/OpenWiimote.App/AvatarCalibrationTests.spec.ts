import type { Page } from '@playwright/test';
import { expect, test } from './support/fixtures';
import { TOKEN, device, stateWith } from './support/state';
const KEY = 'openwiimote.poseCalibration';
const A = '02:00:00:00:00:01', B = '02:00:00:00:00:02';
const S = Math.SQRT1_2;
const HOLDS = [[0,0,1], [0,S,S], [0,-S,S], [-S,0,S], [S,0,S]];
const BASIS = { x:{x:1,y:0,z:0}, y:{x:0,y:1,z:0}, z:{x:0,y:0,z:1} };
async function open(page: Page, two = false) {
  const remotes = [device('one','Remote A',A)];
  if (two) remotes.push(device('two','Remote B',B));
  const payload = stateWith(remotes);
  let polls = 0, frozen: string | null = null, invalid = false, moving = false;
  const writes: string[] = [];
  page.on('request', request => {
    if (request.method() === 'POST' && !request.url().endsWith('/api/auth')) writes.push(request.url());
  });
  await page.route('**/api/state', route => {
    polls++;
    for (const remote of payload.devices as typeof remotes) {
      remote.state.lastReport = frozen || new Date().toISOString();
      if (moving) { remote.state.accelX = polls % 2 ? .8 : -.8; remote.state.accelZ = .6; }
    }
    return route.fulfill({json: invalid ? {} : payload});
  });
  await page.goto(`/?token=${TOKEN}`);
  await expect(page.locator('#app')).toBeVisible();
  await page.locator('#poseCalibration summary').click();
  const wait = async (count = 8) => {
    const start = polls;
    await expect.poll(() => polls-start, {intervals:[25,50]}).toBeGreaterThanOrEqual(count);
  };
  const hold = async (v: number[], index = 0) => {
    moving = false;
    Object.assign(remotes[index].state,{accelX:v[0],accelY:v[1],accelZ:v[2]});
    await wait();
  };
  return { payload,remotes,writes,wait,hold,
    freeze: (age = 0) => { frozen = new Date(Date.now()-age).toISOString(); },
    move: () => { moving = true; }, invalidate: (value:boolean) => { invalid = value; } };
}
const saved = (page: Page) => page.evaluate(key => JSON.parse(localStorage.getItem(key) || '{}'),KEY);
async function complete(page: Page, h: Awaited<ReturnType<typeof open>>) {
  for (const vector of HOLDS) { await h.hold(vector); await page.locator('#poseCapture').click(); }
}
test('five still holds save only this remote and leave raw readings and profiles untouched', async ({page}, info) => {
  const h = await open(page,true);
  for(let i=0;i<5;i++) {
    await expect(page.locator('#poseStepLabel')).toHaveText(`${i+1} / 5`);
    await h.hold(HOLDS[i]);await page.locator('#poseCapture').click();
  }
  await expect(page.locator('#poseStepLabel')).toHaveText('Setup saved');
  expect(Object.keys(await saved(page))).toEqual([A]);
  expect((await saved(page))[A].basis).toEqual(BASIS);
  await expect(page.locator('#detailAccel')).toContainText('0.71');
  expect(h.writes).toEqual([]);expect(h.payload.activeProfile).toBe('Default');
  await page.reload();
  await page.locator('#poseCalibration summary').click();
  await expect(page.locator('#poseStepLabel')).toHaveText('Setup saved');
  expect(Object.keys(await saved(page))).toEqual([A]);
  await page.locator('#poseCalibration').screenshot({path:info.outputPath('guided-setup.png')});
});
test('saved setup survives reload and renders both pitch signs without IR inventing a turn', async ({page}) => {
  await page.addInitScript(({key,basis,address})=>localStorage.setItem(key,JSON.stringify({[address]:{basis}})),{key:KEY,basis:BASIS,address:A});
  const h = await open(page);
  await expect(page.locator('#poseStepLabel')).toHaveText('Setup saved');
  for(const sign of [-1,1]) {
    await h.hold([0,sign*S,S]);
    await expect.poll(async()=> (await page.evaluate('PoseView.rotate(target, [-1,0,0])'))[1]).toBeCloseTo(sign*S,3);
  }
  const before = await page.evaluate('({...target})');
  Object.assign(h.remotes[0].state,{irPoints:[{isValid:true,x:1,y:10},{isValid:true,x:20,y:10}]});
  await h.wait();expect(await page.evaluate('({...target})')).toEqual(before);
  await page.reload();await page.locator('#poseCalibration summary').click();
  await expect(page.locator('#poseStepLabel')).toHaveText('Setup saved');
});
test('switching remotes or reconnecting cancels incomplete holds', async ({page}) => {
  const h=await open(page,true);await h.hold(HOLDS[0]);await page.locator('#poseCapture').click();
  await page.locator('#tabs [data-tab="device:two"]').click();
  await expect(page.locator('#poseStepLabel')).toHaveText('1 / 5');
  await page.locator('#poseCapture').click();await expect(page.locator('#poseStatus')).toContainText('Hold still');
  await h.hold(HOLDS[0],1);await page.locator('#poseCapture').click();
  h.payload.devices=[];await h.wait();
  await expect(page.locator('#poseCapture')).toBeDisabled();
  h.payload.devices=h.remotes;await h.wait();
  await expect(page.locator('#poseStepLabel')).toHaveText('1 / 5');expect(await saved(page)).toEqual({});
});
test('frozen recent reports and moving gravity cannot produce a hold', async ({page}) => {
  const h=await open(page);h.freeze();await h.wait();
  await page.locator('#poseCapture').click();await expect(page.locator('#poseStepLabel')).toHaveText('1 / 5');
  h.move();await h.wait();await page.locator('#poseCapture').click();expect(await saved(page)).toEqual({});
});
test('repeated directions never replace an existing valid setup', async ({page}) => {
  await page.addInitScript(({key,basis,address})=>localStorage.setItem(key,JSON.stringify({[address]:{basis}})),{key:KEY,basis:BASIS,address:A});
  const h=await open(page);await page.locator('#poseRestart').click();
  for(let i=0;i<5;i++) {await h.hold(HOLDS[0]);await page.locator('#poseCapture').click();}
  await expect(page.locator('#poseStatus')).toContainText('did not separate');
  expect((await saved(page))[A].basis).toEqual(BASIS);
});
test('one-point level belongs to its remote, keeps gravity, and does not change controller tilt', async ({page}) => {
  const h=await open(page,true);await h.hold([.5,0,Math.sqrt(.75)]);
  await page.locator('#viewOptions summary').click();await page.locator('#tiltCalibration').check();
  await expect(page.locator('#tiltCalibrationNote')).toContainText('for this remote');
  expect((await saved(page))[A].level.x).toBe(.5);
  const vector=await page.evaluate('overrideVector(selectedDevice().state)');
  expect(Math.hypot(vector.x,vector.y,vector.z)).toBeCloseTo(1,5);expect(vector.z).toBeCloseTo(1,5);
  await expect(page.locator('#detailTilt')).toHaveText('0° / 0°');
  await page.locator('#tabs [data-tab="device:two"]').click();await expect(page.locator('#tiltCalibration')).not.toBeChecked();
  expect((await saved(page))[B]).toBeUndefined();expect(h.writes).toEqual([]);
});
test('storage failures are visible and a finished setup can be saved on retry', async ({page}) => {
  await page.addInitScript(()=>{
    const original=Storage.prototype.setItem;
    (window as any).restoreAvatarStorage=()=>{Storage.prototype.setItem=original;};
    Storage.prototype.setItem=function(key,value) { if(key==='openwiimote.poseCalibration') throw new Error('QuotaExceededError');return original.call(this,key,value); };
  });
  const h=await open(page);await complete(page,h);
  await expect(page.locator('#poseCapture')).toHaveText('Retry save');
  await expect(page.locator('#poseStatus')).toContainText('Cannot save');expect(await saved(page)).toEqual({});
  await page.evaluate('restoreAvatarStorage()');await page.locator('#poseCapture').click();
  await expect(page.locator('#poseStepLabel')).toHaveText('Setup saved');expect((await saved(page))[A].basis).toEqual(BASIS);
});
test('service failure cancels a draft and saved setup on another remote remains intact', async ({page}) => {
  await page.addInitScript(({key,basis,address})=>localStorage.setItem(key,JSON.stringify({[address]:{basis}})),{key:KEY,basis:BASIS,address:B});
  const h=await open(page,true);await h.hold(HOLDS[0]);await page.locator('#poseCapture').click();
  h.invalidate(true);await expect(page.locator('#poseStatus')).toContainText('Service connection lost');
  h.invalidate(false);await h.wait();await expect(page.locator('#poseStepLabel')).toHaveText('1 / 5');
  expect((await saved(page))[A]).toBeUndefined();expect((await saved(page))[B].basis).toEqual(BASIS);
});
test('clear is per-remote and malformed storage can recover through a complete setup', async ({page}) => {
  await page.addInitScript(()=>localStorage.setItem('openwiimote.poseCalibration','[]'));
  const h=await open(page);await complete(page,h);expect((await saved(page))[A].basis).toEqual(BASIS);
  await page.locator('#poseClear').click();expect(await saved(page)).toEqual({});
  await expect(page.locator('#poseStepLabel')).toHaveText('1 / 5');
});
test('a failed level clear restores the checkbox and keeps the saved reference', async ({page}) => {
  await page.addInitScript(({key,address})=>{
    localStorage.setItem(key,JSON.stringify({[address]:{level:{x:.5,y:0,z:Math.sqrt(.75)}}}));
    const original=Storage.prototype.setItem;
    Storage.prototype.setItem=function(k,v) { if(k===key) throw new Error('quota');return original.call(this,k,v); };
  },{key:KEY,address:A});
  const h=await open(page);await page.locator('#viewOptions summary').click();
  await expect(page.locator('#tiltCalibration')).toBeChecked();
  // Use click: uncheck() would wait for the rejected change to remain unchecked.
  await page.locator('#tiltCalibration').click();
  await expect(page.locator('#tiltCalibration')).toBeChecked();
  await expect(page.locator('#alert')).toContainText('Cannot save');
  expect((await saved(page))[A].level.x).toBe(.5);expect(h.writes).toEqual([]);
});
test('malformed older view preferences cannot break avatar rendering', async ({page}) => {
  await page.addInitScript(()=>localStorage.setItem('openwiimote.tiltOverrides',JSON.stringify({mapX:null,mapY:{},mapZ:17,maxTilt:-100,rest:{x:0,y:0,z:1}})));
  const h=await open(page);await h.hold([.5,0,Math.sqrt(.75)]);
  await page.locator('#tiltOverrides summary').click();
  await expect(page.locator('#devMapX')).toHaveValue('x');
  await expect(page.locator('#devMapY')).toHaveValue('y');
  await expect(page.locator('#devMapZ')).toHaveValue('z');
  await expect(page.locator('#devMaxTilt')).toHaveValue('5');
  const q=await page.evaluate('({...target})');
  expect(Object.values(q).every(Number.isFinite)).toBe(true);
  expect(h.writes).toEqual([]);
});
