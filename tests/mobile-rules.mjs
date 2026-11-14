import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {occurrences,imageIds,dateString,portableHash,validateMobileState,ProfileWriter,visible,endLongTerm,convertedOccurrence,completionOrigin,previewImport,resolveImport} from '../src/DailyRecord.Mobile/app.js';
const event={StartDate:'2024-01-31',EndDate:null,Repeat:'Monthly'};
assert.deepEqual(occurrences(event,'2024-01-01','2024-03-31'),['2024-01-31','2024-02-29','2024-03-31']);
assert.deepEqual(occurrences({...event,StartDate:'2024-02-29',Repeat:'Yearly'},'2024-01-01','2028-12-31'),['2024-02-29','2025-02-28','2026-02-28','2027-02-28','2028-02-29']);
assert.deepEqual(occurrences({...event,StartDate:'2026-10-12',Repeat:'Weekly',EndDate:'2026-10-25'},'2026-10-13','2026-11-01'),['2026-10-19']);
const lunar=JSON.parse(fs.readFileSync(new URL('../src/DailyRecord.Mobile/lunar-dates.json',import.meta.url),'utf8'));
assert.deepEqual(occurrences({...event,StartDate:'2026-01-01',Repeat:'LunarYearly',LunarMonth:1,LunarDay:1},'2026-01-01','2026-12-31',lunar),['2026-02-17']);
assert.equal(lunar['2-30'].filter(date=>date.startsWith('2023-')).length,1);
assert.equal(dateString(new Date(2026,9,12,0,5)),'2026-10-12');
assert.deepEqual(imageIds({Tasks:[{ImageIds:['A','B']}],Events:[{ImageIds:['B']}],Activities:[{ImageIds:['C']}]}),['A','B','C']);
console.log('PASS mobile recurrence: monthly clamping, leap years, weekly boundary, lunar regular month, local date and historic image references');
assert.deepEqual(occurrences({StartDate:'2028-02-28',EndDate:'2028-03-01',Repeat:'Daily'},'2028-02-01','2028-03-31'),['2028-02-28','2028-02-29','2028-03-01']);
const dailyState={Events:[{Id:'daily',Title:'长期',Repeat:'Daily',StartDate:'2026-10-10',EndDate:null}],Tasks:[],Activities:[],Settings:{NormalDays:0,ImportantDays:3,VeryImportantDays:7}};
const dailyTask=day=>({Id:day,Title:'每天推进',EventId:'daily',ScheduledDate:day,OriginalScheduledDate:day,OccurrenceDate:day,Notes:'',ImageIds:[],IsImportant:true,State:'Open'});
dailyState.Tasks.push(dailyTask('2026-10-11'),dailyTask('2026-10-12'),dailyTask('2026-10-13'));
assert.equal(visible(dailyState.Tasks[0],'2026-10-12',dailyState),false);assert.equal(visible(dailyState.Tasks[1],'2026-10-12',dailyState),true);
endLongTerm(dailyState,'2026-10-12','2026-10-12');assert.equal(dailyState.Events[0].EndDate,'2026-10-11');assert.equal(dailyState.Tasks[2].State,'Cancelled');
assert.equal(visible(dailyState.Tasks[1],'2026-10-12',dailyState),false);assert.equal(dailyState.Activities.at(-1).Kind,'Ended');
endLongTerm(dailyState,'2026-10-12','2026-10-12');assert.equal(dailyState.Activities.filter(a=>a.Kind==='Ended').length,1);
console.log('PASS mobile daily recurrence, no overdue accumulation, explicit end and preserved end history');
assert.throws(()=>validateMobileState({SchemaVersion:2,Tasks:[],Events:[],Activities:[],Settings:{NormalDays:-1,ImportantDays:3,VeryImportantDays:7},News:{}}));
if(process.argv[2]){
 const backup=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));
 assert.equal(await portableHash(backup),backup.Sha256);
 const state=validateMobileState(JSON.parse(backup.Payload));
 assert.equal(state.Tasks[0].Title,'通用备份示例');
 const invalidTime=structuredClone(state);invalidTime.Tasks[0].CreatedAt='not-a-time';assert.throws(()=>validateMobileState(invalidTime));
 invalidTime.Tasks[0].CreatedAt='2026-10-14';assert.throws(()=>validateMobileState(invalidTime));
 state.Tasks[0].ScheduledDate='2026-02-30';assert.throws(()=>validateMobileState(state));
 console.log('PASS portable backup: desktop hash matches browser implementation, PNG references preserved, invalid dates rejected before restore');
 const quickFile=path.join(path.dirname(process.argv[2]),'quick-color-sample.json');
 if(fs.existsSync(quickFile)){
  const backup=JSON.parse(fs.readFileSync(quickFile,'utf8'));assert.equal(await portableHash(backup),backup.Sha256);
  const data=validateMobileState(JSON.parse(backup.Payload));assert.equal(data.Settings.QuickTaskColor,'Blue');
  assert.deepEqual(data.Tasks[0].QuickColorRestore,{AppliedColor:'Blue',PreviousColor:'Purple',PreviousOverride:false});
  const round=resolveImport(previewImport(data,structuredClone(data)),{});assert.deepEqual(round.Tasks[0].QuickColorRestore,data.Tasks[0].QuickColorRestore);
  const bad=structuredClone(data);bad.Tasks[0].QuickColorRestore.AppliedColor='None';assert.throws(()=>validateMobileState(bad));
  bad.Tasks[0].QuickColorRestore=null;bad.Settings.QuickTaskColor='None';assert.throws(()=>validateMobileState(bad));
  console.log('PASS quick-color restore metadata, custom preference, portable hash, file roundtrip and invalid metadata rejection');
 }
 const colorFile=path.join(path.dirname(process.argv[2]),'color-sample.json');
 if(fs.existsSync(colorFile)){
  const colorBackup=JSON.parse(fs.readFileSync(colorFile,'utf8'));assert.equal(await portableHash(colorBackup),colorBackup.Sha256);
  const colored=validateMobileState(JSON.parse(colorBackup.Payload));
  assert.equal(colored.Tasks[0].Color,'Gray');assert.equal(colored.Activities[0].Color,'Blue');assert.equal(colored.Settings.TaskColorNames[0],'上午');
  const round=resolveImport(previewImport(colored,structuredClone(colored)),{});assert.equal(round.Tasks[0].Color,'Gray');assert.equal(round.Activities[0].Color,'Blue');
  const invalidColor=structuredClone(colored);invalidColor.Tasks[0].Color='Orange';assert.throws(()=>validateMobileState(invalidColor));
  invalidColor.Tasks[0].Color='Blue';invalidColor.Settings.TaskColorNames=['a','a','b','c','d'];assert.throws(()=>validateMobileState(invalidColor));
  console.log('PASS desktop color backup hash, task/history color, custom names, image payload, file roundtrip and invalid metadata rejection');
 }
 const convertedFile=path.join(path.dirname(process.argv[2]),'conversion-sample.json');
 if(fs.existsSync(convertedFile)){
  const convertedBackup=JSON.parse(fs.readFileSync(convertedFile,'utf8'));assert.equal(await portableHash(convertedBackup),convertedBackup.Sha256);
  const converted=validateMobileState(JSON.parse(convertedBackup.Payload)),task=converted.Tasks.find(t=>t.ConversionSources?.length);
  assert(task);assert.equal(task.EventId,null);assert.equal(task.State,'Completed');assert.equal(task.CompletedOrigin.Repeat,'Daily');
  const source=task.ConversionSources[0];assert.equal(convertedOccurrence(converted,source.EventId,source.Day),true);
  const round=resolveImport(previewImport(converted,structuredClone(converted)),{});assert.deepEqual(round.Tasks.find(t=>t.Id===task.Id).ConversionSources,task.ConversionSources);
  assert.deepEqual(completionOrigin(converted,task),{EventId:null,Day:null,Repeat:'None'});
  const invalid=structuredClone(converted);invalid.Tasks.push({...structuredClone(task),Id:'regenerated',EventId:source.EventId,OccurrenceDate:source.Day,ConversionSources:[]});assert.throws(()=>validateMobileState(invalid));
  console.log('PASS converted task portable backup, image payload, original habit origin, no source regeneration, file roundtrip and duplicate rejection');
 }
}

let cached={Id:"queue",Revision:0,State:{Tasks:[]}};
const writer=new ProfileWriter(()=>cached,async value=>{await new Promise(resolve=>setTimeout(resolve,5));cached=value;});
await Promise.all([writer.update("queue",p=>{p.State.Tasks.push({Title:"第一条"});p.Revision++;}),writer.update("queue",p=>{p.State.Tasks.push({Title:"第二条"});p.Revision++;})]);
assert.equal(cached.State.Tasks.length,2);assert.equal(cached.Revision,2);
console.log("PASS queued mobile saves preserve both concurrent edits");
