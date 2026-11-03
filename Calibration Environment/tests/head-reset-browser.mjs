import { pathToFileURL, fileURLToPath } from 'node:url';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE_PATH ? pathToFileURL(process.env.PLAYWRIGHT_MODULE_PATH).href : 'playwright');
import assert from 'node:assert/strict';
const browser = await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-unsafe-swiftshader']});
const page = await browser.newPage({viewport:{width:1600,height:1100}});
const errors=[]; page.on('pageerror',e=>errors.push(e.message));
let x=0, stale=false, enabled=false;
await page.route('**/api/tracking', async route=>{
 if(route.request().method()==='POST'){enabled=route.request().postDataJSON().enabled; return route.fulfill({json:{ok:true}});}
 return route.fulfill({json:{status:'connected',message:'Synthetic browser check',sequence:1,ageMs:stale?2000:0,devices:[{serial:'test-head',kind:'tracker',connected:true,valid:true,matrix:[[1,0,0,x],[0,1,0,1.7],[0,0,1,1]]},{serial:'Null Serial Number',kind:'headset',connected:true,valid:true,matrix:[[1,0,0,0],[0,1,0,0],[0,0,1,0]]}]}});
});
try {
 await page.goto('http://127.0.0.1:8766');
 await page.waitForFunction(()=>document.querySelector('#head-quality').textContent==='Live');
 assert.ok(enabled);
 const draft=await page.evaluate(()=>JSON.parse(localStorage.getItem('elts-calibration-v1-draft')).config);
 assert.equal(draft.head.serial,'test-head'); assert.equal(draft.weapon.serial,'');
 await page.locator('#reset-head').click();
 await page.waitForFunction(()=>document.querySelector('#head-position').textContent==='XYZ 0.000 / 1.305 / 2.000 m');
 x=0.3;
 await page.waitForFunction(()=>document.querySelector('#head-position').textContent==='XYZ 0.300 / 1.305 / 2.000 m');
 await page.screenshot({path:fileURLToPath(new URL('../test-results/head-reset-desktop.png', import.meta.url)),fullPage:true});
 stale=true;
 await page.waitForFunction(()=>document.querySelector('#reset-head').disabled);
 assert.deepEqual(errors,[]);
 console.log('PASS: auto-connect, real tracker assignment, single tracker leaves Weapon empty, reset, continued motion, stale rejection, no page errors.');
} finally {await browser.close();}
