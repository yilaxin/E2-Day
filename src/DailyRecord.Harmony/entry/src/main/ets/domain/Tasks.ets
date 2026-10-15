import { AppState, TaskRecord, EventRecord, ActivityRecord, dateString, today, addDays, validDate } from './Models';
export class LunarDates { Month: number = 1; Day: number = 1; Dates: string[] = []; }
export function occurrences(event: EventRecord, from: string, to: string, lunar: LunarDates[]): string[] {
  if (event.StartDate > from) from = event.StartDate;
  if (event.EndDate && event.EndDate < to) to = event.EndDate;
  if (to < from) return [];
  const values: string[] = []; const start = new Date(`${event.StartDate}T12:00:00`);
  const first = new Date(`${from}T12:00:00`); const last = new Date(`${to}T12:00:00`);
  if (event.Repeat === 'None') values.push(event.StartDate);
  else if (event.Repeat === 'Daily') for (let day = from; day <= to; day = addDays(day, 1)) values.push(day);
  else if (event.Repeat === 'Weekly') {
    let day = from; while (new Date(`${day}T12:00:00`).getDay() !== start.getDay()) day = addDays(day, 1);
    for (; day <= to; day = addDays(day, 7)) values.push(day);
  } else if (event.Repeat === 'Monthly') {
    for (let year = first.getFullYear(), month = first.getMonth(); year < last.getFullYear() || year === last.getFullYear() && month <= last.getMonth();) {
      values.push(dateString(new Date(year, month, Math.min(start.getDate(), new Date(year, month + 1, 0).getDate()), 12)));
      if (++month === 12) { year++; month = 0; }
    }
  } else if (event.Repeat === 'Yearly') {
    for (let year = first.getFullYear(); year <= last.getFullYear(); year++) values.push(dateString(new Date(year, start.getMonth(), Math.min(start.getDate(), new Date(year, start.getMonth() + 1, 0).getDate()), 12)));
  } else {
    const dates = lunar.find(row => row.Month === event.LunarMonth && row.Day === event.LunarDay); if (dates) values.push(...dates.Dates);
  }
  return values.filter(day => day >= from && day <= to);
}
export function makeTask(id: string, title: string, notes: string, day: string, important: boolean, images: string[]): TaskRecord {
  if (!title.trim() || title.length > 500 || notes.length > 50000 || !validDate(day)) throw new Error('请填写标题和有效日期。');
  const row = new TaskRecord(); row.Id = id; row.Title = title.trim(); row.Notes = notes.trim(); row.ScheduledDate = day;
  row.OriginalScheduledDate = day; row.IsImportant = important; row.ImageIds = images.slice(); row.CreatedAt = new Date().toISOString(); return row;
}
export function snapshot(state: AppState, task: TaskRecord, kind: 'Completed' | 'Cancelled' | 'Ended', id: string, day: string = today()): void {
  const row = new ActivityRecord(); row.Id = id; row.TaskId = task.Id; row.Kind = kind; row.Day = day; row.At = new Date().toISOString();
  row.Title = task.Title; row.Notes = task.Notes; row.ImageIds = task.ImageIds.slice(); row.ScheduledDate = task.ScheduledDate; row.IsImportant = task.IsImportant;
  state.Activities.push(row);
}
export function toggleCompleted(state: AppState, id: string, activityId: string, day: string = today()): void {
  const task = state.Tasks.find(row => row.Id === id); if (!task || task.State === 'Cancelled') throw new Error('任务不存在或已取消。');
  if (task.State === 'Completed') {
    task.State = 'Open'; task.CompletedOn = null;
    state.Activities.filter(row => row.TaskId === id && row.Kind === 'Completed').forEach(row => row.Reverted = true);
  } else { task.State = 'Completed'; task.CompletedOn = day; snapshot(state, task, 'Completed', activityId, day); }
}
export function cancelOrRestore(state: AppState, id: string, activityId: string): void {
  const task = state.Tasks.find(row => row.Id === id); if (!task) throw new Error('任务不存在。');
  if (task.State === 'Cancelled') {
    task.State = 'Open'; task.ArchiveReason = ''; task.HasOverride = true;
    state.Activities.filter(row => row.TaskId === id && row.Kind === 'Cancelled').forEach(row => row.Reverted = true);
  } else if (task.State === 'Open') { task.State = 'Cancelled'; task.ArchiveReason = '已取消'; snapshot(state, task, 'Cancelled', activityId); }
}
export function leadDays(state: AppState, event: EventRecord): number {
  return event.Importance === 'Normal' ? state.Settings.NormalDays : event.Importance === 'Important' ? state.Settings.ImportantDays : state.Settings.VeryImportantDays;
}
export function visible(state: AppState, task: TaskRecord, day: string = today()): boolean {
  if (task.State !== 'Open') return false;
  const event = state.Events.find(row => row.Id === task.EventId);
  if (event && event.Repeat === 'Daily') return task.ScheduledDate === day && event.StartDate <= day && (!event.EndDate || event.EndDate >= day);
  return (event ? addDays(task.ScheduledDate, -leadDays(state, event)) : task.ScheduledDate) <= day;
}
export function sorted(tasks: TaskRecord[]): TaskRecord[] {
  return tasks.slice().sort((a: TaskRecord, b: TaskRecord) => Number(b.IsImportant) - Number(a.IsImportant) || a.ScheduledDate.localeCompare(b.ScheduledDate) || a.CreatedAt.localeCompare(b.CreatedAt));
}
