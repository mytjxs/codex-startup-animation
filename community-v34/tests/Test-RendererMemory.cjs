'use strict';
// Optional development test. Original media, isolated offline browser, no pressure allocation.
// node Test-RendererMemory.cjs result.json ambient.mp4 [renderer.js]
const fs=require('fs'),path=require('path'),assert=require('assert/strict'),crypto=require('crypto');
const {chromium}=require('playwright');
const output=process.argv[2],media=process.argv[3],sourcePath=process.argv[4]||path.resolve(__dirname,'..','renderer.js');
if(!output||!media)throw Error('Output JSON and original ambient MP4 required');
const source=fs.readFileSync(sourcePath,'utf8'),chrome=process.env.CHROME_BIN||path.join(process.env.ProgramFiles,'Google/Chrome/Application/chrome.exe');
const report={passed:false,checks:[],networkBlocked:true,actualCodexStarted:false,pressureSimulated:true,sourceSha256:crypto.createHash('sha256').update(source).digest('hex')};
const check=(label,actual,expected=true)=>{assert.deepEqual(actual,expected,label);report.checks.push(label);};
let browser;const deadline=setTimeout(()=>{if(browser)void browser.close().catch(()=>{});},28000);
(async()=>{
 browser=await chromium.launch({executablePath:chrome,headless:true,timeout:5000,args:['--disable-background-networking','--disable-component-update','--no-first-run']});
 const context=await browser.newContext();await context.route('**/*',r=>r.abort());const page=await context.newPage();page.setDefaultTimeout(3000);
 async function setup(){
  await page.setContent('<html><head><style>html,body,#root,main{width:100%;height:100%;margin:0}</style></head><body><div id="root"><main class="_MainContentSurface_test_2" data-app-shell-main-surface="default"><div class="_WorkspaceContent_test_2"></div></main></div></body></html>');
  await page.evaluate(()=>{window.blobs=[];window.revoked=[];window.loadsWithSource=0;const create=URL.createObjectURL.bind(URL),revoke=URL.revokeObjectURL.bind(URL),load=HTMLMediaElement.prototype.load;URL.createObjectURL=f=>{const u=create(f);blobs.push({url:u,name:f.name,size:f.size});return u;};URL.revokeObjectURL=u=>{revoked.push(u);revoke(u);};HTMLMediaElement.prototype.load=function(){if(this.getAttribute('src'))loadsWithSource++;return load.call(this);};});
  check('Install '+report.checks.length,(await page.evaluate(source)).code,'installed');
  check('Configure '+report.checks.length,(await page.evaluate(()=>__codexDragonWallpaperV1.configure({managed:true,documentGeneration:'0123456789abcdef0123456789abcdef'}))).ok);
  await page.locator('#codex-dragon-file-v1').setInputFiles(media);
 }
 async function state(){return page.evaluate(()=>__codexDragonWallpaperV1.status());}
 async function attach(){await page.evaluate(()=>{window.attachResult=null;void __codexDragonWallpaperV1.attach({deferReveal:true}).then(r=>window.attachResult=r);});}
 async function visible(){try{await page.waitForFunction(()=>{const s=__codexDragonWallpaperV1.status();return s.playing&&s.firstFrameConfirmed&&s.stageVisible&&s.wallpaperApplied;},null,{timeout:5000});}catch(e){report.lastSyntheticState=await state();throw e;}}
 await setup();
 await page.evaluate(()=>__codexDragonWallpaperV1.memoryPressure(true));await attach();
 check('Critical before attach creates no Blob',await page.evaluate(()=>blobs.length),0);
 check('Critical before attach does not load decoder',await page.evaluate(()=>loadsWithSource),0);
 let s=await state();check('Critical attach retains file session',[s.attached,s.dormant,s.memoryPressure,s.prepared,s.firstFrameConfirmed,s.playing,s.disposed],[true,true,true,false,false,false,false]);
 check('Attach resolves as suspended',await page.evaluate(()=>window.attachResult),{ok:true,code:'hibernating'});
 check('No decoder deadline while suspended',s.preparationTimerPending,false);
 check('No duplicate attach',(await page.evaluate(()=>__codexDragonWallpaperV1.attach())).code,'already-attached');
 await page.evaluate(()=>__codexDragonWallpaperV1.reveal());check('Reveal while critical stays suspended',(await state()).dormant);
 await page.evaluate(()=>__codexDragonWallpaperV1.memoryPressure(false));await visible();s=await state();
 check('Fresh recovered first frame',[s.videoWidth,s.videoHeight,s.activeClip,s.memoryPressure,s.recoveryNeeded],[2560,1440,'ambient',false,false]);
 check('One source after pressure recovery',await page.evaluate(()=>blobs.length),1);
 const generation=s.mediaGeneration;
 await page.evaluate(()=>__codexDragonWallpaperV1.memoryPressure(true));s=await state();check('Visible decoder releases on critical',[s.dormant,s.prepared,s.firstFrameConfirmed,s.playing],[true,false,false,false]);
 check('Released Blob count',await page.evaluate(()=>revoked.length),1);
 await page.evaluate(()=>__codexDragonWallpaperV1.pause());await page.evaluate(()=>__codexDragonWallpaperV1.memoryPressure(false));
 check('Manual pause prevents pressure wake',await page.evaluate(()=>blobs.length),1);
 check('Manual pause preserved',(await state()).manuallyPaused);
 await page.evaluate(()=>__codexDragonWallpaperV1.reveal());await visible();check('Resume uses fresh generation',(await state()).mediaGeneration>generation);
 await page.evaluate(()=>{__codexDragonWallpaperV1.memoryPressure(true);__codexDragonWallpaperV1.dispose();});
 check('Disable leaves no stage',await page.locator('#codex-dragon-stage-v1').count(),0);
 check('All allocated sources released',await page.evaluate(()=>blobs.length===revoked.length));
 // Pressure arriving during the first decode must not convert it into a fault.
 await setup();await page.evaluate(()=>{window.attachResult=null;void __codexDragonWallpaperV1.attach({deferReveal:true}).then(r=>window.attachResult=r);__codexDragonWallpaperV1.memoryPressure(true);__codexDragonWallpaperV1.reveal();});
 s=await state();check('In-flight prepare safely hibernates',[s.dormant,s.prepared,s.recoveryNeeded,s.preparationTimerPending],[true,false,false,false]);
 await page.evaluate(()=>__codexDragonWallpaperV1.memoryPressure(false));await visible();
 check('In-flight prepare later resolves',(await page.evaluate(()=>window.attachResult)).ok);
 await page.evaluate(()=>__codexDragonWallpaperV1.dispose());
 report.passed=true;console.log(JSON.stringify({passed:true,checks:report.checks.length}));
})().catch(e=>{report.error=e.message;console.error(e.message);process.exitCode=1;}).finally(async()=>{clearTimeout(deadline);if(browser)await browser.close();fs.writeFileSync(output,JSON.stringify(report,null,2));});
