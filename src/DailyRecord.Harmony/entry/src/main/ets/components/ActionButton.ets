@Component
export struct ActionButton {
  @Prop label: string = '';
  @Prop primary: boolean = false;
  @Prop isEnabled: boolean = true;
  action: () => void = () => {};

  build() {
    Button(this.label)
      .fontSize(this.primary ? 16 : 14)
      .fontColor(this.primary ? Color.White : '#216FA6')
      .backgroundColor(this.primary ? '#216FA6' : '#E5F1FA')
      .borderRadius(12).width('100%').height(this.primary ? 48 : 44)
      .enabled(this.isEnabled).onClick(() => this.action())
  }
}
