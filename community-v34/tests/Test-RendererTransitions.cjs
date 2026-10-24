'use strict';
// Offline structural transitions only. Uses the unchanged packaged MP4, never
// opens an official application, account, or network connection.
// node Test-RendererTransitions.cjs output.json ambient.mp4 [renderer.js]
const fs=require('fs'),path=require('path'),assert=require('assert/strict'),crypto=require('crypto');
const {chromium}=require('playwright');
const [output,media,sourcePath=path.resolve(__dirname,'..','renderer.js')]=process.argv.slice(2);
if(!output||!media)throw Error('Output JSON and original ambient MP4 required');
const source=fs.readFileSync(sourcePath,'utf8');
const report={passed:false,checks:[],networkBlocked:true,actualCodexStarted:false,sourceSha256:crypto.createHash('sha256').update(source).digest('hex')};
const check=(name,actual,expected=true)=>{assert.deepEqual(actual,expected,name);report.checks.push(name);};
const shell='<div id="root"><div id="outer" class="_Layout_test_1" data-app-shell-frame="true"><div id="menu" class="_ApplicationMenuTopBar_test_2">Menu</div><div id="workspace" class="_Workspace_test_3"><aside id="app-shell-sidebar" class="_LeftPanel_test_4"><nav data-app-navigation-rail>Nav</nav></aside><main id="main" class="_MainContentSurface_test_5" data-app-shell-main-surface="default"><div id="content" class="_WorkspaceContent_test_6"><div id="viewport" class="_MainContentViewport_test_7"><div id="frame" class="_MainContentFrame_test_8"><div id="fade-wrapper" data-app-shell-main-content-top-fade="visible"><div id="fade" class="_MainContentTopFade_test_9" aria-hidden="true"></div><article id="article"><p id="stream">Synthetic</p><div id="composer" class="_ComposerLayoutRoot_composer2_1" data-composer-surface-variant="default"><textarea></textarea><button id="send">Send</button></div></article></div></div></div></div></main></div></div></div>';
const native='html,body,#root,#outer{height:700px;width:1000px;margin:0}:root{--color-surface:#1d1e2c;--color-surface-elevated-secondary:#343646}#outer,#menu,#app-shell-sidebar{background:var(--color-surface)}#fade{background-image:linear-gradient(var(--color-surface),transparent);pointer-events:none}#composer{background:var(--composer-layout-surface-background,var(--color-surface-elevated-secondary));backdrop-filter:var(--composer-layout-surface-backdrop-filter,none)}article{padding:100px}';
let browser;const started=Date.now(),deadline=setTimeout(()=>{if(browser)void browser.close();},28000);
(async()=>{
 browser=await chromium.launch({executablePath:process.env.CHROME_BIN||path.join(process.env.ProgramFiles,'Google/Chrome/Application/chrome.exe'),headless:true,timeout:5000,args:['--disable-background-networking','--disable-component-update','--no-first-run']});
 const context=await browser.newContext({viewport:{width:1000,height:700}});await context.route('**/*',r=>r.abort());const page=await context.newPage(),errors=[];page.on('pageerror',e=>errors.push(String(e)));page.setDefaultTimeout(2500);
 const status=()=>page.evaluate(()=>__codexDragonWallpaperV1.status());
 const painted=()=>page.evaluate(()=>document.documentElement.getAttribute('data-codex-dragon-background')==='v1');
 async function setup(mutate=null){
  await page.setContent('<html><head><style>'+native+'</style></head><body>'+shell+'</body></html>');
  await page.evaluate(()=>{
   window.callbacks=0;window.observations=[];window.styleChanges=0;const Native=MutationObserver;
   window.MutationObserver=class extends Native{constructor(fn){super((...a)=>{callbacks++;fn(...a);});}observe(n,o){observations.push({id:n.id,tag:n.tagName,options:o});return super.observe(n,o);}};
   window.NativeObserver=Native;
  });
  if(mutate)await page.evaluate(mutate);
  check('Install '+report.checks.length,(await page.evaluate(source)).ok);
  return status();
 }
 let s=await setup();check('Initial audited main accepted',s.layoutSupported);
 await page.evaluate(()=>{document.documentElement.setAttribute('data-codex-dragon-background','v1');document.getElementById('fade').className='_MainContentTopFade_test_99';});
 check('Compiler suffix update keeps supported shape',(await status()).layoutSupported);
 check('In-place fade suffix rebinds before attach',await page.locator('#fade').evaluate(n=>getComputedStyle(n).backgroundImage),'none');
 await page.evaluate(()=>document.getElementById('menu').className='_ApplicationMenuTopBar_test_101');check('Menu compiler suffix accepted',(await status()).layoutSupported);check('Menu remains clear',await page.locator('#menu').evaluate(n=>getComputedStyle(n).backgroundColor),'rgba(0, 0, 0, 0)');
 await page.evaluate(()=>document.getElementById('frame').className='_MainContentFrame_foreign_8');s=await status();check('Cached frame rejects foreign hash',s.layoutSupported,false);check('Cached frame rejection reason',s.layoutReason,'shell-shape');
 await page.evaluate(()=>document.getElementById('frame').className='_MainContentFrame_test_8');check('Corrected frame recovers',(await status()).layoutSupported);
 // The renderer can be installed while the official route is still mounting.
 s=await setup(()=>{window.savedMain=document.getElementById('main');window.savedMain.remove();});check('Incomplete shell not promoted',s.layoutSupported,false);
 await page.evaluate(()=>document.getElementById('workspace').append(window.savedMain));check('Delayed first main recognized without reinstall',(await status()).layoutSupported);
 await page.evaluate(()=>{const n=document.getElementById('main').cloneNode(true);n.id='cached';n.className='_MainContentSurface_foreign_1';n.setAttribute('data-app-shell-active-page','false');document.getElementById('workspace').append(n);});check('Inactive malformed cache stays ignored',(await status()).layoutSupported);
 await page.evaluate(()=>document.getElementById('cached').remove());
 await page.locator('#codex-dragon-file-v1').setInputFiles(media);check('Real media attaches',(await page.evaluate(()=>__codexDragonWallpaperV1.attach())).ok);await page.waitForFunction(()=>document.documentElement.hasAttribute('data-codex-dragon-background'),null,{timeout:10000});
 await page.evaluate(()=>{window.savedVideo=document.querySelector('#codex-dragon-stage-v1 video');new NativeObserver(()=>styleChanges++).observe(document.getElementById('codex-dragon-style-v1'),{childList:true});callbacks=0;});
 // No status poll: a validated chrome node changing only its class must wake
 // the bounded observer, while chat text/class mutations must remain invisible.
 await page.evaluate(()=>document.getElementById('fade').className='_MainContentTopFade_test_102');
 await page.waitForFunction(()=>getComputedStyle(document.getElementById('fade')).backgroundImage==='none');check('Attached fade attr wakes without status poll',await page.evaluate(()=>callbacks>0));
 await page.evaluate(()=>document.getElementById('frame').className='_MainContentFrame_foreign_8');await page.waitForFunction(()=>!document.documentElement.hasAttribute('data-codex-dragon-background'));s=await status();check('Attached malformed attr suspends',s.layoutSuspended&&!s.stageVisible&&s.layoutReason==='shell-shape');
 await page.evaluate(()=>document.getElementById('frame').className='_MainContentFrame_test_8');await page.waitForFunction(()=>document.documentElement.hasAttribute('data-codex-dragon-background'));check('Corrected attr restores same decoder',await page.evaluate(()=>savedVideo===document.querySelector('#codex-dragon-stage-v1 video')));
 await page.evaluate(()=>document.getElementById('main').setAttribute('data-app-shell-main-surface','unknown'));await page.waitForFunction(()=>!document.documentElement.hasAttribute('data-codex-dragon-background'));check('Semantic main attr loss suspends',(await status()).layoutSuspended);
 await page.evaluate(()=>document.getElementById('main').setAttribute('data-app-shell-main-surface','default'));await page.waitForFunction(()=>document.documentElement.hasAttribute('data-codex-dragon-background'));check('Semantic main attr restoration resumes',await painted());
 await page.evaluate(()=>{callbacks=0;styleChanges=0;for(let i=0;i<100;i++){document.getElementById('fade').className='_MainContentTopFade_test_'+(200+i);document.getElementById('menu').className='_ApplicationMenuTopBar_test_'+(400+i);}});
 await page.waitForFunction(()=>getComputedStyle(document.getElementById('fade')).backgroundImage==='none');await page.waitForTimeout(50);check('Rapid shell attr batch has bounded callbacks',await page.evaluate(()=>callbacks<=3));check('Rapid shell attr batch rebuilds once',await page.evaluate(()=>styleChanges),1);
 await page.evaluate(()=>{callbacks=0;styleChanges=0;for(let i=0;i<100;i++){const n=document.getElementById('stream');n.className='stream-'+i;n.textContent='Token '+i;document.getElementById('article').append(document.createElement('span'));}});await page.waitForTimeout(80);check('Conversation attr and text streaming never wakes observer',await page.evaluate(()=>callbacks),0);check('Conversation streaming never rewrites CSS',await page.evaluate(()=>styleChanges),0);
 const observations=await page.evaluate(()=>observations);check('No broad class subtree observer',observations.every(o=>!(o.options.subtree&&o.options.attributeFilter&&o.options.attributeFilter.includes('class'))));check('No broad child list observer',observations.every(o=>!o.options.childList||!o.options.subtree));check('No conversation observer',observations.every(o=>!['article','stream'].includes(o.id)));
 await page.evaluate(()=>{document.getElementById('outer').querySelectorAll('[class]').forEach(n=>{if(n.id!=='composer')n.className=n.className.replaceAll('_test_','_nextBuild9_');});document.getElementById('outer').className='_Layout_nextBuild9_17';});await page.waitForFunction(()=>document.documentElement.hasAttribute('data-codex-dragon-background'));s=await status();check('Atomic full module hash transition recognized',s.layoutSupported&&!s.layoutSuspended);check('Full module transition retains one decoder',await page.evaluate(()=>document.querySelectorAll('#codex-dragon-stage-v1 video').length===1&&savedVideo===document.querySelector('#codex-dragon-stage-v1 video')));
 check('Sidebar remains transparent',await page.locator('#app-shell-sidebar').evaluate(n=>getComputedStyle(n).backgroundColor),'rgba(0, 0, 0, 0)');check('Composer original alpha preserved',await page.locator('#composer').evaluate(n=>getComputedStyle(n).backgroundColor.includes('/ 0.6)')));
 await page.locator('textarea').fill('Synthetic input');check('Typing preserved',await page.locator('textarea').inputValue(),'Synthetic input');await page.evaluate(()=>document.getElementById('send').onclick=()=>window.clicked=true);await page.locator('#send').click();check('Click preserved',await page.evaluate(()=>window.clicked));
 // A fresh official frame may mount before its H/page-surface flag is set.
 // Its semantic anchor is observable, but is never sufficient for styling.
 await page.evaluate(()=>{document.getElementById('outer').remove();document.getElementById('root').insertAdjacentHTML('beforeend','<div id="next-frame" class="_Layout_route42_1 _PageSurfaceLayout_route42_2" data-app-shell-frame="true" data-app-shell-page-surface="false"><div id="next-row" data-app-shell-workspace-row="true"><div id="next-decoration" class="_PageSurface_route42_3" aria-hidden="true"></div><section>Fixture</section></div></div>');});await page.waitForFunction(()=>!document.documentElement.hasAttribute('data-codex-dragon-background'));check('Incomplete replacement frame stays unpainted',await painted(),false);
 await page.evaluate(()=>document.getElementById('next-frame').setAttribute('data-app-shell-page-surface','true'));await page.waitForFunction(()=>document.documentElement.hasAttribute('data-codex-dragon-background'));check('Delayed official frame flag wakes without host status',(await status()).layoutSurfaceKind,'frame');
 await page.evaluate(()=>document.getElementById('next-row').setAttribute('data-app-shell-workspace-row','false'));await page.waitForFunction(()=>!document.documentElement.hasAttribute('data-codex-dragon-background'));check('Frame row flag loss suspends without host status',await painted(),false);
 await page.evaluate(()=>document.getElementById('next-row').setAttribute('data-app-shell-workspace-row','true'));await page.waitForFunction(()=>document.documentElement.hasAttribute('data-codex-dragon-background'));check('Frame row flag restoration resumes',(await status()).layoutSupported);
 await page.evaluate(()=>document.getElementById('next-decoration').setAttribute('aria-hidden','false'));await page.waitForFunction(()=>!document.documentElement.hasAttribute('data-codex-dragon-background'));check('Non-inert decoration never painted',(await status()).layoutReason,'frame-shape');
 await page.evaluate(()=>document.getElementById('next-decoration').setAttribute('aria-hidden','true'));await page.waitForFunction(()=>document.documentElement.hasAttribute('data-codex-dragon-background'));check('Restored inert decoration resumes same video',await page.evaluate(()=>document.querySelector('video')===savedVideo));
 await page.evaluate(()=>__codexDragonWallpaperV1.dispose());check('Dispose removes owned nodes',await page.locator('#codex-dragon-stage-v1,#codex-dragon-file-v1,#codex-dragon-style-v1').count(),0);check('No page errors',errors,[]);report.passed=true;
})().catch(e=>{report.error=String(e.stack||e);process.exitCode=1;}).finally(async()=>{clearTimeout(deadline);if(browser)await browser.close();report.elapsedMs=Date.now()-started;fs.writeFileSync(output,JSON.stringify(report,null,2));console.log(JSON.stringify({passed:report.passed,checks:report.checks.length,elapsedMs:report.elapsedMs,error:report.error}));});
