'use strict';
// Optional development test: npm install playwright; set CHROME_BIN if needed.
// Usage: node Test-RendererLayout.cjs result.json ambient.mp4 [renderer.js]
// Fresh offline profile and synthetic UI only. Never launches or modifies Codex.
const fs=require('fs'),path=require('path'),assert=require('assert/strict'),crypto=require('crypto');
const {chromium}=require('playwright');
const output=process.argv[2],media=process.argv[3],sourcePath=process.argv[4]||path.resolve(__dirname,'..','renderer.js');
if(!output||!media)throw new Error('Provide an output JSON path outside source and the packaged ambient MP4');
const source=fs.readFileSync(sourcePath,'utf8');
const chrome=process.env.CHROME_BIN||(process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Google/Chrome/Application/chrome.exe'));
if(!chrome)throw new Error('Set CHROME_BIN to an installed Chrome executable');
const report={passed:false,checks:[],sourceSha256:crypto.createHash('sha256').update(source).digest('hex'),networkBlocked:true,actualCodexStarted:false,realPackagedMedia:true};
const started=Date.now();
let activeBrowser=null,deadlineHit=false;
const deadline=setTimeout(()=>{deadlineHit=true;if(activeBrowser)void activeBrowser.close().catch(()=>{});},27000);
const check=(name,actual,expected=true)=>{assert.deepEqual(actual,expected,name);report.checks.push(name);};
const allowed=['not-checked','root-missing','main-count','main-token','workspace-shape','shell-shape','composer-ambiguous','frame-count','frame-token','frame-row','frame-shape','ready'];
// Synthetic shell uses the audited semantic hierarchy. Minimal paint fixtures
// model the native custom-property contract; no extracted app bundle is needed.
const composer='<div id="composer" class="_ComposerLayoutRoot_5est5_2" data-composer-surface-variant="default"><textarea aria-label="Test input"></textarea><button id="send">Send</button></div>';
const shell=`<div id="root"><div id="outer" data-app-shell-frame="true"><div class="_Workspace_etoz9_2"><aside id="app-shell-sidebar"><nav data-app-navigation-rail>Navigation</nav></aside><main class="_MainContentSurface_etoz9_2" data-app-shell-main-surface="default"><div class="_WorkspaceContent_etoz9_2"><div class="_MainContentViewport_etoz9_2"><div id="frame" class="_MainContentFrame_etoz9_2"><div id="wrapper" data-app-shell-main-content-top-fade="visible"><div id="headerfade" class="_MainContentTopFade_etoz9_2" aria-hidden="true"></div><article id="article"><p id="stream">Synthetic text</p><div class="thread-scroll-container"><div><div aria-hidden="true" class="pointer-events-none sticky bottom-0 z-10"><div id="fade" aria-hidden="true" class="pointer-events-none absolute inset-x-0 z-0 from-surface"></div></div></div><div id="footerwrap" data-thread-scroll-footer="true"><div id="footer" aria-hidden="true" class="pointer-events-none absolute inset-x-0 z-0 bottom-0 bg-surface"></div>${composer}</div></div></article></div></div></div></div></main></div></div></div>`;
const native=`html,body,#root{height:700px;width:1000px;margin:0}#outer{height:700px;background:var(--color-surface)}#app-shell-sidebar{position:absolute;width:100px;height:700px;background:var(--color-surface)}nav[data-app-navigation-rail]{height:100%;background:var(--color-surface)}main{height:700px;}article{position:relative;padding:100px 100px;}#footerwrap{position:relative;}#footer{position:absolute;inset:0;background:var(--color-surface);pointer-events:none}#headerfade,#fade{pointer-events:none;background-image:linear-gradient(var(--color-surface),transparent)}._ComposerLayoutRoot_5est5_2,._ComposerLayoutRoot_next42_8{position:relative;height:120px;background:var(--composer-layout-surface-background,var(--color-surface-elevated-secondary));backdrop-filter:var(--composer-layout-surface-backdrop-filter,none)}`;
(async()=>{
 const browser=await chromium.launch({executablePath:chrome,headless:true,timeout:5000,args:['--disable-background-networking','--disable-component-update','--no-first-run']});activeBrowser=browser;
 try{
  const context=await browser.newContext({viewport:{width:1000,height:700}});await context.route('**/*',r=>r.abort());const page=await context.newPage(),errors=[];page.setDefaultTimeout(2500);page.on('pageerror',e=>errors.push(String(e)));
  async function setup({theme='dark',engine='electron',prefs={},mutate=null}={}){
   await page.emulateMedia({colorScheme:theme,contrast:'no-preference',forcedColors:'none',...prefs});
   const vars=theme==='dark'?'--color-surface:#1d1e2c;--color-surface-elevated-secondary:#343646;color:#ececf1':'--color-surface:#fff;--color-surface-elevated-secondary:#f2f2f2;color:#222';
   await page.setContent(`<html ${engine===null?'':`data-codex-window-type="${engine}"`}><head><style>${native}:root{${vars}}</style></head><body>${shell}</body></html>`);
   if(mutate)await page.evaluate(mutate);
   check('Install '+report.checks.length,(await page.evaluate(source)).code,'installed');
   const state=await page.evaluate(()=>__codexDragonWallpaperV1.status());
   if(state.layoutSupported)await page.evaluate(()=>document.documentElement.setAttribute('data-codex-dragon-background','v1'));
   return state;
  }
  async function state(){const s=await page.evaluate(()=>__codexDragonWallpaperV1.status());check('Bounded layout reason '+report.checks.length,allowed.includes(s.layoutReason));return s;}
  async function bg(id){return page.locator('#'+id).evaluate(n=>getComputedStyle(n).backgroundColor);}
  async function delayedSetup(){return setup({mutate:()=>{window.delayedComposer=document.getElementById('composer').outerHTML;document.getElementById('composer').remove();}});}
  // Regression first: baseline r5 caches a missing composer forever.
  check('Initial shell without composer supported',(await delayedSetup()).layoutSupported);
  await page.evaluate(()=>document.getElementById('footerwrap').insertAdjacentHTML('beforeend',window.delayedComposer));
  check('Delayed composer remains supported',(await page.evaluate(()=>__codexDragonWallpaperV1.status())).layoutSupported);
  check('Delayed composer obtains original glass alpha',(await bg('composer')).includes('/ 0.6)'));
  check('Delayed composer diagnostic',(await state()).layoutReason,'ready');
  await page.evaluate(()=>document.getElementById('composer').className='_ComposerLayoutRoot_next42_8');
  check('In-place composer class change supported',(await state()).layoutSupported);
  check('In-place new class gets original glass alpha',(await bg('composer')).includes('/ 0.6)'));
  await page.evaluate(()=>{const n=document.getElementById('composer').cloneNode(true);n.id='inactive';n.setAttribute('data-app-shell-active-page','false');n.className='_ComposerLayoutRoot_foreign_2';document.getElementById('footerwrap').append(n);});
  check('Inactive foreign composer ignored',(await state()).layoutSupported);
  await page.evaluate(()=>document.getElementById('inactive').removeAttribute('data-app-shell-active-page'));
  check('Two differing active tokens rejected',(await state()).layoutReason,'composer-ambiguous');
  await page.evaluate(()=>document.getElementById('inactive').remove());
  check('Composer ambiguity removal recovers',(await state()).layoutReason,'ready');
  await page.evaluate(()=>document.getElementById('composer').className+=' _ComposerLayoutRoot_other_2');
  check('Duplicate component tokens rejected',(await state()).layoutReason,'composer-ambiguous');
  await page.evaluate(()=>document.getElementById('composer').className='_ComposerLayoutRoot_5est5_2');
  check('Valid token recovers',(await state()).layoutReason,'ready');
  await page.evaluate(()=>{window.replacementComposer=document.getElementById('composer').outerHTML;document.getElementById('composer').remove();});
  check('Composer removal after cache supported',(await state()).layoutReason,'ready');
  check('Removed composer binding retired',await page.locator('#codex-dragon-style-v1').evaluate(n=>n.textContent.includes('._ComposerLayoutRoot_5est5_2')),false);
  await page.evaluate(()=>document.getElementById('footerwrap').insertAdjacentHTML('beforeend',window.replacementComposer.replace('_5est5_2','_next42_8')));
  check('Replacement composer after cache supported',(await state()).layoutReason,'ready');check('Replacement composer after cache gets glass',(await bg('composer')).includes('/ 0.6)'));
  await page.evaluate(()=>{const p=document.getElementById('footerwrap');for(let i=0;i<15;i++){const n=document.getElementById('composer').cloneNode(true);n.id='extra'+i;p.append(n);}});
  check('Exactly 16 matching composers accepted',(await state()).layoutReason,'ready');
  await page.evaluate(()=>{const n=document.getElementById('composer').cloneNode(true);n.id='seventeenth';document.getElementById('footerwrap').append(n);});
  check('Composer bound of 16 enforced',(await state()).layoutReason,'composer-ambiguous');
  for(const engine of ['electron','tauri','wry','webview','browser',null]){const s=await setup({engine});check(String(engine)+' strict shape accepted',s.layoutSupported);check(String(engine)+' ready reason',s.layoutReason,'ready');check(String(engine)+' frame transparent',await bg('outer'),'rgba(0, 0, 0, 0)');}
  const invalid=[
   ['frame-token',()=>document.querySelector('main').remove()],
   ['main-count',()=>document.querySelector('main').after(document.querySelector('main').cloneNode(true))],
   ['frame-token',()=>document.querySelector('main').setAttribute('data-app-shell-active-page','false')],
   ['main-token',()=>document.querySelector('main').className='ordinary-content'],
   ['workspace-shape',()=>document.querySelector('._WorkspaceContent_etoz9_2').className='_WorkspaceContent_foreign_2'],
   ['shell-shape',()=>document.getElementById('frame').className='_MainContentFrame_foreign_2']
  ];
  for(const[reason,mutate]of invalid){const s=await setup({engine:'tauri',mutate});check('Malformed shape rejected '+report.checks.length,s.layoutSupported,false);check('Malformed reason '+report.checks.length,s.layoutReason,reason);check('Malformed page never marked '+report.checks.length,await page.evaluate(()=>document.documentElement.hasAttribute('data-codex-dragon-background')),false);}
  await setup();await page.evaluate(()=>document.getElementById('root').replaceWith(document.createElement('div')));check('Root replacement detected',(await state()).layoutReason,'root-missing');
  await page.setContent('<html><body><main>Unrelated page</main></body></html>');check('No root rejected',(await page.evaluate(source)).layoutReason,'root-missing');
  for(const theme of ['dark','light']){
   await setup({theme});check(theme+' composer glass',(await bg('composer')).includes('/ 0.6)'));check(theme+' footer transparent',await bg('footer'),'rgba(0, 0, 0, 0)');check(theme+' top fade cleared',await page.locator('#headerfade').evaluate(n=>getComputedStyle(n).backgroundImage),'none');
   check(theme+' text opacity unchanged',await page.locator('#stream').evaluate(n=>getComputedStyle(n).opacity),'1');
   await page.locator('textarea').fill('Synthetic input test');check(theme+' typing preserved',await page.locator('textarea').inputValue(),'Synthetic input test');
   await page.evaluate(()=>document.getElementById('send').addEventListener('click',()=>window.clicked=true));await page.locator('#send').click({timeout:2000});check(theme+' click preserved',await page.evaluate(()=>window.clicked));
   await page.evaluate(()=>__codexDragonWallpaperV1.dispose());check(theme+' native backdrop restored',await bg('outer'),theme==='dark'?'rgb(29, 30, 44)':'rgb(255, 255, 255)');check(theme+' owned nodes removed',await page.locator('#codex-dragon-stage-v1,#codex-dragon-file-v1,#codex-dragon-style-v1').count(),0);
  }
  for(const prefs of [{contrast:'more'},{forcedColors:'active'}]){await setup({prefs});check('Accessibility frame opaque '+JSON.stringify(prefs),(await bg('outer'))!=='rgba(0, 0, 0, 0)');check('Accessibility composer native '+JSON.stringify(prefs),!(await bg('composer')).includes('/ 0.6)'));await page.emulateMedia({contrast:'no-preference',forcedColors:'none'});check('Accessibility toggle restores clear frame '+JSON.stringify(prefs),await bg('outer'),'rgba(0, 0, 0, 0)');check('Accessibility toggle restores original glass '+JSON.stringify(prefs),(await bg('composer')).includes('/ 0.6)'));}
  // Actual attach installs the real production observers and decodes the exact
  // supplied media. This checks the lazily-mounted composer without new polling.
  await setup({mutate:()=>{
   window.delayedComposer=document.getElementById('composer').outerHTML;document.getElementById('composer').remove();window.callbacks=0;window.observations=[];
   const monitor=window.lifecycleMonitor={timers:new Set(),listeners:[],activeObservers:new Set(),observerInstances:0};
   const nativeTimeout=window.setTimeout,nativeClear=window.clearTimeout;
   window.setTimeout=function(fn,ms,...args){const id=nativeTimeout(function(){monitor.timers.delete(id);fn(...args);},ms);monitor.timers.add(id);return id;};window.clearTimeout=function(id){monitor.timers.delete(id);return nativeClear(id);};
   const nativeAdd=EventTarget.prototype.addEventListener,nativeRemove=EventTarget.prototype.removeEventListener;
   const capture=o=>typeof o==='boolean'?o:!!(o&&o.capture),owned=t=>t===window||t===document||t instanceof HTMLVideoElement;
   EventTarget.prototype.addEventListener=function(type,fn,options){if(owned(this)&&!monitor.listeners.some(x=>x.target===this&&x.type===type&&x.fn===fn&&x.capture===capture(options)))monitor.listeners.push({target:this,type,fn,capture:capture(options)});return nativeAdd.call(this,type,fn,options);};
   EventTarget.prototype.removeEventListener=function(type,fn,options){monitor.listeners=monitor.listeners.filter(x=>!(x.target===this&&x.type===type&&x.fn===fn&&x.capture===capture(options)));return nativeRemove.call(this,type,fn,options);};
   const Native=window.MutationObserver;window.MutationObserver=class extends Native{constructor(fn){super((...args)=>{window.callbacks++;fn(...args)});monitor.observerInstances++;}observe(n,o){monitor.activeObservers.add(this);window.observations.push({id:n.id,tag:n.tagName,options:o});return super.observe(n,o);}disconnect(){monitor.activeObservers.delete(this);return super.disconnect();}};
   window.lifecycleCounts=()=>({timers:monitor.timers.size,listeners:monitor.listeners.length,observers:monitor.activeObservers.size,instances:monitor.observerInstances});
  }});
  await page.locator('#codex-dragon-file-v1').setInputFiles(media);
  check('Real packaged media attached',(await page.evaluate(()=>__codexDragonWallpaperV1.attach())).ok);
  await page.waitForFunction(()=>__codexDragonWallpaperV1.status().stageVisible,{},{timeout:10000});
  await page.evaluate(()=>{const v=document.querySelector('#codex-dragon-stage-v1 video');v.dispatchEvent(new Event('waiting'));window.callbacks=0;document.getElementById('footerwrap').insertAdjacentHTML('beforeend',window.delayedComposer);const until=performance.now()+45;while(performance.now()<until){}v.dispatchEvent(new Event('stalled'));v.dispatchEvent(new Event('canplay'));});await page.waitForTimeout(100);
  check('No broad child-list callback for lazy composer',await page.evaluate(()=>window.callbacks),0);
  check('Existing status discovers actual attached composer',(await state()).layoutReason,'ready');check('Attached composer gets original alpha',(await bg('composer')).includes('/ 0.6)'));
  const observations=await page.evaluate(()=>window.observations);check('No subtree child-list observer',observations.every(r=>!r.options.childList||!r.options.subtree));check('No article observer',observations.every(r=>r.tag!=='ARTICLE'&&r.id!=='stream'));
  await page.evaluate(()=>{window.callbacks=0;for(let i=0;i<100;i++){document.getElementById('stream').textContent='Streaming '+i;document.getElementById('article').append(document.createElement('span'));}});await page.waitForTimeout(100);check('Text streaming adds no observer callbacks',await page.evaluate(()=>window.callbacks),0);
  // Fail closed on ambiguity and recover using the existing status call.
  await page.evaluate(()=>document.getElementById('composer').className+=' _ComposerLayoutRoot_foreign_2');const blocked=await state();check('Attached ambiguous composer suspends',blocked.layoutSuspended&&!blocked.stageVisible&&blocked.layoutReason==='composer-ambiguous');
  check('Ambiguity removes appearance marker',await page.evaluate(()=>document.documentElement.hasAttribute('data-codex-dragon-background')),false);
  await page.evaluate(()=>document.getElementById('composer').className='_ComposerLayoutRoot_5est5_2');const recovered=await state();check('Attached valid composer restores layout',recovered.layoutSupported&&!recovered.layoutSuspended&&recovered.layoutReason==='ready');
  check('Recovery restores same glass alpha',(await bg('composer')).includes('/ 0.6)'));
  await page.waitForFunction(()=>__codexDragonWallpaperV1.status().playing,{},{timeout:2500});
  const countsBefore=await page.evaluate(()=>window.lifecycleCounts());report.lifecycleBefore=countsBefore;
  await page.evaluate(()=>window.savedRoute=document.querySelector('main').cloneNode(true));
  for(let cycle=0;cycle<6;cycle++){
   await page.evaluate(()=>document.querySelector('main').remove());
   const absent=await state();check('Unmount suspends media '+cycle,absent.layoutSuspended&&!absent.stageVisible&&absent.layoutReason==='frame-token');
   check('Unmount removes own appearance marker '+cycle,await page.evaluate(()=>document.documentElement.hasAttribute('data-codex-dragon-background')),false);
   await page.evaluate(cycle=>{const labels=['tauri','electron',null];const label=labels[cycle%labels.length];if(label===null)document.documentElement.removeAttribute('data-codex-window-type');else document.documentElement.setAttribute('data-codex-window-type',label);document.querySelector('._Workspace_etoz9_2').append(window.savedRoute.cloneNode(true));},cycle);
   await page.waitForFunction(()=>{const s=__codexDragonWallpaperV1.status();return s.layoutSupported&&s.stageVisible&&s.playing;},{},{timeout:2500});
   check('Remounted route keeps original input alpha '+cycle,(await bg('composer')).includes('/ 0.6)'));
   check('Remounted route retains one video '+cycle,await page.locator('#codex-dragon-stage-v1 video').count(),1);
  }
  const countsAfter=await page.evaluate(()=>window.lifecycleCounts());report.lifecycleAfter=countsAfter;
  check('Repeated routes do not add persistent listeners',countsAfter.listeners,countsBefore.listeners);
  check('Repeated routes reuse observer instances',countsAfter.instances,countsBefore.instances);
  check('Repeated routes keep active observer bound',countsAfter.observers,countsBefore.observers);
  check('Repeated routes keep timers bounded',countsAfter.timers<=countsBefore.timers+1);
  check('Media dimensions preserved',recovered.videoWidth,2560);check('Single video only',await page.locator('#codex-dragon-stage-v1 video').count(),1);
  check('Original video opacity retained',source.includes('to { opacity: .26; }'));
  await page.evaluate(()=>__codexDragonWallpaperV1.dispose());check('Real attach cleanup',await page.locator('#codex-dragon-stage-v1,#codex-dragon-file-v1,#codex-dragon-style-v1').count(),0);
  const countsDisposed=await page.evaluate(()=>window.lifecycleCounts());report.lifecycleDisposed=countsDisposed;
  check('Dispose releases every tracked timeout',countsDisposed.timers,0);check('Dispose removes every tracked listener',countsDisposed.listeners,0);check('Dispose disconnects every owned observer',countsDisposed.observers,0);
  check('No page errors',errors,[]);report.passed=true;
 }finally{await browser.close();}
})().catch(e=>{report.error=String(e.stack||e);process.exitCode=1;}).finally(()=>{clearTimeout(deadline);if(deadlineHit){report.passed=false;report.error='Renderer layout test reached its 27 second cleanup deadline';process.exitCode=1;}report.elapsedMs=Date.now()-started;fs.writeFileSync(output,JSON.stringify(report,null,2));console.log(JSON.stringify({passed:report.passed,checks:report.checks.length,elapsedMs:report.elapsedMs,error:report.error}));});
