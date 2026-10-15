import { TaskRow } from '../presentation/TaskRows';

@Component
export struct TaskCard {
  @ObjectLink row: TaskRow;
  @Prop busy: boolean = false;
  onToggle: (id: string) => void = () => {};
  onImportant: (id: string) => void = () => {};
  onEdit: (id: string) => void = () => {};

  build() {
    Column({ space: 10 }) {
      Row({ space: 10 }) {
        Button(this.row.task.State === 'Completed' ? '✓' : '○')
          .width(44).height(44).fontColor('#216FA6').backgroundColor('#E5F1FA')
          .accessibilityText(this.row.task.State === 'Completed' ? '撤销完成' : '标记完成')
          .enabled(!this.busy && this.row.task.State !== 'Cancelled')
          .onClick(() => this.onToggle(this.row.task.Id))
        Text(this.row.task.Title).fontSize(17).fontWeight(FontWeight.Medium)
          .fontColor(this.row.task.State === 'Completed' ? '#8998A1' : '#203C52')
          .decoration({ type: this.row.task.State === 'Completed' ? TextDecorationType.LineThrough : TextDecorationType.None })
          .layoutWeight(1)
        Button(this.row.task.IsImportant ? '★' : '☆').width(44).height(44)
          .fontColor('#A77928').backgroundColor(Color.Transparent)
          .accessibilityText(this.row.task.IsImportant ? '取消重要标记' : '标为重要')
          .enabled(!this.busy).onClick(() => this.onImportant(this.row.task.Id))
      }.width('100%')
      if (this.row.task.Notes) {
        Text(this.row.task.Notes).fontSize(14).fontColor('#617384').width('100%')
      }
      Row() {
        Text(`${this.row.task.ScheduledDate} · ${this.row.task.EventId ? '日历事件' : '日常任务'}${this.row.task.ImageIds.length ? ' · ' + this.row.task.ImageIds.length + ' 张图片' : ''}`).fontSize(12).fontColor('#617384')
        Blank()
        Button('编辑').fontSize(13).backgroundColor(Color.Transparent).fontColor('#216FA6')
          .enabled(!this.busy).onClick(() => this.onEdit(this.row.task.Id))
      }.width('100%')
    }.padding(16).backgroundColor(Color.White).borderRadius(18).width('100%').margin({ bottom: 10 })
  }
}
