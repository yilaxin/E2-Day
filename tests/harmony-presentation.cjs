// Verify the actual view-model update path. ArkUI binding/rendering is checked
// separately by the SDK build and on-device interaction.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('../.build/harmony-tests/node_modules/typescript');
const root = path.resolve(__dirname, '..');
const source = path.join(root, 'src/DailyRecord.Harmony/entry/src/main/ets');
const stage = path.join(root, '.build/harmony-presentation-tests');
for (const file of ['domain/Models', 'domain/Tasks', 'presentation/TaskRows']) {
  const target = path.join(stage, file + '.js');
  fs.mkdirSync(path.dirname(target), { recursive: true });
  const input = fs.readFileSync(path.join(source, file + '.ets'), 'utf8');
  const result = ts.transpileModule(input, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, experimentalDecorators: true } });
  fs.writeFileSync(target, result.outputText);
}
global.Observed = value => value;
const { updateTaskRows } = require(path.join(stage, 'presentation/TaskRows'));
delete global.Observed;
const { makeTask, toggleCompleted } = require(path.join(stage, 'domain/Tasks'));
const { AppState, copyState } = require(path.join(stage, 'domain/Models'));
const state = new AppState();
state.Tasks = [makeTask('one', '界面测试', '', '2026-10-15', false, []), makeTask('two', '保留位置', '备注', '2026-10-15', false, [])];
let rows = updateTaskRows([], copyState(state).Tasks);
const first = rows[0], second = rows[1], originalFirst = first.task, originalSecond = second.task;
rows = updateTaskRows(rows, copyState(state).Tasks);
assert.equal(rows[0], first); assert.equal(rows[1], second);
assert.equal(first.task, originalFirst); assert.equal(second.task, originalSecond);
toggleCompleted(state, 'one', 'activity-one', '2026-10-15');
rows = updateTaskRows(rows, copyState(state).Tasks);
assert.equal(rows[0], first); assert.equal(first.task.State, 'Completed');
assert.notEqual(first.task, originalFirst); assert.equal(second.task, originalSecond);
toggleCompleted(state, 'one', 'activity-two', '2026-10-15');
rows = updateTaskRows(rows, copyState(state).Tasks);
assert.equal(first.task.State, 'Open'); assert.equal(first.task.CompletedOn, null);
state.Tasks[0].Title = '新的标题'; state.Tasks[0].Notes = '新的备注'; state.Tasks[0].IsImportant = true;
rows = updateTaskRows(rows, copyState(state).Tasks);
assert.equal(first.task.Title, '新的标题'); assert.equal(first.task.Notes, '新的备注'); assert.equal(first.task.IsImportant, true);
rows = updateTaskRows(rows, [state.Tasks[1], state.Tasks[0]]);
assert.equal(rows[0], second); assert.equal(rows[1], first);
assert.equal(updateTaskRows(rows, [state.Tasks[1]])[0], second);
console.log('PASS native task row completion/undo/title/star updates preserve stable row identity and untouched task objects.');
