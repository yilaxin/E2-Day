import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import {webcrypto} from 'node:crypto';
import {previewImport,resolveImport,portableHash,canonical} from '../src/DailyRecord.Mobile/app.js';
const directory=process.argv[2];
if(!directory)throw new Error('Pass the desktop fixture directory');
for(const name of ['difference','occurrence']){
 const fixture=JSON.parse(fs.readFileSync(directory+'/'+name+'.json','utf8'));
 const before=JSON.stringify(fixture),plan=previewImport(fixture.Current,fixture.Incoming);
 assert.equal(plan.Added,fixture.ExpectedAdded);assert.equal(plan.Conflicts.length,fixture.ExpectedConflicts);
 assert.equal(JSON.stringify(fixture),before,'Preview changed its input');
 if(plan.Conflicts.length){assert.throws(()=>resolveImport(plan,{}));const chosen=resolveImport(plan,{0:'incoming'});assert.equal(chosen.Tasks[0].Title,fixture.Incoming.Tasks[0].Title);}
 else{const merged=resolveImport(plan,{});assert.equal(merged.Tasks.length,1);assert.equal(merged.Activities[0].TaskId,merged.Tasks[0].Id);assert.equal(previewImport(merged,fixture.Incoming).Added,0);}
}
const file=JSON.parse(fs.readFileSync(directory+'/exchange.json','utf8'));
assert.equal(file.Format,'E2DayExchange');assert.equal(await portableHash(file),file.Sha256);assert.equal(file.Images.length,2);
const original=JSON.parse(fs.readFileSync(directory+'/difference.json','utf8')).Current;
const equivalent=structuredClone(original);equivalent.Tasks[0].CreatedAt='2026-10-14T00:00:00.123Z';
assert.equal(previewImport(original,equivalent).Conflicts.length,0);
console.log('PASS desktop/mobile import plans agree, timestamps normalize, duplicates stay single, and image exchange hashes match');

const local={Id:'local-account',Revision:0,State:original};const added=structuredClone(original);added.Tasks[0].Id='new-phone-task';
let failed=true;let committed=[];
const database={transaction(){const pending=[];const tx={error:new Error('QuotaExceededError'),objectStore:store=>({put:(value,key)=>pending.push({store,key,value})})};queueMicrotask(()=>{if(failed)tx.onabort();else{committed=pending;tx.oncomplete();}});return tx;}};
const context=vm.createContext({structuredClone,crypto:webcrypto,Uint8Array,TextEncoder,AbortSignal,console,Blob,Date,fixture:local,fakeDb:database});
const source=fs.readFileSync(new URL('../src/DailyRecord.Mobile/app.js',import.meta.url),'utf8').replaceAll('export ','');
vm.runInContext(source,context);
vm.runInContext('profile=structuredClone(fixture); database=fakeDb; globalThis.inspectProfile=()=>profile; globalThis.importFile=commitImport;',context);
const plan=previewImport(original,added);plan.AccountId=local.Id;
await assert.rejects(context.importFile(plan,{},[]),/QuotaExceeded/);assert.equal(canonical(context.inspectProfile()),canonical(local));assert.equal(committed.length,0);
failed=false;await context.importFile(plan,{},[]);assert.equal(context.inspectProfile().State.Tasks.length,2);
assert.ok(committed.some(item=>String(item.key).startsWith('before-restore:local-account:')));assert.ok(committed.some(item=>item.key==='profile:local-account'));
const other=previewImport(context.inspectProfile().State,added);other.AccountId='different-account';await assert.rejects(context.importFile(other,{},[]),/账号/);
console.log('PASS mobile import commits records, images and the before-import copy together; quota failure and account switching preserve the original');
