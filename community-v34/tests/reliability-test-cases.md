# Public package reliability coverage

Requirements source: maintainer acceptance requirements and previously reported failure classes.
Date: 2026-10-24. Candidate: R13 isolated review build.
Priority: stability, preserved media quality, current signed official identity, no private-install changes.

This is a coverage map, not a claim that every Windows/driver/client combination has passed.
Fixtures must run in new local directories. Never point them at an installed application,
user desktop, account data, or a live Codex process. Developer tests are not the end-user diagnostic.

## R13 first-launch regression focus

- Repeated valid discovery replies taking 2.4 seconds: old 2-second requests time out;
  adaptive fresh requests succeed without accepting any expired request ID.
- Target appears after 13 seconds, empty target responses, silence, terminal transport
  failure, cancellation, ambiguous targets and a single attach all share finite bounds.
- Initial hidden lease survives additional hidden auxiliary windows. Only the retained
  window may be shown; initial ambiguity, lost marker, later user hide/minimize and
  changed identity must remain non-actions.
- Loss reason and native process-exit evidence are separate; foreign CDP sessions must
  not contaminate the active document. Final logging waits at most 200 ms on exit.
- Report history distinguishes the first failed cold start from a later successful run.
  Numeric evidence cannot establish whether the user voluntarily quit.
- These reproduce reachable defects, not proof that every failure on the feedback
  computer has the same cause. Real first cold start and visible handoff remain acceptance.

## Coverage matrix

| Requirement | Automated evidence / relevant suites | Acceptance still requiring the affected computer |
| --- | --- | --- |
| Ordinary official version and install-path updates are rediscovered | Test-OfficialIdentity.ps1; Test-OfficialIdentityTransitions.ps1; current registration, manifest, signer, process-generation contracts | Real Store update followed by next cold start |
| Official shortcut binding does not depend on an old EXE filename | Test-OfficialShortcutDiscovery.ps1; Test-OfficialShortcut.ps1 binding/rollback fixtures | Actual desktop shortcut format and local policy |
| Never replace an already-running official process with a different newly installed version | OfficialIdentity; ReplayReadOnlyHandles; ProcessSession contracts | User's real already-running client and privilege combination |
| Keep signature, path, package-family and session checks | OfficialIdentity; OfficialShortcut negative fixtures | Availability of the local certificate trust chain |
| Preparation and 59% readiness waits are bounded; late replies cannot reset deadlines | StartupReadiness; StartupHandoff; SlowStartupCompatibility; StartupTargetSelection | Perceived cold-start speed on the target hardware |
| ESC releases the cover while retaining the appropriate wallpaper request | ResourceProtectionRecovery; Test-StartupLifecycleRecovery.ps1 | Actual startup film and subsequent wallpaper appearance |
| Never hide an unrelated window or leave an owned target hidden | NativeHideAcknowledgement; NativeRestoreVisibility; hidden-maximize; native geometry fixtures | Visible black-frame behavior on the user's DWM/driver |
| Repeat launch preserves original client, rejects stale session/handle | ReplayReadOnlyHandles; ReplayDiagnosticReport; startup UI orchestration | Foreground, minimized, tray, and privilege scenarios |
| Repeat launch can restore an initially minimized verified target once; later user minimization is preserved | Test-MinimizedReplay.ps1 using real User32 windows on a private non-input desktop; Test-StartupLifecycleRecovery.ps1 | Real guardian IPC, visible DWM transitions and actual client behavior |
| Recognize verified main and page-surface-frame layouts | Test-RendererLayout.cjs; Test-RendererShellRoutes.cjs; Test-RendererTransitions.cjs | New official UI structures not represented by verified fixtures |
| Route remounts and cached inactive pages must not keep stale styles or attach to wrong content | Test-RendererLayout.cjs; Test-RendererShellRoutes.cjs; Test-RendererTransitions.cjs | Actual account-specific routes and experiments |
| Input/composer/sidebar remain usable and readable | RendererLayout/ShellRoutes paint, pointer and scope assertions | Real visual readability and local theme |
| Six videos and all original assets are included, unmodified | Build media manifest; Test-PublicPackage; full ZIP closure/hash/source audit | Target GPU decoder support and smoothness |
| First appearance is ambient; later 25% alternate, without quality reduction | Test-RendererMediaCycles.cjs; Test-RendererAsyncLifecycle.cjs; unchanged media hashes | Actual alternate clip appearance; randomness is not one in every four guaranteed |
| Memory protection pauses/recoveries retain correct intent and generation | StartupMemoryPolicy; HostMemoryRecovery; ResourceProtectionRecovery; RendererMemory | Real memory pressure without intentionally exhausting the machine |
| Pipe, transport and operation failures cannot masquerade as readiness | BridgeCommandDrain; StartupHandoff; RuntimeDiagnostics | Actual antivirus interference, update interruption or driver faults |
| Diagnostics locate failures without exposing private data | PortableReport; DiagnosticAnalysis; LayoutGeometryDiagnostics; AutomaticIdentityDiagnostics | User supplies the report from the directory actually launched |
| Startup and later route failures both retain structural evidence | RuntimeDiagnostics; LayoutGeometryDiagnostics | Capture soon after failure before bounded log rotation |
| An old playing snapshot cannot hide a newer public-entry operation failure | Test-DiagnosticAnalysis.ps1: fixed public codes, ambiguous timing, later launch boundaries, neutral exits and unknown operation mode | Public logs without a mode field cannot establish launch versus preview/check/configure |
| Broken package still generates a useful report; repeated clicks never queue | DetailedDiagnostics: missing/size/hash/locked media, invalid/missing manifest, report replacement, receipt mismatch, concurrent lock | Output-folder permissions and security software |
| No credentials or local configuration enter distribution | Allowlisted package inventory, source correspondence and secret/private-pattern scan | No claim that automated pattern scans detect every possible secret |

