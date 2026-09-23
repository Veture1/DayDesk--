using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DayDesk;

public static class UiChecks {
    static string output; static int passed;
    static void Assert(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);passed++;}
    static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject {
        if(root is T)yield return (T)root;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var c in All<T>(VisualTreeHelper.GetChild(root,i)))yield return c;
    }
    static void Click(Window w,string label){w.UpdateLayout();var b=All<Button>(w).First(x=>Convert.ToString(x.Content)==label);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    static void Later(Action action){Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,action);}
    static Window Modal(string title){return Application.Current.Windows.Cast<Window>().Single(x=>x.Title==title);}
    static void Pump(int milliseconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    static void Capture(Window w,string name){
        w.UpdateLayout();var content=(FrameworkElement)w.Content;int width=(int)Math.Ceiling(content.ActualWidth),height=(int)Math.Ceiling(content.ActualHeight);
        if(width<1||height<1)throw new Exception("Missing layout for "+name);
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(UI.Canvas,null,new Rect(0,0,width,height));dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,width,height));}
        var bmp=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bmp.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(output,name+".png")))png.Save(f);
    }
    [STAThread] public static int Main(string[] args){
        output=args[0];Directory.CreateDirectory(output);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=0;
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((s,e)=>{if(s is Window){((Window)s).Opacity=0;((Window)s).ShowInTaskbar=false;}}));
        app.DispatcherUnhandledException+=(s,e)=>{Console.Error.WriteLine(e.Exception);e.Handled=true;result=1;app.Shutdown();};
        var store=new DeskStore(Path.Combine(output,"data"));
        store.SaveKeyEvent(new KeyEvent{Id="weekly",Title="每周组会",Repeat="weekly",WeekDay=3,Time="17:30",MonthDay=1});
        store.State.Notes.Add(new Note{Id="idea",Date=DeskStore.Today(),Text="珍贵想法：把大目标拆成眼前能做的下一步。",Color="#FFF2C9"});
        store.State.Todos.Add(new Todo{Id="todo",Date=DeskStore.Today(),Title="完成今天的一小步",Done=false});store.Save();
        var main=new MainWindow(store){Opacity=0,ShowInTaskbar=false,Topmost=true};main.Show();
        Later(()=>{
            try{
                Pump(60);
                Later(()=>{var manager=Modal("管理我的主线");manager.UpdateLayout();All<TextBox>(manager).First(x=>x.MaxLength==500).Text="学英语";var exp=All<Expander>(manager).First();exp.IsExpanded=true;manager.UpdateLayout();All<TextBox>(manager).First(x=>x.AcceptsReturn).Text="基础词汇\n日常听力\n口语练习";Click(manager,"＋ 添加主线");Assert(store.State.Tracks.Count==1&&store.State.Tracks[0].Nodes.Count==3,"Mainline form creates a custom route");Capture(manager,"tracks");manager.Close();});
                Dialogs.ShowTracks(main,store);Pump(80);Capture(main,"main");
                Later(()=>{var agenda=Modal("关键日程与准备清单");
                    Later(()=>{var editor=Modal("添加关键日程");editor.UpdateLayout();All<TextBox>(editor).First(x=>x.MaxLength==100).Text="重要会议";All<ComboBox>(editor).First(x=>x.Items.Count==3).SelectedIndex=2;editor.UpdateLayout();All<TextBox>(editor).First(x=>x.MaxLength==2).Text="15";All<TextBox>(editor).First(x=>x.MaxLength==5).Text="14:30";Capture(editor,"event-editor");Click(editor,"保存日程");});
                    Click(agenda,"＋ 添加关键日程");Assert(store.State.KeyEvents.Any(x=>x.Title=="重要会议"&&x.Repeat=="monthly"&&x.MonthDay==15&&x.Time=="14:30"),"Monthly event form saves recurrence and time");Capture(agenda,"agenda");agenda.Close();});
                Dialogs.ShowDeparture(main,store);
                Later(()=>{var joke=Modal("电击你");Pump(650);Capture(joke,"joke");Assert(All<TextBlock>(joke).Any(t=>t.Text=="怎么又没do完？快点给我大do特do，狠狠do！"),"Unfinished day shows the requested joke");joke.Close();});
                Click(main,"收工存档");Assert(store.State.Notebook.Count==1&&store.State.Notes.Count==0,"Finish day archives ideas before showing reminder");
                Click(main,"收工存档");Assert(store.State.LastFunReminderDate==DeskStore.Today()&&store.State.Notebook.Count==1,"Repeated finish keeps one archive and one reminder per day");
                Later(()=>{var notebook=Modal("笔记本 · 留住每天的想法");Capture(notebook,"notebook");var search=All<TextBox>(notebook).First();search.Text="not-found-test";Assert(All<TextBlock>(notebook).Any(t=>t.Text.Contains("没有找到匹配")),"Notebook search filters archived ideas");notebook.Close();});
                NotebookDialog.Show(main,store);
                var drawer=(DrawerController)typeof(MainWindow).GetField("drawer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(main);
                var tab=(Window)typeof(DrawerController).GetField("tab",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(drawer);tab.Opacity=0;
                drawer.Fold();Pump(350);Assert(!main.IsVisible&&tab.IsVisible&&tab.Width==34,"Drawer folds into a small side tab");drawer.Expand();Pump(350);Assert(main.IsVisible&&!tab.IsVisible,"Side tab restores the window");
                main.Width=420;main.Height=600;Pump(70);Capture(main,"compact");Assert(main.ActualWidth<=420&&main.ActualHeight<=600,"Compact window respects its requested dimensions");
                main.Close();Console.WriteLine("Passed "+passed+" in-process UI checks; rendered images in "+output);app.Shutdown();
            }catch(Exception ex){Console.Error.WriteLine(ex);result=1;app.Shutdown();}
        });app.Run();return result;
    }
}
