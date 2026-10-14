import assert from 'node:assert/strict';
import fs from 'node:fs';
import {occurrences,imageIds,dateString,portableHash,validateMobileState,ProfileWriter,visible,endLongTerm} from '../src/DailyRecord.Mobile/app.js';
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
 state.Tasks[0].ScheduledDate='2026-02-30';assert.throws(()=>validateMobileState(state));
 console.log('PASS portable backup: desktop hash matches browser implementation, PNG references preserved, invalid dates rejected before restore');
}

let cached={Id:"queue",Revision:0,State:{Tasks:[]}};
const writer=new ProfileWriter(()=>cached,async value=>{await new Promise(resolve=>setTimeout(resolve,5));cached=value;});
await Promise.all([writer.update("queue",p=>{p.State.Tasks.push({Title:"第一条"});p.Revision++;}),writer.update("queue",p=>{p.State.Tasks.push({Title:"第二条"});p.Revision++;})]);
assert.equal(cached.State.Tasks.length,2);assert.equal(cached.Revision,2);
console.log("PASS queued mobile saves preserve both concurrent edits");
