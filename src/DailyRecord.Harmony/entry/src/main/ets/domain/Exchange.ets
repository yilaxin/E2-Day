import { AppState, TaskRecord, EventRecord, ActivityRecord, ExchangeFile, ExchangeImage, ImportPlan, ImportConflict, CryptoPort, copyState, validateState, referencedImages } from './Models';

function time(text: string): string { return new Date(text).toISOString(); }
function taskKey(row: TaskRecord, omitCreated: boolean = false): string {
  return JSON.stringify([row.Id, row.EventId ?? null, row.OccurrenceDate ?? null, row.ScheduledDate, row.OriginalScheduledDate, row.Title, row.Notes, row.ImageIds, row.EventImportance, row.IsImportant, row.HasOverride, row.State, omitCreated ? '' : time(row.CreatedAt), row.CompletedOn ?? null, row.ArchiveReason]);
}
function eventKey(row: EventRecord): string {
  return JSON.stringify([row.Id, row.Title, row.Notes, row.ImageIds, row.StartDate, row.IsImportant, row.EndDate ?? null, row.Importance, row.Repeat, row.LunarMonth, row.LunarDay]);
}
function activityKey(row: ActivityRecord): string {
  return JSON.stringify([row.Id, row.TaskId, row.Kind, row.Day, time(row.At), row.Title, row.Notes, row.ImageIds, row.ScheduledDate, row.IsImportant, row.Reverted]);
}
export function fingerprint(state: AppState): string {
  return JSON.stringify([state.SchemaVersion, state.Events.map(eventKey), state.Tasks.map(row => taskKey(row)), state.Activities.map(activityKey), state.Settings.NormalDays, state.Settings.ImportantDays, state.Settings.VeryImportantDays]);
}
export function previewImport(current: AppState, incoming: AppState): ImportPlan {
  validateState(current); validateState(incoming); const plan = new ImportPlan(); plan.Fingerprint = fingerprint(current); plan.Proposed = copyState(current);
  const foreign = copyState(incoming);
  for (const task of foreign.Tasks) if (task.EventId) {
    const canonical = current.Tasks.find(row => row.EventId === task.EventId && row.OccurrenceDate === task.OccurrenceDate);
    if (canonical && canonical.Id !== task.Id) { for (const history of foreign.Activities) if (history.TaskId === task.Id) history.TaskId = canonical.Id; task.Id = canonical.Id; }
  }
  validateState(foreign);
  for (const row of foreign.Events) {
    const local = plan.Proposed.Events.find(old => old.Id === row.Id);
    if (!local) { plan.Proposed.Events.push(row); plan.Added++; }
    else if (eventKey(local) === eventKey(row)) plan.Unchanged++;
    else plan.Conflicts.push(new ImportConflict('Events', row.Id, JSON.stringify(local), JSON.stringify(row)));
  }
  for (const row of foreign.Tasks) {
    const local = plan.Proposed.Tasks.find(old => old.Id === row.Id);
    if (!local) { plan.Proposed.Tasks.push(row); plan.Added++; }
    else {
      if (row.EventId && taskKey(local, true) === taskKey(row, true)) row.CreatedAt = local.CreatedAt;
      if (taskKey(local) === taskKey(row)) plan.Unchanged++;
      else plan.Conflicts.push(new ImportConflict('Tasks', row.Id, JSON.stringify(local), JSON.stringify(row)));
    }
  }
  for (const row of foreign.Activities) {
    const local = plan.Proposed.Activities.find(old => old.Id === row.Id);
    if (!local) { plan.Proposed.Activities.push(row); plan.Added++; }
    else if (activityKey(local) === activityKey(row)) plan.Unchanged++;
    else plan.Conflicts.push(new ImportConflict('Activities', row.Id, JSON.stringify(local), JSON.stringify(row)));
  }
  validateState(plan.Proposed); return plan;
}
export function resolveImport(plan: ImportPlan, choices: string[]): AppState {
  if (choices.length !== plan.Conflicts.length || choices.some(choice => choice !== 'current' && choice !== 'incoming')) throw new Error('请为每条不同记录选择要保留的版本。');
  const state = copyState(plan.Proposed);
  plan.Conflicts.forEach((conflict: ImportConflict, index: number) => {
    const text = choices[index] === 'incoming' ? conflict.Incoming : conflict.Current;
    if (conflict.Collection === 'Tasks') state.Tasks[state.Tasks.findIndex(row => row.Id === conflict.Id)] = JSON.parse(text) as TaskRecord;
    else if (conflict.Collection === 'Events') state.Events[state.Events.findIndex(row => row.Id === conflict.Id)] = JSON.parse(text) as EventRecord;
    else state.Activities[state.Activities.findIndex(row => row.Id === conflict.Id)] = JSON.parse(text) as ActivityRecord;
  });
  validateState(state);
  for (const task of state.Tasks) {
    const histories = state.Activities.filter(row => row.TaskId === task.Id && !row.Reverted);
    const groups = new Map<string, ActivityRecord[]>();
    for (const history of histories) {
      if (history.Kind === 'Completed' && (task.State !== 'Completed' || history.Day !== task.CompletedOn) || history.Kind === 'Cancelled' && task.State !== 'Cancelled') { history.Reverted = true; continue; }
      const key = `${history.Kind}/${history.Day}`; const group = groups.get(key) ?? []; group.push(history); groups.set(key, group);
    }
    groups.forEach((rows: ActivityRecord[]) => {
      rows.sort((a: ActivityRecord, b: ActivityRecord) => Date.parse(a.At) - Date.parse(b.At) || a.Id.localeCompare(b.Id));
      for (let index = 1; index < rows.length; index++) rows[index].Reverted = true;
    });
  }
  return state;
}
export async function readExchange(text: string, crypto: CryptoPort): Promise<ExchangeFile> {
  const file = JSON.parse(text) as ExchangeFile;
  if (!file || !['E2DayExchange', 'E2DayMobileBackup'].includes(file.Format) || file.Version !== 1 || typeof file.Payload !== 'string' || !Array.isArray(file.Images)) throw new Error('请选择电脑或手机 2.0 导出的交换文件。');
  if (file.Images.some(image => !image)) throw new Error('图片列表含有空记录。');
  const ids = file.Images.map(image => image.Id);
  if (ids.some(id => typeof id !== 'string' || !/^[A-F0-9]{64}$/.test(id)) || new Set(ids).size !== ids.length) throw new Error('图片列表损坏或重复。');
  if (await crypto.hashText(file.Payload + '\n' + ids.slice().sort().join('\n')) !== file.Sha256) throw new Error('文件校验失败，原有记录保持不变。');
  const state = validateState(JSON.parse(file.Payload) as AppState);
  for (const image of file.Images) {
    if (typeof image.FileName !== 'string' || !image.FileName.trim() || image.FileName.length > 240 || typeof image.Data !== 'string') throw new Error('备注图片信息不完整。');
    const bytes = crypto.decodeBase64(image.Data);
    if (bytes.length < 8 || bytes.length > 10000000 || !bytes.subarray(0, 8).every((value: number, index: number) => value === [137, 80, 78, 71, 13, 10, 26, 10][index]) || await crypto.hashBytes(bytes) !== image.Id) throw new Error('备注图片校验失败。');
  }
  if (referencedImages(state).some(id => !ids.includes(id))) throw new Error('文件缺少记录所需的图片。');
  return file;
}
export async function writeExchange(state: AppState, images: ExchangeImage[], crypto: CryptoPort): Promise<string> {
  validateState(state); const required = referencedImages(state); const file = new ExchangeFile(); file.ExportedAt = new Date().toISOString(); file.Payload = JSON.stringify(state);
  file.Images = required.map(id => { const image = images.find(row => row.Id === id); if (!image) throw new Error('图片缺失，尚未导出完整文件。'); return image; });
  file.Sha256 = await crypto.hashText(file.Payload + '\n' + file.Images.map(image => image.Id).sort().join('\n'));
  // Apply the same checks to outgoing files so damaged local images cannot escape unnoticed.
  const text = JSON.stringify(file); await readExchange(text, crypto); return text;
}
