using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static async Task VerifyLongTermPeriods(HolidayService holidays, string output)
    {
        static void Check(bool okay, string message) {if(!okay) throw new InvalidOperationException(message);}
        var clock = new PreviewClock(); var day = clock.Today;
        using var store = new SqliteStore(Path.Combine(output, "period-ui", Guid.NewGuid().ToString("N")));
        var service = new TaskService(store, clock);
        var window = new MainWindow(service, store, holidays, true) { Width = 1400, Height = 940, ShowInTaskbar = false };
        async Task Layout() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        void ClickTag(string tag) => FindButton(window, tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bool Has(string tag) => Descendants(window).OfType<FrameworkElement>().Any(e => Equals(e.Tag, tag));
        try
        {
            window.Show(); await Layout();
            window.ItemEditorPresenter = dialog =>
            {
                Exception? failure = null;
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        Descendants(dialog).OfType<ComboBox>().Single(c => Equals(c.Tag,"item-kind")).SelectedIndex=(int)ItemKind.LongTerm;
                        var duration=Descendants(dialog).OfType<ComboBox>().Single(c=>Equals(c.Tag,"period-duration"));
                        Check(duration.SelectedIndex == 1,"新长期任务没有默认30天。"); duration.SelectedIndex=0;
                        Descendants(dialog).OfType<TextBox>().Single(t=>Equals(t.Tag,"task-title")).Text="七天阅读计划";
                        Check(Descendants(dialog).OfType<TextBlock>().Any(t=>Equals(t.Tag,"period-preview")&&t.Text.Contains(day.AddDays(6).ToString("yyyy/M/d"))),"编辑预览未显示含首日的截止日期。");
                        Render((FrameworkElement)dialog.Content,580,920,Path.Combine(output,"period-editor.png"));
                        FindButton(dialog,"item-save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch(Exception ex) {failure=ex; dialog.Close();}
                },DispatcherPriority.ContextIdle);
                var result=dialog.ShowDialog(); if(failure is not null)throw failure; return result;
            };
            FindButton(window,"copy-today-image");
            // Use the visible add entry and the same save path as normal input.
            var add=Descendants(window).OfType<Button>().Single(b=>Equals(b.Content,"＋ 添加任务")); add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout();
            var ev=service.State.Events.Single(); Check(ev.EndDate==day.AddDays(6)&&ev.LongTermPeriods.Count==1,"真实编辑保存没有写入7天期限。");
            var first=service.State.Tasks.Single(); service.ToggleCompleted(first);
            clock.Today=day.AddDays(6);service.RefreshDay();window.Navigate(PageKind.Today);await Layout();
            var last=service.VisibleOpen(clock.Today).Single();ClickTag("complete:"+last.Id);await Layout();
            Check(Has("period-decision:"+ev.Id)&&Has("period-renew:"+ev.Id),"最后一天完成后续期入口消失。");
            Check(Descendants(window).OfType<TextBlock>().Any(t=>Equals(t.Tag,"period-progress:"+last.Id)&&t.Text.Contains("7/7")&&t.Text.Contains("2 天")),"阶段进度显示错误。");
            Render((FrameworkElement)window.Content,1400,940,Path.Combine(output,"period-last-day.png"));
            void Renew(bool save)
            {
                window.RenewalPresenter=dialog=>
                {
                    Exception? failure=null;
                    dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
                    {
                        try
                        {
                            Check(dialog.Start==clock.Today.AddDays(1)&&dialog.Days==7,"续期未从次日衔接原时长。");
                            Render((FrameworkElement)dialog.Content,520,600,Path.Combine(output,"period-renewal.png"));
                            FindButton(dialog,save?"renew-save":"renew-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        catch(Exception ex){failure=ex;dialog.Close();}
                    },DispatcherPriority.ContextIdle);
                    var result=dialog.ShowDialog();if(failure is not null)throw failure;return result;
                };
                ClickTag("period-renew:"+ev.Id);
            }
            var before=StateJson.Serialize(service.State); var revision=service.Revision;Renew(false);await Layout();
            Check(before==StateJson.Serialize(service.State)&&revision==service.Revision,"取消续期写入数据。");
            Renew(true);await Layout();Check(ev.LongTermPeriods.Count==2&&service.State.Events.Count==1&&!Has("period-decision:"+ev.Id),"续期未延续同一事项。");
            clock.Today=day.AddDays(30);service.RefreshDay();window.Navigate(PageKind.Today);await Layout();
            Check(Has("period-decision:"+ev.Id)&&!service.VisibleOpen(clock.Today).Any(),"错过到期仍自动生成或未显示待决定。");
            Render((FrameworkElement)window.Content,1400,940,Path.Combine(output,"period-expired.png"));
            ClickTag("period-later:"+ev.Id);await Layout();Check(!Has("period-decision:"+ev.Id),"稍后决定没有收起当前提示。");
            clock.Today=clock.Today.AddDays(1);service.RefreshDay();window.Navigate(PageKind.Today);
            await Layout();ClickTag("period-confirm:"+ev.Id);await Layout();Check(!service.LongTermDecisions().Any()&&service.State.Tasks.Count(t=>t.State==TaskState.Completed)==2,"结束改变了打卡历史。");
            var resumed=false; window.RenewalPresenter=dialog=>
            {
                Check(dialog.Start==clock.Today&&dialog.Days==7,"历史重新开始没有沿用原时长或从今天开始。");resumed=true;return false;
            };
            window.Navigate(PageKind.History);await Layout();
            Descendants(window).OfType<TextBox>().Single(t=>Equals(t.ToolTip,"搜索所有日期的标题和备注")).Text=ev.Title;await Layout();
            FindButton(window,"more:"+first.Id).ContextMenu!.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"重新开始长期任务")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(resumed,"历史中的重新开始未打开阶段选择。");

            service.AddLongTermTask("旧日常习惯", "", day, true);service.AddEvent("项目截止", "", day.AddDays(2), EventImportance.Normal, RepeatRule.None);
            service.AddTask("普通安排", "", day.AddDays(2));window.Navigate(PageKind.Year);await Layout();
            var dateTag="date:"+day.AddDays(2).ToString("yyyy-MM-dd");var cell=FindButton(window,dateTag);var allTip=(string)cell.ToolTip;
            Check(allTip.Contains("旧日常习惯")&&allTip.Contains("项目截止"),"全部日历范围缺任务。");
            var scroll=(ScrollViewer)window.FindName("CalendarScroller");window.Height=700;await Layout();scroll.ScrollToVerticalOffset(150);await Layout();var offset=scroll.VerticalOffset;
            void Mode(CalendarViewMode mode,string menuTag="calendar-view-menu") => FindButton(window,menuTag).ContextMenu!.Items.OfType<MenuItem>().Single(m=>Equals(m.Tag,"calendar-mode:"+mode)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var recordCount=service.State.Tasks.Count;Mode(CalendarViewMode.HideLongTerm);await Layout();
            Check(ReferenceEquals(cell,FindButton(window,dateTag))&&!((string)cell.ToolTip).Contains("旧日常习惯")&&((string)cell.ToolTip).Contains("项目截止")&&Math.Abs(scroll.VerticalOffset-offset)<1,"隐藏长期重建控件、漏事件或改变视口。");
            ClickTag("calendar-toggle");await Layout();Check(service.State.Settings.CalendarView==CalendarViewMode.Plain&&!((string)cell.ToolTip).Contains("项目截止")&&Math.Abs(scroll.VerticalOffset-offset)<1,"纯日历未隐藏标记或跳动。");
            scroll.ScrollToTop();await Layout();Render((FrameworkElement)window.Content,1400,700,Path.Combine(output,"calendar-plain.png"));
            scroll.ScrollToVerticalOffset(offset);await Layout();
            ClickTag("calendar-toggle");await Layout();Check(service.State.Settings.CalendarView==CalendarViewMode.HideLongTerm,"未恢复纯日历之前的视图。");
            ClickTag($"month:{day:yyyy-MM}");await Layout();var monthCell=FindButton(window,"month-date:"+day.AddDays(2).ToString("yyyy-MM-dd"));
            var monthScroll=(ScrollViewer)window.FindName("MonthScroller");monthScroll.ScrollToVerticalOffset(200);await Layout();offset=monthScroll.VerticalOffset;
            ClickTag("month-calendar-toggle");await Layout();
            Check(ReferenceEquals(monthCell,FindButton(window,"month-date:"+day.AddDays(2).ToString("yyyy-MM-dd")))&&!Descendants(monthCell).OfType<TextBlock>().Any(t=>Equals(t.Tag,"month-task"))&&Math.Abs(monthScroll.VerticalOffset-offset)<1,"月历纯视图未复用或改变视口。");
            Check(service.State.Tasks.Count==recordCount&&service.ForDate(day.AddDays(2)).Count>=3,"显示开关改动了数据或日期明细。");
            Check(store.Load().Settings.PreviousCalendarView==CalendarViewMode.HideLongTerm,"日历偏好未持久化。");
            File.WriteAllText(Path.Combine(output,"period-calendar-result.txt"),"PASS: actual modal 30-day default/7-day save and inclusive preview; last-day completion retains renewal; progress, cancel without writes, same-series renewal; expired pause/later/confirm preserve history; three calendar modes, pure toggle returns previous mode, cached day identity and year/month scroll positions retained; record counts, details and saved preference preserved. Isolated sample data only.");
        }
        finally {window.Close();}
    }
}
