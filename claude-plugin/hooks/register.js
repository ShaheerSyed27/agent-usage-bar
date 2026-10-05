// A quiet observer. No prompts, credentials, transcripts, network, or UI changes.
export function numericLimits(rows, now) {
  const limits = {};
  if (!Array.isArray(rows)) return limits;
  for (const row of rows.slice(0, 16)) {
    if (!row || (row.kind !== 'five_hour' && row.kind !== 'seven_day')) continue;
    if (Object.prototype.hasOwnProperty.call(limits, row.kind)) continue;
    const used = row.percentUsed;
    if (typeof used !== 'number' || !Number.isFinite(used) || used < 0 || used > 100) continue;
    if (typeof row.resetsAt !== 'string' || !/(Z|[+-]\d{2}:\d{2})$/.test(row.resetsAt)) continue;
    const reset = Math.floor(Date.parse(row.resetsAt) / 1000);
    if (!Number.isFinite(reset) || reset <= Math.floor(now / 1000) || reset >= 4102444800) continue;
    limits[row.kind] = { used_percentage: used, resets_at: reset };
  }
  return limits;
}

async function publish($, rows) {
  try {
    const limits = numericLimits(rows, await $.clock.now());
    // Starting an empty session must not erase another session's valid sample.
    if (Object.keys(limits).length === 0) return;
    const local = await $.env.get('LOCALAPPDATA');
    if (!local || !/^[A-Za-z]:[\\/]/.test(local)) return;
    await $.process.run([local + '\\Programs\\Agent Usage Bar\\ClaudeUsageBridge.exe', '--desktop'], {
      stdin: JSON.stringify({ rate_limits: limits }), timeoutMs: 1500
    });
  } catch {
    // A missing widget must never interfere with a Code session.
  }
}

export function register(on) {
  on('session.start', async ($, e, next) => {
    const result = await next(e);
    try {
      const usage = await $.session.usage();
      await publish($, usage.rateLimits);
    } catch { /* No reading available yet. Leave the session alone. */ }
    return result;
  });
  on('session.measure', async ($, e, next) => {
    const result = await next(e);
    await publish($, e.rateLimits);
    return result;
  });
}