## Executable regression scenarios

### TC-F-001 — Updated signed official installation (high)
- Requirement: version/path updates do not require fixed-version pins.
- Preconditions: isolated package records representing two valid generations.
- Steps: resolve the original inventory; replace registration with the newer manifest/path;
  resolve again; independently attest an already-running original process generation.
- Expected: new cold start selects the current registered identity; an existing process is
  checked against its own executable and creation time; no old version or hash is written
  into generated automatic-policy configuration.
- Postcondition: every rejected lease is released, successful lease remains owned until disposal.

### TC-ST-002 — Store changes registration during discovery (high)
- Preconditions: fake inventory/acquisition providers; no real package modified.
- Steps: change registration between pre-acquire and post-acquire inventories; then keep
  it stable. Repeat with registration changing on both allowed attempts.
- Expected: only a bounded rediscovery for this race; persistent change yields a fixed failure.
  Signer/path failures are not retried as availability errors. Running-process attestation is unchanged.
- Postcondition: no stale executable selected and no leaked file lease.

### TC-ERR-003 — Identity lookalikes and hostile manifest (high)
- Preconditions: private temporary fixture tree.
- Steps: supply traversal, duplicate application IDs, ambiguous executable, linked ancestor,
  wrong family, wrong architecture, invalid signature and a signer sharing only an OpenAI prefix.
- Expected: reject ambiguous/untrusted inputs; do not repair them by skipping security checks.
- Postcondition: no shortcut, installed application, or live process modified.

### TC-ST-004 — ESC, delayed replies and repeated cancellation (high)
- Preconditions: never-shown WPF fixture and fake owned bridge/session.
- Steps: request skip before preparation; deliver delayed success; request skip again; test
  disabled/retired state and shared-deadline expiration.
- Expected: one terminal cover decision; late proof cannot reopen/recommit it; explicit
  wallpaper intent wins; cleanup targets only the owned generation.
- Postcondition: all fixture resources disposed without controlling another window.

### TC-E-005 — Probe timing and window geometry boundaries (high)
- Preconditions: fake HWND/generation evidence or an isolated native fixture desktop.
- Steps: stale success followed by pending response, handle reuse, minimize/restore,
  late maximize, mixed DPI, unavailable frame rectangle, and out-of-range relative edges.
