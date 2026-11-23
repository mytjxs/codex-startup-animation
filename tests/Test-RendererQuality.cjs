'use strict';
// Optional browser test: install playwright and set CHROME_BIN when needed.
const fs=require('fs'),path=require('path'),assert=require('assert/strict');
const {chromium}=require('playwright');
const source=fs.readFileSync(path.resolve(__dirname,'..','renderer.js'),'utf8');
const output=process.argv[2];if(!output)throw new Error('An output JSON path outside source is required');
const chromeBin=process.env.CHROME_BIN||(process.env.ProgramFiles&&path.join(process.env.ProgramFiles,'Google/Chrome/Application/chrome.exe'));
if(!chromeBin)throw new Error('Set CHROME_BIN to an installed Chrome executable');
(async()=>{
 const browser=await chromium.launch({executablePath:chromeBin,headless:true,args:['--disable-background-networking','--disable-component-update','--no-first-run']});
 let checks=0;const check=(actual,expected,label)=>{assert.deepEqual(actual,expected,label);checks++;};
 try {
  const context=await browser.newContext();await context.route('**/*',r=>r.abort());const page=await context.newPage();
  await page.setContent('<html><body><div id="root"></div></body></html>');
  check((await page.evaluate(source)).code,'installed','renderer installed');
  const known=await page.evaluate(()=>{
   const video=document.querySelector('#codex-dragon-stage-v1 video');let calls=0;
   video.getVideoPlaybackQuality=()=>{calls++;return {totalVideoFrames:200,droppedVideoFrames:25,corruptedVideoFrames:0};};
   const state=window.__codexDragonWallpaperV1.status();return {total:state.videoTotalFrames,dropped:state.videoDroppedFrames,corrupted:state.videoCorruptedFrames,calls,paused:video.paused,time:video.currentTime,attached:state.attached};
  });
  check(known,{total:200,dropped:25,corrupted:0,calls:1,paused:true,time:0,attached:false},'one read and no playback mutation');
  const malformed=await page.evaluate(()=>{
   const video=document.querySelector('#codex-dragon-stage-v1 video');video.getVideoPlaybackQuality=()=>({totalVideoFrames:'20',droppedVideoFrames:-2,corruptedVideoFrames:NaN});
   const s=window.__codexDragonWallpaperV1.status();return [s.videoTotalFrames,s.videoDroppedFrames,s.videoCorruptedFrames];
  });check(malformed,[-1,-1,-1],'malformed values are unknown');
  const ranges=await page.evaluate(()=>{
   const video=document.querySelector('#codex-dragon-stage-v1 video');video.getVideoPlaybackQuality=()=>({totalVideoFrames:2147483647,droppedVideoFrames:2147483648,corruptedVideoFrames:0.5});
   const s=window.__codexDragonWallpaperV1.status();return [s.videoTotalFrames,s.videoDroppedFrames,s.videoCorruptedFrames];
  });check(ranges,[2147483647,-1,-1],'quality bounds');
  for(const mode of ['missing','throws','null']){
   const unknown=await page.evaluate(mode=>{
    const video=document.querySelector('#codex-dragon-stage-v1 video');video.getVideoPlaybackQuality=mode==='missing'?undefined:mode==='throws'?()=>{throw new Error('synthetic');}:()=>null;
    const s=window.__codexDragonWallpaperV1.status();return [s.videoTotalFrames,s.videoDroppedFrames,s.videoCorruptedFrames,s.attached,s.disposed];
   },mode);check(unknown,[-1,-1,-1,false,false],mode+' does not affect renderer lifecycle');
  }
  await page.evaluate(()=>window.__codexDragonWallpaperV1.dispose());
  check(await page.locator('#codex-dragon-stage-v1,#codex-dragon-file-v1,#codex-dragon-style-v1').count(),0,'cleanup unchanged');
  fs.writeFileSync(output,JSON.stringify({passed:true,checks,networkBlocked:true,actualCodexStarted:false},null,2));
  console.log(JSON.stringify({passed:true,checks}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
