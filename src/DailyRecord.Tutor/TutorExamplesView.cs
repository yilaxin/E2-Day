using System.IO;
using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;

namespace DailyRecord.App;

public sealed partial class TutorWindow
{
    internal TutorStore ExampleStore => _store;
    internal bool IsExample { get; }
    internal Action<TutorWindow> PresentExamples { get; set; } = window => window.ShowDialog();

    private UIElement ExampleEntry()
    {
        var button = Ui.Button("体验示例数据 →", OpenExamples, true); button.Tag = "examples-open";
        return Ui.Card(Ui.Stack(Ui.Text("先看看，完整的教学记录是什么样", 18, Ui.Accent, true),
            Ui.Text("6 位虚构学生、近三个月课程、反馈与重复排课，一次体验全部图表。示例在独立窗口中打开。", 13, Ui.Muted), button));
    }

    private UIElement ExampleBanner()
    {
        var exit = Ui.Button("返回我的记录", Close); exit.Tag = "examples-close";
        var panel = Ui.Stack(Ui.Text("示例体验 · 全部为虚构数据", 17, Ui.Accent, true),
            Ui.Text("可以自由修改、确认授课或填写反馈。关闭窗口后本次修改不保留；再次进入会按当天重新生成。", 12, Ui.Muted), exit);
        var card = Ui.Card(panel, new Thickness(14)); card.Tag = "examples-banner";
        return card;
    }

    internal void OpenExamples()
    {
        // Each demonstration owns a unique temporary database. No account, real backup or real store is opened.
        var directory = Path.Combine(Path.GetTempPath(), "E2DayTutor-Examples", Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new TutorStore(directory);
            store.Save(TutorExamples.Create(Today));
            var example = new TutorWindow(new TutorService(store), store, Today, isExample: true)
            { Owner = this, AccountName = "虚构示例", Title = "E²-Day Teacher · 示例体验" };
            example._dashboardFrom = DateOnly.FromDayNumber(Math.Max(new DateOnly(1901, 1, 1).DayNumber, Today.DayNumber - 83));
            example._dashboardTo = DateOnly.FromDayNumber(Math.Min(new DateOnly(2100, 12, 31).DayNumber, Today.DayNumber + 7));
            example.Navigate(6);
            try { PresentExamples(example); }
            finally { example.Close(); }
        }
        finally
        {
            // Only remove this session's generated folder after its SQLite connection has been disposed.
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* OS temporary files can be reclaimed later. */ }
        }
    }
}
