const clone=value=>structuredClone(value);
const hex=bytes=>Array.from(new Uint8Array(bytes),b=>b.toString(16).padStart(2,'0')).join('').toUpperCase();
const digest=async text=>hex(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(text)));
export const dateString=date=>`${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`;
const dayDate=value=>new Date(`${value}T12:00:00`);
const today=()=>dateString(new Date());
const addDays=(value,days)=>{const d=dayDate(value);d.setDate(d.getDate()+days);return dateString(d);};
const uuid=()=>crypto.randomUUID().replaceAll('-','');
const emptyState=()=>({SchemaVersion:2,Events:[],Tasks:[],Activities:[],Settings:{NormalDays:0,ImportantDays:3,VeryImportantDays:7},News:{FetchedAt:null,Articles:[]}});
export const imageIds=state=>[...new Set([...state.Tasks,...state.Events,...state.Activities].flatMap(record=>record.ImageIds||[]))];
export const portableHash=async backup=>digest(backup.Payload+'\n'+backup.Images.map(image=>image.Id).sort().join('\n'));

export class ProfileWriter{
 constructor(read,persist){this.read=read;this.persist=persist;this.tail=Promise.resolve();}
 enqueue(action){const result=this.tail.then(action);this.tail=result.catch(()=>{});return result;}
 update(id,change){return this.enqueue(async()=>{const current=this.read();if(!current||current.Id!==id)throw new Error('账号已切换，请重新打开操作。');const next=clone(current);if(await change(next)===false)return current;await this.persist(next);return next;});}
 replace(next){return this.enqueue(async()=>{await this.persist(next);return next;});}
}
export function validateMobileState(state){
 const fail=()=>{throw new Error('备份记录不完整，当前记录未替换。');};
 const validDate=value=>typeof value==='string'&&value>='1901-01-01'&&value<='2100-12-31'&&/^\d{4}-\d{2}-\d{2}$/.test(value)&&dateString(dayDate(value))===value;
 const validTime=value=>typeof value==='string'&&/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(value)&&Number.isFinite(Date.parse(value));
 if(!state||![1,2].includes(state.SchemaVersion)||!Array.isArray(state.Tasks)||!Array.isArray(state.Events)||!Array.isArray(state.Activities)||!state.Settings||!state.News||!Array.isArray(state.News.Articles))fail();
 const records=[...state.Tasks,...state.Events,...state.Activities];
 for(const collection of [state.Tasks,state.Events,state.Activities])if(collection.some(row=>!row||typeof row.Id!=='string'||!row.Id)||new Set(collection.map(row=>row.Id)).size!==collection.length)fail();
 for(const row of records)if(typeof row.Title!=='string'||!row.Title.trim()||typeof row.Notes!=='string'||!Array.isArray(row.ImageIds)||row.ImageIds.length>8||new Set(row.ImageIds).size!==row.ImageIds.length||row.ImageIds.some(id=>typeof id!=='string'||! /^[A-F0-9]{64}$/.test(id)))fail();
 const events=new Set(state.Events.map(row=>row.Id)),tasks=new Set(state.Tasks.map(row=>row.Id)),occurrencesSeen=new Set();
 for(const row of state.Events)if(!validDate(row.StartDate)||!['Normal','Important','VeryImportant'].includes(row.Importance)||!['None','Weekly','Monthly','Yearly','LunarYearly','Daily'].includes(row.Repeat)||row.Repeat==='LunarYearly'&&(!Number.isInteger(row.LunarMonth)||row.LunarMonth<1||row.LunarMonth>12||!Number.isInteger(row.LunarDay)||row.LunarDay<1||row.LunarDay>30))fail();
 for(const row of state.Tasks){if(!validDate(row.ScheduledDate)||!validDate(row.OriginalScheduledDate)||!['Open','Completed','Cancelled'].includes(row.State)||!['Normal','Important','VeryImportant'].includes(row.EventImportance)||typeof row.CreatedAt!=='string'||row.CompletedOn!==null&&row.CompletedOn!==undefined&&!validDate(row.CompletedOn)||row.State==='Completed'&&!row.CompletedOn)fail();if(row.EventId){const key=row.EventId+'/'+row.OccurrenceDate;if(!events.has(row.EventId)||!validDate(row.OccurrenceDate)||occurrencesSeen.has(key))fail();occurrencesSeen.add(key);}}
 for(const row of state.Tasks)if(!validTime(row.CreatedAt))fail();
 for(const row of state.Activities)if(!tasks.has(row.TaskId)||!validDate(row.Day)||!validDate(row.ScheduledDate)||!validTime(row.At)||!['Viewed','Completed','Cancelled','Ended'].includes(row.Kind))fail();
 if(['NormalDays','ImportantDays','VeryImportantDays'].some(key=>!Number.isInteger(state.Settings[key])||state.Settings[key]<0||state.Settings[key]>365))fail();
 return state;
}
export function occurrences(event,from,to,lunar={}){
 const result=[],start=event.StartDate;from=from>start?from:start;if(event.EndDate&&event.EndDate<to)to=event.EndDate;if(to<from)return result;
 const push=date=>{if(date>=from&&date<=to)result.push(date);};
 const s=dayDate(start),f=dayDate(from),t=dayDate(to);
 if(event.Repeat==='None')push(start);
 else if(event.Repeat==='Daily'){for(let date=from;date<=to;date=addDays(date,1))push(date);}
 else if(event.Repeat==='Weekly'){let date=from;while(dayDate(date).getDay()!==s.getDay())date=addDays(date,1);for(;date<=to;date=addDays(date,7))push(date);}
 else if(event.Repeat==='Monthly'){for(let y=f.getFullYear(),m=f.getMonth();y<t.getFullYear()||y===t.getFullYear()&&m<=t.getMonth();){push(dateString(new Date(y,m,Math.min(s.getDate(),new Date(y,m+1,0).getDate()),12)));if(++m===12){m=0;y++;}}}
 else if(event.Repeat==='Yearly'){for(let y=f.getFullYear();y<=t.getFullYear();y++)push(dateString(new Date(y,s.getMonth(),Math.min(s.getDate(),new Date(y,s.getMonth()+1,0).getDate()),12)));}
 else if(event.Repeat==='LunarYearly'){for(const date of lunar[`${event.LunarMonth}-${event.LunarDay}`]||[])push(date);}
 return result;
}
let database,profile=null,page='today',selectedDay=today(),month=selectedDay.slice(0,7),lunar={},savedText='已保存到本机',offlineReady=false;
let objectUrls=[],editorUrls=[];
const profileWriter=new ProfileWriter(()=>profile,async next=>{await put('kv',`profile:${next.Id}`,next);profile=next;});
const $=selector=>document.querySelector(selector);
const node=(tag,attributes={},...children)=>{
 const element=document.createElement(tag);
 for(const [key,value]of Object.entries(attributes)){
  if(key.startsWith('on'))element.addEventListener(key.slice(2),value);
  else if(key==='class')element.className=value;
  else if(key==='checked'||key==='disabled')element[key]=!!value;
  else if(value!==undefined&&value!==null)element.setAttribute(key,String(value));
 }
 for(const child of children.flat())if(child!==null&&child!==undefined)element.append(child instanceof Node?child:document.createTextNode(String(child)));
 return element;
};
const btn=(text,action,className='')=>node('button',{type:'button',class:className,onclick:async event=>{const button=event.currentTarget;button.disabled=true;try{await action();}catch(error){toast(error.message);}finally{button.disabled=false;}}},text);
const paragraph=(text,className='sub')=>node('p',{class:className},text);
function toast(message){const target=$('#toast');target.textContent=message;target.style.display='block';clearTimeout(toast.timer);toast.timer=setTimeout(()=>target.style.display='none',5000);}
function openDB(){return new Promise((resolve,reject)=>{const request=indexedDB.open('e2day-independent-local-v1',1);request.onupgradeneeded=()=>{request.result.createObjectStore('kv');request.result.createObjectStore('images');};request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error);});}
function get(store,key){return new Promise((resolve,reject)=>{const tx=database.transaction(store),request=tx.objectStore(store).get(key);request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error);});}
function put(store,key,value){return new Promise((resolve,reject)=>{const tx=database.transaction(store,'readwrite');tx.objectStore(store).put(value,key);tx.oncomplete=resolve;tx.onerror=()=>reject(tx.error);tx.onabort=()=>reject(tx.error||new Error('手机存储不足，修改尚未保存。'));});}
function allKeys(){return new Promise((resolve,reject)=>{const request=database.transaction('kv').objectStore('kv').getAllKeys();request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error);});}
async function keys(){return (await allKeys()).filter(key=>String(key).startsWith('profile:'));}
async function save(next){await profileWriter.replace(next);}
async function edit(change){const id=profile.Id;await profileWriter.update(id,next=>{change(next.State);next.Revision=(next.Revision||0)+1;});savedText='已保存到本机';render();}
async function verifier(password,salt,iterations){const bytes=new Uint8Array(salt.match(/../g).map(v=>parseInt(v,16)));const key=await crypto.subtle.importKey('raw',new TextEncoder().encode(password),'PBKDF2',false,['deriveBits']);return hex(await crypto.subtle.deriveBits({name:'PBKDF2',salt:bytes,iterations,hash:'SHA-256'},key,256));}
async function unlock(stored,password){
 if(await verifier(password,stored.Salt,stored.Iterations)!==stored.PasswordHash)throw new Error('密码不正确，请使用本机账号密码或离线恢复码。');
 await save(stored);await put('kv','active',stored.Id);await ensureUpcoming();savedText='已保存到本机';render();
}
async function ensureUpcoming(){
 if(!profile)return;const id=profile.Id;await profileWriter.update(id,async next=>{let changed=false;
 for(const event of next.State.Events){const lead=next.State.Settings[`${event.Importance==='Normal'?'Normal':event.Importance==='Important'?'Important':'VeryImportant'}Days`]||0;
  for(const date of occurrences(event,event.Repeat==='Daily'?today():event.StartDate,event.Repeat==='Daily'?today():addDays(today(),lead),lunar))if(!next.State.Tasks.some(task=>task.EventId===event.Id&&(event.Repeat==='None'||task.OccurrenceDate===date))){
   next.State.Tasks.push({Id:(await digest(`${event.Id}:${date}`)).slice(0,32).toLowerCase(),EventId:event.Id,OccurrenceDate:date,ScheduledDate:date,OriginalScheduledDate:date,Title:event.Title,Notes:event.Notes,ImageIds:[...event.ImageIds],EventImportance:event.Importance,IsImportant:!!event.IsImportant,HasOverride:false,State:'Open',CreatedAt:new Date().toISOString(),CompletedOn:null,ArchiveReason:''});changed=true;
  }
 }
 if(!changed)return false;next.Revision=(next.Revision||0)+1;
 });
}
export function canonical(value){
 if(Array.isArray(value))return '['+value.map(canonical).join(',')+']';
 if(value&&typeof value==='object')return '{'+Object.keys(value).filter(key=>value[key]!==undefined).sort().map(key=>{let item=value[key];if(['CreatedAt','At'].includes(key)&&typeof item==='string'&&!Number.isNaN(Date.parse(item)))item=new Date(item).toISOString();return JSON.stringify(key)+':'+canonical(item);}).join(',')+'}';
 return JSON.stringify(value);
}
const recordFingerprint=state=>canonical({...state,News:null});
export function previewImport(current,incoming){
 validateMobileState(current);validateMobileState(incoming);const fingerprint=recordFingerprint(current),proposed=clone(current),foreign=clone(incoming),conflicts=[];let added=0,unchanged=0;
 const occurrences=new Map(current.Tasks.filter(task=>task.EventId).map(task=>[task.EventId+'/'+task.OccurrenceDate,task.Id]));
 for(const task of foreign.Tasks.filter(task=>task.EventId)){const id=occurrences.get(task.EventId+'/'+task.OccurrenceDate);if(id&&id!==task.Id){for(const history of foreign.Activities.filter(history=>history.TaskId===task.Id))history.TaskId=id;task.Id=id;}}
 validateMobileState(foreign);
 for(const collection of ['Events','Tasks','Activities']){const rows=proposed[collection],byId=new Map(rows.map(row=>[row.Id,row]));for(const row of foreign[collection]){const old=byId.get(row.Id);if(!old){rows.push(clone(row));added++;continue;}if(collection==='Tasks'&&row.EventId){const a=clone(old),b=clone(row);delete a.CreatedAt;delete b.CreatedAt;if(canonical(a)===canonical(b))row.CreatedAt=old.CreatedAt;}if(canonical(old)===canonical(row))unchanged++;else conflicts.push({Collection:collection,Id:row.Id,Current:clone(old),Incoming:clone(row)});}}
 validateMobileState(proposed);return {Fingerprint:fingerprint,Proposed:proposed,Conflicts:conflicts,Added:added,Unchanged:unchanged};
}
export function resolveImport(plan,choices){
 if(Object.keys(choices).length!==plan.Conflicts.length||plan.Conflicts.some((_,index)=>!['current','incoming'].includes(choices[index])))throw new Error('请为每条不同的记录选择要保留的版本。');
 const state=clone(plan.Proposed);plan.Conflicts.forEach((conflict,index)=>{const at=state[conflict.Collection].findIndex(row=>row.Id===conflict.Id);state[conflict.Collection][at]=clone(choices[index]==='incoming'?conflict.Incoming:conflict.Current);});validateMobileState(state);
 for(const task of state.Tasks){const histories=state.Activities.filter(history=>history.TaskId===task.Id&&!history.Reverted);for(const history of histories)if(history.Kind==='Completed'&&(task.State!=='Completed'||history.Day!==task.CompletedOn)||history.Kind==='Cancelled'&&task.State!=='Cancelled')history.Reverted=true;const groups=new Map();for(const history of histories.filter(history=>!history.Reverted)){const key=history.Kind+'/'+history.Day;if(!groups.has(key))groups.set(key,[]);groups.get(key).push(history);}for(const group of groups.values())for(const redundant of group.sort((a,b)=>Date.parse(a.At)-Date.parse(b.At)||a.Id.localeCompare(b.Id)).slice(1))redundant.Reverted=true;}
 return state;
}
export function visible(task,day,state){const event=state.Events.find(e=>e.Id===task.EventId);if(event?.Repeat==='Daily')return task.State==='Open'&&task.ScheduledDate===day&&event.StartDate<=day&&(!event.EndDate||event.EndDate>=day);const lead=state.Settings[`${task.EventImportance==='Normal'?'Normal':task.EventImportance==='Important'?'Important':'VeryImportant'}Days`]||0;return task.State==='Open'&&(task.EventId?addDays(task.ScheduledDate,-lead):task.ScheduledDate)<=day;}
function sorted(tasks){return [...tasks].sort((a,b)=>Number(b.IsImportant)-Number(a.IsImportant)||a.ScheduledDate.localeCompare(b.ScheduledDate)||a.CreatedAt.localeCompare(b.CreatedAt));}
function taskRow(task){
 const card=node('article',{class:`task ${task.State==='Completed'?'completed':''}`});
 card.append(btn(task.State==='Completed'?'✓':'',()=>toggle(task.Id),'check '+(task.State==='Completed'?'done':'')));card.firstChild.setAttribute('aria-label',task.State==='Completed'?'撤销完成':'标记完成');
 const content=node('div',{class:'task-content'},node('div',{class:'task-title'},task.Title));
 content.append(btn('编辑',()=>taskEditor(task.Id),'quiet'));content.lastChild.style.float='right';content.lastChild.style.fontSize='10px';content.lastChild.style.padding='2px 0';
 if(task.Notes)content.append(node('div',{class:'task-notes'},task.Notes));
 const longTerm=profile.State.Events.find(e=>e.Id===task.EventId&&e.Repeat==='Daily');
 const metadata=node('div',{class:'task-meta'},node('span',{},task.ScheduledDate),node('span',{},longTerm?'长期任务':task.EventId?'日历事件':'日常任务'));content.append(metadata);
 if(longTerm&&(!longTerm.EndDate||longTerm.EndDate>=today()))content.append(btn('结束长期任务',()=>edit(state=>endLongTerm(state,task.Id)),'quiet danger'));
 if(task.ImageIds.length){const images=node('div',{class:'task-images'});content.append(images);for(const id of task.ImageIds){const owner=profile.Id;void get('images',`${owner}/${id}`).then(blob=>{if(!blob||!images.isConnected)return;const url=URL.createObjectURL(blob);objectUrls.push(url);images.append(node('img',{src:url,alt:'备注图片',onclick:()=>preview(blob)}));});}}
 card.append(content,btn(task.IsImportant?'★':'☆',()=>edit(state=>{const t=state.Tasks.find(t=>t.Id===task.Id);t.IsImportant=!t.IsImportant;}),'star'));return card;
}
function snapshot(state,task,kind,day=today()){state.Activities.push({Id:uuid(),TaskId:task.Id,Kind:kind,Day:day,At:new Date().toISOString(),Title:task.Title,Notes:task.Notes,ImageIds:[...task.ImageIds],ScheduledDate:task.ScheduledDate,IsImportant:task.IsImportant,Reverted:false});}
export function endLongTerm(state,id,day=today()){
 const selected=state.Tasks.find(t=>t.Id===id),event=state.Events.find(e=>e.Id===selected?.EventId&&e.Repeat==='Daily');
 if(!event)throw new Error('这不是长期任务。');if(event.EndDate&&event.EndDate<day)return;event.EndDate=addDays(day,-1);
 for(const task of state.Tasks.filter(t=>t.EventId===event.Id&&t.State==='Open'&&t.ScheduledDate>=day)){task.State='Cancelled';task.ArchiveReason='长期任务已结束';snapshot(state,task,'Cancelled',day);}
 snapshot(state,selected,'Ended',day);
}
async function toggle(id){await edit(state=>{const task=state.Tasks.find(task=>task.Id===id);if(task.State==='Cancelled')return;if(task.State==='Completed'){task.State='Open';task.CompletedOn=null;for(const activity of state.Activities.filter(a=>a.TaskId===id&&a.Kind==='Completed'))activity.Reverted=true;}else{task.State='Completed';task.CompletedOn=today();snapshot(state,task,'Completed');}});}
function render(){
 for(const url of objectUrls)URL.revokeObjectURL(url);objectUrls=[];
 const root=$('#app');root.replaceChildren(node('header',{class:'top'},node('a',{class:'brand',href:'./'},'E²',node('span',{},'Day')),node('span',{class:'edition'},'探索每一天')));
 const main=node('main');root.append(main);if(!profile){void loginView(main);return;}
 main.append(paragraph(profile.Name,'eyebrow'));
 if(page==='today'){
  main.append(node('h1',{},new Date().toLocaleDateString('zh-CN',{month:'long',day:'numeric',weekday:'long'})),paragraph('把今天的事，一件一件记好。'));
  main.append(node('div',{class:'sync'},node('span',{class:`dot ${offlineReady?'':'offline'}`}),savedText));
  const open=sorted(profile.State.Tasks.filter(task=>visible(task,today(),profile.State))),done=profile.State.Tasks.filter(task=>task.State==='Completed'&&task.CompletedOn===today());
  main.append(node('div',{class:'stats'},node('div',{class:'stat'},node('span',{},'待处理事项'),node('strong',{},open.length),node('span',{},'不急，一件件来')),node('div',{class:'stat'},node('span',{},'今天已完成'),node('strong',{},done.length),node('span',{},'每一步都有记录'))));
  main.append(node('div',{class:'section'},node('h2',{},'今天的清单'),node('small',{},`${open.filter(t=>t.IsImportant).length} 项重要`)));
  if(open.length)main.append(...open.map(taskRow));else main.append(node('div',{class:'empty'},'今天的清单很轻。',node('br'),'记下一件想做的小事吧。'));
  if(done.length)main.append(node('div',{class:'section'},node('h2',{},'已经完成')), ...sorted(done).map(taskRow));
 }else if(page==='calendar')calendarView(main);else accountView(main);
 root.append(node('nav',{class:'bottom','aria-label':'主要页面'},...['today','calendar','account'].map((name,index)=>btn(['今日','日历','账号'][index],()=>{page=name;render();window.scrollTo(0,0);},page===name?'active':''))));
 if(page!=='account')root.append(btn('＋ 记一件事',()=>taskEditor(null,page==='calendar'?selectedDay:today()),'primary add'));
}
async function loginView(main){
 main.append(node('h1',{},'你的记录，随身带着'),paragraph('在手机上独立记录，离线也能使用。需要交换记录时，导入或导出文件。'));
 const storedKeys=await keys();if(profile||!main.isConnected)return;
 if(storedKeys.length){const panel=node('section',{class:'panel lock-accounts'},node('h2',{},'这台设备上的账号'));for(const key of storedKeys){const stored=await get('kv',key);panel.append(btn(stored.Name,()=>passwordDialog(stored),'quiet'));}main.append(panel);}
 const form=node('form',{class:'panel'}),name=node('input',{name:'username',autocomplete:'username',required:true,minlength:2,maxlength:40}),password=node('input',{name:'password',type:'password',autocomplete:'new-password',required:true,minlength:8,maxlength:128}),confirm=node('input',{name:'confirm',type:'password',autocomplete:'new-password',required:true,minlength:8,maxlength:128});
 form.append(node('h2',{},'创建本机账号'),paragraph('账号只保存在这台设备上。电脑和手机各自使用自己的账号，交换文件包含记录和图片。'));
 for(const [label,input]of [['账号名',name],['密码 · 8 至 128 个字符',password],['再输入一次密码',confirm]])form.append(node('label',{},label),input);
 const error=node('p',{class:'error',role:'alert'}),submit=node('button',{type:'submit',class:'primary full'},'创建账号并开始');form.append(error,submit);
 form.addEventListener('submit',async event=>{event.preventDefault();submit.disabled=true;error.textContent='';try{
  const accountName=name.value.trim().normalize();if(accountName.length<2||accountName.length>40||/[\u0000-\u001f\u007f-\u009f]/.test(accountName))throw new Error('账号名需为 2 至 40 个字符。');if(password.value.length<8||password.value.length>128||password.value!==confirm.value)throw new Error('请填写 8 至 128 个字符的密码，并确认两次相同。');
  for(const key of await keys()){const existing=await get('kv',key);if(existing.Name.toLocaleLowerCase()===accountName.toLocaleLowerCase())throw new Error('这台设备上已有同名账号。');}
  const salt=hex(crypto.getRandomValues(new Uint8Array(16))),recovery=hex(crypto.getRandomValues(new Uint8Array(20))),next={Id:uuid(),Name:accountName,Salt:salt,Iterations:600000,PasswordHash:await verifier(password.value,salt,600000),RecoveryHash:await digest(recovery),State:emptyState(),Revision:0};
  await save(next);await put('kv','active',next.Id);navigator.storage?.persist?.().catch(()=>{});render();showRecovery(recovery);
 }catch(ex){error.textContent=ex.message;}finally{submit.disabled=false;}});
 main.append(form,paragraph('E²-Day 2.0.0 · 独立离线应用 · 文件交换','version'));
}
function passwordDialog(stored){const dialog=$('#notice');dialog.replaceChildren(node('h2',{},'登录 '+stored.Name));const password=node('input',{type:'password',autocomplete:'current-password',placeholder:'输入本机账号密码'}),error=node('p',{class:'error'});dialog.append(password,error,node('div',{class:'actions'},btn('取消',()=>dialog.close()),btn('登录',async()=>{try{await unlock(await get('kv','profile:'+stored.Id),password.value);dialog.close();}catch(ex){error.textContent=ex.message;}},'primary')),btn('使用离线恢复码',()=>{dialog.close();passwordReset(stored,true);},'full quiet'));dialog.showModal();password.focus();}
function showRecovery(code){const dialog=$('#notice');dialog.replaceChildren(node('h2',{},'保存本机账号恢复码'),paragraph('忘记密码时，可在这台设备重置密码。记录仍需要本机数据或自己的备份文件。'),node('p',{class:'code'},code),btn('下载恢复码',()=>download(new Blob(['E²-Day 本机账号：'+profile.Name+'\n恢复码：'+code+'\n请保存在自己的安全位置。'],{type:'text/plain'}),'E2-Day-恢复码.txt')),btn('我已保存，继续',()=>dialog.close(),'primary full'));dialog.showModal();}
function calendarView(main){
 main.append(node('h1',{},'给日子一点安排'),paragraph('选择日期，查看当天任务和日历事件。'));
 main.append(node('div',{class:'calendar-head'},btn('‹',()=>{month=dateString(new Date(Number(month.slice(0,4)),Number(month.slice(5))-2,1,12)).slice(0,7);render();}),node('strong',{},`${month.slice(0,4)} 年 ${Number(month.slice(5))} 月`),btn('›',()=>{month=dateString(new Date(Number(month.slice(0,4)),Number(month.slice(5)),1,12)).slice(0,7);render();})));
 const grid=node('div',{class:'calendar'},...['一','二','三','四','五','六','日'].map(text=>node('span',{class:'weekday'},text)));
 const first=dayDate(`${month}-01`),offset=(first.getDay()+6)%7,last=new Date(first.getFullYear(),first.getMonth()+1,0).getDate();for(let i=0;i<offset;i++)grid.append(node('span'));
 for(let day=1;day<=last;day++){const date=`${month}-${String(day).padStart(2,'0')}`;const has=profile.State.Tasks.some(t=>t.ScheduledDate===date&&t.State!=='Cancelled')||profile.State.Events.some(e=>occurrences(e,date,date,lunar).length);const button=btn(String(day),()=>{selectedDay=date;render();},`day ${date===selectedDay?'selected':''} ${date===today()?'today':''}`);button.setAttribute('aria-label',`${date}${has?'，有安排':''}`);if(has)button.append(node('i'));grid.append(button);}main.append(grid);
 main.append(node('div',{class:'section'},node('h2',{},`${Number(selectedDay.slice(5,7))} 月 ${Number(selectedDay.slice(8))} 日`),btn('回到今天',()=>{selectedDay=today();month=selectedDay.slice(0,7);render();},'quiet')));
 const tasks=sorted(profile.State.Tasks.filter(t=>t.State!=='Cancelled'&&(t.ScheduledDate===selectedDay||t.CompletedOn===selectedDay)));
 main.append(...tasks.map(taskRow));
 for(const event of profile.State.Events.filter(e=>occurrences(e,selectedDay,selectedDay,lunar).length&&!tasks.some(t=>t.EventId===e.Id&&t.OccurrenceDate===selectedDay)))main.append(node('div',{class:'task'},node('div',{class:'task-content'},node('div',{class:'task-title'},event.Title),paragraph(event.Notes),paragraph('日历事件 · 到提醒时间会加入清单'))));
 if(!tasks.length&&!profile.State.Events.some(e=>occurrences(e,selectedDay,selectedDay,lunar).length))main.append(node('div',{class:'empty'},'这一天还没有安排。'));
}
function accountView(main){
 main.append(node('h1',{},'账号与文件'),paragraph('任务和图片保存在这台设备，通过交换文件带到另一台设备。'));
 main.append(node('section',{class:'panel'},node('h2',{},profile.Name),paragraph(savedText),paragraph(profile.State.Tasks.length+' 个任务 · '+profile.State.Events.length+' 个事件 · '+profile.State.Activities.length+' 条历史'),btn('锁定 / 切换账号',lockAccount,'quiet')));
 main.append(node('section',{class:'panel'},node('h2',{},'文件交换'),paragraph('导出文件包含任务、事件、历史和图片。导入先预览并合并，同一记录有差异时选择版本，确认前保存本机副本。'),btn('导出交换文件',exportBackup,'primary full'),btn('导入交换文件',importBackup,'full'),btn('查看导入前副本',beforeRestoreList,'full quiet')));
 main.append(node('section',{class:'panel'},node('h2',{},'离线使用'),paragraph(offlineReady?'离线入口已准备好，关闭电脑或断网也可记录。':'正在准备离线入口，请保持联网直到准备完成。'),paragraph('日常记录自动保存。导出的交换文件可以另存一份作为个人备份。')));
 main.append(node('section',{class:'panel'},node('h2',{},'本机账号密码'),btn('修改密码',()=>passwordReset(profile,false),'quiet')));
 main.append(paragraph('E²-Day 2.0.0 · 独立离线应用 · 文件交换','version'));
}
async function lockAccount(){await profileWriter.tail;await put('kv','active',null);profile=null;render();window.scrollTo(0,0);}
async function taskEditor(id,date=today()){
 const original=id?profile.State.Tasks.find(t=>t.Id===id):null,dialog=$('#editor'),images=[...(original?.ImageIds||[])];editorUrls.forEach(URL.revokeObjectURL);editorUrls=[];
 dialog.replaceChildren(node('h2',{},original?'修改这件事':'记一件事'),paragraph(original?.EventId?'修改仅影响这次事件，后续重复规则可在电脑上调整。':'留下一点想法，慢慢完成。'));
 const title=node('input',{value:original?.Title||'',maxlength:500,required:true,placeholder:'想做什么？'}),notes=node('textarea',{maxlength:50000,placeholder:'备注、想法或需要准备的东西'},original?.Notes||''),scheduled=node('input',{type:'date',value:original?.ScheduledDate||date,min:'1901-01-01',max:'2100-12-31'}),important=node('input',{type:'checkbox',checked:original?.IsImportant||false}),gallery=node('div',{class:'editor-images'}),error=node('p',{class:'error'}),picker=node('input',{type:'file',accept:'image/png,image/jpeg,image/webp,image/heic',multiple:true,hidden:true});
 dialog.append(node('label',{},'标题'),title,node('label',{},'原定日期'),scheduled,node('label',{},'备注'),notes,node('label',{class:'checkbox'},important,'标为重要'),node('label',{},'备注图片 · 最多 8 张'),gallery);
 if(original&&profile.State.Events.some(e=>e.Id===original.EventId&&e.Repeat==='Daily'))scheduled.disabled=true;
 const owner=profile.Id;
 async function showImages(){editorUrls.forEach(URL.revokeObjectURL);editorUrls=[];gallery.replaceChildren();for(const id of images){const blob=await get('images',`${owner}/${id}`);if(!blob)continue;const url=URL.createObjectURL(blob);editorUrls.push(url);gallery.append(node('div',{class:'editor-image'},node('img',{src:url,alt:'待保存备注图片'}),btn('移除',()=>{images.splice(images.indexOf(id),1);void showImages();})));}}
 let importing=false;const upload=btn('＋ 添加图片',()=>picker.click());dialog.append(upload,picker,error);
 picker.addEventListener('change',async()=>{importing=true;upload.disabled=true;try{for(const file of picker.files){if(images.length>=8)throw new Error('每则备注最多 8 张图片。');const blob=await imagePNG(file),id=hex(await crypto.subtle.digest('SHA-256',await blob.arrayBuffer()));await put('images',`${owner}/${id}`,blob);if(!images.includes(id))images.push(id);}await showImages();}catch(ex){error.textContent=ex.message;}finally{importing=false;upload.disabled=false;picker.value='';}});
 const saveButton=btn('保存',async()=>{if(importing)throw new Error('图片还在处理，请稍等。');if(!title.value.trim())throw new Error('请填写标题。');if(scheduled.value<'1901-01-01'||scheduled.value>'2100-12-31')throw new Error('请选择 1901 至 2100 年的有效日期。');if(profile.Id!==owner)throw new Error('账号已切换，请重新打开编辑。');await edit(state=>{if(original){const task=state.Tasks.find(t=>t.Id===id);Object.assign(task,{Title:title.value.trim(),Notes:notes.value.trim(),ScheduledDate:scheduled.value,IsImportant:important.checked,ImageIds:[...images],HasOverride:!!task.EventId});}else state.Tasks.push({Id:uuid(),EventId:null,OccurrenceDate:null,ScheduledDate:scheduled.value,OriginalScheduledDate:scheduled.value,Title:title.value.trim(),Notes:notes.value.trim(),ImageIds:[...images],EventImportance:'Normal',IsImportant:important.checked,HasOverride:false,State:'Open',CreatedAt:new Date().toISOString(),CompletedOn:null,ArchiveReason:''});});dialog.close();},'primary');
 dialog.append(node('div',{class:'actions'},btn('取消',()=>dialog.close()),saveButton));if(original)dialog.append(btn(original.State==='Cancelled'?'恢复任务':'取消任务',async()=>{await edit(state=>{const task=state.Tasks.find(t=>t.Id===id);if(task.State==='Cancelled'){task.State='Open';task.ArchiveReason='';task.HasOverride=true;state.Activities.filter(a=>a.TaskId===id&&a.Kind==='Cancelled').forEach(a=>a.Reverted=true);}else if(task.State==='Open'){task.State='Cancelled';task.ArchiveReason='已取消';snapshot(state,task,'Cancelled');}});dialog.close();},'quiet danger'));
 await showImages();dialog.showModal();title.focus();
}
async function imagePNG(file){
 if(file.size>20_000_000)throw new Error('单张原图不能超过 20 MB。');let bitmap;
 try{bitmap=await createImageBitmap(file);}catch{const url=URL.createObjectURL(file);try{bitmap=await new Promise((resolve,reject)=>{const image=new Image();image.onload=()=>resolve(image);image.onerror=()=>reject(new Error('这张图片无法打开，请使用 PNG 或 JPEG。'));image.src=url;});}finally{URL.revokeObjectURL(url);}}
 const width=bitmap.width||bitmap.naturalWidth,height=bitmap.height||bitmap.naturalHeight,scale=Math.min(1,2048/Math.max(width,height)),canvas=document.createElement('canvas');canvas.width=Math.max(1,Math.round(width*scale));canvas.height=Math.max(1,Math.round(height*scale));canvas.getContext('2d').drawImage(bitmap,0,0,canvas.width,canvas.height);bitmap.close?.();
 const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/png'));if(!blob||blob.size>10_000_000)throw new Error('图片处理后仍超过 10 MB，请缩小后重试。');return blob;
}
function preview(blob){const dialog=$('#notice'),url=URL.createObjectURL(blob);dialog.replaceChildren(node('img',{src:url,alt:'备注图片预览',class:'wide-image'}),btn('关闭',()=>dialog.close(),'full'));dialog.addEventListener('close',()=>URL.revokeObjectURL(url),{once:true});dialog.showModal();}
function readable(value){
 const names={Open:'待处理',Completed:'已完成',Cancelled:'已取消',Daily:'每天',Weekly:'每周',Monthly:'每月',Yearly:'每年',LunarYearly:'每年农历',None:'不重复',Normal:'普通',Important:'重要',VeryImportant:'非常重要',Viewed:'看过',Ended:'结束长期任务'};
 let text=(value.Title||'历史记录')+'\n'+(value.Notes||'')+'\n日期：'+(value.ScheduledDate||value.StartDate||value.Day||'');
 if(value.State)text+=' · '+names[value.State];if(value.CompletedOn)text+=' · 完成于 '+value.CompletedOn;
 if(value.IsImportant!==undefined)text+='\n重要标记：'+(value.IsImportant?'是':'否');
 if(value.Repeat)text+='\n重复规则：'+names[value.Repeat]+(value.EndDate?' · 截止 '+value.EndDate:'');
 if(value.LunarMonth)text+=' · 农历 '+value.LunarMonth+' 月 '+value.LunarDay+' 日';
 if(value.Importance)text+='\n提醒档位：'+names[value.Importance];if(value.Kind)text+='\n历史操作：'+names[value.Kind];
 if(value.At)text+=' · '+new Date(value.At).toLocaleString('zh-CN');if(value.Reverted!==undefined)text+=' · 已撤销：'+(value.Reverted?'是':'否');
 return text+'\n图片：'+(value.ImageIds||[]).length;
}
async function commitImport(plan,choices,blobs){return profileWriter.enqueue(async()=>{
 if(!profile||profile.Id!==plan.AccountId||recordFingerprint(profile.State)!==plan.Fingerprint)throw new Error('当前账号或记录已有变化，请重新预览文件。');const state=resolveImport(plan,choices);state.News=clone(profile.State.News);const next=clone(profile);next.State=state;next.Revision=(next.Revision||0)+1;const key='before-restore:'+profile.Id+':'+Date.now()+':'+uuid();
 await new Promise((resolve,reject)=>{const tx=database.transaction(['kv','images'],'readwrite');tx.objectStore('kv').put({State:clone(profile.State),At:new Date().toISOString(),Reason:'导入前'},key);for(const [id,blob]of blobs)tx.objectStore('images').put(blob,next.Id+'/'+id);tx.objectStore('kv').put(next,'profile:'+next.Id);tx.oncomplete=resolve;tx.onerror=()=>reject(tx.error);tx.onabort=()=>reject(tx.error||new Error('存储不足，导入没有改变原记录。'));});profile=next;savedText='文件已合并并保存到本机';
});}
function previewImportImage(blob){const url=URL.createObjectURL(blob),dialog=node('dialog',{class:'image-dialog'},node('img',{src:url,alt:'版本中的备注图片',class:'wide-image'}));dialog.append(btn('关闭图片',()=>dialog.close(),'full'));document.body.append(dialog);dialog.addEventListener('close',()=>{URL.revokeObjectURL(url);dialog.remove();},{once:true});dialog.showModal();}
function importPreview(plan,blobs){
 const dialog=$('#notice'),choices={};dialog.replaceChildren(node('h2',{},'预览文件导入'),paragraph('导入到本机账号：'+profile.Name),paragraph('新增 '+plan.Added+' 条 · 相同 '+plan.Unchanged+' 条 · 需选择 '+plan.Conflicts.length+' 条。当前已有的其他记录会继续保留。'));
 plan.Conflicts.forEach((conflict,index)=>{const box=node('section',{class:'choice'},node('strong',{},({Tasks:'任务',Events:'事件',Activities:'历史'}[conflict.Collection])+' · '+(conflict.Current.Title||conflict.Incoming.Title)));for(const [key,label,value]of [['current','保留本机',conflict.Current],['incoming','采用文件',conflict.Incoming]]){const radio=node('input',{type:'radio',name:'import-choice-'+index,value:key,onchange:()=>choices[index]=key});const version=node('div',{},node('label',{},radio,node('span',{},label+'：'+readable(value)))),gallery=node('div',{class:'task-images'});version.append(gallery);box.append(version);for(const id of value.ImageIds||[]){const available=key==='incoming'?blobs.find(([imageId])=>imageId===id)?.[1]:null;void Promise.resolve(available||get('images',profile.Id+'/'+id)).then(blob=>{if(!blob||!gallery.isConnected)return;const url=URL.createObjectURL(blob);gallery.append(node('img',{src:url,alt:label+'备注图片',onclick:()=>previewImportImage(blob)}));dialog.addEventListener('close',()=>URL.revokeObjectURL(url),{once:true});});}}dialog.append(box);});
 const error=node('p',{class:'error'});dialog.append(error,node('div',{class:'actions'},btn('取消',()=>dialog.close()),btn('确认合并',async()=>{try{await commitImport(plan,choices,blobs);dialog.close();await ensureUpcoming();render();}catch(ex){error.textContent=ex.message;}},'primary')));dialog.showModal();
}
async function exportBackup(){await profileWriter.tail;const owner=clone(profile),images=[];for(const id of imageIds(owner.State)){const blob=await get('images',`${owner.Id}/${id}`);if(!blob)throw new Error('图片缺失，完整备份尚未导出。');const bytes=new Uint8Array(await blob.arrayBuffer());let binary='';for(let start=0;start<bytes.length;start+=16384)binary+=String.fromCharCode(...bytes.subarray(start,start+16384));images.push({Id:id,FileName:'手机备注.png',Data:btoa(binary)});}
 const envelope={Format:'E2DayExchange',Version:1,ExportedAt:new Date().toISOString(),Payload:JSON.stringify(owner.State),Images:images};envelope.Sha256=await digest(envelope.Payload+'\n'+images.map(image=>image.Id).sort().join('\n'));download(new Blob([JSON.stringify(envelope)],{type:'application/json'}),`E2-Day-交换文件-${today()}.json`);toast('备份已生成，请在手机文件中确认保存。');}
