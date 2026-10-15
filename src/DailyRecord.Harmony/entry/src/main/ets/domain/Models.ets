export type Importance = 'Normal' | 'Important' | 'VeryImportant';
export type Repeat = 'None' | 'Daily' | 'Weekly' | 'Monthly' | 'Yearly' | 'LunarYearly';
export type TaskStatus = 'Open' | 'Completed' | 'Cancelled';
export type ActivityKind = 'Viewed' | 'Completed' | 'Cancelled' | 'Ended';
export class EventRecord {
  Id: string = ''; Title: string = ''; Notes: string = ''; ImageIds: string[] = [];
  StartDate: string = ''; EndDate: string | null = null; IsImportant: boolean = false;
  Importance: Importance = 'Normal'; Repeat: Repeat = 'None'; LunarMonth: number = 0; LunarDay: number = 0;
}
export class TaskRecord {
  Id: string = ''; EventId: string | null = null; OccurrenceDate: string | null = null;
  ScheduledDate: string = ''; OriginalScheduledDate: string = ''; Title: string = ''; Notes: string = '';
  ImageIds: string[] = []; EventImportance: Importance = 'Normal'; IsImportant: boolean = false;
  HasOverride: boolean = false; State: TaskStatus = 'Open'; CreatedAt: string = ''; CompletedOn: string | null = null;
  ArchiveReason: string = '';
}
export class ActivityRecord {
  Id: string = ''; TaskId: string = ''; Kind: ActivityKind = 'Viewed'; Day: string = ''; At: string = '';
  Title: string = ''; Notes: string = ''; ImageIds: string[] = []; ScheduledDate: string = '';
  IsImportant: boolean = false; Reverted: boolean = false;
}
export class Settings { NormalDays: number = 0; ImportantDays: number = 3; VeryImportantDays: number = 7; }
export class NewsArticle {
  Title: string = ''; Summary: string = ''; Url: string = ''; Source: string = ''; Language: string = ''; PublishedAt: string = '';
}
export class News { FetchedAt: string | null = null; Articles: NewsArticle[] = []; }
export class AppState {
  SchemaVersion: number = 2; Events: EventRecord[] = []; Tasks: TaskRecord[] = []; Activities: ActivityRecord[] = [];
  Settings: Settings = new Settings(); News: News = new News();
}
export class ExchangeImage { Id: string = ''; FileName: string = ''; Data: string = ''; }
export class ExchangeFile {
  Format: string = 'E2DayExchange'; Version: number = 1; ExportedAt: string = ''; Payload: string = '';
  Images: ExchangeImage[] = []; Sha256: string = '';
}
export class LocalAccount {
  Id: string = ''; Name: string = ''; Salt: string = ''; PasswordHash: string = ''; RecoveryHash: string = ''; Iterations: number = 600000;
}
export class ImportConflict {
  Collection: string = ''; Id: string = ''; Current: string = ''; Incoming: string = '';
  constructor(collection: string, id: string, current: string, incoming: string) {
    this.Collection = collection; this.Id = id; this.Current = current; this.Incoming = incoming;
  }
}
export class ImportPlan {
  AccountId: string = ''; Fingerprint: string = ''; Proposed: AppState = new AppState();
  Conflicts: ImportConflict[] = []; Added: number = 0; Unchanged: number = 0; Images: ExchangeImage[] = [];
}
export interface CryptoPort {
  hashText(text: string): Promise<string>;
  hashBytes(bytes: Uint8Array): Promise<string>;
  decodeBase64(text: string): Uint8Array;
}
export function copyState(state: AppState): AppState { return JSON.parse(JSON.stringify(state)) as AppState; }
export function dateString(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}
export function today(): string { return dateString(new Date()); }
export function addDays(day: string, count: number): string {
  const date = new Date(`${day}T12:00:00`); date.setDate(date.getDate() + count); return dateString(date);
}
export function validDate(day: string): boolean {
  return typeof day === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(day) && day >= '1901-01-01' && day <= '2100-12-31' && dateString(new Date(`${day}T12:00:00`)) === day;
}
function validTime(value: string): boolean {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$/.test(value) && Number.isFinite(Date.parse(value));
}
function note(title: string, notes: string, ids: string[]): void {
  if (typeof title !== 'string' || !title.trim() || typeof notes !== 'string' || !Array.isArray(ids) || ids.length > 8 || new Set(ids).size !== ids.length || ids.some(id => typeof id !== 'string' || !/^[A-F0-9]{64}$/.test(id))) throw new Error('图文记录不完整。');
}
function unique(ids: string[]): void {
  if (ids.some(id => typeof id !== 'string' || !id.trim()) || new Set(ids).size !== ids.length) throw new Error('记录身份缺失或重复。');
}
function importance(value: string): boolean { return ['Normal', 'Important', 'VeryImportant'].includes(value); }
export function validateState(state: AppState): AppState {
  if (!state || ![1, 2].includes(state.SchemaVersion) || !Array.isArray(state.Tasks) || !Array.isArray(state.Events) || !Array.isArray(state.Activities) || !state.Settings || !state.News || !Array.isArray(state.News.Articles)) throw new Error('请选择完整的 E²-Day 记录文件。');
  if (state.Tasks.some(row => !row) || state.Events.some(row => !row) || state.Activities.some(row => !row)) throw new Error('文件含有空记录。');
  unique(state.Tasks.map(row => row.Id)); unique(state.Events.map(row => row.Id)); unique(state.Activities.map(row => row.Id));
  const eventIds = new Set(state.Events.map(row => row.Id)); const taskIds = new Set(state.Tasks.map(row => row.Id)); const occurrenceIds = new Set<string>();
  for (const event of state.Events) {
    note(event.Title, event.Notes, event.ImageIds);
    if (typeof event.IsImportant !== 'boolean' || !Number.isInteger(event.LunarMonth) || !Number.isInteger(event.LunarDay)) throw new Error('事件字段类型无效。');
    if (!validDate(event.StartDate) || event.EndDate !== null && event.EndDate !== undefined && !validDate(event.EndDate) || !importance(event.Importance) || !['None', 'Daily', 'Weekly', 'Monthly', 'Yearly', 'LunarYearly'].includes(event.Repeat)) throw new Error('事件日期或重复规则无效。');
    if (event.Repeat === 'LunarYearly' && (!Number.isInteger(event.LunarMonth) || event.LunarMonth < 1 || event.LunarMonth > 12 || !Number.isInteger(event.LunarDay) || event.LunarDay < 1 || event.LunarDay > 30)) throw new Error('农历重复日期无效。');
  }
  for (const task of state.Tasks) {
    note(task.Title, task.Notes, task.ImageIds);
    if (typeof task.IsImportant !== 'boolean' || typeof task.HasOverride !== 'boolean' || typeof task.ArchiveReason !== 'string' || task.OccurrenceDate !== null && task.OccurrenceDate !== undefined && !validDate(task.OccurrenceDate)) throw new Error('任务字段类型无效。');
    if (!validDate(task.ScheduledDate) || !validDate(task.OriginalScheduledDate) || !validTime(task.CreatedAt) || !importance(task.EventImportance) || !['Open', 'Completed', 'Cancelled'].includes(task.State) || task.CompletedOn !== null && task.CompletedOn !== undefined && !validDate(task.CompletedOn) || task.State === 'Completed' && !task.CompletedOn) throw new Error('任务日期或状态无效。');
    if (task.EventId !== null && task.EventId !== undefined) {
      const key = `${task.EventId}/${task.OccurrenceDate}`;
      if (!eventIds.has(task.EventId) || !task.OccurrenceDate || !validDate(task.OccurrenceDate) || occurrenceIds.has(key)) throw new Error('事件任务重复或缺少对应事件。');
      occurrenceIds.add(key);
    }
  }
  for (const history of state.Activities) {
    note(history.Title, history.Notes, history.ImageIds);
    if (typeof history.IsImportant !== 'boolean' || typeof history.Reverted !== 'boolean') throw new Error('历史字段类型无效。');
    if (!taskIds.has(history.TaskId) || !validDate(history.Day) || !validDate(history.ScheduledDate) || !validTime(history.At) || !['Viewed', 'Completed', 'Cancelled', 'Ended'].includes(history.Kind)) throw new Error('历史记录无效或缺少对应任务。');
  }
  const settings = state.Settings;
  if ([settings.NormalDays, settings.ImportantDays, settings.VeryImportantDays].some(day => !Number.isInteger(day) || day < 0 || day > 365)) throw new Error('提醒天数需在 0 到 365 之间。');
  if (state.News.Articles.some(row => !row || typeof row.Title !== 'string' || !row.Title.trim() || typeof row.Summary !== 'string' || typeof row.Url !== 'string' || typeof row.Source !== 'string' || typeof row.Language !== 'string' || !validTime(row.PublishedAt)) || state.News.FetchedAt !== null && !validTime(state.News.FetchedAt)) throw new Error('资讯缓存字段无效。');
  return state;
}
export function referencedImages(state: AppState): string[] {
  const ids = new Set<string>();
  for (const row of state.Tasks) for (const id of row.ImageIds) ids.add(id);
  for (const row of state.Events) for (const id of row.ImageIds) ids.add(id);
  for (const row of state.Activities) for (const id of row.ImageIds) ids.add(id);
  return Array.from(ids);
}
