// Runs the actual native repository and crypto sources against local SQLite and
// Node crypto adapters. This tests transaction/account behavior, not HarmonyOS APIs.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const Module = require('node:module');
const { DatabaseSync } = require('node:sqlite');
const ts = require('../.build/harmony-tests/node_modules/typescript');
const root = path.resolve(__dirname, '..');
const source = path.join(root, 'src/DailyRecord.Harmony/entry/src/main/ets');
const stage = path.join(root, '.build/harmony-storage-tests', crypto.randomUUID());
for (const directory of ['domain', 'platform']) {
  fs.mkdirSync(path.join(stage, directory), { recursive: true });
  for (const name of fs.readdirSync(path.join(source, directory)).filter(name => name.endsWith('.ets') && name !== 'Files.ets')) {
    const compiled = ts.transpileModule(fs.readFileSync(path.join(source, directory, name), 'utf8'), {
      compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS }
    });
    fs.writeFileSync(path.join(stage, directory, name.replace(/\.ets$/, '.js')), compiled.outputText);
  }
}
const database = new DatabaseSync(path.join(stage, 'isolated.db'));
let failAfterImage = false;
let imageContentReads = 0;
class Rows {
  constructor(rows) { this.rows = rows; this.index = -1; }
  goToFirstRow() { this.index = 0; return this.rows.length > 0; }
  goToNextRow() { return ++this.index < this.rows.length; }
  getString(column) { return Object.values(this.rows[this.index])[column]; }
  close() {}
}
const rdb = {
  async executeSql(sql, args = []) {
    database.prepare(sql).run(...args);
    if (failAfterImage && sql.includes('INSERT OR REPLACE INTO records') && args[1].startsWith('image:')) {
      failAfterImage = false; throw new Error('Simulated disk failure after image write');
    }
  },
  async querySql(sql, args = []) {
    if (sql.startsWith('SELECT content FROM records') && String(args[1]).startsWith('image:')) imageContentReads++;
    return new Rows(database.prepare(sql).all(...args));
  },
  beginTransaction() { database.exec('BEGIN'); },
  commit() { database.exec('COMMIT'); },
  rollBack() { database.exec('ROLLBACK'); }
};
const util = {
  TextEncoder: class { encodeInto(text) { return new Uint8Array(Buffer.from(text, 'utf8')); } },
  TextDecoder: class { decodeToString(bytes) { return new TextDecoder('utf-8', { fatal: true }).decode(bytes); } },
  Base64Helper: class {
    decodeSync(text) { return new Uint8Array(Buffer.from(text, 'base64')); }
    encodeToStringSync(bytes) { return Buffer.from(bytes).toString('base64'); }
  }
};
const cryptoFramework = {
  createRandom() { return { generateRandomSync: size => ({ data: new Uint8Array(crypto.randomBytes(size)) }) }; },
  createMd(name) {
    assert.equal(name, 'SHA256'); const hash = crypto.createHash('sha256');
    return { async update(blob) { hash.update(blob.data); }, async digest() { return { data: new Uint8Array(hash.digest()) }; } };
  },
  createKdf(name) {
    assert.equal(name, 'PBKDF2|SHA256');
    return { async generateSecret(spec) {
      assert.equal(spec.algName, 'PBKDF2'); assert.equal(spec.iterations, 600000);
      return new Promise((resolve, reject) => crypto.pbkdf2(spec.password, spec.salt, spec.iterations, spec.keySize, 'sha256', (error, data) => error ? reject(error) : resolve({ data: new Uint8Array(data) })));
    } };
  }
};
const originalLoad = Module._load;
Module._load = function (name, parent, main) {
  if (name === '@kit.ArkData') return { relationalStore: { SecurityLevel: { S2: 2 }, async getRdbStore() { return rdb; } } };
  if (name === '@kit.ArkTS') return { util };
  if (name === '@kit.CryptoArchitectureKit') return { cryptoFramework };
  return originalLoad.call(this, name, parent, main);
};
const { Repository } = require(path.join(stage, 'platform/Repository.js'));
const { makeTask } = require(path.join(stage, 'domain/Tasks.js'));
Module._load = originalLoad;
const context = { resourceManager: { async getRawFileContent() {
  return fs.readFileSync(path.join(source, '../resources/rawfile/lunar-dates.json'));
} } };
const fixtureRoot = process.argv[2] || fs.readFileSync(path.join(root, '.build/tests/latest-result.txt'), 'utf8').match(/Data: ([^\r\n]+)/)[1] + '/file-exchange';
(async () => {
  const repo = new Repository(); await repo.open(context);
  const salt = repo.crypto.random(16), password = '原生测试密码-123';
  assert.equal(await repo.crypto.password(password, salt), crypto.pbkdf2Sync(password, Buffer.from(salt, 'hex'), 600000, 32, 'sha256').toString('hex').toUpperCase());
  const recovery = await repo.create('测试甲', password, password); const firstId = repo.account.Id;
  await repo.mutate(state => state.Tasks.push(makeTask('local-a', '甲的记录', '', '2026-10-15', false, [])));
  const restarted = new Repository(); await restarted.open(context);
  assert.equal(restarted.state.Tasks[0].Title, '甲的记录');
  await restarted.lock(); const locked = new Repository(); await locked.open(context); assert.equal(locked.account, null);
  await assert.rejects(locked.login(firstId, 'wrong password'), /密码/);
  await locked.login(firstId, password);
  const prior = JSON.stringify(locked.state);
  const input = fs.readFileSync(path.join(fixtureRoot, 'exchange.json'), 'utf8');
  const plan = await locked.importPreview(input);
  failAfterImage = true;
  await assert.rejects(locked.importCommit(plan, plan.Conflicts.map(() => 'incoming')), /disk failure/);
  assert.equal(JSON.stringify(locked.state), prior);
  assert.equal((await locked.copies()).length, 0);
  assert.equal(database.prepare("SELECT count(*) AS n FROM records WHERE key LIKE 'image:%'").get().n, 0);
  const afterFailure = new Repository(); await afterFailure.open(context);
  assert.equal(JSON.stringify(afterFailure.state), prior);
  await locked.importCommit(plan, plan.Conflicts.map(() => 'incoming'));
  imageContentReads = 0;
  await locked.mutate(state => state.Tasks[0].IsImportant = !state.Tasks[0].IsImportant);
  assert.equal(imageContentReads, 0, 'Saving task status must check image existence without reading all PNG contents');
  assert.equal(locked.state.Tasks.length, 3); assert.equal((await locked.copies()).length, 1);
  const exported = JSON.parse(await locked.exportFile()); assert.equal(exported.Images.length, 2);
  assert.equal(exported.PasswordHash, undefined); assert.ok(!JSON.stringify(exported).includes(recovery));
  console.log('PASS native SQLite persistence survives restart; failed image import rolls back state, images and before-copy together');

  const stale = await locked.importPreview(input);
  await locked.mutate(state => state.Tasks[0].Title = '已在预览后修改');
  await assert.rejects(locked.importCommit(stale, stale.Conflicts.map(() => 'incoming')), /已有变化/);
  const beforeRestore = JSON.stringify(locked.state), copy = (await locked.copies())[0];
  await locked.restoreCopy(copy.Id); assert.equal(locked.state.Tasks.length, 1); assert.equal((await locked.copies()).length, 2);
  const snapshots = database.prepare("SELECT content FROM records WHERE owner = ? AND key LIKE 'before:%'").all(firstId);
  assert.ok(snapshots.some(row => row.content === beforeRestore));
  await Promise.all(Array.from({ length: 12 }, (_, index) => locked.mutate(state => state.Tasks.push(makeTask('queued-' + index, '并行保存 ' + index, '', '2026-10-15', false, [])))));
  assert.equal(locked.state.Tasks.length, 13);
  console.log('PASS stale import previews are rejected; restore retains current copy; queued writes do not lose tasks');

  const accountPlan = await locked.importPreview(input);
  await locked.create('测试乙', password, password); assert.equal(locked.state.Tasks.length, 0);
  await assert.rejects(locked.importCommit(accountPlan, accountPlan.Conflicts.map(() => 'incoming')), /账号或记录/);
  assert.equal((await locked.copies()).length, 0);
  const clean = JSON.parse((JSON.parse(await locked.exportFile())).Payload); assert.equal(clean.Tasks.length, 0);
  await locked.lock();
  const newCode = await locked.resetPassword(firstId, recovery.toLowerCase(), '新测试密码-456', '新测试密码-456');
  await assert.rejects(locked.login(firstId, password), /密码/);
  await assert.rejects(locked.resetPassword(firstId, recovery, '另一密码-789', '另一密码-789'), /恢复码/);
  await locked.login(firstId, '新测试密码-456'); assert.equal(locked.state.Tasks.length, 13); assert.notEqual(newCode, recovery);
  console.log('PASS account data/copies remain isolated; recovery rotates its code and password without losing records');
  console.log('Platform adapters are local test doubles; official SDK compilation and real-device validation remain required.');
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(() => database.close());
