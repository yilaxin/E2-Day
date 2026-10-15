import { common } from '@kit.AbilityKit';
import { AppState, TaskRecord, EventRecord, ActivityRecord, ExchangeImage, ImportPlan, ImportConflict, LocalAccount, today, copyState, dateString } from '../domain/Models';
import { makeTask, toggleCompleted, cancelOrRestore, visible } from '../domain/Tasks';
import { Repository, SavedCopy } from '../platform/Repository';
import { NativeFiles } from '../platform/Files';
import { TaskRow, updateTaskRows } from '../presentation/TaskRows';
import { TaskCard } from '../components/TaskCard';
import { ActionButton } from '../components/ActionButton';

@Entry
@Component
struct NativeIndex {
  private repo: Repository = new Repository();
  private files: NativeFiles | null = null;
  private initialized: boolean = false;
  @State private state: AppState = new AppState();
  @State private taskRows: TaskRow[] = [];
  @State private accountName: string = '';
  @State private page: string = 'today';
  @State private busy: boolean = false;
  @State private message: string = '正在打开本机记录…';
  @State private accounts: LocalAccount[] = [];
  @State private selectedAccount: string = '';
  @State private name: string = '';
  @State private password: string = '';
  @State private confirmation: string = '';
  @State private recovery: string = '';
  @State private recoveryCode: string = '';
  @State private editingId: string = '';
  @State private title: string = '';
  @State private notes: string = '';
  @State private date: string = today();
  @State private important: boolean = false;
  @State private attachments: ExchangeImage[] = [];
  @State private plan: ImportPlan = new ImportPlan();
  @State private choices: string[] = [];
  @State private previewImages: ExchangeImage[] = [];
  @State private viewingImage: string = '';
  @State private copies: SavedCopy[] = [];
  @State private calendarDate: string = today();
  @State private month: string = today().slice(0, 7);