- Expected: stale evidence cannot prove readiness; no unbounded wait; unknown geometry is
  reported as unknown and cannot drive a new window placement decision.
- Postcondition: fixture-owned windows restored or destroyed; user desktop unchanged.

### TC-ST-006 — Layout changes in place (high)
- Preconditions: offline synthetic verified main/frame shell with packaged video.
- Steps: navigate/remount/cache routes; mutate audited shell/composer attributes in place;
  change CSS-module hashes consistently and inconsistently; temporarily remove a required node.
- Expected: bind only verified structure; invalidate stale transparency; recover when the
  valid structure returns. No blanket generic-main/body transparency, text-content observation,
  duplicate video decoder, or unbounded callback storm.
- Postcondition: disposal removes owned styles, nodes and observers.

### TC-E-007 — Media selection and lifecycle (high)
- Preconditions: packaged media, deterministic random boundary values, offline browser.
- Steps: first cycle; later draws just below/at the 25% boundary; unavailable/failed alternate;
  pause/resume, hidden page, memory suspension, disposal, and generation change.
- Expected: first ambient; later boundary behavior matches policy; original media bytes and
  configured quality remain; old callbacks cannot play in the next decoder generation.
- Postcondition: at most the intended single video element/decoder is retained.

### TC-ERR-008 — Runtime route failure evidence (high)
- Preconditions: previously valid session status in the host diagnostic fixture.
- Steps: change layout failure reason after preparation; poll rapidly; restore valid status.
- Expected: runtime state, quality and fixed shape counts are retained under the existing
  rate limit; no new CDP request/timer; shape-only evidence never claims playback success.
- Postcondition: no raw DOM, page text, URL, account, path or generation token in the exported report.

### TC-ERR-009 — Damaged package and concurrent report creation (high)
- Preconditions: synthetic package with metadata-only, non-runnable fixture assemblies.
- Steps: delete media; corrupt size/hash; exclusively lock media; corrupt/remove manifest;
  replace prior report; supply forged or stale receipt; hold the diagnostic lock and invoke again.
- Expected: bounded fixed report for inspectable failures, accurate component/index/reason,
  second diagnostic exits promptly, prior report unchanged when busy; no app is launched.
- Postcondition: only the fixture's fixed Diagnostics/Reports output changes.

### TC-F-010 — Complete distributable ZIP (high)
- Preconditions: freshly built archive and original public media manifest.
- Steps: normalize/check archive names; stream-hash every manifest member; ensure no extras;
  compare all assets with original hashes; compare source and packaged scripts; inspect four
  assembly versions/frameworks; extract to a new directory and run Detailed diagnostics.
- Expected: complete source/binary/media correspondence; zero runtime profiles, reports,
  feedback attachments, secrets or private paths in the distributable; no official app started.
- Postcondition: sealed ZIP remains unchanged after testing its extracted copy.

## End-user one-pass acceptance

Browser fixtures really decode the original local MP4, confirm presented frames and
2560 x 1440 dimensions, and exercise computed paint, input/click behavior and cleanup.
Random values, ended/error events, gap/idle/lease timers, hidden/freeze/pressure signals
and stale asynchronous callbacks are deliberately injected. These tests are not a
complete-clip visual review, real system sleep/pressure or a hardware benchmark.

Native minimized-replay fixtures create only their own windows and cover on a private
non-input desktop; they never switch the user's desktop or control the installed app.
Placement, visibility, minimize state and rollback use real Windows calls. Guardian
acknowledgement is injected, so these cases do not establish full helper IPC health
or prove the absence of every visible black frame on another computer.

Use the simple ordered steps in START-HERE.txt and Diagnostics/README.txt: cold start,
home/thread/project routes, sidebar, resize/maximize, repeat launch, minimize/relaunch,
normal exit/reopen, ESC, then collect one report from the actual launch directory.
Do not run developer fixtures on the end-user machine. No GPU stress test is required.
Unexpected future runtime/identity/UI changes remain explicit unsupported cases rather than
being described as guaranteed compatible or silently bypassing validation.
