'use strict';
// Executes the emitted actual C# operation expression and the actual renderer
// lease/prepare/commit functions in an isolated VM. No browser or app is started.
const fs = require('fs');
const vm = require('vm');
const assert = require('assert');
const source = fs.readFileSync(process.argv[2], 'utf8');
const expression = fs.readFileSync(process.argv[3], 'utf8').replace(/^\uFEFF/, '');
const prepareExpression = fs.readFileSync(require('path').join(require('path').dirname(process.argv[3]), 'leased-prepare.js'), 'utf8').replace(/^\uFEFF/, '');
const operationId = '0123456789abcdef0123456789abcdef';
const start = source.indexOf('  function renewLease(');
const end = source.indexOf('  function retire(', start);
assert(start > 0 && end > start, 'renderer function anchors missing');
const actualFunctions = source.slice(start, end);
const results = [];
function run(name, alter, expectedOk, renewals, commits, action = 'commit') {
  let clock = 1000, renewalCount = 0, commitCount = 0;
  const context = vm.createContext({
    performance: { now: () => clock },
    documentGeneration: 'abcdef0123456789abcdef0123456789', managed: true,
    disposed: false, retired: false, frozen: false, manuallyPaused: false,
    leaseNeedsRenew: false, recoveryNeeded: false, attached: true,
    mediaReady: true, prepareOperationId: operationId,
    preparedGeneration: 1, generation: 1, commitAccepted: false,
    leaseDeadline: 45000, deferred: true, revealed: false, code: 'prepared',
    ensureSurface: () => true, hidden: () => false,
    armLease: () => {}, armRevealTimer: () => {}, resume: () => {},
    reveal: () => ({ ok: true, code: 'reveal-pending' })
  });
  vm.runInContext(actualFunctions, context);
  const api = {
    renewLease: generation => { renewalCount++; return context.renewLease(generation); },
    commit: (op, expiry) => { commitCount++; assert.equal(expiry, 1750); return context.commit(op, expiry); },
    prepare: (op, expiry) => { commitCount++; assert.equal(expiry, 1750); return context.prepare(op, expiry); }
  };
  context.window = { __codexDragonWallpaperV1: api };
  alter(context, value => { clock = value; });
  const reply = vm.runInContext(action === 'prepare' ? prepareExpression : expression, context);
  assert.equal(reply.ok, expectedOk, name + ' response');
  assert.equal(renewalCount, renewals, name + ' renewal count');
  assert.equal(commitCount, commits, name + ' operation count');
  assert.equal(context.commitAccepted, action === 'commit' && expectedOk, name + ' commit proof');
  if (action === 'prepare' && expectedOk) assert.equal(context.prepareOperationId, operationId);
  results.push({ case: name, passed: true, renewals: renewalCount, commits: commitCount });
}
run('fresh-operation', () => {}, true, 1, 1);
run('resume-needs-renewal', c => { c.leaseNeedsRenew = true; }, true, 1, 1);
run('expired-before-evaluation', (c, now) => now(1750), false, 0, 0);
run('expiry-outside-original-budget', (c, now) => now(249), false, 0, 0);
run('wrong-document-rejects-renewal', c => { c.documentGeneration = 'different_generation'; }, false, 1, 0);
run('expired-during-renewal', (c, now) => { c.armLease = () => now(1750); }, false, 1, 1);
run('frozen-operation-rejected', c => { c.frozen = true; }, false, 1, 1);
run('retired-renewal-rejected', c => { c.retired = true; }, false, 1, 0);
run('decoder-generation-changed', c => { c.generation = 2; }, false, 1, 1);
run('preparation-operation-mismatch', c => { c.prepareOperationId = 'other_operation'; }, false, 1, 1);
run('surface-lost-operation-rejected', c => { c.ensureSurface = () => false; }, false, 1, 1);
run('prepare-fresh-operation', c => { c.prepareOperationId = ''; }, true, 1, 1, 'prepare');
run('prepare-resume-needs-renewal', c => { c.leaseNeedsRenew = true; c.prepareOperationId = ''; }, true, 1, 1, 'prepare');
run('prepare-expired-during-renewal', (c, now) => { c.armLease = () => now(1750); }, false, 1, 1, 'prepare');
console.log(JSON.stringify({ passed: true, productStarted: false, cases: results }));