  aboutToAppear(): void { this.run(async () => {
    const context = this.getUIContext().getHostContext() as common.UIAbilityContext;
    await this.repo.open(context); this.files = new NativeFiles(context, this.repo.crypto); this.initialized = true;
    await this.refresh(true); this.message = '已保存到本机';
  }); }
  onPageShow(): void { if (this.initialized) this.run(async () => { await this.repo.ensureUpcoming(); await this.refresh(); }); }
  private async refresh(includeAccounts: boolean = false): Promise<void> {
    const next = copyState(this.repo.state);
    this.taskRows = updateTaskRows(this.taskRows, next.Tasks);
    this.state = next; this.accountName = this.repo.account ? this.repo.account.Name : '';
    if (includeAccounts) {
      const names = await this.repo.accounts();
      this.accounts = names.map(row => { const display = new LocalAccount(); display.Id = row.Id; display.Name = row.Name; return display; });
    }
  }
  private run(action: () => Promise<void>): void {
    if (this.busy) return; this.busy = true;
    action().then(() => { this.busy = false; }).catch((error: Error) => { this.message = error.message || '操作未完成，已保存的记录会继续保留。'; this.busy = false; });
  }
  private tasks(): TaskRow[] {
    return this.taskRows.filter(row => {
      const task = row.task;
      if (this.page === 'cancelled') return task.State === 'Cancelled';
      if (this.page === 'calendar') return task.State !== 'Cancelled' && (task.ScheduledDate === this.calendarDate || task.CompletedOn === this.calendarDate);
      return visible(this.state, task) || task.State === 'Completed' && task.CompletedOn === today();
    }).sort((a: TaskRow, b: TaskRow) => Number(b.task.IsImportant) - Number(a.task.IsImportant) || a.task.ScheduledDate.localeCompare(b.task.ScheduledDate) || a.task.CreatedAt.localeCompare(b.task.CreatedAt));
  }
  private toggleTask(id: string): void { this.run(async () => {
    await this.repo.mutate(state => toggleCompleted(state, id, this.repo.crypto.id()));
    await this.refresh(); this.message = this.repo.state.Tasks.find(task => task.Id === id)?.State === 'Completed' ? '已标记完成并保存' : '已撤销完成并保存';
  }); }
  private toggleImportant(id: string): void { this.run(async () => {
    await this.repo.mutate(state => { const task = state.Tasks.find(row => row.Id === id); if (task) task.IsImportant = !task.IsImportant; });
    await this.refresh();
  }); }
  private openEditor(id: string): void { this.run(async () => {
    const task = this.repo.state.Tasks.find(row => row.Id === id); if (!task) throw new Error('任务不存在，请刷新后再试。');
    await this.editTask(task);
  }); }
  private async editTask(task: TaskRecord | null): Promise<void> {
    this.editingId = task ? task.Id : ''; this.title = task ? task.Title : ''; this.notes = task ? task.Notes : '';
    this.date = task ? task.ScheduledDate : this.page === 'calendar' ? this.calendarDate : today(); this.important = task ? task.IsImportant : false;
    const images: ExchangeImage[] = []; if (task) for (const id of task.ImageIds) images.push(await this.repo.image(id)); this.attachments = images; this.page = 'editor';
  }
  private async saveTask(): Promise<void> {
    const attachments = this.attachments.slice(); const draft = makeTask(this.editingId || this.repo.crypto.id(), this.title, this.notes, this.date, this.important, attachments.map(row => row.Id));
    await this.repo.mutate(state => {
      const original = state.Tasks.find(row => row.Id === this.editingId);
      if (original) { original.Title = draft.Title; original.Notes = draft.Notes; original.ScheduledDate = draft.ScheduledDate; original.ImageIds = draft.ImageIds; original.IsImportant = draft.IsImportant; original.HasOverride = !!original.EventId; }
      else state.Tasks.push(draft);
    }, attachments);
    this.attachments = []; this.page = 'today'; await this.refresh(); this.message = '已保存到本机';
  }
  private async selectImport(): Promise<void> {
    if (!this.files) throw new Error('文件选择器还未准备好。');
    const text = await this.files.chooseExchange(); if (text === null) return;
    const plan = await this.repo.importPreview(text); const images: ExchangeImage[] = [];
    for (const conflict of plan.Conflicts) {
      const versions = [conflict.Current, conflict.Incoming];
      for (const version of versions) for (const id of (JSON.parse(version) as TaskRecord).ImageIds) {
        if (!images.some(row => row.Id === id)) images.push(plan.Images.find(row => row.Id === id) ?? await this.repo.image(id));
      }
    }
    this.plan = plan; this.previewImages = images; this.choices = plan.Conflicts.map(() => ''); this.page = 'import';
  }
  private async exportFile(): Promise<void> {
    if (!this.files) throw new Error('文件选择器还未准备好。');
    const text = await this.repo.exportFile(); this.message = await this.files.saveText(text) ? '交换文件已保存，包含记录和图片。' : '已取消导出。';
  }
  private recordText(text: string, collection: string): string {
    if (collection === 'Events') {
      const row = JSON.parse(text) as EventRecord;
      return `${row.Title}\n${row.Notes}\n开始：${row.StartDate}${row.EndDate ? '\n结束：' + row.EndDate : ''}\n重复：${this.repeatText(row.Repeat)}\n提醒：${row.Importance === 'VeryImportant' ? '非常重要' : row.Importance === 'Important' ? '重要' : '普通'}\n重要：${row.IsImportant ? '是' : '否'}\n图片：${row.ImageIds.length}`;
    }
    if (collection === 'Activities') {
      const row = JSON.parse(text) as ActivityRecord;
      return `${row.Title}\n${row.Notes}\n记录日期：${row.Day}\n操作：${row.Kind === 'Completed' ? '完成' : row.Kind === 'Cancelled' ? '取消' : row.Kind === 'Ended' ? '结束' : '查看'}\n时间：${row.At}\n原定：${row.ScheduledDate}\n已撤回：${row.Reverted ? '是' : '否'}\n图片：${row.ImageIds.length}`;
    }
    const row = JSON.parse(text) as TaskRecord;
    return `${row.Title}\n${row.Notes}\n日期：${row.ScheduledDate || ''}\n状态：${row.State === 'Completed' ? '已完成' : row.State === 'Cancelled' ? '已取消' : '待处理'}${row.CompletedOn ? ' · ' + row.CompletedOn : ''}\n重要：${row.IsImportant ? '是' : '否'}\n图片：${row.ImageIds.length}`;
  }
  private repeatText(repeat: string): string {
    return repeat === 'Daily' ? '每天' : repeat === 'Weekly' ? '每周' : repeat === 'Monthly' ? '每月' : repeat === 'Yearly' ? '每年' : repeat === 'LunarYearly' ? '每年农历' : '不重复';
  }
  private versionImages(text: string): ExchangeImage[] {
    const ids = (JSON.parse(text) as TaskRecord).ImageIds; return this.previewImages.filter(image => ids.includes(image.Id));
  }
  private choose(index: number, choice: string): void { const choices = this.choices.slice(); choices[index] = choice; this.choices = choices; }
  private days(): number[] {
    const values: number[] = []; const year = Number(this.month.slice(0, 4)); const month = Number(this.month.slice(5));
    const offset = (new Date(year, month - 1, 1, 12).getDay() + 6) % 7;
    for (let index = 0; index < offset; index++) values.push(-index);
    const max = new Date(year, month, 0, 12).getDate(); for (let day = 1; day <= max; day++) values.push(day); return values;
  }
  private changeMonth(step: number): void {
    this.month = dateString(new Date(Number(this.month.slice(0, 4)), Number(this.month.slice(5)) - 1 + step, 1, 12)).slice(0, 7);
    this.calendarDate = this.month + '-01';
  }

