// Real server + SQLite integration, using Node's built-in test runner (no browser framework).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { once } from 'node:events';

test('HTTP commands, concurrency, backup and restart', { timeout: 60000 }, async t => {
  const directory = resolve('test-results', `http-${Date.now()}`);
  await mkdir(directory, { recursive: true });
  const database = resolve(directory, 'students.db');
  const base = 'http://127.0.0.1:5081';
  let process;
  let output = '';
  async function start(db = database) {
    process = spawn('dotnet', [resolve('src/Server/bin/Debug/net10.0/Server.dll'), '--demo'], {
      cwd: resolve('src/Server'), windowsHide: true,
      env: { ...globalThis.process.env, STUDENT_DB: db, STUDENT_PORT: '5081' },
      stdio: ['ignore', 'pipe', 'pipe']
    });
    process.stdout.on('data', data => output += data);
    process.stderr.on('data', data => output += data);
    for (let n = 0; n < 100; n++) {
      if (process.exitCode !== null) throw new Error(`Server exited: ${output}`);
      try { if ((await fetch(base + '/api/health')).ok) return; } catch {}
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error(`Server did not start: ${output}`);
  }
  async function stop() {
    if (process && process.exitCode === null) {
      const exit = once(process, 'exit');
      process.kill();
      await exit;
    }
  }
  const get = async route => {
    const reply = await fetch(base + route);
    assert.equal(reply.status, 200, await reply.clone().text());
    return reply.json();
  };
  const revision = async () => (await get('/api/health')).Events;
  const send = async (Intent, Values = {}, Target = '', Targets = [], Revision) => {
    Revision ??= await revision();
    const reply = await fetch(base + '/api/command', { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ Intent, Values: Object.entries(Values).map(([Key, Value]) => ({ Key, Value })), Target, Targets, Revision }) });
    return { status: reply.status, ...await reply.json() };
  };
  try {
    await start();
    await t.test('demo seeding, every page, frontend assets and missing routes', async () => {
      assert.equal(await revision(), 110);
      for (const route of ['students', 'courses', 'course&key=digital', 'course&key=design', 'reports', 'reports&key=foundation', 'system', 'new']) {
        const page = await get('/api/page?route=' + route);
        assert.ok(page.Title && Array.isArray(page.Tables));
      }
      for (const route of ['unknown', 'student&key=invalid', 'reports&key=unknown'])
        assert.equal((await fetch(base + '/api/page?route=' + route)).status, 404);
      assert.match(await (await fetch(base)).text(), /app.js/);
      assert.equal((await fetch(base + '/app.js')).status, 200);
    });
    let id;
    await t.test('new student + application is atomic, even when application fails', async () => {
      const before = await revision();
      const failed = await send('create', { name: 'Failed workflow', email: 'test@example.test', birthDate: '2000-01-01', course: 'unknown' });
      assert.equal(failed.status, 422);
      assert.equal(await revision(), before);
      const created = await send('create', { name: 'HTTP Test', email: 'test@example.test', birthDate: '2000-01-01', course: 'digital' });
      assert.equal(created.status, 200);
      assert.equal(await revision(), before + 2);
      id = created.NavigateTo.split('/')[1];
      assert.equal((await get('/api/page?route=student&key=' + id)).Title, 'HTTP Test');
    });
    await t.test('concurrent commands with the same revision accept exactly one', async () => {
      const current = await revision();
      const values = { name: 'HTTP Test Renamed', email: 'test@example.test', birthDate: '2000-01-01' };
      const results = await Promise.all([send('edit', values, id, [], current), send('edit', values, id, [], current)]);
      assert.deepEqual(results.map(r => r.status).sort(), [200, 409]);
      assert.equal(await revision(), current + 1);
    });
    await t.test('bulk failure appends nothing; valid bulk appends the entire batch', async () => {
      const page = await get('/api/page?route=course&key=design');
      const pending = page.Tables[0].Rows.filter(row => row.Selectable).map(row => row.Id);
      const ended = (await get('/api/page?route=course&key=digital')).Tables[0].Rows.find(row => row.Cells[2].Value === 'Rejected').Id;
      const before = await revision();
      assert.equal((await send('approve', { date: '2026-10-08' }, '', [...pending, ended])).status, 422);
      assert.equal(await revision(), before);
      assert.equal((await send('approve', { date: '2026-10-08' }, '', pending)).status, 200);
      assert.equal(await revision(), before + pending.length);
    });
    await t.test('invalid transport and cross-origin writes are rejected', async () => {
      const before = await revision();
      assert.equal((await send('attendance', { date: 'bad', mark: 'invalid' }, 'bad')).status, 400);
      const invalid = await fetch(base + '/api/command', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{' });
      assert.equal(invalid.status, 400);
      const nullField = await send('create', { name: null, email: 'test@example.test', birthDate: '2000-01-01' });
      assert.equal(nullField.status, 400);
      const crossOrigin = await fetch(base + '/api/command', { method: 'POST', headers: { 'Content-Type': 'application/json', Origin: 'https://unrelated.example' }, body: '{}' });
      assert.equal(crossOrigin.status, 400);
      assert.equal(await revision(), before);
    });
    await t.test('rebuild preserves exact projections and history', async () => {
      const before = await get('/api/state');
      assert.equal((await send('rebuild')).status, 200);
      assert.deepEqual(await get('/api/state'), before);
    });
    const expected = await get('/api/state');
    const backupPath = resolve(directory, 'backup.db');
    await t.test('online backup is a downloadable SQLite database', async () => {
      const response = await fetch(base + '/api/backup');
      assert.equal(response.status, 200);
      const bytes = Buffer.from(await response.arrayBuffer());
      assert.equal(bytes.subarray(0, 15).toString(), 'SQLite format 3');
      await writeFile(backupPath, bytes);
    });
    await t.test('restart replays saved events without seeding twice', async () => {
      await stop();
      await start();
      assert.deepEqual(await get('/api/state'), expected);
    });
    await t.test('downloaded backup restores to the same projection', async () => {
      await stop();
      await start(backupPath);
      assert.deepEqual(await get('/api/state'), expected);
    });
  } finally {
    await stop();
    await writeFile(resolve(directory, 'server.log'), output);
  }
});
