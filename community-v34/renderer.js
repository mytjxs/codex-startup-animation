(() => {
  'use strict';
  // Original, local-only renderer. No network, storage, chat text or UI polling.
  const key = '__codexDragonWallpaperV1';
  if (window[key] && typeof window[key].dispose === 'function') window[key].dispose();
  const stageId = 'codex-dragon-stage-v1';
  const fileId = 'codex-dragon-file-v1';
  const styleId = 'codex-dragon-style-v1';
  if ([stageId, fileId, styleId].some(id => document.getElementById(id))) {
    return { ok: false, code: 'element-conflict' };
  }
  const root = document.getElementById('root');
  if (!root || !document.body) return { ok: false, code: 'unsupported-layout', layoutReason: 'root-missing' };
  const marker = 'data-codex-dragon-background';
  const previousMarker = document.documentElement.getAttribute(marker);
  const style = document.createElement('style');
  style.id = styleId;
  const styleTemplate = `
/* Continuous wallpaper with clear window chrome. Keep text and controls opaque;
   only the audited background surfaces are transparent. */
html[data-codex-dragon-background="v1"] #root {
  position: relative;
  isolation: isolate;
  --dragon-glass-panel: color-mix(in srgb, var(--color-surface, #1d1e2c) 18%, transparent);
  --dragon-glass-header: transparent;
  --dragon-glass-input: color-mix(in srgb, var(--color-surface-elevated-secondary, #343646) 60%, transparent);
  --dragon-glass-blur: blur(12px);
  --dragon-chrome-text-shadow: 0 1px 2px rgb(0 0 0 / 35%);
}
html[data-codex-dragon-background="v1"] #root :is(
  [data-app-shell-frame],
  ._Workspace_yl8z4_2,
  main._MainContentSurface_yl8z4_2[data-app-shell-main-surface="default"],
  ._LeftPanel_yl8z4_2,
  #app-shell-sidebar
) { background-color: transparent !important; }

/* The list gets a thin tint, with no blur or per-row compositing surface. */
html[data-codex-dragon-background="v1"] #root :is(
  .app-shell-left-panel .sidebar-navigation:not(.sidebar-navigation .sidebar-navigation),
  nav[data-app-navigation-rail]
) {
  background-color: var(--dragon-glass-panel) !important;
  -webkit-backdrop-filter: none !important;
  backdrop-filter: none !important;
  text-shadow: var(--dragon-chrome-text-shadow);
}
html[data-codex-dragon-background="v1"] #root .app-shell-left-panel .sidebar-navigation .sidebar-navigation {
  background-color: transparent !important;
}

/* No frosted horizontal strip across the menu or conversation title. */
html[data-codex-dragon-background="v1"] #root :is(
  ._ApplicationMenuTopBar_yl8z4_2,
  ._FloatingHeader_yl8z4_2,
  [data-app-shell-titlebar][data-pip-obstacle="app-shell-header"],
  [data-app-shell-main-titlebar]
) {
  background-color: var(--dragon-glass-header) !important;
  -webkit-backdrop-filter: none !important;
  backdrop-filter: none !important;
  text-shadow: var(--dragon-chrome-text-shadow);
}

/* Native decoration also paints a separate surface-to-clear fade. Leave its
   geometry and the scrolled header divider intact, clearing only its paint. */
html[data-codex-dragon-background="v1"] #root ._MainContentTopFade_yl8z4_2 {
  background-image: none !important;
  background-color: transparent !important;
}

/* The composer keeps its existing reading surface and drag-target behavior. */
html[data-codex-dragon-background="v1"] #root main[data-app-shell-main-surface="default"] ._ComposerLayoutRoot_boifs_2:is(
  [data-composer-surface-variant="default"],
  [data-composer-surface-variant="opaque"],
  [data-composer-surface-variant="secondary"]
):not([data-composer-drag-active]) {
  --composer-layout-surface-background: var(--dragon-glass-input);
  --composer-layout-surface-backdrop-filter: var(--dragon-glass-blur);
}

@media (prefers-reduced-transparency: reduce), (prefers-contrast: more), (forced-colors: active) {
  html[data-codex-dragon-background="v1"] #root {
    --dragon-glass-panel: var(--color-surface, Canvas);
    --dragon-glass-header: var(--color-surface, Canvas);
    --dragon-glass-input: var(--color-surface-elevated-secondary, Canvas);
    --dragon-glass-blur: none;
    --dragon-chrome-text-shadow: none;
  }
  /* Match the ID specificity of the transparent shell rule above. */
  html[data-codex-dragon-background="v1"] #root :is([data-app-shell-frame], #app-shell-sidebar) {
    background-color: var(--color-surface, Canvas) !important;
  }
}

html[data-codex-dragon-background="v1"] #root main._MainContentSurface_yl8z4_2[data-app-shell-main-surface="default"] > ._WorkspaceContent_yl8z4_2::before { background-color: transparent !important; }
html[data-codex-dragon-background="v1"] #root [data-app-shell-frame="true"] > [data-app-shell-workspace-row="true"] > div[aria-hidden="true"]._PageSurface_yl8z4_2 { background-color: transparent !important; }
/* Clear only verified footer decorations. Glass variables style the composer;
   menus and controls retain their native behavior. Removing the marker restores
   the client's original backgrounds without changing its DOM or layout. */
html[data-codex-dragon-background="v1"] #root main._MainContentSurface_yl8z4_2[data-app-shell-main-surface="default"] > ._WorkspaceContent_yl8z4_2 [data-thread-scroll-footer="true"] > div[aria-hidden="true"].pointer-events-none.absolute.inset-x-0.z-0.bottom-0.bg-surface { background-color: transparent !important; }
html[data-codex-dragon-background="v1"] #root main._MainContentSurface_yl8z4_2[data-app-shell-main-surface="default"] > ._WorkspaceContent_yl8z4_2 .thread-scroll-container > div > div[aria-hidden="true"].pointer-events-none.sticky.bottom-0.z-10 > div[aria-hidden="true"].pointer-events-none.absolute.inset-x-0.z-0.from-surface { background-image: none !important; }
html[data-codex-dragon-background="v1"] #root main._MainContentSurface_yl8z4_2[data-app-shell-main-surface="default"] > ._WorkspaceContent_yl8z4_2 [data-thread-scroll-footer="true"]:has([data-thread-focus-mode]) { background-color: transparent !important; }
#codex-dragon-stage-v1 { position: absolute; inset: 0; z-index: -20; pointer-events: none; overflow: hidden; background: var(--color-surface, #101518); contain: strict; }
#codex-dragon-stage-v1 video { width: 100%; height: 100%; object-fit: cover; object-position: right center; opacity: 0; }
#codex-dragon-stage-v1[data-ready="true"] video { opacity: .26; animation: codex-dragon-reveal-v1 650ms ease both; }
@keyframes codex-dragon-reveal-v1 { from { opacity: 0; } to { opacity: .26; } }
#codex-dragon-stage-v1[data-probe="true"] { position: fixed; inset: auto 0 0 auto; z-index: 0; width: 1px; height: 1px; background: transparent; }
#codex-dragon-stage-v1[data-probe="true"]::after { display: none; }
#codex-dragon-stage-v1::after { content: ""; position: absolute; inset: 0; background: linear-gradient(90deg, color-mix(in srgb, var(--color-surface, #101518) 25%, transparent), color-mix(in srgb, var(--color-surface, #101518) 28%, transparent) 56%, color-mix(in srgb, var(--color-surface, #101518) 12%, transparent)); }
@media (prefers-reduced-motion: reduce) { #codex-dragon-stage-v1 video { animation: none; } }
`;
  style.textContent = styleTemplate;
  const input = document.createElement('input');
  input.id = fileId; input.type = 'file'; input.accept = 'video/mp4'; input.multiple = true; input.hidden = true;
  input.setAttribute('aria-hidden', 'true'); input.tabIndex = -1;
  const stage = document.createElement('div');
  stage.id = stageId; stage.hidden = true; stage.setAttribute('aria-hidden', 'true');
  const video = document.createElement('video');
  video.muted = true; video.defaultMuted = true; video.loop = false;
  video.playsInline = true; video.preload = 'auto'; video.controls = false;
  video.setAttribute('aria-hidden', 'true'); video.tabIndex = -1;
  stage.appendChild(video);
  let disposed = false, attached = false, playing = false, code = 'idle';
  let objectUrl = null, generation = 0, frameRevision = 0, timer = null, frame = null, settlePending = null;
  let loaded = null, markerApplied = false, layoutSupported = false, mediaReady = false;
  let layoutReason = 'not-checked';
  let firstFrameConfirmed = false, playStarted = false, probeOnly = false;
  let pauseRevision = 0, playAttempt = 0;
  let deferred = false, revealRequested = false, revealed = false, manuallyPaused = false;
  let revealWaiting = false, mediaSurface = null;
  let layoutSuspended = false, surfaceTimer = null, surfaceObserver = null, routeObserver = null, checkingSurface = false, layoutResumeNeeded = false;
  let mediaFile = null, dormant = false, memoryPressure = false, savedTime = 0, restoreSeek = false;
  let ambientFile = null, alternateFile = null, activeClip = 'ambient', alternateFailed = false;
  let idleTimer = null, healthTimer = null, healthTime = 0, healthFrames = null, healthChecked = 0, stalledMs = 0;
  const idleReleaseMs = 120000, healthIntervalMs = 2000, stallLimitMs = 12000;
  let managed = false, documentGeneration = '00000000000000000000000000000000';
  let recoveryNeeded = false, recoveryInFlight = false, recoveryStable = false, recoveryReason = '', retired = false;
  let completedCycles = 0, prepareOperationId = '', preparedGeneration = 0, commitAccepted = false;
  let leaseTimer = null, leaseDeadline = 0, leaseNeedsRenew = false, leaseRevision = 0, frozen = false;
  let presented = 0, healthPresented = 0, healthFrame = null, recoveryWasRevealed = false;
  function notify() { if (managed && typeof window.__codexDragonNotifyV2 === 'function') { try { window.__codexDragonNotifyV2('state'); } catch {} } }
  function clearLease() { if (leaseTimer !== null) clearTimeout(leaseTimer); leaseTimer = null; leaseRevision++; }
  function armLease() {
    clearLease();
    if (!managed || disposed || retired || hidden() || manuallyPaused || frozen) return;
    const revision = leaseRevision;
    leaseTimer = setTimeout(() => {
      leaseTimer = null;
      if (revision !== leaseRevision || disposed || hidden() || manuallyPaused || frozen) return;
      // A very late callback may be the first work after system sleep. Require
      // a fresh controller acknowledgement during one bounded grace period.
      if (!leaseNeedsRenew && performance.now() - leaseDeadline > 5000) {
        leaseNeedsRenew = true; pauseVideo(); suspendGap(); clearRevealTimer();
        leaseDeadline = performance.now() + 8000; armLease(); notify(); return;
      }
      retire('control-lease-expired');
    }, Math.max(1, leaseDeadline - performance.now()));
  }
  function configure(options) {
    if (disposed || attached || !options || !/^[a-f0-9]{32}$/.test(options.documentGeneration)) return { ok: false, code: 'invalid-generation' };
    documentGeneration = options.documentGeneration; managed = options.managed === true;
    leaseDeadline = performance.now() + 45000; armLease();
    return { ok: true, code: 'configured' };
  }
  function renewLease(expected) {
    if (disposed || retired || expected !== documentGeneration) return { ok: false, code: 'stale-generation' };
    const waiting = leaseNeedsRenew;
    leaseDeadline = performance.now() + 45000; leaseNeedsRenew = false; armLease();
    if (waiting && attached && !hidden() && !manuallyPaused && !frozen && (!deferred || revealed || !mediaReady)) { armRevealTimer(); resume(); }
    return { ok: true, code };
  }
  function operationCurrent(expiresAt) { const left = expiresAt - performance.now(); return !managed || (Number.isFinite(expiresAt) && left > 0 && left <= 1500); }
  function prepare(operation, expiresAt) {
    if (!operationCurrent(expiresAt)) return { ok: false, code: 'expired-operation' };
    if (disposed || retired || frozen || manuallyPaused || leaseNeedsRenew || recoveryNeeded || !/^[a-f0-9]{32}$/.test(operation || '') || !attached || !mediaReady || !ensureSurface()) return { ok: false, code: 'not-prepared' };
    if (commitAccepted && operation !== prepareOperationId) return { ok: false, code: 'already-committed' };
    prepareOperationId = operation; preparedGeneration = generation;
    return { ok: true, code: 'prepared' };
  }
  function commit(operation, expiresAt) {
    if (!operationCurrent(expiresAt)) return { ok: false, code: 'expired-operation' };
    if (disposed || retired || frozen || manuallyPaused || leaseNeedsRenew || !operation || operation !== prepareOperationId || preparedGeneration !== generation || !attached || !mediaReady || !ensureSurface()) return { ok: false, code: 'stale-operation' };
    if (commitAccepted) return { ok: true, code: 'commit-accepted' };
    commitAccepted = true; return reveal();
  }
  function retire(reason) {
    if (disposed || retired) return { ok: true, code: disposed ? 'disposed' : code };
    retired = true; recoveryNeeded = false; clearLease(); fail(reason); notify();
    return { ok: true, code };
  }
  function recover(expected) {
    if (!managed || disposed || retired || !recoveryNeeded || expected !== generation || !mediaFile) return { ok: false, code: 'recovery-rejected' };
    ensureSurface(); if (disposed || retired) return { ok: false, code: 'recovery-rejected' };
    recoveryNeeded = false; recoveryInFlight = true; recoveryStable = false;
    attached = true; dormant = true; savedTime = 0; revealed = recoveryWasRevealed || revealRequested; revealRequested = revealed && !manuallyPaused;
    watchSurface();
    if (!hidden() && !manuallyPaused && !memoryPressure && !frozen && !leaseNeedsRenew) wake();
    return { ok: true, code: 'recovery-accepted' };
  }
  function clearHealth() {
    if (healthTimer !== null) clearTimeout(healthTimer);
    if (healthFrame !== null && typeof video.cancelVideoFrameCallback === 'function') video.cancelVideoFrameCallback(healthFrame);
    healthFrame = null;
    healthTimer = null; stalledMs = 0; healthChecked = 0;
  }
  function frameCount() {
    try { const q = video.getVideoPlaybackQuality(); return Number.isFinite(q.totalVideoFrames) ? Math.max(0, q.totalVideoFrames - (Number.isFinite(q.droppedVideoFrames) ? q.droppedVideoFrames : 0)) : null; } catch { return null; }
  }
  function shouldWatch() {
    return !disposed && attached && mediaReady && firstFrameConfirmed && revealed && playing &&
      !hidden() && !layoutSuspended && !manuallyPaused && !betweenAppearances && !dormant && !memoryPressure && !frozen && !leaseNeedsRenew;
  }
  function armHealth() {
    if (healthTimer !== null || !shouldWatch()) return;
    if (!healthChecked) { healthChecked = performance.now(); healthTime = video.currentTime; healthFrames = frameCount(); healthPresented = presented; }
    const epoch = generation, revision = frameRevision;
    if (typeof video.requestVideoFrameCallback === 'function' && healthFrame === null) healthFrame = video.requestVideoFrameCallback((_now, metadata) => {
      if (epoch !== generation || revision !== frameRevision || disposed || !attached) return;
      healthFrame = null;
      presented = metadata && Number.isFinite(metadata.presentedFrames) ? metadata.presentedFrames : presented + 1;
    });
    healthTimer = setTimeout(() => {
      healthTimer = null;
      if (epoch !== generation || revision !== frameRevision || !shouldWatch()) { clearHealth(); return; }
      if (!ensureSurface()) return;
      const now = performance.now(), elapsed = now - healthChecked, current = video.currentTime, frames = frameCount();
      // A suspended computer or blocked event loop is not evidence of decoder
      // failure. Require consecutive on-time samples, never a wall-clock jump.
      const hasFrameCallback = typeof video.requestVideoFrameCallback === 'function';
      const presentationProgress = hasFrameCallback ? presented > healthPresented : frames !== null && healthFrames !== null && frames > healthFrames;
      const progressed = current > healthTime + .02 && presentationProgress;
      stalledMs = progressed || elapsed > healthIntervalMs * 2.5 || current < healthTime ? 0 : stalledMs + Math.max(0, elapsed);
      healthChecked = now; healthTime = current; healthFrames = frames; healthPresented = presented;
      if (stalledMs >= stallLimitMs) { fail('playback-stalled'); return; }
      armHealth();
    }, healthIntervalMs);
  }
  function clearIdleTimer() { if (idleTimer !== null) clearTimeout(idleTimer); idleTimer = null; }
  function scheduleIdleRelease() {
    if (idleTimer !== null || dormant || !attached || (!layoutSuspended && (!mediaReady || !revealed)) || (!hidden() && !manuallyPaused && !layoutSuspended)) return;
    const epoch = generation;
    idleTimer = setTimeout(() => { idleTimer = null; if (epoch === generation && (hidden() || manuallyPaused || layoutSuspended)) hibernate(); }, idleReleaseMs);
  }
  function hibernate() {
    if (disposed || !attached || dormant) return;
    // Pressure can return while wake is decoding its first frame. That decoder
    // already owns a Blob and deadline even though mediaReady is still false.
    // Unload this in-flight attempt too; it is a pause, not a recovery failure.
    suspendGap(); clearPending(); clearIdleTimer(); pauseVideo(); playAttempt++;
    // A fresh src resets currentTime before loadeddata restores the saved seek.
    // Preserve that original position if pressure interrupts this pending seek.
    if (!restoreSeek) savedTime = Number.isFinite(video.currentTime) ? Math.max(0, video.currentTime) : 0;
    dormant = true; mediaReady = false; playStarted = false; firstFrameConfirmed = false; revealWaiting = false;
    restoreSeek = false; stage.hidden = true; stage.removeAttribute('data-ready'); restoreMarker();
    releaseSource();
    input.value = ''; code = 'hibernating';
  }
  function wake() {
    if (!dormant || disposed || retired || !attached || hidden() || manuallyPaused || memoryPressure || frozen || leaseNeedsRenew) return;
    if (!ensureSurface()) return;
    if (!mediaFile) { fail('unsupported-layout'); return; }
    // Keep the remaining quiet interval without loading a decoder just to
    // display its empty last frame. The gap callback performs the one wake.
    if (betweenAppearances) { code = 'paused'; scheduleGap(); return; }
    clearPending(); generation++; dormant = false; restoreSeek = true; code = 'resuming'; notify();
    try {
      objectUrl = URL.createObjectURL(mediaFile); loaded = prime; video.addEventListener('loadeddata', loaded);
      armFailureTimer('resume-timeout');
      video.src = objectUrl; video.load();
    } catch { fail('media-error'); }
  }
  function setMemoryPressure(active) {
    if (disposed) return { ok: false, code: 'disposed' };
    memoryPressure = active === true;
    if (memoryPressure) hibernate(); else wake();
    return { ok: true, code };
  }
  // Both approved clips exit to an empty background. Keep one decoder only;
  // retain its last frame during the quiet interval and reload only on a change.
  let betweenAppearances = false, gapTimer = null, gapDeadline = 0, gapRemaining = 0, gapRevision = 0;
  function switchClip(file, clip) {
    if (!file || disposed || retired || !attached) return;
    // Reuse the bounded unload/resume path so stale play promises and frame
    // callbacks cannot certify the next decoder. File handles are not buffers.
    hibernate();
    mediaFile = file; activeClip = clip; savedTime = 0;
    wake(); notify();
  }
  function beginNextAppearance() {
    // Draw only when this quiet interval really expires. Visibility changes,
    // hibernation and retries keep the same choice; there is no fixed 1-in-4 quota.
    const alternate = completedCycles > 0 && alternateFile && !alternateFailed && Math.random() < .25;
    const next = alternate ? alternateFile : ambientFile;
    if (next && next !== mediaFile) { switchClip(next, alternate ? 'particles' : 'ambient'); return; }
    savedTime = 0;
    if (!dormant) { try { video.currentTime = 0; } catch { fail('media-error'); return; } }
    resume();
  }
  function suspendGap() {
    gapRevision++;
    if (gapTimer !== null) {
      gapRemaining = Math.max(0, gapDeadline - performance.now());
      clearTimeout(gapTimer); gapTimer = null;
    }
  }
  function resetGap() {
    suspendGap(); betweenAppearances = false; gapRemaining = 0; gapDeadline = 0;
  }
  function scheduleGap() {
    if (!betweenAppearances || gapTimer !== null || disposed || !attached || hidden() || layoutSuspended || manuallyPaused || memoryPressure || frozen || leaseNeedsRenew) return;
    const epoch = generation, revision = ++gapRevision;
    code = 'paused';
    gapDeadline = performance.now() + gapRemaining;
    gapTimer = setTimeout(() => {
      if (disposed || !attached || epoch !== generation || revision !== gapRevision || !betweenAppearances) return;
      gapTimer = null;
      if (hidden() || manuallyPaused || memoryPressure || frozen || leaseNeedsRenew) return;
      gapRemaining = 0;
      if (!ensureSurface()) return;
      betweenAppearances = false;
      beginNextAppearance();
    }, gapRemaining);
  }
  function onEnded() {
    if (disposed || !attached || !mediaReady || !revealed || betweenAppearances || video.ended === false) return;
    ensureSurface();
    // The ending event still belongs to this full appearance when navigation
    // temporarily has no active page. Record its quiet interval exactly once.
    if (disposed || retired || !attached) return;
    clearPending(); playAttempt++; pauseVideo();
    betweenAppearances = true;
    gapRemaining = 10000 + Math.floor(Math.random() * 5001);
    completedCycles++; if (recoveryInFlight && firstFrameConfirmed) { recoveryStable = true; recoveryInFlight = false; }
    code = 'paused'; scheduleGap();
    notify();
  }
  const mainSelector = '#root main[data-app-shell-main-surface="default"]';
  const shellComponents = ['Layout', 'Workspace', 'MainContentClip', 'LeftPanel', 'ApplicationMenuTopBar', 'FloatingHeader', 'MainContentTopFade', 'PageSurface', 'PageSurfaceLayout', 'ApplicationMenuPageSurfaceLayout'];
  let layoutSurfaceKind = 'none';
  function emptyLayoutAudit() {
    // Fixed integers only. -1 means this branch was not examined, never zero.
    // Collected from the same bounded semantic checks, with no new observer,
    // frame callback, text reads, storage, URL, or extra periodic work.
    return { layoutAuditFrameCount: -1, layoutAuditActiveFrameCount: -1,
      layoutAuditAnyMainCount: -1, layoutAuditActiveAnyMainCount: -1,
      layoutAuditPageSurfaceFrameCount: -1, layoutAuditLayoutTokenValid: -1,
      layoutAuditPageLayoutTokenValid: -1, layoutAuditFrameHashMatches: -1,
      layoutAuditPageSurfaceFlag: -1, layoutAuditDirectRowCount: -1,
      layoutAuditDecorationCount: -1, layoutAuditDecorationValid: -1,
      layoutAuditComposerCount: -1, layoutAuditComposerAmbiguous: -1 };
  }
  let layoutAudit = emptyLayoutAudit();
  let styleBinding = '', bindingMain = null, bindingCore = '', bindingComposer = '', bindingsDirty = true;
  let boundShellNodes = [], watchedShellNodes = [], boundStyleNodes = [], watchedStyleNodes = [], bindingAttributes = [];
  const shellAttributeFilter = ['class', 'id', 'aria-hidden', 'data-app-shell-main-surface', 'data-app-shell-frame',
    'data-app-shell-page-surface', 'data-app-shell-workspace-row', 'data-app-shell-main-content-top-fade',
    'data-app-shell-left-panel-appearance', 'data-composer-surface-variant'];
  function attributeSignature(node) { return shellAttributeFilter.map(name => node.getAttribute(name)); }
  function cachedAttributesIntact() {
    // Before attach the shell is revalidated without using this cache. Once
    // attached, this snapshot closes the gap before observer dispatch. Never
    // sample chat text, descendants, computed paint, or per-frame state.
    return bindingAttributes.every(entry => entry.node.isConnected && entry.values.every((value, index) => value === entry.node.getAttribute(shellAttributeFilter[index])));
  }
  function componentToken(node, name) {
    const prefix = '_' + name + '_';
    const tokens = String(node.className || '').split(/\s+/).filter(token => token.startsWith(prefix));
    // The complete known component token is mandatory. A partial match or two
    // versions on one node is ambiguous, even if one happens to be supported.
    if (tokens.length !== 1) return null;
    const match = /^_[A-Za-z]+_([A-Za-z0-9]+)_([0-9]+)$/.exec(tokens[0]);
    return match ? { token: tokens[0], hash: match[1] } : null;
  }
  function bindSurfaceStyle(main, workspace, mainToken, workspaceToken, frameOnly = false) {
    const core = (frameOnly ? 'frame ' : 'main ') + mainToken.token + ' ' + workspaceToken.token;
    // A composer may mount lazily below the unobserved conversation subtree.
    // Recheck its small semantic signature on existing surface/status checks;
    // never observe chat text or invalidate styles for ordinary token streaming.
    const composers = main.querySelectorAll('[data-composer-surface-variant]');
    let composer = ''; const composerNodes = [];
    layoutAudit.layoutAuditComposerCount = Math.min(composers.length, 129);
    layoutAudit.layoutAuditComposerAmbiguous = 0;
    if (composers.length > 16) { layoutAudit.layoutAuditComposerAmbiguous = 1; layoutReason = 'composer-ambiguous'; return false; }
    for (const node of composers) {
      if (!['default', 'opaque', 'secondary'].includes(node.getAttribute('data-composer-surface-variant')) || node.closest('[data-app-shell-active-page="false"]')) continue;
      const actual = componentToken(node, 'ComposerLayoutRoot');
      if (!actual || (composer && composer !== actual.token)) { layoutAudit.layoutAuditComposerAmbiguous = 1; layoutReason = 'composer-ambiguous'; return false; }
      composer = actual.token; composerNodes.push(node);
    }
    if (attached && !bindingsDirty && bindingMain === main && bindingCore === core && bindingComposer === composer && cachedAttributesIntact()) { layoutReason = 'ready'; return true; }
    layoutReason = 'shell-shape';
    const bindings = frameOnly ? { Layout: mainToken.token } : { MainContentSurface: mainToken.token, WorkspaceContent: workspaceToken.token };
    if (composer) bindings.ComposerLayoutRoot = composer;
    const nodes = new Set(), spines = new Set(), containers = new Set([workspace]);
    let ancestor = main, depth = 0;
    while (ancestor && ancestor !== root && depth++ < 24) { spines.add(ancestor); ancestor = ancestor.parentElement; }
    if (ancestor !== root) return false;
    spines.add(root);
    // Inspect direct shell children only. The frame exception below reads only
    // its semantic chrome wrapper, never conversation descendants or text rows.
    function children(node) { if (node.children.length > 128) return false; for (const child of node.children) nodes.add(child); return nodes.size <= 128; }
    for (const spine of spines) { nodes.add(spine); if (!children(spine)) return false; }
    if (!children(workspace)) return false;
    for (const child of workspace.children) {
      if (!componentToken(child, 'MainContentViewport')) continue;
      containers.add(child);
      if (!children(child)) return false;
      for (const wrapper of child.children) {
        if (String(wrapper.className || '').split(/\s+/).some(token => token.startsWith('_MainContentFrame_'))) {
          const frameToken = componentToken(wrapper, 'MainContentFrame');
          if (!frameToken || frameToken.hash !== mainToken.hash || wrapper.children.length > 128) return false;
          containers.add(wrapper);
          let fadeWrapper = null;
          for (const chromeWrapper of wrapper.children) {
            if (chromeWrapper.tagName !== 'DIV' || !chromeWrapper.hasAttribute('data-app-shell-main-content-top-fade')) continue;
            if (fadeWrapper || chromeWrapper.children.length > 128) return false;
            fadeWrapper = chromeWrapper; containers.add(chromeWrapper);
            if (containers.size > 128) return false;
            let fadeNode = null;
            for (const decoration of chromeWrapper.children) {
              if (decoration.tagName !== 'DIV' || decoration.getAttribute('aria-hidden') !== 'true' ||
                  !String(decoration.className || '').split(/\s+/).some(token => token.startsWith('_MainContentTopFade_'))) continue;
              if (fadeNode) return false;
              fadeNode = decoration; nodes.add(decoration);
              if (nodes.size > 128) return false;
            }
          }
          continue;
        }
        if (wrapper.tagName === 'ARTICLE') continue;
        containers.add(wrapper);
        if (!children(wrapper)) return false;
      }
    }
    // These observed semantic chrome anchors do not include chat text/classes.
    const chrome = root.querySelectorAll('#app-shell-sidebar, [data-app-shell-left-panel-appearance]');
    if (chrome.length > 32) return false;
    for (const node of chrome) { if (node.closest('[data-app-shell-active-page="false"]')) continue; nodes.add(node); containers.add(node); if (!children(node)) return false; }
    const attributeNodes = new Set([...spines, ...containers, ...composerNodes]);
    for (const node of nodes) {
      if (node.closest('[data-app-shell-active-page="false"]')) continue;
      for (const name of shellComponents) {
        if (!String(node.className || '').split(/\s+/).some(token => token.startsWith('_' + name + '_'))) continue;
        const actual = componentToken(node, name);
        if (!actual || actual.hash !== mainToken.hash || (bindings[name] && bindings[name] !== actual.token)) return false;
        bindings[name] = actual.token; attributeNodes.add(node);
      }
    }
    if (attributeNodes.size > 128) return false;
    const signature = JSON.stringify(bindings);
    if (signature !== styleBinding) {
      // Change exact selectors only. All paint values and accessibility rules
      // remain byte-for-byte from the audited template; removal rolls back.
      let css = styleTemplate.replace(/_([A-Za-z]+)_(?:yl8z4|boifs)_2\b/g, (token, name) => bindings[name] || token);
      if (frameOnly) {
        // The audited shell also hosts home/settings pages without a main.
        // Only this exact frame's composer and inert decoration are styled.
        const frameSelector = '[data-app-shell-frame="true"].' + mainToken.token;
        css = css.replace('main[data-app-shell-main-surface="default"] .', frameSelector + ' .');
      }
      style.textContent = css;
      styleBinding = signature;
    }
    bindingMain = main; bindingCore = core; bindingComposer = composer; bindingsDirty = false;
    boundShellNodes = [...spines, ...containers].filter(node => node !== root && !node.closest('[data-app-shell-active-page="false"]'));
    boundStyleNodes = [...attributeNodes].filter(node => node !== root && !node.closest('[data-app-shell-active-page="false"]'));
    bindingAttributes = boundStyleNodes.map(node => ({ node, values: attributeSignature(node) }));
    layoutReason = 'ready';
    return true;
  }
  function supportedFrameSurface() {
    // A default conversation main is not mounted on every official route.
    // Require the independently audited outer frame + direct workspace row;
    // never promote a generic main/body, or hide content on an unknown page.
    const frames = root.querySelectorAll('[data-app-shell-frame="true"]');
    layoutReason = 'frame-count';
    layoutAudit.layoutAuditFrameCount = Math.min(frames.length, 129);
    if (frames.length > 128) return null;
    layoutAudit.layoutAuditActiveFrameCount = 0; layoutAudit.layoutAuditPageSurfaceFrameCount = 0;
    let selected = null;
    for (const frame of frames) {
      if (frame.closest('[data-app-shell-active-page="false"]')) continue;
      layoutAudit.layoutAuditActiveFrameCount++;
      if (frame.getAttribute('data-app-shell-page-surface') === 'true') layoutAudit.layoutAuditPageSurfaceFrameCount++;
      if (!selected) selected = frame;
    }
    if (!selected) { layoutReason = 'main-count'; return null; }
    if (layoutAudit.layoutAuditActiveFrameCount !== 1) return null;
    layoutReason = 'frame-token';
    const token = componentToken(selected, 'Layout'), pageToken = componentToken(selected, 'PageSurfaceLayout');
    layoutAudit.layoutAuditLayoutTokenValid = token ? 1 : 0;
    layoutAudit.layoutAuditPageLayoutTokenValid = pageToken ? 1 : 0;
    layoutAudit.layoutAuditFrameHashMatches = token && pageToken ? (token.hash === pageToken.hash ? 1 : 0) : -1;
    layoutAudit.layoutAuditPageSurfaceFlag = selected.getAttribute('data-app-shell-page-surface') === 'true' ? 1 : 0;
    if (selected.tagName !== 'DIV' || selected.getAttribute('data-app-shell-page-surface') !== 'true' || !token || !pageToken || pageToken.hash !== token.hash || selected.children.length > 128) return null;
    layoutReason = 'frame-row';
    let row = null, rowInvalid = false;
    layoutAudit.layoutAuditDirectRowCount = 0;
    for (const child of selected.children) {
      if (child.getAttribute('data-app-shell-workspace-row') !== 'true') continue;
      layoutAudit.layoutAuditDirectRowCount++;
      if (row || child.tagName !== 'DIV' || child.children.length > 128) rowInvalid = true;
      if (!row) row = child;
    }
    if (!row || rowInvalid) return null;
    layoutReason = 'frame-shape';
    // The official page-surface branch emits both PageSurfaceLayout and one
    // inert PageSurface. A bare boot/login frame is not this contract.
    let pageSurface = null;
    const unknownMains = selected.querySelectorAll('main');
    layoutAudit.layoutAuditAnyMainCount = Math.min(unknownMains.length, 129);
    if (unknownMains.length > 128) return null;
    layoutAudit.layoutAuditActiveAnyMainCount = 0;
    for (const candidate of unknownMains) if (!candidate.closest('[data-app-shell-active-page="false"]')) layoutAudit.layoutAuditActiveAnyMainCount++;
    if (layoutAudit.layoutAuditActiveAnyMainCount !== 0) return null;
    layoutAudit.layoutAuditDecorationCount = 0; layoutAudit.layoutAuditDecorationValid = 0;
    let decorationInvalid = false;
    for (const child of row.children) {
      if (!String(child.className || '').split(/\s+/).some(value => value.startsWith('_PageSurface_'))) continue;
      layoutAudit.layoutAuditDecorationCount++;
      const decoration = componentToken(child, 'PageSurface');
      if (pageSurface || !decoration || decoration.hash !== token.hash || child.tagName !== 'DIV' || child.getAttribute('aria-hidden') !== 'true') decorationInvalid = true;
      if (!pageSurface) pageSurface = child;
    }
    if (!pageSurface || decorationInvalid) return null;
    layoutAudit.layoutAuditDecorationValid = 1;
    if (!bindSurfaceStyle(selected, row, token, componentToken(pageSurface, 'PageSurface'), true)) return null;
    layoutSurfaceKind = 'frame';
    return selected;
  }
  function supportedSurface() {
    layoutSurfaceKind = 'none'; layoutAudit = emptyLayoutAudit();
    // The host authenticates the executable and application URL. Here require
    // the audited document shape; a framework's label is not a layout contract.
    if (!root.isConnected || document.getElementById('root') !== root) { layoutReason = 'root-missing'; return null; }
    const mains = document.querySelectorAll(mainSelector);
    layoutReason = 'main-count';
    if (mains.length > 128) return null;
    let selected = null, selectedChild = null, selectedMain = null, selectedWorkspace = null;
    for (const main of mains) {
      // Cached inactive conversations are genuine but are not the destination.
      if (main.closest('[data-app-shell-active-page="false"]')) continue;
      if (selected) { layoutReason = 'main-count'; return null; }
      const actualMain = componentToken(main, 'MainContentSurface');
      if (!actualMain) { layoutReason = 'main-token'; return null; }
      layoutReason = 'workspace-shape';
      let actualWorkspace = null;
      for (const child of main.children) {
        if (!String(child.className || '').split(/\s+/).some(token => token.startsWith('_WorkspaceContent_'))) continue;
        if (actualWorkspace) return null;
        actualWorkspace = componentToken(child, 'WorkspaceContent'); selectedChild = child;
        // A module hash is shared; each component's final index is independent.
        if (!actualWorkspace || actualWorkspace.hash !== actualMain.hash) return null;
      }
      if (!actualWorkspace) return null;
      selected = main; selectedMain = actualMain; selectedWorkspace = actualWorkspace;
    }
    if (!selected) return supportedFrameSurface();
    if (!bindSurfaceStyle(selected, selectedChild, selectedMain, selectedWorkspace)) return null;
    layoutSurfaceKind = 'main';
    return selected;
  }
  function supportedLayout() {
    return supportedSurface() !== null;
  }
  function hidden() { return document.hidden || document.visibilityState === 'hidden'; }
  function decodedBufferReady() { return video.readyState >= 2 && !video.error && video.videoWidth > 0 && video.videoHeight > 0; }
  function pauseVideo() { clearHealth(); pauseRevision++; try { video.pause(); } catch {} playing = false; }
  function boundSurfaceIntact() {
    return !layoutSuspended && stage.isConnected && stage.parentElement === (probeOnly ? document.body : root) &&
      (probeOnly || (document.getElementById('root') === root && supportedSurface() === mediaSurface));
  }
  function clearSurfaceWatch() {
    if (surfaceTimer !== null) clearTimeout(surfaceTimer);
    surfaceTimer = null; layoutResumeNeeded = false;
    if (surfaceObserver) surfaceObserver.disconnect();
    if (routeObserver) routeObserver.disconnect();
    surfaceObserver = routeObserver = null; watchedShellNodes = []; watchedStyleNodes = [];
  }
  function queueSurfaceCheck() {
    bindingsDirty = true;
    if (surfaceTimer !== null || disposed || retired || !attached || probeOnly) return;
    surfaceTimer = setTimeout(() => {
      surfaceTimer = null;
      if (ensureSurface() && layoutResumeNeeded && !hidden() && !manuallyPaused && !memoryPressure && !frozen && !leaseNeedsRenew && (!deferred || revealed || !mediaReady)) {
        layoutResumeNeeded = false; armRevealTimer(); resume();
      }
    }, 0);
  }
  function watchSurface() {
    if (disposed || retired || !attached || probeOnly || typeof MutationObserver !== 'function') return;
    if (!routeObserver) {
      // Attribute-only observation ignores text/token streaming entirely.
      routeObserver = new MutationObserver(queueSurfaceCheck);
      routeObserver.observe(root, { subtree: true, attributes: true, attributeFilter: ['data-app-shell-active-page'] });
    }
    if (!surfaceObserver) surfaceObserver = new MutationObserver(queueSurfaceCheck);
    const parents = new Set([root]);
    // A new official frame can mount before its page-surface flag/classes are
    // complete. Observe only bounded semantic DIV anchors so their later attrs
    // can wake validation; observation alone never authorizes any styling.
    const candidateFrames = root.querySelectorAll('[data-app-shell-frame="true"]');
    const pendingFrames = candidateFrames.length <= 128 ? [...candidateFrames].filter(node =>
      node.tagName === 'DIV' && !node.closest('[data-app-shell-active-page="false"]')) : [];
    // Validated shell spines only; text/markdown descendants are not observed.
    const shells = [...boundShellNodes, ...document.querySelectorAll(mainSelector), ...pendingFrames];
    for (const shell of shells) {
      let node = shell, depth = 0;
      while (node && node !== root && depth++ < 24 && parents.size < 128) { parents.add(node); node = node.parentElement; }
    }
    const connected = [...parents].filter(node => node.isConnected);
    const attributes = [...new Set([...boundStyleNodes, ...pendingFrames])].filter(node => node.isConnected).slice(0, 128);
    if (connected.length === watchedShellNodes.length && connected.every((node, index) => node === watchedShellNodes[index]) &&
        attributes.length === watchedStyleNodes.length && attributes.every((node, index) => node === watchedStyleNodes[index])) return;
    surfaceObserver.disconnect(); watchedShellNodes = connected; watchedStyleNodes = attributes;
    const observedAttributes = new Set(attributes);
    for (const node of connected) surfaceObserver.observe(node, observedAttributes.has(node) ?
      { childList: true, attributes: true, attributeFilter: shellAttributeFilter } : { childList: true });
    // Decoration/composer nodes are exact validated targets, not subtree roots.
    for (const node of attributes) if (!parents.has(node)) surfaceObserver.observe(node, { attributes: true, attributeFilter: shellAttributeFilter });
  }

  function ensureSurface() {
    if (disposed || retired || (!attached && !recoveryNeeded)) return false;
    if (probeOnly) return boundSurfaceIntact();
    if (checkingSurface) return boundSurfaceIntact();
    checkingSurface = true;
    try {
      // Moving the complete official page is normal. Removing/replacing our
      // own stage, style, or input is not: never seize an unrelated node.
      if (!root.isConnected || document.getElementById('root') !== root || stage.parentElement !== root || document.getElementById(styleId) !== style ||
          document.getElementById(fileId) !== input || !style.isConnected || !input.isConnected) {
        fail('unsupported-layout'); return false;
      }
      const next = supportedSurface(); layoutSupported = next !== null;
      const moved = next && next !== mediaSurface, wasSuspended = layoutSuspended;
      if (!next || moved) {
        if (!layoutSuspended || moved) {
          suspendGap(); clearPending(); pauseVideo(); playAttempt++;
          stage.hidden = true; restoreMarker(); layoutSuspended = true;
        }
      }
      if (!next) { code = 'paused'; scheduleIdleRelease(); watchSurface(); return false; }
      if (moved) mediaSurface = next; // Keep the single video in the shared root across routes.
      layoutSuspended = false;
      if (moved || wasSuspended) {
        clearIdleTimer();
        if (!mediaReady && !dormant) { loaded = prime; video.addEventListener('loadeddata', loaded); }
        if (mediaReady && revealed && !dormant) {
          stage.hidden = false; stage.setAttribute('data-ready', 'true');
          document.documentElement.setAttribute(marker, 'v1'); markerApplied = true;
        }
        watchSurface();
        layoutResumeNeeded = true; queueSurfaceCheck(); scheduleIdleRelease();
        notify();
      } else watchSurface();
      return true;
    } finally { checkingSurface = false; }
  }
  function stageVisible() {
    // Fixed, owned media nodes only. This is bounded geometry/style evidence,
    // not a claim that another application/portal cannot occlude the pixels.
    if (!attached || disposed || probeOnly || hidden() || stage.hidden || !boundSurfaceIntact()) return false;
    try {
      const s = getComputedStyle(stage), v = getComputedStyle(video), r = stage.getBoundingClientRect();
      return s.display !== 'none' && s.visibility !== 'hidden' && s.visibility !== 'collapse' &&
        v.display !== 'none' && v.visibility !== 'hidden' && v.visibility !== 'collapse' &&
        Number(s.opacity) > 0 && Number(v.opacity) > 0 && r.width > 1 && r.height > 1 &&
        r.right > 0 && r.bottom > 0 && r.left < window.innerWidth && r.top < window.innerHeight;
    } catch { return false; }
  }
  function playbackQuality() {
    // Numeric diagnostics only, sampled by the existing status requests.
    // No observers, extra frame callbacks, polling, or playback adaptations.
    const sample = { videoTotalFrames: -1, videoDroppedFrames: -1, videoCorruptedFrames: -1 };
    try {
      if (typeof video.getVideoPlaybackQuality !== 'function') return sample;
      const quality = video.getVideoPlaybackQuality();
      for (const [source, target] of [['totalVideoFrames', 'videoTotalFrames'], ['droppedVideoFrames', 'videoDroppedFrames'], ['corruptedVideoFrames', 'videoCorruptedFrames']]) {
        const value = quality && quality[source];
        if (Number.isSafeInteger(value) && value >= 0 && value <= 2147483647) sample[target] = value;
      }
    } catch { /* Unsupported metrics are unknown, never evidence of zero drops. */ }
    return sample;
  }
  function status() {
    // An explicit status request must not describe a detached, replaced SPA
    // surface as playing. Check only our node and fixed shell structure.
    if (attached) ensureSurface();
    // A covered native window may delay media events/compositor callbacks.
    // The host's existing bounded status poll can observe decoded data without
    // claiming that a frame has been presented. No background polling is added.
    if (managed && deferred && !revealed && !mediaReady) prime();
    layoutSupported = supportedLayout();
    const documentHidden = hidden(), now = video.currentTime;
    const mediaTimeMs = Number.isFinite(now) && now >= 0 ? Math.min(86400000, Math.floor(now * 1000)) : 0;
    const wallpaperApplied = markerApplied && style.isConnected && document.getElementById(styleId) === style &&
      document.documentElement.getAttribute(marker) === 'v1';
    return { ...playbackQuality(), videoWidth: video.videoWidth, videoHeight: video.videoHeight,
      ...layoutAudit, attached, playing: playing && !video.paused && !documentHidden, disposed, code, layoutSupported, layoutReason, layoutSurfaceKind, mediaReady,
      wallpaperApplied, firstFrameConfirmed, prepared: mediaReady, revealed, manuallyPaused,
      probeOnly, documentHidden, stageVisible: stageVisible(), mediaTimeMs, betweenAppearances, dormant, memoryPressure,
      protocolVersion: 2, documentGeneration, mediaGeneration: generation, rendererClockMs: Math.floor(performance.now()), actualPhase: disposed || retired ? 'retired' : recoveryNeeded ? 'faulted' : dormant ? 'hibernating' : layoutSuspended ? 'paused' : code === 'resuming' ? 'resuming' : betweenAppearances ? 'gap' : manuallyPaused ? 'paused' : !mediaReady ? 'preparing' : !revealed ? 'prepared' : 'visible',
      desiredMode: disposed || retired ? 'disabled' : manuallyPaused ? 'user-paused' : 'enabled', health: retired || recoveryNeeded ? 'faulted' : leaseNeedsRenew ? 'stale' : 'fresh',
      recoveryNeeded, recoveryInFlight, recoveryStable, recoveryReason, completedCycles, prepareOperationId, commitAccepted, leaseNeedsRenew, presentedFrames: presented,
      activeClip, alternateAvailable: !!alternateFile && !alternateFailed, alternateFailed,
      layoutSuspended, videoReadyState: video.readyState, videoNetworkState: video.networkState,
      videoPaused: video.paused, videoSeeking: video.seeking, videoErrorCode: video.error ? video.error.code : 0,
      playStarted, frameCallbackPending: frame !== null, preparationTimerPending: timer !== null, frozen,
      nextAppearanceMs: betweenAppearances ? Math.ceil(gapTimer !== null ? Math.max(0, gapDeadline - performance.now()) : gapRemaining) : 0 };
  }
  function clearPending() {
    if (timer !== null) { clearTimeout(timer); timer = null; }
    if (frame !== null && typeof video.cancelVideoFrameCallback === 'function') video.cancelVideoFrameCallback(frame);
    frame = null; frameRevision++;
    if (loaded) video.removeEventListener('loadeddata', loaded);
    loaded = null;
  }
  function clearRevealTimer() {
    if (revealWaiting && timer !== null) { clearTimeout(timer); timer = null; }
  }
  function armRevealTimer() {
    if (revealWaiting && timer === null && !hidden() && !layoutSuspended && !manuallyPaused && !dormant && !memoryPressure && !frozen && !leaseNeedsRenew) armFailureTimer('reveal-timeout');
  }
  function armFailureTimer(reason) {
    const epoch = generation, revision = frameRevision;
    timer = setTimeout(() => {
      if (disposed || retired || epoch !== generation || revision !== frameRevision) return;
      timer = null;
      if (hidden() || layoutSuspended || manuallyPaused || frozen || leaseNeedsRenew) return;
      fail(reason);
    }, 8000);
  }
  function restoreMarker() {
    if (!markerApplied) return;
    if (document.documentElement.getAttribute(marker) === 'v1') {
      if (previousMarker === null) document.documentElement.removeAttribute(marker);
      else document.documentElement.setAttribute(marker, previousMarker);
    }
    markerApplied = false;
  }
  function releaseSource() {
    // Cleanup remains best-effort even when a decoder has already failed.
    // In particular, load() throwing must not retain our Blob URL forever.
    try { video.removeAttribute('src'); video.load(); } catch {}
    if (objectUrl !== null) { const url = objectUrl; objectUrl = null; try { URL.revokeObjectURL(url); } catch {} }
  }
  function releaseMedia() {
    clearIdleTimer(); clearHealth(); resetGap(); mediaFile = null; dormant = false; restoreSeek = false;
    pauseVideo(); attached = false; mediaReady = false; playStarted = false; firstFrameConfirmed = false;
    revealed = false; revealWaiting = false;
    releaseSource(); stage.removeAttribute('data-ready');
    stage.hidden = true; restoreMarker();
    input.value = '';
  }
  function fail(nextCode) {
    if (disposed) return;
    const mediaFailure = ['media-error', 'play-rejected', 'first-frame-timeout', 'reveal-timeout', 'resume-timeout', 'playback-stalled'].includes(nextCode);
    if (!retired && mediaFailure && activeClip === 'particles' && ambientFile && !alternateFailed && attached) {
      // An optional clip must not take down the established wallpaper. Disable
      // it for this document and use one bounded original-clip resume attempt.
      alternateFailed = true; alternateFile = null;
      switchClip(ambientFile, 'ambient'); return;
    }
    const recoverable = managed && !retired && mediaFailure;
    const savedFile = mediaFile, savedPause = manuallyPaused, wasRevealed = revealed;
    code = nextCode; clearPending(); releaseMedia(); clearSurfaceWatch();
    generation++; playAttempt++;
    prepareOperationId = ''; preparedGeneration = 0; commitAccepted = false; recoveryStable = false;
    if (recoverable) { mediaFile = savedFile; manuallyPaused = savedPause; recoveryWasRevealed = wasRevealed; recoveryNeeded = true; recoveryReason = nextCode; }
    else { ambientFile = null; alternateFile = null; if (managed) { retired = true; clearLease(); } }
    if (settlePending) { const settle = settlePending; settlePending = null; settle({ ok: false, code }); }
    notify();
  }
  function ready() {
    if (disposed || !attached) return;
    // All preparation entrances (including visibility/pause handlers) share
    // this buffer gate. Visibility alone cannot certify decodable video data.
    if (managed && deferred && !revealed && (frozen || leaseNeedsRenew || !decodedBufferReady())) return;
    clearPending(); mediaReady = true;
    if (deferred && !revealed) {
      pauseVideo(); stage.hidden = true; stage.removeAttribute('data-ready'); code = 'prepared';
    } else {
      revealed = true;
      code = hidden() ? 'ready-hidden' : manuallyPaused ? 'paused' : 'playing';
      if (hidden() || manuallyPaused) pauseVideo();
    }
    if (settlePending) { const settle = settlePending; settlePending = null; settle({ ok: true, code }); }
    if (deferred && revealRequested && !revealed) reveal();
    if (memoryPressure) hibernate(); else { scheduleIdleRelease(); armHealth(); }
    notify();
  }
  function prime() {
    if (disposed || retired || frozen || leaseNeedsRenew || !attached || video.readyState < 2) return;
    if (!ensureSurface()) return;
    if (dormant) return;
    if (restoreSeek) {
      restoreSeek = false;
      try { video.currentTime = Number.isFinite(video.duration) ? Math.min(savedTime, Math.max(0, video.duration - .05)) : savedTime; }
      catch { fail('media-error'); return; }
      // The user can hide/pause while a switched source is loading. Certify
      // decoded data without claiming presentation, then arm normal idle release.
      if (hidden() || manuallyPaused) { ready(); return; }
      resume(); return;
    }
    if (betweenAppearances) return;
    // Preparation happens behind the splash, so compositor delivery is not a
    // prerequisite. Require an actual decoded video buffer; retain false
    // firstFrameConfirmed until reveal() receives its own fresh frame callback.
    // This also permits hidden -> visible transitions while prepared/paused.
    if (managed && deferred && !revealed && !mediaReady) {
      if (decodedBufferReady()) ready();
      return;
    }
    if ((deferred && mediaReady && !revealed) || (manuallyPaused && mediaReady)) return;
    if (manuallyPaused) { if (settlePending) ready(); return; }
    if (!hidden() && !playStarted) return;
    if (hidden()) { ready(); return; }
    stage.setAttribute('data-ready', 'true'); stage.hidden = false;
    if (!probeOnly && layoutSupported && (!deferred || revealed)) {
      document.documentElement.setAttribute(marker, 'v1'); markerApplied = true;
    }
    if (revealWaiting) { revealWaiting = false; if (timer !== null) clearTimeout(timer); timer = null; }
    if (!settlePending && firstFrameConfirmed) return;
    if (typeof video.requestVideoFrameCallback !== 'function') { if (settlePending || !mediaReady) ready(); return; }
    const epoch = generation, revision = frameRevision;
    if (frame === null) frame = video.requestVideoFrameCallback(() => {
      if (disposed || !attached || epoch !== generation || revision !== frameRevision) return;
      frame = null;
      firstFrameConfirmed = true; ready();
    });
  }
  function resume() {
    if (disposed || retired || !attached || recoveryNeeded || frozen || leaseNeedsRenew) return;
    if (!ensureSurface()) return;
    layoutResumeNeeded = false;
    if (dormant) { wake(); return; }
    if (memoryPressure) { hibernate(); return; }
    if (betweenAppearances) { scheduleGap(); return; }
    if (!mediaReady && timer === null && !hidden() && !manuallyPaused) armFailureTimer(recoveryInFlight || revealed ? 'resume-timeout' : 'first-frame-timeout');
    const epoch = generation;
    const attempt = ++playAttempt, pauseAtStart = pauseRevision;
    try {
      Promise.resolve(video.play()).then(() => {
        if (disposed || epoch !== generation || attempt !== playAttempt || !attached) return;
        if (hidden() || manuallyPaused || frozen || leaseNeedsRenew || (deferred && mediaReady && !revealed)) pauseVideo();
        else { playing = true; playStarted = true; if (!settlePending) code = 'playing'; prime(); armHealth(); }
      }, error => {
        if (disposed || epoch !== generation || attempt !== playAttempt || !attached) return;
        if (error && error.name === 'AbortError' && pauseRevision !== pauseAtStart) return;
        fail('play-rejected');
      });
    } catch { fail('play-rejected'); }
  }
  function onVisibility() {
    if (!attached || disposed) return;
    if (hidden()) {
      suspendGap();
      clearRevealTimer();
      pauseVideo();
      clearLease();
      if (video.readyState >= 2 && settlePending) ready();
      else if (!settlePending) code = manuallyPaused ? 'paused' : 'ready-hidden';
      scheduleIdleRelease();
    } else if (!manuallyPaused && (!deferred || revealed || !mediaReady)) {
      clearIdleTimer();
      if (managed) { leaseNeedsRenew = true; pauseVideo(); code = 'unverified'; leaseDeadline = performance.now() + 8000; armLease(); notify(); }
      else { armRevealTimer(); resume(); }
    }
  }
  function reveal() {
    if (disposed || retired) return { ok: false, code: disposed ? 'disposed' : 'retired' };
    revealRequested = true; manuallyPaused = false;
    if (attached && !ensureSurface()) return { ok: !retired && !disposed, code: retired ? code : 'reveal-pending' };
    clearIdleTimer();
    if (dormant) { wake(); return { ok: true, code: 'reveal-pending' }; }
    if (!attached || !mediaReady) return { ok: true, code: 'reveal-pending' };
    if (!revealed) {
      revealed = true;
      playStarted = false; firstFrameConfirmed = false; clearPending();
      code = 'reveal-pending';
      if (!probeOnly) stage.removeAttribute('data-probe');
      stage.hidden = true; stage.removeAttribute('data-ready');
      try { video.currentTime = 0; } catch { fail('media-error'); return { ok: false, code }; }
      revealWaiting = true;
    }
    armRevealTimer();
    if (memoryPressure) hibernate();
    else if (!hidden()) resume(); else { pauseVideo(); code = 'ready-hidden'; scheduleIdleRelease(); }
    return { ok: true, code: 'reveal-pending' };
  }
  function pause() {
    if (disposed) return { ok: false, code: 'disposed' };
    suspendGap();
    manuallyPaused = true; revealRequested = false; clearRevealTimer(); pauseVideo();
    prepareOperationId = ''; preparedGeneration = 0; commitAccepted = false;
    clearLease();
    if (attached && !mediaReady && video.readyState >= 2) ready();
    if (attached && mediaReady) code = 'paused';
    scheduleIdleRelease();
    return { ok: true, code: 'paused' };
  }
  function dispose() {
    if (disposed) return;
    disposed = true; generation++; code = 'disposed';
    clearLease(); clearSurfaceWatch();
    clearPending();
    if (settlePending) { const settle = settlePending; settlePending = null; settle({ ok: false, code }); }
    video.removeEventListener('error', onMediaError); video.removeEventListener('ended', onEnded); releaseMedia();
    ambientFile = null; alternateFile = null;
    video.removeEventListener('canplay', prime); video.removeEventListener('seeked', prime);
    document.removeEventListener('visibilitychange', onVisibility);
    window.removeEventListener('pagehide', dispose);
    document.removeEventListener('freeze', onFreeze); document.removeEventListener('resume', onResume);
    stage.remove(); input.remove(); style.remove();
    if (window[key] === api) delete window[key];
  }
  async function attach(options = {}) {
    if (disposed || retired) return { ok: false, code: disposed ? 'disposed' : 'retired' };
    if (attached || settlePending) return { ok: false, code: 'already-attached' };
    mediaSurface = supportedSurface(); layoutSupported = mediaSurface !== null;
    probeOnly = options.mediaProbe === true;
    deferred = options.deferReveal === true;
    if (!layoutSupported && !probeOnly) { code = 'unsupported-layout'; return { ok: false, code }; }
    // The active main remains the layout/route gate. The single video lives
    // beneath the complete root, so sidebar, composer and menu sample the same
    // continuous frame. Scoped CSS clears only audited shell backgrounds;
    // portals, control hit testing, and native window drag regions are intact.
    (probeOnly ? document.body : root).appendChild(stage);
    if (probeOnly || deferred) stage.setAttribute('data-probe', 'true'); else stage.removeAttribute('data-probe');
    const file = input.files && input.files[0];
    if (!file || file.name !== 'dragon-wallpaper-ambient-2k-144fps.mp4' || file.type !== 'video/mp4' || !Number.isSafeInteger(file.size) || file.size <= 0) {
      code = 'invalid-file'; input.value = ''; return { ok: false, code };
    }
    if (file.size > 256 * 1024 * 1024) { code = 'file-too-large'; input.value = ''; return { ok: false, code }; }
    // The native host hashes the optional file before supplying it. Reject an
    // unknown/oversized optional input without changing the required original.
    const alternate = input.files && input.files[1];
    ambientFile = file;
    alternateFile = alternate && alternate.name === 'dragon-wallpaper-particles-1440p48.mp4' && alternate.type === 'video/mp4' &&
      Number.isSafeInteger(alternate.size) && alternate.size > 0 && alternate.size <= 256 * 1024 * 1024 ? alternate : null;
    activeClip = 'ambient'; alternateFailed = !!alternate && !alternateFile; completedCycles = 0;
    generation++; code = 'loading'; revealed = false; manuallyPaused = false;
    return new Promise(resolve => {
      settlePending = resolve;
      try {
        mediaFile = file; attached = true; layoutSuspended = false; watchSurface();
        // Keep the validated File handles, not an allocated decoder, while the
        // host reports critical commit pressure. The existing lease/reveal and
        // wake paths resume this same attachment after pressure has cleared.
        if (memoryPressure) {
          hibernate();
          const settle = settlePending; settlePending = null;
          settle({ ok: true, code }); notify(); return;
        }
        objectUrl = URL.createObjectURL(file);
        loaded = prime;
        video.addEventListener('loadeddata', loaded);
        armFailureTimer('first-frame-timeout');
        video.src = objectUrl;
        video.load();
        if (hidden()) {
          pauseVideo();
          if (video.readyState >= 2) ready();
        } else resume();
      } catch { fail('media-error'); }
    });
  }
  function onMediaError() { if (attached && !disposed && video.error !== null) fail('media-error'); }
  function onFreeze() { if (disposed || retired) return; frozen = true; prepareOperationId = ''; preparedGeneration = 0; commitAccepted = false; clearPending(); clearLease(); suspendGap(); pauseVideo(); notify(); }
  function onResume() { if (disposed || retired) return; frozen = false; if (managed) { leaseNeedsRenew = true; leaseDeadline = performance.now() + 8000; armLease(); notify(); } else onVisibility(); }
  const api = Object.freeze({ attach, dispose, status, reveal, pause, memoryPressure: setMemoryPressure, configure, renewLease, prepare, commit, recover, retire });
  video.addEventListener('error', onMediaError);
  video.addEventListener('ended', onEnded);
  video.addEventListener('canplay', prime); video.addEventListener('seeked', prime);
  document.body.appendChild(style); document.body.appendChild(stage); document.body.appendChild(input);
  document.addEventListener('visibilitychange', onVisibility);
  document.addEventListener('freeze', onFreeze); document.addEventListener('resume', onResume);
  window.addEventListener('pagehide', dispose);
  window[key] = api;
  return { ok: true, code: 'installed' };
})();
