import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const web = new URL('../src/DailyRecord.Mobile/', import.meta.url);
const promptly = async promise => {
  let timer;
  try {
    return await Promise.race([promise, new Promise((_, reject) => {
      timer = setTimeout(() => reject(new Error('Cached records waited for the unavailable computer')), 200);
    })]);
  } finally { clearTimeout(timer); }
};

const handlers = new Map();
const cachedPage = { text: 'offline app shell' };
let requests = 0;
const cache = { match: async () => cachedPage };
const worker = vm.createContext({
  URL,
  self: { location: { href: 'https://example.test/E2-Day/mobile/sw.js' }, addEventListener: (name, handler) => handlers.set(name, handler) },
  caches: { open: async () => cache, match: async () => cachedPage },
  fetch: () => { requests++; return new Promise(() => {}); }
});
vm.runInContext(fs.readFileSync(new URL('sw.js', web), 'utf8'), worker);
let navigation;
handlers.get('fetch')({ request: { method: 'GET', mode: 'navigate', url: 'https://example.test/E2-Day/mobile/' }, respondWith: value => { navigation = value; } });
assert.equal(await promptly(navigation), cachedPage);
assert.equal(requests, 0);
let intercepted = false;
handlers.get('fetch')({ request: { method: 'GET', mode: 'cors', url: 'https://example.test/E2-Day/mobile/api/state' }, respondWith: () => { intercepted = true; } });
assert.equal(intercepted, false, 'Account responses must not enter the shell cache');
console.log('PASS offline navigation displays its cached page without waiting for a stalled network');

const renders = [];
const stored = { Id: 'offline-fixture', State: { Tasks: [{ Title: '离线记录测试' }] } };
const app = vm.createContext({
  structuredClone, Uint8Array, TextEncoder, AbortSignal, console,
  window: { isSecureContext: true, addEventListener() {} },
  crypto: { subtle: {} }, navigator: { serviceWorker: { getRegistration: async () => null, register: () => new Promise(() => {}) } },
  fetch: () => new Promise(() => {}), setInterval() {},
  renders, stored
});
// Exercise the real startup function with local persistence and rendering adapters.
// Network and worker registration deliberately never settle.
const source = fs.readFileSync(new URL('app.js', web), 'utf8').replaceAll('export ', '');
vm.runInContext(source, app);
vm.runInContext(`
  globalThis.document = { addEventListener() {} };
  openDB = async () => ({});
  get = async (_, key) => key === 'active' ? stored.Id : key === 'profile:' + stored.Id ? stored : undefined;
  ensureUpcoming = async () => {};
  render = () => renders.push(profile?.State.Tasks[0]?.Title);
  sync = async () => {};
  globalThis.startup = boot();
`, app);
await promptly(app.startup);
assert.deepEqual(renders, ['离线记录测试']);
console.log('PASS local account and offline edits render while auxiliary requests and worker updates are stalled');