function download(blob,name){const url=URL.createObjectURL(blob),a=node('a',{href:url,download:name});document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),60000);}
function importBackup(){
 const picker=node('input',{type:'file',accept:'.json,application/json'});
 picker.addEventListener('change',async()=>{try{
  const file=picker.files[0];if(!file)return;if(file.size>512_000_000)throw new Error('备份超过 512 MB。');
  const backup=JSON.parse(await file.text());if(!['E2DayMobileBackup','E2DayExchange'].includes(backup.Format)||backup.Version!==1||!Array.isArray(backup.Images)||await portableHash(backup)!==backup.Sha256)throw new Error('请选择电脑版或手机版 2.0 导出的交换文件。旧版电脑备份可先在电脑版恢复，再导出交换文件。');
  const state=validateMobileState(JSON.parse(backup.Payload)),owner=profile.Id,available=new Set(),blobs=[];
  for(const image of backup.Images){const bytes=Uint8Array.from(atob(image.Data),c=>c.charCodeAt(0));if(bytes.length>10_000_000||bytes.length<8||!bytes.subarray(0,8).every((value,index)=>value===[137,80,78,71,13,10,26,10][index])||hex(await crypto.subtle.digest('SHA-256',bytes))!==image.Id||available.has(image.Id))throw new Error('备份图片校验失败。');available.add(image.Id);blobs.push([image.Id,new Blob([bytes],{type:'image/png'})]);}
  if(imageIds(state).some(id=>!available.has(id)))throw new Error('备份缺少图片。');
  if(profile?.Id!==owner)throw new Error('账号已切换，请重新选择文件。');await profileWriter.tail;const plan=previewImport(profile.State,state);plan.AccountId=owner;importPreview(plan,blobs);
 }catch(ex){toast(ex.message);}});picker.click();
}
async function preserveBeforeRestore(){await put('kv',`before-restore:${profile.Id}:${Date.now()}`,{State:clone(profile.State),At:new Date().toISOString()});}
async function beforeRestoreList(){
 const owner=profile.Id,entries=(await allKeys()).filter(key=>String(key).startsWith(`before-restore:${owner}:`)).sort().reverse(),dialog=$('#notice');
 dialog.replaceChildren(node('h2',{},'恢复前副本'),paragraph('这些副本与当前记录一起保存在手机中，供导入操作回退。'));
 if(!entries.length)dialog.append(paragraph('尚未导入文件，因此没有导入前副本。'));
 for(const key of entries){const stored=await get('kv',key);dialog.append(btn(`${new Date(stored.At||Number(String(key).split(':').at(-1))).toLocaleString('zh-CN')} · ${stored.State.Tasks.length} 个任务`,async()=>{dialog.close();if(!await ask('恢复此副本','先保留当前记录，再恢复这个副本，只在本机恢复。 '))return;if(profile?.Id!==owner)throw new Error('请保持当前账号。');validateMobileState(stored.State);for(const id of imageIds(stored.State))if(!await get('images',`${owner}/${id}`))throw new Error('副本图片缺失，请使用导出的完整备份。');await preserveBeforeRestore();await edit(state=>{for(const field of Object.keys(state))delete state[field];Object.assign(state,clone(stored.State));});},'full quiet'));}
 dialog.append(btn('关闭',()=>dialog.close(),'full'));dialog.showModal();
}
function ask(title,text){return new Promise(resolve=>{const dialog=$('#notice');dialog.replaceChildren(node('h2',{},title),paragraph(text));let answered=false;const answer=value=>{answered=true;dialog.close();resolve(value);};dialog.append(node('div',{class:'actions'},btn('取消',()=>answer(false)),btn('确认',()=>answer(true),'primary')));dialog.addEventListener('close',()=>{if(!answered)resolve(false);},{once:true});dialog.showModal();});}
function passwordReset(stored,forgot){
 const dialog=$('#notice'),password=node('input',{type:'password',autocomplete:'new-password',minlength:8,maxlength:128}),confirm=node('input',{type:'password',autocomplete:'new-password',minlength:8,maxlength:128}),recovery=node('input',{autocomplete:'off',placeholder:'离线恢复码'}),error=node('p',{class:'error'});dialog.replaceChildren(node('h2',{},forgot?'恢复本机账号访问':'修改本机账号密码'));if(forgot)dialog.append(recovery);dialog.append(node('label',{},'新密码 · 8 至 128 个字符'),password,node('label',{},'再次输入新密码'),confirm,error,node('div',{class:'actions'},btn('取消',()=>dialog.close()),btn('保存新密码',async()=>{try{
 if(password.value.length<8||password.value.length>128||password.value!==confirm.value)throw new Error('请检查密码长度及两次输入是否相同。');const updated=await profileWriter.enqueue(async()=>{const latest=await get('kv','profile:'+stored.Id);if(forgot&&await digest(recovery.value.trim().toUpperCase())!==latest.RecoveryHash)throw new Error('恢复码不正确。');const next=clone(latest),code=hex(crypto.getRandomValues(new Uint8Array(20)));next.Salt=hex(crypto.getRandomValues(new Uint8Array(16)));next.Iterations=600000;next.PasswordHash=await verifier(password.value,next.Salt,next.Iterations);next.RecoveryHash=await digest(code);await put('kv','profile:'+next.Id,next);if(profile?.Id===next.Id)profile=next;return {next,code};});await unlock(updated.next,password.value);dialog.close();showRecovery(updated.code);
 }catch(ex){error.textContent=ex.message;}},'primary')));dialog.showModal();
}
async function refreshLunarDates(){
 try{
  const response=await fetch('./lunar-dates.json',{signal:AbortSignal.timeout(5000)});if(!response.ok)return;
  const dates=await response.json();await put('kv','lunar-dates',dates);lunar=dates;
  if(profile){await ensureUpcoming();render();}
 }catch{/* Cached records remain usable when the computer is unavailable. */}
}
async function prepareOfflineEntry(){
 if(!('serviceWorker'in navigator)){toast('当前浏览器无法准备离线入口，请更新浏览器后重新打开。');return;}
 const existing=await navigator.serviceWorker.getRegistration().catch(()=>null);offlineReady=!!existing?.active;if(page==='account'&&profile)render();
 try{await navigator.serviceWorker.register('./sw.js');await navigator.serviceWorker.ready;offlineReady=true;if(page==='account'&&profile)render();}catch{if(!offlineReady)toast('离线入口尚未准备完成，请联网后重新打开。');}
}
async function boot(){
 try{if(!window.isSecureContext||!crypto.subtle)throw new Error('请通过正式手机安装页面的 HTTPS 地址打开。');database=await openDB();
  lunar=await get('kv','lunar-dates')||{};
  const active=await get('kv','active');if(active)profile=await get('kv',`profile:${active}`)||null;
  await ensureUpcoming();render();
  document.addEventListener('visibilitychange',async()=>{if(document.visibilityState==='visible'){await ensureUpcoming();render();}});
  void refreshLunarDates();void prepareOfflineEntry();
 }catch(error){$('#app').replaceChildren(node('main',{},node('h1',{},'还需要完成首次配置'),paragraph(error.message),paragraph('已保存的记录会继续保留。请检查安装页面是否使用 HTTPS，以及手机存储空间。')));}
}
if(typeof document!=='undefined'){
 if(!navigator.locks)$('#app').replaceChildren(node('main',{},node('h1',{},'请更新手机浏览器'),paragraph('请更新当前手机浏览器后重新打开，已保存的记录会继续保留。')));
 else void navigator.locks.request('e2day-independent-editor',{ifAvailable:true},async lock=>{
  if(!lock){$('#app').replaceChildren(node('main',{},node('h1',{},'E²-Day 已经打开'),paragraph('请使用已有页面或主屏幕应用。关闭其他 E²-Day 页面后，再点击重新打开。'),btn('重新打开',()=>location.reload(),'primary')));return;}
  await boot();await new Promise(()=>{});
 });
}
