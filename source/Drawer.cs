using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DayDesk {
public sealed class DrawerController {
    readonly Window main, tab;
    readonly Action beforeFold;
    readonly DispatcherTimer foldTimer, hoverTimer;
    bool moving, closing;
    public DrawerController(Window window,Action save) {
        main=window;beforeFold=save;
        tab=new Window {Title="日程 · 侧边入口",Owner=main,Width=34,Height=114,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,Topmost=true,WindowStartupLocation=WindowStartupLocation.Manual};
        var label=UI.Text("‹\n日\n程",15,Brushes.White,true);label.TextAlignment=TextAlignment.Center;
        tab.Content=new Border{Background=UI.Accent,CornerRadius=new CornerRadius(12,0,0,12),Child=label,Padding=new Thickness(2,11,2,11),Cursor=Cursors.Hand,ToolTip="点击或停留，展开日程"};
        foldTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(900)};
        foldTimer.Tick+=(s,e)=>{foldTimer.Stop();if(!main.IsActive&&!main.Topmost&&!HasDialog()&&Mouse.Captured==null)Fold();};
        hoverTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};
        hoverTimer.Tick+=(s,e)=>{hoverTimer.Stop();Expand();};
        tab.MouseEnter+=(s,e)=>hoverTimer.Start();tab.MouseLeave+=(s,e)=>hoverTimer.Stop();tab.MouseLeftButtonUp+=(s,e)=>Expand();
        var menu=new ContextMenu();var open=new MenuItem{Header="展开日程"};open.Click+=(s,e)=>Expand();menu.Items.Add(open);var exit=new MenuItem{Header="退出日程"};exit.Click+=(s,e)=>main.Close();menu.Items.Add(exit);((Border)tab.Content).ContextMenu=menu;
        main.Deactivated+=(s,e)=>{if(main.IsVisible&&!moving)foldTimer.Start();};main.Activated+=(s,e)=>foldTimer.Stop();
        main.StateChanged+=(s,e)=>{if(main.WindowState==WindowState.Minimized){main.WindowState=WindowState.Normal;Fold();}};
        main.Closing+=(s,e)=>{if(e.Cancel)return;closing=true;foldTimer.Stop();hoverTimer.Stop();tab.Close();};
    }
    bool HasDialog(){foreach(Window w in main.OwnedWindows)if(w!=tab&&w.IsVisible)return true;return false;}
    public void Fold(){
        if(closing||moving||!main.IsVisible||HasDialog())return;
        beforeFold();foldTimer.Stop();hoverTimer.Stop();moving=true;
        var area=SystemParameters.WorkArea;
        Action finish=()=>{main.BeginAnimation(Window.LeftProperty,null);main.Hide();tab.Left=area.Right-tab.Width;tab.Top=area.Top+(area.Height-tab.Height)/2;tab.Show();moving=false;};
        if(!SystemParameters.ClientAreaAnimation){finish();return;}
        var motion=new DoubleAnimation(main.Left,area.Right-8,TimeSpan.FromMilliseconds(180)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseIn}};
        motion.Completed+=(s,e)=>finish();main.BeginAnimation(Window.LeftProperty,motion);
    }
    public void Expand(){
        if(closing||moving)return;hoverTimer.Stop();foldTimer.Stop();
        var area=SystemParameters.WorkArea;double target=area.Right-main.Width-12;
        tab.Hide();main.Left=target;main.Show();main.Activate();moving=true;
        if(!SystemParameters.ClientAreaAnimation){moving=false;return;}
        var motion=new DoubleAnimation(area.Right-12,target,TimeSpan.FromMilliseconds(230)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
        motion.Completed+=(s,e)=>{main.BeginAnimation(Window.LeftProperty,null);main.Left=target;moving=false;};main.BeginAnimation(Window.LeftProperty,motion);
    }
}
public static class FunReminder {
    public static void Show(Window owner,int unfinished){
        var w=UI.Dialog(owner,"电击你",460,390);w.ResizeMode=ResizeMode.NoResize;
        var p=new StackPanel{Margin=new Thickness(24)};
        var bolt=UI.Text("⚡  电击你  ⚡",24,UI.Brush("#B17619"),true);bolt.HorizontalAlignment=HorizontalAlignment.Center;p.Children.Add(bolt);
        var words=UI.Text("怎么又没do完？快点给我大do特do，狠狠do！",23,UI.Ink,true);words.TextAlignment=TextAlignment.Center;words.Margin=new Thickness(0,20,0,14);p.Children.Add(words);
        var hint=UI.Text("还有 "+unfinished+" 项待办。充个电，再慢慢来。",12,UI.Muted);hint.TextAlignment=TextAlignment.Center;p.Children.Add(hint);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,24,0,0)};
        actions.Children.Add(UI.Button("do！这就去",()=>w.Close(),true));actions.Children.Add(UI.Button("今天先饶我",()=>w.Close()));p.Children.Add(actions);
        w.Content=UI.Card(p,"#FFF5CD",0);
        w.Loaded+=(s,e)=>{if(!SystemParameters.ClientAreaAnimation)return;double x=w.Left;var shake=new DoubleAnimationUsingKeyFrames();double[] offsets={0,-6,6,-5,5,-3,3,0};for(int i=0;i<offsets.Length;i++)shake.KeyFrames.Add(new LinearDoubleKeyFrame(x+offsets[i],KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i*75))));shake.Completed+=(a,b)=>{w.BeginAnimation(Window.LeftProperty,null);w.Left=x;};w.BeginAnimation(Window.LeftProperty,shake);};
        w.ShowDialog();
    }
}
}
