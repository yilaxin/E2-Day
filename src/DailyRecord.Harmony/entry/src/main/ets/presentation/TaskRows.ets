import { TaskRecord } from '../domain/Models';

// ForEach keeps the same keyed component. Keep its observed model as well,
// replacing the task property only after a successful repository commit.
@Observed
export class TaskRow {
  task: TaskRecord;
  constructor(task: TaskRecord) { this.task = task; }
}

export function updateTaskRows(previous: TaskRow[], tasks: TaskRecord[]): TaskRow[] {
  const rows = new Map<string, TaskRow>();
  previous.forEach(row => rows.set(row.task.Id, row));
  return tasks.map(task => {
    const row = rows.get(task.Id);
    if (!row) return new TaskRow(task);
    if (JSON.stringify(row.task) !== JSON.stringify(task)) row.task = task;
    return row;
  });
}
