using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;

namespace DayDesk {
public static class Program {
    [STAThread] public static void Main(string[] args) {
        string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
        for(int i=0;i<args.Length-1;i++)if(args[i]=="--data")dir=Path.GetFullPath(args[i+1]);
        bool acquired=false;
        string mutexName="Local\\DayDesk_"+BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(dir.ToLowerInvariant()))).Replace("-","");
        using(var mutex=new Mutex(true,mutexName,out acquired)) {
            if(!acquired){MessageBox.Show("日程已经在运行，请切换到已打开的窗口。","日程 · DayDesk");return;}
            try {
                var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose}; app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show(e.Exception.Message,"日程 · 操作未完成");e.Handled=true;};
                var store=new DeskStore(dir); app.Run(new MainWindow(store));
            }catch(Exception ex){MessageBox.Show("无法打开日程："+ex.Message+"\n请确认程序文件夹可以写入。","日程 · DayDesk");}
        }
    }
}
public class MainWindow:Window {
    readonly DeskStore store; DateTime selected=DateTime.Today; bool pinned=false; bool changing=false; bool noteDirty=false; DrawerController drawer;
    Grid root,upper; StackPanel tasks,tracks; WrapPanel notes; TextBlock dateLabel,taskCount,saveLabel,departureLabel,inboxLabel,dayLabel;
    Button inboxButton; DispatcherTimer inboxTimer,saveTimer; DateTime lastDay=DateTime.Today;
    string SelectedDate {get{return selected.ToString("yyyy-MM-dd");}}
    public MainWindow(DeskStore data) {
        store=data;Title="日程 · DayDesk";Background=UI.Canvas;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;MinWidth=420;MinHeight=Math.Min(560,SystemParameters.WorkArea.Height-24);
        WindowStartupLocation=WindowStartupLocation.Manual;DockRight(); Build(); Render();
        store.Changed+=()=>{if(!changing)Dispatcher.BeginInvoke(new Action(Render));};
        saveTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(650)}; saveTimer.Tick+=(s,e)=>FlushNotes();
        inboxTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};inboxTimer.Tick+=(s,e)=>{RefreshInbox();UpdateAgenda();if(DateTime.Today!=lastDay){bool wasToday=selected==lastDay;lastDay=DateTime.Today;if(wasToday){FlushNotes();selected=DateTime.Today;Render();}}};inboxTimer.Start();
        Closing+=(s,e)=>{try{FlushNotes();store.Save();inboxTimer.Stop();}catch(Exception ex){e.Cancel=true;UI.Toast(this,"保存失败，窗口暂未关闭："+ex.Message);}};
        SizeChanged+=(s,e)=>Reflow();
        Loaded+=(s,e)=>{drawer=new DrawerController(this,()=>FlushNotes());if(!String.IsNullOrEmpty(store.RecoveryMessage))UI.Toast(this,store.RecoveryMessage);};
    }
    void DockRight(bool wide=false){WindowState=WindowState.Normal;var a=SystemParameters.WorkArea;Width=Math.Min(a.Width-24,wide?Math.Max(480,a.Width/3):500);Height=Math.Min(wide?a.Height-24:760,a.Height-24);Left=a.Right-Width-12;Top=a.Top+(a.Height-Height)/2;}
    void Build(){
        root=new Grid{Margin=new Thickness(24,18,24,12)};Content=root;
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1.1,GridUnitType.Star),MinHeight=170});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(16)});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star),MinHeight=110});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var header=new Grid{Margin=new Thickness(0,0,0,18)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        dateLabel=UI.Text("",12,UI.Muted);var title=UI.Stack(UI.Text("日程",28,UI.Ink,true),dateLabel);header.Children.Add(title);
        var actions=new WrapPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};
        actions.Children.Add(UI.Button("AI 更新",()=>{FlushNotes();Dialogs.ShowUpdates(this,store);Render();},true));
        var options=UI.Button("⋯",()=>{});options.ToolTip="窗口与趣味提醒设置";var settings=new ContextMenu();
        AddMenu(settings,"紧凑窄窗",()=>DockRight());AddMenu(settings,"带鱼屏 ⅓",()=>DockRight(true));
        var keep=new MenuItem{Header="保持展开 / 置顶",IsCheckable=true,IsChecked=Topmost};keep.Click+=(s,e)=>{pinned=keep.IsChecked;Topmost=pinned;};settings.Items.Add(keep);
        var fun=new MenuItem{Header="趣味收工提醒",IsCheckable=true,IsChecked=store.State.FunReminderEnabled??true};fun.Click+=(s,e)=>{FlushNotes();store.SetFunReminderEnabled(fun.IsChecked);};settings.Items.Add(fun);AddMenu(settings,"退出日程",()=>Close());options.Click+=(s,e)=>{settings.PlacementTarget=options;settings.IsOpen=true;};actions.Children.Add(options);
        actions.Children.Add(UI.Button("收起 ›",()=>{if(drawer!=null)drawer.Fold();}));Grid.SetColumn(actions,1);header.Children.Add(actions);root.Children.Add(header);
        var depGrid=new Grid();depGrid.ColumnDefinitions.Add(new ColumnDefinition());depGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});departureLabel=UI.Text("",12,UI.Brush("#52695B"));depGrid.Children.Add(departureLabel);var depMore=UI.Text("编辑与清单  ›",12,UI.Accent,true);depMore.Margin=new Thickness(12,0,0,0);Grid.SetColumn(depMore,1);depGrid.Children.Add(depMore);
        var departure=UI.Card(depGrid,"#EAF0E5",12);departure.Margin=new Thickness(0,0,0,18);departure.Cursor=Cursors.Hand;departure.MouseLeftButtonUp+=(s,e)=>{FlushNotes();Dialogs.ShowDeparture(this,store);Render();};Grid.SetRow(departure,1);root.Children.Add(departure);
        upper=new Grid();upper.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.25,GridUnitType.Star)});upper.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});upper.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});Grid.SetRow(upper,2);root.Children.Add(upper);
        var todoPanel=new Grid();todoPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});todoPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});todoPanel.RowDefinitions.Add(new RowDefinition());
        var th=new DockPanel{Margin=new Thickness(0,0,0,12)};var addTodo=UI.Button("＋",()=>AddTodo());addTodo.ToolTip="添加待办";DockPanel.SetDock(addTodo,Dock.Right);th.Children.Add(addTodo);taskCount=UI.Text("",11,UI.Muted);DockPanel.SetDock(taskCount,Dock.Right);th.Children.Add(taskCount);th.Children.Add(UI.Text("每日待办",17,UI.Ink,true));todoPanel.Children.Add(th);
        var days=new DockPanel{Margin=new Thickness(0,0,0,10)};var arrows=new StackPanel{Orientation=Orientation.Horizontal};arrows.Children.Add(UI.Button("‹",()=>ChangeDay(-1)));arrows.Children.Add(UI.Button("今天",()=>{FlushNotes();selected=DateTime.Today;Render();}));arrows.Children.Add(UI.Button("›",()=>ChangeDay(1)));DockPanel.SetDock(arrows,Dock.Right);days.Children.Add(arrows);dayLabel=UI.Text("",12,UI.Muted);days.Children.Add(dayLabel);Grid.SetRow(days,1);todoPanel.Children.Add(days);
        tasks=new StackPanel();var taskScroll=new ScrollViewer{Content=tasks,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(taskScroll,2);todoPanel.Children.Add(taskScroll);upper.Children.Add(todoPanel);
        var trackPanel=new Grid();trackPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});trackPanel.RowDefinitions.Add(new RowDefinition());
        var trackHead=new DockPanel{Margin=new Thickness(0,0,0,12)};
        var manageTracks=UI.Button("管理",()=>{FlushNotes();Dialogs.ShowTracks(this,store);Render();});manageTracks.ToolTip="新增、改名、排序和删除主线";manageTracks.Padding=new Thickness(10,6,10,6);manageTracks.Margin=new Thickness(6,0,0,0);manageTracks.VerticalAlignment=VerticalAlignment.Top;DockPanel.SetDock(manageTracks,Dock.Right);trackHead.Children.Add(manageTracks);
        trackHead.Children.Add(UI.Stack(UI.Text("主线",17,UI.Ink,true),UI.Text("按你的目标安排",11,UI.Muted)));trackPanel.Children.Add(trackHead);
        tracks=new StackPanel();var trackScroll=new ScrollViewer{Content=tracks,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(trackScroll,1);trackPanel.Children.Add(trackScroll);Grid.SetColumn(trackPanel,2);upper.Children.Add(trackPanel);
        var splitter=new GridSplitter{Height=5,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Center,Background=UI.Brush("#DBE2D8"),ResizeDirection=GridResizeDirection.Rows,ResizeBehavior=GridResizeBehavior.PreviousAndNext};Grid.SetRow(splitter,3);root.Children.Add(splitter);
        var lower=new Grid();lower.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});lower.RowDefinitions.Add(new RowDefinition());var nh=new DockPanel{Margin=new Thickness(0,8,0,12)};
        var noteActions=new StackPanel{Orientation=Orientation.Horizontal};var addNote=UI.Button("＋",()=>AddNote());addNote.ToolTip="新建便利贴";noteActions.Children.Add(addNote);
        noteActions.Children.Add(UI.Button("笔记本",()=>{FlushNotes();NotebookDialog.Show(this,store);Render();}));var archive=UI.Button("收工存档",()=>FinishDay());archive.ToolTip="把这个日期的想法收进笔记本";archive.Margin=new Thickness(0);noteActions.Children.Add(archive);DockPanel.SetDock(noteActions,Dock.Right);nh.Children.Add(noteActions);nh.Children.Add(UI.Text("当日想法",17,UI.Ink,true));lower.Children.Add(nh);
        notes=new WrapPanel();var noteScroll=new ScrollViewer{Content=notes,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(noteScroll,1);lower.Children.Add(noteScroll);Grid.SetRow(lower,4);root.Children.Add(lower);
        var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};saveLabel=UI.Text("●  已保存在本机",10,UI.Muted);DockPanel.SetDock(saveLabel,Dock.Right);footer.Children.Add(saveLabel);inboxLabel=UI.Text("",10,UI.Muted);inboxButton=UI.Button("",()=>OpenInbox());inboxButton.Padding=new Thickness(8,4,8,4);inboxButton.MinHeight=20;inboxButton.FontSize=10;footer.Children.Add(inboxButton);Grid.SetRow(footer,5);root.Children.Add(footer);
    }
    void ChangeDay(int offset){FlushNotes();selected=selected.AddDays(offset);Render();}
    void Persist(bool redraw=true){changing=true;try{store.Save();saveLabel.Text="●  已保存在本机";}catch{saveLabel.Text="保存失败，请重试";throw;}finally{changing=false;}if(redraw)Render();}
    void FlushNotes(){saveTimer.Stop();if(!noteDirty)return;Persist(false);noteDirty=false;}
    void Render(){
        if(noteDirty)FlushNotes();dateLabel.Text=DateTime.Today.ToString("yyyy 年 M 月 d 日  ·  dddd",new System.Globalization.CultureInfo("zh-CN"));dayLabel.Text=selected==DateTime.Today?"今天":selected.ToString("M 月 d 日");
        var list=store.State.Todos.Where(t=>t.Date==SelectedDate).OrderBy(t=>t.Done).ToList();taskCount.Text=list.Count==0?"":list.Count(t=>t.Done)+" / "+list.Count+"  ";tasks.Children.Clear();
        int overdue=store.State.Todos.Count(t=>!t.Done&&String.CompareOrdinal(t.Date,DeskStore.Today())<0);
        if(selected==DateTime.Today&&overdue>0){var b=UI.Button(overdue+" 项往期待办待处理  ›",()=>ShowOverdue());b.Margin=new Thickness(0,0,4,10);tasks.Children.Add(b);}
        if(list.Count==0){var blank=UI.Stack(UI.Text("给今天留一点方向",17,UI.Ink,true),UI.Text("点击 ＋ 添加第一件事。\n小步往前，就很好。",12,UI.Muted));blank.Margin=new Thickness(10,28,8,8);tasks.Children.Add(blank);}
        foreach(var todo in list)tasks.Children.Add(TodoRow(todo));
        tracks.Children.Clear();
        if(store.State.Tracks.Count==0){var blank=UI.Stack(UI.Text("你的目标，你来定义",16,UI.Ink,true),UI.Text("学英语、健身、论文……\n把一条主线拆成几个节点。",12,UI.Muted));blank.Margin=new Thickness(4,10,4,18);tracks.Children.Add(blank);tracks.Children.Add(UI.Button("＋ 创建第一条主线",()=>{FlushNotes();Dialogs.ShowTracks(this,store);Render();},true));}
        foreach(var tr in store.State.Tracks){var track=tr;var current=track.Nodes.FirstOrDefault(n=>n.Status=="current");string pos=current!=null?current.Title:(track.Nodes.Count==0?"添加路线节点":(track.Nodes.All(n=>n.Status=="done")?"本轮已完成":"选择当前节点"));
            var text=UI.Stack(UI.Text(track.Title,11,UI.Muted),UI.Text(pos+"  ›",14,UI.Ink,true));var card=UI.Card(text,"#FFFFFF",12);card.Margin=new Thickness(0,0,2,9);card.BorderBrush=UI.Brush(track.Color??"#E3E9DE");card.Cursor=Cursors.Hand;card.MouseLeftButtonUp+=(s,e)=>{FlushNotes();Dialogs.ShowTrack(this,store,track);Render();};tracks.Children.Add(card);}
        UpdateAgenda();
        notes.Children.Clear();var noteList=store.State.Notes.Where(n=>n.Date==SelectedDate||n.Pinned).OrderByDescending(n=>n.Pinned).ToList();if(noteList.Count==0){var n=UI.Stack(UI.Text("让想法先落在这里。",21,UI.Brush("#879282"),true),UI.Text("灵感、复盘，或者明天想尝试的事。",12,UI.Muted));n.Margin=new Thickness(12,32,12,0);notes.Children.Add(n);}
        foreach(var note in noteList)notes.Children.Add(NoteCard(note));Reflow();RefreshInbox();
    }
    void UpdateAgenda(){
        int remain=store.State.DepartureItems.Count(d=>!d.Done);var now=DateTime.Now;
        var occurrences=store.State.KeyEvents.Select(ev=>new{Event=ev,Next=DeskStore.NextOccurrence(ev,now)}).Where(x=>x.Next.HasValue).ToList();
        var nearest=occurrences.Where(x=>x.Next.Value>=now).OrderBy(x=>x.Next.Value).FirstOrDefault()??occurrences.OrderByDescending(x=>x.Next.Value).FirstOrDefault();
        string countdown="关键日程  ·  设置日期或重复安排";
        if(nearest!=null){int days=(nearest.Next.Value.Date-DateTime.Today).Days;string relative=days>0?"还有 "+days+" 天":days==0?"今天":"已过 "+(-days)+" 天";countdown=nearest.Event.Title+"  ·  "+relative+"  "+nearest.Next.Value.ToString("HH:mm");}
        else if(store.State.KeyEvents.Count>0)countdown=store.State.KeyEvents[0].Title+"  ·  设置日期";
        var next=store.State.DepartureItems.Where(d=>!d.Done).OrderBy(d=>String.IsNullOrEmpty(d.DueDate)?"9999-12-31":d.DueDate).FirstOrDefault();
        departureLabel.Text=countdown+(next==null?(store.State.KeyEvents.Count>1?"\n共 "+store.State.KeyEvents.Count+" 个关键日程":""):"\n准备事项 "+remain+" 项 · "+next.Title);
    }
    UIElement TodoRow(Todo todo){
        var grid=new Grid{Margin=new Thickness(0,0,0,9)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});
        var check=new CheckBox{IsChecked=todo.Done,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,4,0,0)};check.ToolTip="标记完成";check.Click+=(s,e)=>{FlushNotes();store.SetTodoDone(todo,check.IsChecked==true);Persist();};grid.Children.Add(check);
        var title=UI.Text(todo.Title,13,todo.Done?UI.Muted:UI.Ink);if(todo.Done)title.TextDecorations=TextDecorations.Strikethrough;var desc=UI.Stack(title);
        var tr=store.State.Tracks.FirstOrDefault(t=>t.Id==todo.TrackId);if(tr!=null){var node=tr.Nodes.FirstOrDefault(n=>n.Id==todo.NodeId);desc.Children.Add(UI.Text(tr.Title+(node!=null?" / "+node.Title:""),10,UI.Muted));}Grid.SetColumn(desc,1);grid.Children.Add(desc);
        var more=UI.Button("⋯",()=>{});more.Padding=new Thickness(2);more.Margin=new Thickness(1,0,0,0);more.MinHeight=24;var menu=new ContextMenu();AddMenu(menu,"编辑",()=>AddTodo(todo));AddMenu(menu,"移到明天",()=>{todo.Date=DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");Persist();});AddMenu(menu,"删除",()=>{store.State.Todos.Remove(todo);Persist();});more.Click+=(s,e)=>{menu.PlacementTarget=more;menu.IsOpen=true;};Grid.SetColumn(more,2);grid.Children.Add(more);
        var card=UI.Card(grid,"#FFFFFF",10);card.Margin=new Thickness(0,0,2,8);return card;
    }
    static void AddMenu(ContextMenu menu,string text,Action action){var item=new MenuItem{Header=text};item.Click+=(s,e)=>action();menu.Items.Add(item);}
    void AddTodo(Todo editing=null){FlushNotes();var w=UI.Dialog(this,editing==null?"添加待办":"编辑待办",520,410);var title=UI.Input(editing!=null?editing.Title:"");var date=UI.Input(editing!=null?editing.Date:SelectedDate);var link=new ComboBox{Margin=new Thickness(0,8,0,18),Padding=new Thickness(8),MinHeight=34};var choices=new List<Tuple<string,string,string,string>>();choices.Add(Tuple.Create("不关联主线","","",""));
        foreach(var tr in store.State.Tracks)foreach(var n in tr.Nodes){choices.Add(Tuple.Create(tr.Title+" / "+n.Title,tr.Id,n.Id,""));foreach(var task in n.Tasks)choices.Add(Tuple.Create("    ↳ "+task.Title,tr.Id,n.Id,task.Id));}
        foreach(var c in choices)link.Items.Add(c.Item1);link.SelectedIndex=0;if(editing!=null){int i=choices.FindIndex(c=>c.Item2==editing.TrackId&&c.Item3==editing.NodeId&&c.Item4==(editing.NodeTaskId??""));if(i>=0)link.SelectedIndex=i;}
        var p=UI.Stack(UI.Text("要做什么",12,UI.Muted),title,UI.Text("日期 · yyyy-MM-dd",12,UI.Muted),date,UI.Text("关联到主线节点（可选）",12,UI.Muted),link);p.Margin=new Thickness(24);title.Margin=date.Margin=new Thickness(0,7,0,16);
        var b=UI.Button("保存待办",()=>{DateTime d;if(String.IsNullOrWhiteSpace(title.Text))throw new Exception("请填写待办内容。");if(!DateTime.TryParseExact(date.Text,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out d))throw new Exception("日期格式请使用 yyyy-MM-dd。");var c=choices[link.SelectedIndex];var t=editing??new Todo{Id=DeskStore.NewId()};t.Title=title.Text.Trim();t.Date=date.Text;t.TrackId=c.Item2;t.NodeId=c.Item3;t.NodeTaskId=c.Item4;
            if(!String.IsNullOrEmpty(c.Item2)&&String.IsNullOrEmpty(c.Item4)){var track=store.State.Tracks.First(tr=>tr.Id==c.Item2);var node=track.Nodes.First(n=>n.Id==c.Item3);var matches=node.Tasks.Where(nt=>nt.Title==t.Title).ToList();if(matches.Count>1)throw new Exception("存在同名任务，请在下拉框中选择具体任务。");var task=matches.FirstOrDefault();if(task==null){task=new NodeTask{Id=DeskStore.NewId(),Title=t.Title,Done=t.Done};node.Tasks.Add(task);store.SetNodeTaskDone(track,node,task,task.Done);}t.NodeTaskId=task.Id;}
            if(!String.IsNullOrEmpty(t.NodeTaskId)){var nt=store.State.Tracks.First(tr=>tr.Id==t.TrackId).Nodes.First(n=>n.Id==t.NodeId).Tasks.First(x=>x.Id==t.NodeTaskId);t.Done=nt.Done;}if(editing==null)store.State.Todos.Add(t);store.SetTodoDone(t,t.Done);Persist();w.Close();},true);p.Children.Add(b);w.Content=p;w.Loaded+=(s,e)=>title.Focus();w.ShowDialog();}
    void AddNote(){FlushNotes();var n=new Note{Id=DeskStore.NewId(),Date=SelectedDate,Text="",Color=new[]{"#FFF2C9","#E6EEDC","#EAE4F5","#F8E4D9"}[store.State.Notes.Count%4],Pinned=false};store.State.Notes.Add(n);Persist();}
    void FinishDay(){FlushNotes();int count=store.ArchiveNotes(SelectedDate);Render();saveLabel.Text=count>0?"已收好 "+count+" 条想法":"今天的想法已收好";int left=store.State.Todos.Count(t=>t.Date==SelectedDate&&!t.Done);if(left>0&&(store.State.FunReminderEnabled??true)&&store.State.LastFunReminderDate!=DeskStore.Today()){store.MarkFunReminded(DeskStore.Today());FunReminder.Show(this,left);}}
    UIElement NoteCard(Note note){
        var p=new Grid();p.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});p.RowDefinitions.Add(new RowDefinition());var head=new DockPanel();var ops=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var pin=UI.Button(note.Pinned?"●":"○",()=>{FlushNotes();note.Pinned=!note.Pinned;Persist();});pin.ToolTip=note.Pinned?"取消固定":"固定到每天";pin.Background=Brushes.Transparent;pin.Padding=new Thickness(3);pin.MinHeight=22;ops.Children.Add(pin);
        var remove=UI.Button("×",()=>{FlushNotes();store.State.Notes.Remove(note);Persist();});remove.ToolTip="删除便利贴";remove.Background=Brushes.Transparent;remove.Padding=new Thickness(3);remove.MinHeight=22;ops.Children.Add(remove);DockPanel.SetDock(ops,Dock.Right);head.Children.Add(ops);head.Children.Add(UI.Text(note.Date==DeskStore.Today()?"今天":note.Date,10,UI.Muted));p.Children.Add(head);
        var input=UI.Input(note.Text,true);input.BorderThickness=new Thickness(0);input.Background=Brushes.Transparent;input.Padding=new Thickness(0,10,0,0);input.VerticalContentAlignment=VerticalAlignment.Top;input.MinHeight=105;input.FontSize=14;input.TextChanged+=(s,e)=>{note.Text=input.Text;noteDirty=true;saveLabel.Text="正在保存…";saveTimer.Stop();saveTimer.Start();};Grid.SetRow(input,1);p.Children.Add(input);
        var card=UI.Card(p,String.IsNullOrEmpty(note.Color)?"#FFF2C9":note.Color,14);card.Margin=new Thickness(0,0,12,12);card.Height=186;card.Tag="note";return card;
    }
    void Reflow(){if(root==null||notes==null)return;double available=Math.Max(380,ActualWidth-64);int columns=available>=800?3:2;double width=(available-(columns*12))/columns;foreach(var child in notes.Children.OfType<Border>())child.Width=Math.Max(155,width);}
    void ShowOverdue(){FlushNotes();var w=UI.Dialog(this,"往期待办",550,540);var p=new StackPanel{Margin=new Thickness(20)};foreach(var todo in store.State.Todos.Where(t=>!t.Done&&String.CompareOrdinal(t.Date,DeskStore.Today())<0).OrderBy(t=>t.Date).ToList()){var t=todo;var row=UI.Stack(UI.Text(t.Date+" · "+t.Title,14),UI.Button("移到今天",()=>{t.Date=DeskStore.Today();Persist();w.Close();}));row.Margin=new Thickness(0,0,0,16);p.Children.Add(row);}w.Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};w.ShowDialog();}
    List<string> Inbox(){var dir=Path.Combine(store.DataDirectory,"inbox");Directory.CreateDirectory(dir);return Directory.GetFiles(dir,"*.json").OrderBy(File.GetLastWriteTimeUtc).ToList();}
    void RefreshInbox(){try{int count=Inbox().Count;inboxButton.Content=count>0?"AI 待审更新 · "+count+"  ›":"本地工作台 · 数据文件夹";inboxButton.Foreground=count>0?UI.Accent:UI.Muted;}catch{inboxButton.Content="数据文件夹暂不可访问";}}
    void OpenInbox(){FlushNotes();var files=Inbox();if(files.Count==0){System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(store.DataDirectory){UseShellExecute=true});return;}var w=UI.Dialog(this,"AI 待审更新",600,480);var list=new StackPanel{Margin=new Thickness(22)};Action refresh=null;refresh=()=>{list.Children.Clear();list.Children.Add(UI.Text("选择一份更新，检查后应用",18,UI.Ink,true));foreach(var file in Inbox()){var path=file;var button=UI.Button(Path.GetFileName(path)+"  ›",()=>{PreviewInboxFile(w,path);refresh();});button.Margin=new Thickness(0,12,0,0);list.Children.Add(button);}if(Inbox().Count==0)list.Children.Add(UI.Text("所有更新已处理。",14,UI.Muted));};refresh();w.Content=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};w.ShowDialog();Render();}
    void PreviewInboxFile(Window owner,string path){string json=File.ReadAllText(path,System.Text.Encoding.UTF8);Dialogs.ShowUpdates(owner,store,json);var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();Dictionary<string,object> raw;try{raw=serializer.Deserialize<Dictionary<string,object>>(json);}catch{return;}if(raw!=null&&raw.ContainsKey("id")&&store.State.AppliedUpdateIds.Contains(Convert.ToString(raw["id"]))){string archive=Path.Combine(store.DataDirectory,"processed");Directory.CreateDirectory(archive);string dest=Path.Combine(archive,DateTime.Now.ToString("yyyyMMddHHmmssfff")+"-"+Path.GetFileName(path));File.Move(path,dest);}}
}
}
