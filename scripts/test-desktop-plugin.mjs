import { test } from 'node:test';
import assert from 'node:assert/strict';
import { numericLimits, register } from '../claude-plugin/hooks/register.js';

const now = Date.UTC(2026, 9, 6, 1);
const future = new Date(now + 3600000).toISOString();
const valid = { kind: 'five_hour', percentUsed: 34.5, resetsAt: future };

test('desktop usage keeps only the two numeric subscription windows', () => {
  const actual = numericLimits([
    { ...valid, session_id: 'PRIVATE', transcript_path: 'PRIVATE', arbitrary: 'PRIVATE' },
    { kind: 'seven_day', percentUsed: 0, resetsAt: '2026-10-07T14:00:00+13:00' },
    { kind: 'context', percentUsed: 20, resetsAt: future },
    { kind: 'spend_limit', percentUsed: 20, resetsAt: future },
    { ...valid, percentUsed: 90 }
  ], now);
  assert.deepEqual(actual, {
    five_hour: { used_percentage: 34.5, resets_at: (now + 3600000) / 1000 },
    seven_day: { used_percentage: 0, resets_at: Date.UTC(2026,9,7,1) / 1000 }
  });
  assert.equal(JSON.stringify(actual).includes('PRIVATE'), false);
});

test('missing, expired, ambiguous-timezone, malformed and invalid figures are rejected', () => {
  assert.deepEqual(numericLimits(null, now), {});
  for (const percentUsed of [null, '20', true, -1, 101, Infinity, NaN]) {
    assert.deepEqual(numericLimits([{...valid,percentUsed}], now), {});
  }
  for (const resetsAt of [null, 'broken', '2026-10-06T02:00:00', new Date(now).toISOString(), '2100-01-01T00:00:00Z']) {
    assert.deepEqual(numericLimits([{...valid,resetsAt}], now), {});
  }
  assert.equal(numericLimits([{...valid,percentUsed:100}],now).five_hour.used_percentage,100);
});

function fixture(rows) {
  const hooks = new Map();
  const calls = [];
  register((name, handler) => { assert.equal(hooks.has(name),false); hooks.set(name,handler); });
  const $ = {
    clock: {now:async () => now},
    env: {get:async name => { assert.equal(name,'LOCALAPPDATA'); return 'C:\\test-local'; }},
    session: {usage:async () => ({rateLimits:rows,context:{private:'PRIVATE'},cost:{private:'PRIVATE'}})},
    process: {run:async (argv,options) => { calls.push({argv,options}); return {exitCode:0}; }}
  };
  return {hooks,calls,$};
}

test('session start and measure pass through unchanged and send only numeric usage', async () => {
  const {hooks,calls,$} = fixture([valid]);
  assert.deepEqual([...hooks.keys()],['session.start','session.measure']);
  for (const name of hooks.keys()) {
    const e = {rateLimits:[valid],private:'PRIVATE'};
    const expected = {unchanged:true};
    const result = await hooks.get(name)($,e,async value => { assert.equal(value,e); return expected; });
    assert.equal(result,expected);
  }
  assert.equal(calls.length,2);
  for (const call of calls) {
    assert.deepEqual(call.argv,['C:\\test-local\\Programs\\Agent Usage Bar\\ClaudeUsageBridge.exe','--desktop']);
    assert.equal(call.options.timeoutMs,1500);
    assert.deepEqual(JSON.parse(call.options.stdin),{rate_limits:numericLimits([valid],now)});
    assert.equal(call.options.stdin.includes('PRIVATE'),false);
  }
});

test('empty or unavailable usage never erases a sample or blocks a session', async () => {
  const {hooks,calls,$} = fixture([]);
  const expected = {okay:true};
  assert.equal(await hooks.get('session.start')($,{},async()=>expected),expected);
  assert.equal(calls.length,0);
  $.session.usage = async () => { throw new Error('no usage'); };
  assert.equal(await hooks.get('session.start')($,{},async()=>expected),expected);
  $.process.run = async () => { throw new Error('widget missing'); };
  assert.equal(await hooks.get('session.measure')($,{rateLimits:[valid]},async()=>expected),expected);
});

test('no bridge is invoked for an untrusted network or relative local-appdata path', async () => {
  const {hooks,calls,$} = fixture([valid]);
  for (const local of ['relative','\\\\server\\share','']) {
    $.env.get = async () => local;
    await hooks.get('session.measure')($,{rateLimits:[valid]},async()=>({}));
  }
  assert.equal(calls.length,0);
});
