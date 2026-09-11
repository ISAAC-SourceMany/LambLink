// Run after dotnet build of this test project. Requires playwright and Chromium.
// NODE_PATH may point to the bundled node_modules; CHROME_PATH can override the executable.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {pathToFileURL}=require('node:url');
const {spawn}=require('node:child_process');
const {createInterface}=require('node:readline');
const {chromium}=require('playwright');
const {PNG}=require('pngjs');

// Computed background-color can be transparent while Chromium paints an opaque
// iframe canvas. Check the rendered alpha channel outside all overlay cards.
async function assertTransparent(page,label){
  const png=PNG.sync.read(await page.screenshot({omitBackground:true,clip:{x:1800,y:960,width:16,height:16}}));
  for(let i=3;i<png.data.length;i+=4)
    assert.equal(png.data[i],0,`${label}: background must have zero alpha; RGBA=${[...png.data.subarray(i-3,i+1)]}`);
}

(async()=>{
  const data=fs.mkdtempSync(path.join(os.tmpdir(),'LambLink-overlay-'));
  const fixture=spawn('dotnet',[path.join(__dirname,'bin/Release/net8.0/LambLink.OverlayRecoveryTests.dll'),data],{windowsHide:true});
  const lines=[];
  let stderr='';
  createInterface({input:fixture.stdout}).on('line',line=>lines.push(line));
  fixture.stderr.on('data',chunk=>stderr+=chunk);
  async function waitLine(prefix,from=0){
    const deadline=Date.now()+10000;
    while(Date.now()<deadline){
      const line=lines.slice(from).find(line=>line.startsWith(prefix));
      if(line)return line;
      if(fixture.exitCode!==null)throw new Error(stderr||'Fixture exited');
      await new Promise(resolve=>setTimeout(resolve,30));
    }
    throw new Error('Timeout: '+prefix+'\n'+stderr);
  }
  async function command(value){
    const from=lines.length;
    fixture.stdin.write(value+'\n');
    await waitLine('ACK '+value,from);
  }
  let browser;
  try{
    const file=(await waitLine('BOOTSTRAP ')).slice(10);
    browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_PATH||'C:/Program Files/Google/Chrome/Application/chrome.exe'});
    const page=await browser.newPage({viewport:{width:1920,height:1080}});
    const url=pathToFileURL(file).href;
    const visible=()=>page.waitForFunction(()=>document.querySelector('iframe').style.visibility==='visible',null,{timeout:15000});
    const hidden=()=>page.waitForFunction(()=>document.querySelector('iframe').style.visibility==='hidden',null,{timeout:10000});
    await page.goto(url);
    await hidden();
    await assertTransparent(page,'Server offline');
    const first=await page.locator('iframe').getAttribute('src');
    await page.waitForFunction(first=>document.querySelector('iframe').src!==first,first,{timeout:7000});
    assert.equal(page.url(),url,'Offline recovery must retain the local entry point');
    await page.evaluate(()=>window.postMessage({type:'lamblink-overlay-rendered'},'*'));
    await hidden();
    await command('START');
    await visible();
    await page.frameLocator('iframe').locator('#wrap.show').waitFor();
    await assertTransparent(page,'Connected raffle');
    for(const colorScheme of ['light','dark']){
      await page.emulateMedia({colorScheme});
      await assertTransparent(page,`Connected raffle / ${colorScheme} OS theme`);
    }
    await command('STATUS');
    const stable=await page.locator('iframe').getAttribute('src');
    await page.waitForTimeout(4500);
    assert.equal(await page.locator('iframe').getAttribute('src'),stable,'Healthy page must not reload');
    console.log('PASS: browser first, delayed server, real raffle rendering, stable connection');

    await command('STOP');
    await hidden();
    await assertTransparent(page,'Server stopped');
    assert.equal(page.url(),url);
    await command('START');
    await visible();
    await command('STATUS');
    console.log('PASS: server restart hides stale content and recovers automatically');

    await page.route('**/overlay/state?**',route=>route.fulfill({status:503,body:'unavailable'}));
    await hidden();
    await page.unroute('**/overlay/state?**');
    await visible();
    console.log('PASS: state endpoint failure does not count as a rendered connection');

    await page.route('**/overlay/state?**',()=>{});
    await hidden();
    await page.unrouteAll({behavior:'ignoreErrors'});
    await visible();
    await command('STATUS');
    console.log('PASS: stalled state requests recover without manual refresh');

    const second=await browser.newPage();
    await second.goto(url);
    await second.waitForFunction(()=>document.querySelector('iframe').style.visibility==='visible');
    await second.frameLocator('iframe').locator('#wrap.show').waitFor();
    await second.close();
    console.log('PASS: server first and browser reopen');
    await command('DONATION');
    const donation=page.frameLocator('iframe').locator('#donationWrap.show');
    await donation.waitFor();
    assert.match(await donation.innerText(),/복구 테스트/);
    const bounds=await donation.boundingBox();
    assert.equal(bounds.x,18);
    assert.equal(bounds.y,18);
    assert.equal(bounds.width,480);
    await assertTransparent(page,'Donation card');
    console.log('PASS: donation card renders at original viewport coordinates after recovery');
    await waitLine('DISPLAY 11111111111111111111111111111111 PAGE_CONFIRMED');
    await page.close();
    await new Promise(resolve=>setTimeout(resolve,3500));
    await command('OFFLINE_DONATION');
    await waitLine('DISPLAY 22222222222222222222222222222222 WAITING_CLIENT');
    await new Promise(resolve=>setTimeout(resolve,3500));
    const reconnect=await browser.newPage();
    await reconnect.goto(url);
    const queuedCard=reconnect.frameLocator('iframe').locator('#donationWrap.show');
    await queuedCard.filter({hasText:'미연결 테스트'}).waitFor();
    await waitLine('DISPLAY 22222222222222222222222222222222 PAGE_CONFIRMED');
    await reconnect.close();
    console.log('PASS: offline donation waits beyond its display duration and confirms on reconnect');
  }finally{
    if(browser)await browser.close();
    fixture.stdin.end('EXIT\n');
    if(fixture.exitCode===null)await new Promise(resolve=>{
      const timeout=setTimeout(()=>{fixture.kill();resolve();},5000);
      fixture.once('exit',()=>{clearTimeout(timeout);resolve();});
    });
    fs.rmSync(data,{recursive:true,force:true});
  }
})().catch(error=>{console.error(error);process.exitCode=1;});