  @Builder private Field(label: string) { Text(label).fontSize(13).fontColor('#617384').width('100%').margin({ top: 12, bottom: 6 }) }
  @Builder private Login() {
    Text('你的记录，随身带着').fontSize(27).fontWeight(FontWeight.Bold).fontColor('#203C52').margin({ bottom: 12 })
    Text('鸿蒙原生应用 · 独立离线记录').fontSize(14).fontColor('#617384').margin({ bottom: 24 })
    if (this.accounts.length > 0) {
      Text('选择本机账号').fontSize(18).fontWeight(FontWeight.Bold)
      ForEach(this.accounts, (account: LocalAccount) => { ActionButton({ label: account.Name, primary: false, isEnabled: !this.busy, action: () => { this.selectedAccount = account.Id; this.page = 'login'; this.password = ''; } }) }, (account: LocalAccount) => account.Id)
    }
    if (this.page === 'login') {
      this.Field('密码'); TextInput({ placeholder: '本机账号密码', text: this.password }).type(InputType.Password).onChange(value => this.password = value)
      ActionButton({ label: '登录', primary: true, isEnabled: !this.busy, action: () => this.run(async () => { await this.repo.login(this.selectedAccount, this.password); this.password = ''; this.page = 'today'; await this.refresh(true); }) })
      ActionButton({ label: '使用离线恢复码', primary: false, isEnabled: !this.busy, action: () => { this.page = 'reset'; this.password = ''; this.confirmation = ''; } })
    } else if (this.page === 'reset') {
      this.Field('离线恢复码'); TextInput({ text: this.recovery }).onChange(value => this.recovery = value)
      this.Field('新密码'); TextInput({ text: this.password }).type(InputType.Password).onChange(value => this.password = value)
      this.Field('再次输入'); TextInput({ text: this.confirmation }).type(InputType.Password).onChange(value => this.confirmation = value)
      ActionButton({ label: '恢复本机账号', primary: true, isEnabled: !this.busy, action: () => this.run(async () => { this.recoveryCode = await this.repo.resetPassword(this.selectedAccount, this.recovery, this.password, this.confirmation); this.password = ''; this.confirmation = ''; this.recovery = ''; this.page = 'recovery'; }) })
    } else {
      Text('创建本机账号').fontSize(20).fontWeight(FontWeight.Bold).margin({ top: 20 })
      this.Field('账号名 · 2 至 40 个字符'); TextInput({ text: this.name }).maxLength(40).onChange(value => this.name = value)
      this.Field('密码 · 8 至 128 个字符'); TextInput({ text: this.password }).maxLength(128).type(InputType.Password).onChange(value => this.password = value)
      this.Field('再次输入密码'); TextInput({ text: this.confirmation }).maxLength(128).type(InputType.Password).onChange(value => this.confirmation = value)
      ActionButton({ label: '创建账号并开始', primary: true, isEnabled: !this.busy, action: () => this.run(async () => { this.recoveryCode = await this.repo.create(this.name, this.password, this.confirmation); this.password = ''; this.confirmation = ''; this.page = 'recovery'; await this.refresh(true); }) })
    }
    if (this.page === 'login' || this.page === 'reset') {
      ActionButton({ label: '创建新账号', primary: false, isEnabled: !this.busy, action: () => { this.page = 'create'; this.password = ''; this.confirmation = ''; } })
    }
  }
  @Builder private Recovery() {
    Text('保存离线恢复码').fontSize(25).fontWeight(FontWeight.Bold)
    Text('此码用于恢复这台设备上的账号访问，记录仍需要本机数据或你自己的交换文件备份。').fontSize(14).fontColor('#617384')
    Text(this.recoveryCode).fontSize(19).fontColor('#216FA6').copyOption(CopyOptions.LocalDevice).padding(18).backgroundColor(Color.White).borderRadius(12)
    ActionButton({ label: '保存恢复码文件', primary: true, isEnabled: !this.busy, action: () => this.run(async () => { if (this.files) this.message = await this.files.saveText(`E²-Day 本机账号恢复码\n${this.recoveryCode}\n请保存在自己的安全位置。`, 'E2-Day-恢复码.txt') ? '恢复码文件已保存。' : '已取消保存。'; }) })
    ActionButton({ label: '我已保存，继续', primary: false, isEnabled: !this.busy, action: () => { this.recoveryCode = ''; this.page = this.accountName ? 'today' : 'login'; } })
  }
  @Builder private Editor() {
    Text(this.editingId ? '修改这件事' : '记一件事').fontSize(25).fontWeight(FontWeight.Bold)
    this.Field('标题'); TextInput({ text: this.title }).maxLength(500).onChange(value => this.title = value)
    this.Field('原定日期 · 年-月-日'); TextInput({ text: this.date }).maxLength(10).onChange(value => this.date = value)
    this.Field('备注'); TextArea({ text: this.notes }).height(130).maxLength(50000).onChange(value => this.notes = value)
    Toggle({ type: ToggleType.Checkbox, isOn: this.important }).onChange(value => this.important = value)
    Text('标为重要').fontSize(14)
    ForEach(this.attachments, (photo: ExchangeImage) => {
      Row({ space: 10 }) {
        Image('data:image/png;base64,' + photo.Data).width(100).height(80).objectFit(ImageFit.Contain).onClick(() => this.viewingImage = photo.Data)
        Button('移除').fontColor('#A44C35').backgroundColor(Color.Transparent).enabled(!this.busy).onClick(() => this.attachments = this.attachments.filter(row => row.Id !== photo.Id))
      }
    }, (photo: ExchangeImage) => photo.Id)
    ActionButton({ label: '添加备注图片', primary: false, isEnabled: !this.busy, action: () => this.run(async () => { if (this.files) { const selected = await this.files.chooseImages(8 - this.attachments.length); const photos = this.attachments.slice(); for (const image of selected) if (!photos.some(row => row.Id === image.Id)) photos.push(image); this.attachments = photos; } }) })
    ActionButton({ label: '保存', primary: true, isEnabled: !this.busy, action: () => this.run(async () => { await this.saveTask(); }) })
    ActionButton({ label: '取消编辑', primary: false, isEnabled: !this.busy, action: () => { this.attachments = []; this.page = 'today'; } })
    if (this.editingId && this.state.Tasks.find(row => row.Id === this.editingId)?.State !== 'Completed') {
      ActionButton({ label: this.state.Tasks.find(row => row.Id === this.editingId)?.State === 'Cancelled' ? '恢复任务' : '取消任务', primary: false, isEnabled: !this.busy, action: () => this.run(async () => { await this.repo.mutate(state => cancelOrRestore(state, this.editingId, this.repo.crypto.id())); this.attachments = []; this.page = 'today'; await this.refresh(); }) })
    }
  }
  @Builder private ConflictVersion(text: string, collection: string) {
    Text(this.recordText(text, collection)).fontSize(14).width('100%')
    ForEach(this.versionImages(text), (photo: ExchangeImage) => {
      Image('data:image/png;base64,' + photo.Data).width('100%').height(150).objectFit(ImageFit.Contain).onClick(() => this.viewingImage = photo.Data)
    }, (photo: ExchangeImage) => photo.Id)
  }
  @Builder private ImportReview() {
    Text('预览文件导入').fontSize(25).fontWeight(FontWeight.Bold)
    Text(`新增 ${this.plan.Added} 条 · 相同 ${this.plan.Unchanged} 条 · 需选择 ${this.plan.Conflicts.length} 条`).fontSize(15).fontColor('#617384')
    Text('双方独有记录会保留，确认合并前保存本机副本。').fontSize(14).fontColor('#617384')
    ForEach(this.plan.Conflicts, (conflict: ImportConflict, index: number) => {
      Column({ space: 10 }) {
        Text(conflict.Collection === 'Events' ? '事件版本差异' : conflict.Collection === 'Activities' ? '历史版本差异' : '任务版本差异').fontSize(16).fontWeight(FontWeight.Bold)
        this.ConflictVersion(conflict.Current, conflict.Collection)
        ActionButton({ label: this.choices[index] === 'current' ? '✓ 保留本机' : '保留本机', primary: false, isEnabled: !this.busy, action: () => this.choose(index, 'current') })
        this.ConflictVersion(conflict.Incoming, conflict.Collection)
        ActionButton({ label: this.choices[index] === 'incoming' ? '✓ 采用文件' : '采用文件', primary: false, isEnabled: !this.busy, action: () => this.choose(index, 'incoming') })
      }.padding(16).backgroundColor(Color.White).borderRadius(16).width('100%')
    })
    ActionButton({ label: '确认合并', primary: true, isEnabled: !this.busy, action: () => this.run(async () => { await this.repo.importCommit(this.plan, this.choices); this.plan = new ImportPlan(); this.previewImages = []; this.choices = []; this.page = 'account'; await this.refresh(); this.message = '文件已合并并保存到本机。'; }) })
    ActionButton({ label: '取消导入', primary: false, isEnabled: !this.busy, action: () => { this.plan = new ImportPlan(); this.previewImages = []; this.choices = []; this.page = 'account'; } })
  }
  @Builder private Account() {
    Text('账号与文件').fontSize(27).fontWeight(FontWeight.Bold)
    Text(this.accountName).fontSize(20).fontColor('#216FA6')
    Text(`${this.state.Tasks.length} 个任务 · ${this.state.Events.length} 个事件 · ${this.state.Activities.length} 条历史`).fontSize(14).fontColor('#617384')
    ActionButton({ label: '导出交换文件', primary: true, isEnabled: !this.busy, action: () => this.run(async () => { await this.exportFile(); }) })
    ActionButton({ label: '导入交换文件', primary: false, isEnabled: !this.busy, action: () => this.run(async () => { await this.selectImport(); }) })
    ActionButton({ label: '查看导入前副本', primary: false, isEnabled: !this.busy, action: () => this.run(async () => { this.copies = await this.repo.copies(); this.page = 'copies'; }) })
    ActionButton({ label: '查看已取消任务', primary: false, isEnabled: !this.busy, action: () => { this.page = 'cancelled'; } })
    ActionButton({ label: '锁定 / 切换本机账号', primary: false, isEnabled: !this.busy, action: () => this.run(async () => { await this.repo.lock(); this.page = 'create'; this.selectedAccount = ''; await this.refresh(true); }) })
    Text('记录保存在本机，通过交换文件带到电脑。账号与恢复码不会包含在交换文件中。').fontSize(14).fontColor('#617384')
  }
  @Builder private Calendar() {
    Text('给日子一点安排').fontSize(27).fontWeight(FontWeight.Bold)
    Row() { Button('‹').onClick(() => this.changeMonth(-1)); Blank(); Text(this.month).fontSize(20); Blank(); Button('›').onClick(() => this.changeMonth(1)) }.width('100%')
    Row() {
      ForEach(['一', '二', '三', '四', '五', '六', '日'], (day: string) => { Text(day).textAlign(TextAlign.Center).layoutWeight(1).fontColor('#617384') })
    }.width('100%')
    Grid() {
      ForEach(this.days(), (day: number) => {
        GridItem() {
          if (day > 0) {
            Button(String(day)).fontColor(this.calendarDate === `${this.month}-${String(day).padStart(2, '0')}` ? Color.White : '#216FA6').backgroundColor(this.calendarDate === `${this.month}-${String(day).padStart(2, '0')}` ? '#216FA6' : '#E5F1FA').onClick(() => this.calendarDate = `${this.month}-${String(day).padStart(2, '0')}`)
          } else {
            Blank()
          }
        }
      }, (day: number) => String(day))
    }.columnsTemplate('1fr 1fr 1fr 1fr 1fr 1fr 1fr').rowsGap(8).columnsGap(4).height(280)
    Text(this.calendarDate).fontSize(18)
  }
  build() {
    Column() {
      Row() { Image($r('app.media.app_icon')).width(38).height(38); Text('E²-Day').fontSize(24).fontWeight(FontWeight.Bold).fontColor('#216FA6'); Blank(); Text('探索每一天').fontSize(12).fontColor('#617384') }.width('100%').padding(20)
      Row({ space: 8 }) {
        LoadingProgress().width(18).height(18).visibility(this.busy ? Visibility.Visible : Visibility.Hidden)
        Text(this.busy ? '正在处理…' : this.message).fontSize(13).fontColor('#9B6A23').layoutWeight(1).maxLines(2)
      }.width('100%').height(44).padding({ left: 20, right: 20 })
      Scroll() {
        Column({ space: 12 }) {
          if (this.viewingImage) {
            ActionButton({ label: '返回', primary: false, isEnabled: !this.busy, action: () => this.viewingImage = '' })
            Image('data:image/png;base64,' + this.viewingImage).width('100%').height(520).objectFit(ImageFit.Contain)
          } else if (this.page === 'recovery') {
            this.Recovery()
          } else if (!this.accountName) {
            this.Login()
          } else if (this.page === 'editor') {
            this.Editor()
          } else if (this.page === 'import') {
            this.ImportReview()
          } else if (this.page === 'account') {
            this.Account()
          } else if (this.page === 'copies') {
            Text('导入前副本').fontSize(25).fontWeight(FontWeight.Bold)
            Text('恢复前也会先保留当前记录。').fontSize(14)
            ForEach(this.copies, (copy: SavedCopy) => {
              ActionButton({ label: `${copy.At} · ${copy.Count} 个任务`, primary: false, isEnabled: !this.busy, action: () => this.getUIContext().showAlertDialog({ title: '恢复此副本？', message: '先保留当前记录，再恢复所选本机副本。', primaryButton: { value: '取消', action: () => {} }, secondaryButton: { value: '恢复', action: () => this.run(async () => { await this.repo.restoreCopy(copy.Id); await this.refresh(); this.page = 'account'; }) } }) })
            }, (copy: SavedCopy) => copy.Id)
            ActionButton({ label: '返回', primary: false, isEnabled: !this.busy, action: () => this.page = 'account' })
          } else {
            if (this.page === 'calendar') {
              this.Calendar()
            } else {
              Text(this.page === 'cancelled' ? '已取消任务' : new Date().toLocaleDateString('zh-CN')).fontSize(27).fontWeight(FontWeight.Bold).fontColor('#203C52')
            }
            ForEach(this.tasks(), (row: TaskRow) => {
              TaskCard({ row: row, busy: this.busy, onToggle: (id: string) => this.toggleTask(id), onImportant: (id: string) => this.toggleImportant(id), onEdit: (id: string) => this.openEditor(id) })
            }, (row: TaskRow) => row.task.Id)
            if (this.tasks().length === 0) {
              Text('这里还没有事项。').fontSize(15).fontColor('#617384').padding(30)
            }
            ActionButton({ label: '＋ 记一件事', primary: true, isEnabled: !this.busy, action: () => this.run(async () => { await this.editTask(null); }) })
          }
          Text('E²-Day · 独立离线 · 文件交换').fontSize(11).fontColor('#8799A5').margin({ top: 20, bottom: 30 })
        }.width('100%').alignItems(HorizontalAlign.Start).padding({ left: 20, right: 20, bottom: 20 })
      }.layoutWeight(1).width('100%')
      if (this.accountName && !this.viewingImage && !['editor', 'import', 'recovery'].includes(this.page)) {
        Row({ space: 8 }) {
          Button('今日').layoutWeight(1).onClick(() => this.page = 'today')
          Button('日历').layoutWeight(1).onClick(() => this.page = 'calendar')
          Button('账号').layoutWeight(1).onClick(() => this.page = 'account')
        }.padding(16).width('100%').backgroundColor('#EAF4FC').enabled(!this.busy)
      }
    }.width('100%').height('100%').backgroundColor('#F4F8FD')
  }
}
