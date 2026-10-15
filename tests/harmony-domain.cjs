const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const ts=require('../.build/harmony-tests/node_modules/typescript');
const root=path.resolve(__dirname,'..'),source=path.join(root,'src/DailyRecord.Harmony/entry/src/main/ets/domain');
const stage=path.join(root,'.build/harmony-domain-tests');fs.mkdirSync(stage,{recursive:true});
for(const name of ['Models','Exchange','Tasks'])fs.copyFileSync(path.join(source,name+'.ets'),path.join(stage,name+'.ts'));
const program=ts.createProgram(['Models','Exchange','Tasks'].map(n=>path.join(stage,n+'.ts')),{strict:true,target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS,outDir:path.join(stage,'compiled'),skipLibCheck:true});
const diagnostics=ts.getPreEmitDiagnostics(program);
if(diagnostics.length){process.stderr.write(ts.formatDiagnosticsWithColorAndContext(diagnostics,{getCanonicalFileName:f=>f,getCurrentDirectory:()=>root,getNewLine:()=> '\n'}));process.exit(1);}
program.emit();
const models=require(path.join(stage,'compiled/Models.js')),exchange=require(path.join(stage,'compiled/Exchange.js')),tasks=require(path.join(stage,'compiled/Tasks.js'));
const {AppState,copyState,validateState}=models;
const port={hashText:async text=>crypto.createHash('sha256').update(text,'utf8').digest('hex').toUpperCase(),hashBytes:async bytes=>crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase(),decodeBase64:text=>new Uint8Array(Buffer.from(text,'base64'))};
const fixtureRoot=process.argv[2]||fs.readFileSync(path.join(root,'.build/tests/latest-result.txt'),'utf8').match(/Data: ([^\r\n]+)/)[1]+'/file-exchange';
(async()=>{
 for(const name of ['difference','occurrence']){
  const fixture=JSON.parse(fs.readFileSync(path.join(fixtureRoot,name+'.json'),'utf8')),before=JSON.stringify(fixture),plan=exchange.previewImport(fixture.Current,fixture.Incoming);
  assert.equal(plan.Added,fixture.ExpectedAdded);assert.equal(plan.Conflicts.length,fixture.ExpectedConflicts);assert.equal(JSON.stringify(fixture),before,'Preview mutated input');
  if(plan.Conflicts.length){assert.throws(()=>exchange.resolveImport(plan,[]));assert.equal(exchange.resolveImport(plan,['incoming']).Tasks[0].Title,fixture.Incoming.Tasks[0].Title);}
  else{const state=exchange.resolveImport(plan,[]);assert.equal(state.Tasks.length,1);assert.equal(state.Activities[0].TaskId,state.Tasks[0].Id);assert.equal(exchange.previewImport(state,fixture.Incoming).Added,0);}
 }
 console.log('PASS native ArkTS domain passes strict TypeScript checking and agrees with desktop merge/occurrence fixtures');
 const file=await exchange.readExchange(fs.readFileSync(path.join(fixtureRoot,'exchange.json'),'utf8'),port);assert.equal(file.Images.length,2);
 const outgoing=await exchange.writeExchange(JSON.parse(file.Payload),file.Images,port),second=await exchange.readExchange(outgoing,port);
 assert.deepEqual(JSON.parse(second.Payload),JSON.parse(file.Payload));assert.deepEqual(second.Images,file.Images);
 fs.writeFileSync(path.join(stage,'native-export.json'),outgoing);
 const corrupt=JSON.parse(outgoing);corrupt.Payload+=' ';await assert.rejects(exchange.readExchange(JSON.stringify(corrupt),port),/校验/);
 const missing=JSON.parse(outgoing);missing.Images.pop();missing.Sha256=await port.hashText(missing.Payload+'\n'+missing.Images.map(i=>i.Id).sort().join('\n'));await assert.rejects(exchange.readExchange(JSON.stringify(missing),port),/缺少/);
 const badPng=JSON.parse(outgoing);badPng.Images[0].Data=Buffer.from('bad image').toString('base64');await assert.rejects(exchange.readExchange(JSON.stringify(badPng),port),/图片校验/);
 console.log('PASS desktop/native exchange roundtrip retains all records and both images; corruption and missing images are rejected');
 const fixture=JSON.parse(fs.readFileSync(path.join(fixtureRoot,'difference.json'),'utf8')),state=copyState(fixture.Current),id=state.Tasks[0].Id;
 tasks.toggleCompleted(state,id,'complete-1','2026-10-15');tasks.toggleCompleted(state,id,'undo-1','2026-10-15');tasks.toggleCompleted(state,id,'complete-2','2026-10-16');
 assert.equal(state.Tasks[0].CompletedOn,'2026-10-16');assert.equal(state.Activities.filter(a=>a.Kind==='Completed'&&!a.Reverted).length,1);
 const other=copyState(state);tasks.toggleCompleted(other,id,'undo-2','2026-10-16');tasks.toggleCompleted(other,id,'complete-3','2026-10-17');
 const plan=exchange.previewImport(state,other),merged=exchange.resolveImport(plan,plan.Conflicts.map(()=> 'incoming'));
 assert.equal(merged.Tasks[0].CompletedOn,'2026-10-17');assert.equal(merged.Activities.filter(a=>a.Kind==='Completed'&&!a.Reverted).length,1);
 assert.equal(state.Tasks[0].CompletedOn,'2026-10-16');
 const invalid=copyState(state);invalid.Tasks[0].CreatedAt='invalid';assert.throws(()=>validateState(invalid));
 for (const field of ['IsImportant','HasOverride']) { const malformed=copyState(state);malformed.Tasks[0][field]='false';assert.throws(()=>validateState(malformed)); }
 const wrongOccurrence=copyState(state);wrongOccurrence.Tasks[0].EventId=null;wrongOccurrence.Tasks[0].OccurrenceDate='not-a-date';assert.throws(()=>validateState(wrongOccurrence));
 console.log('PASS completion conflicts preserve snapshots without counting a task twice, preview and resolution leave originals unchanged');
 const event={StartDate:'2024-01-31',EndDate:null,Repeat:'Monthly'};
 assert.deepEqual(tasks.occurrences(event,'2024-01-01','2024-03-31',[]),['2024-01-31','2024-02-29','2024-03-31']);
 assert.deepEqual(tasks.occurrences({...event,Repeat:'Yearly',StartDate:'2024-02-29'},'2024-01-01','2028-12-31',[]),['2024-02-29','2025-02-28','2026-02-28','2027-02-28','2028-02-29']);
 const lunar=JSON.parse(fs.readFileSync(path.join(root,'src/DailyRecord.Harmony/entry/src/main/resources/rawfile/lunar-dates.json'),'utf8'));
 assert.deepEqual(tasks.occurrences({...event,StartDate:'2026-01-01',Repeat:'LunarYearly',LunarMonth:1,LunarDay:1},'2026-01-01','2026-12-31',lunar),['2026-02-17']);
 console.log('PASS native monthly clamping, leap years and lunar recurrence agree with existing desktop rules');
 console.log('Native export fixture: '+path.join(stage,'native-export.json'));
})().catch(error=>{console.error(error);process.exitCode=1;});
