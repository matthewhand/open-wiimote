import { test, expect } from './support/fixtures';
import { AUTH, TOKEN, device, stateWith } from './support/state';
const screen = { id:'wide', name:'Ultrawide', x:-5120,y:0,width:5120,height:1440,fingerprint:'wide:-5120:0:5120:1440' };
const pointer = () => ({ enabled:false,available:true,error:null,deviceKey:'one',displayId:'wide',calibrated:false,calibrating:false,step:0,stable:false,irVisible:true,x:null,y:null,displays:[screen] });

test('chooses one remote and its ultrawide display without touching gamepad or avatar calibration', async ({ page }) => {
  let p=pointer();const calls:any[]=[];
  const snapshot=()=>({...stateWith([device('one','First',''),device('two','Second','')]),pointer:p});
  await page.route('**/api/state',r=>r.fulfill({json:snapshot()}));
  await page.route('**/api/pointer',r=>{const data=r.request().postDataJSON();calls.push(data);if(data.action==='select')p={...p,deviceKey:data.deviceKey,displayId:data.displayId};return r.fulfill({json:snapshot()});});
  await page.goto(`/?token=${TOKEN}`);
  await page.locator('#pointerRemote').selectOption('two');await page.locator('#pointerSelect').click();
  expect(calls).toEqual([{action:'select',deviceKey:'two',displayId:'wide'}]);
  await expect(page.locator('#pointerDisplay')).toContainText('5120 × 1440');
  await expect(page.locator('#pointerEnable')).toBeDisabled();
});

test('full-screen calibration follows server targets and Escape cancels it',async({page})=>{
  let p=pointer();const calls:string[]=[];
  const snapshot=()=>({...stateWith([device('one','First','')]),pointer:p});
  await page.addInitScript(()=>{
    let full:Element|null=null;
    Object.defineProperty(document,'fullscreenElement',{get:()=>full});
    Element.prototype.requestFullscreen=async function(){full=this;document.dispatchEvent(new Event('fullscreenchange'));};
    document.exitFullscreen=async()=>{full=null;document.dispatchEvent(new Event('fullscreenchange'));};
  });
  await page.route('**/api/state',r=>r.fulfill({json:snapshot()}));
  await page.route('**/api/pointer',r=>{const data=r.request().postDataJSON();calls.push(data.action);if(data.action==='calibrate')p={...p,calibrating:true};if(data.action==='cancel')p={...p,calibrating:false};return r.fulfill({json:snapshot()});});
  await page.goto(`/?token=${TOKEN}`);await page.locator('#pointerCalibrate').click();
  await expect(page.locator('#pointerCalibration')).toBeVisible();
  await expect(page.locator('#pointerHint')).toContainText('Target 1 of 4');
  p={...p,step:2,stable:true};await expect(page.locator('#pointerHint')).toContainText('Target 3 of 4');
  await expect(page.locator('#pointerTarget')).toHaveCSS('left',/px$/);
  await page.evaluate(()=>document.exitFullscreen());
  await expect(page.locator('#pointerCalibration')).toBeHidden();
  await expect.poll(()=>calls.includes('cancel')).toBe(true);
});

test('stale tracking and an unapplied remote choice cannot enable the pointer',async({page})=>{
  let p={...pointer(),calibrated:true,irVisible:false};
  await page.route('**/api/state',r=>r.fulfill({json:{...stateWith([device('one','First',''),device('two','Second','')]),pointer:p}}));
  await page.goto(`/?token=${TOKEN}`);await expect(page.locator('#pointerEnable')).toBeDisabled();
  p={...p,irVisible:true};await expect(page.locator('#pointerEnable')).toBeEnabled();
  await page.locator('#pointerRemote').selectOption('two');await expect(page.locator('#pointerEnable')).toBeDisabled();
  await page.waitForTimeout(300);await expect(page.locator('#pointerEnable')).toBeDisabled();
});

test('pointer actions require authentication and POST and reject absent devices without enabling output',async({request})=>{
  expect((await request.post('/api/pointer',{data:{action:'enable'}})).status()).toBe(401);
  expect((await request.get('/api/pointer',{headers:AUTH})).status()).toBe(405);
  expect((await request.post('/api/pointer',{headers:AUTH,data:{action:'select',deviceKey:'missing',displayId:'wide'}})).status()).toBe(400);
  const state=await(await request.get('/api/state',{headers:AUTH})).json();expect(state.pointer.enabled).toBe(false);
});
