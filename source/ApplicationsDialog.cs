using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;

namespace DayDesk {
public static class ApplicationsDialog {
    public static void Show(Window owner,DeskStore store,string trackId) {
        var track=store.State.Tracks.First(x=>x.Id==trackId);
        var w=UI.Dialog(owner,track.Title+" · 投递记录",650,760);w.MaxHeight=SystemParameters.WorkArea.Height*.94;
        var shell=new DockPanel{Margin=new Thickness(22)};var close=UI.Button("关闭",()=>w.Close());close.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(close,Dock.Bottom);shell.Children.Add(close);
        var content=new StackPanel();shell.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});w.Content=shell;
        Action render=null;string query="";
        render=()=>{
            track=store.State.Tracks.First(x=>x.Id==trackId);content.Children.Clear();
            content.Children.Add(UI.Text(track.Title+" · 投递记录",23,UI.Ink,true));
            var summary=UI.Text(DeskStore.ApplicationSummary(track),13,UI.Accent,true);summary.Margin=new Thickness(0,10,0,10);content.Children.Add(summary);
            var bar=new WrapPanel{Margin=new Thickness(0,0,0,12)};
            bar.Children.Add(UI.Button("＋ 添加公司",()=>{Edit(w,store,trackId,null);render();},true));
            if(!String.IsNullOrEmpty(track.ApplicationsSourceUrl))bar.Children.Add(UI.Button("打开原始清单",()=>{try{Process.Start(new ProcessStartInfo(track.ApplicationsSourceUrl){UseShellExecute=true});}catch(Exception ex){UI.Toast(w,ex.Message);}}));
            bar.Children.Add(UI.Button("设置清单链接",()=>{var url=UI.Prompt(w,"投递清单链接","粘贴 Notion 或其他清单链接",track.ApplicationsSourceUrl??"");if(url==null)return;try{store.SetApplicationsSource(trackId,url);render();}catch(Exception ex){UI.Toast(w,ex.Message);}}));content.Children.Add(bar);
            var hint=UI.Text("这里记录该地区的公司与投递状态。原始清单链接用于跳转，不会自动同步；可用 AI 更新批量整理。",12,UI.Muted);hint.Margin=new Thickness(0,0,0,16);content.Children.Add(hint);
            if(track.Applications.Count==0)content.Children.Add(UI.Text("还没有公司记录。添加公司或导入清单后，就能在这里查看各阶段的进展。",14,UI.Muted));
            var search=UI.Input(query);search.ToolTip="搜索公司、岗位或备注";search.Margin=new Thickness(0,0,0,12);content.Children.Add(UI.Text("搜索公司 / 岗位",12,UI.Muted));content.Children.Add(search);
            var records=new StackPanel();content.Children.Add(records);Action renderRows=()=>{records.Children.Clear();
            foreach(var item in track.Applications.Where(x=>String.IsNullOrEmpty(query)||(x.Company+" "+x.Role+" "+x.Notes).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).OrderBy(x=>Array.IndexOf(DeskStore.ApplicationStatuses,x.Status)).ThenBy(x=>x.Company)) {
                var record=item;var box=new StackPanel();
                box.Children.Add(UI.Text(record.Company,17,UI.Ink,true));
                if(!String.IsNullOrEmpty(record.Role))box.Children.Add(UI.Text(record.Role,13,UI.Muted));
                var row=new WrapPanel{Margin=new Thickness(0,8,0,0)};var status=new ComboBox{MinWidth=118,Margin=new Thickness(0,0,10,6),Padding=new Thickness(6)};
                foreach(string label in DeskStore.ApplicationLabels)status.Items.Add(label);status.SelectedIndex=Array.IndexOf(DeskStore.ApplicationStatuses,record.Status);
                status.SelectionChanged+=(s,e)=>{if(status.SelectedIndex<0)return;try{store.SaveApplication(trackId,new JobApplication{Id=record.Id,Company=record.Company,Role=record.Role,Status=DeskStore.ApplicationStatuses[status.SelectedIndex],AppliedDate=record.AppliedDate,Notes=record.Notes});w.Dispatcher.BeginInvoke(new Action(render));}catch(Exception ex){UI.Toast(w,ex.Message);}};row.Children.Add(status);
                row.Children.Add(UI.Button("编辑",()=>{Edit(w,store,trackId,record);render();}));
                row.Children.Add(UI.Button("删除",()=>{if(MessageBox.Show(w,"删除「"+record.Company+"」的这条投递记录？","删除投递记录",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;try{store.DeleteApplication(trackId,record.Id);render();}catch(Exception ex){UI.Toast(w,ex.Message);}}));box.Children.Add(row);
                if(!String.IsNullOrEmpty(record.AppliedDate))box.Children.Add(UI.Text("投递日期："+record.AppliedDate,11,UI.Muted));
                if(!String.IsNullOrEmpty(record.Notes))box.Children.Add(new Expander{Header="备注与原始清单信息",Content=UI.Text(record.Notes,12,UI.Muted),Margin=new Thickness(0,6,0,0)});
                var card=UI.Card(box,"#FFFFFF",14);card.Margin=new Thickness(0,0,6,10);records.Children.Add(card);
            }
            if(track.Applications.Count>0&&records.Children.Count==0)records.Children.Add(UI.Text("没有找到匹配记录。",13,UI.Muted));};
            search.TextChanged+=(s,e)=>{query=search.Text.Trim();renderRows();};renderRows();
        };render();w.ShowDialog();
    }
    static void Edit(Window owner,DeskStore store,string trackId,JobApplication item) {
        var w=UI.Dialog(owner,item==null?"添加投递记录":"编辑投递记录",520,660);w.MaxHeight=SystemParameters.WorkArea.Height*.94;
        var p=new StackPanel{Margin=new Thickness(22)};
        var company=UI.Input(item==null?"":item.Company);company.MaxLength=200;
        var role=UI.Input(item==null?"":item.Role);role.MaxLength=300;
        var date=UI.Input(item==null?"":item.AppliedDate);date.MaxLength=10;
        var notes=UI.Input(item==null?"":item.Notes,true);notes.Height=96;notes.MaxLength=10000;
        var status=new ComboBox{Padding=new Thickness(8),MinHeight=34};foreach(string label in DeskStore.ApplicationLabels)status.Items.Add(label);status.SelectedIndex=item==null?0:Array.IndexOf(DeskStore.ApplicationStatuses,item.Status);
        string[] labels={"公司","岗位（可留空）","投递状态","投递日期 · yyyy-MM-dd（可留空）","备注"};FrameworkElement[] fields={company,role,status,date,notes};
        for(int i=0;i<fields.Length;i++){p.Children.Add(UI.Text(labels[i],12,UI.Muted));fields[i].Margin=new Thickness(0,5,0,12);p.Children.Add(fields[i]);}
        var actions=new WrapPanel();actions.Children.Add(UI.Button("保存投递记录",()=>{try{store.SaveApplication(trackId,new JobApplication{Id=item==null?null:item.Id,Company=company.Text,Role=role.Text,Status=DeskStore.ApplicationStatuses[status.SelectedIndex],AppliedDate=date.Text,Notes=notes.Text});w.Close();}catch(Exception ex){UI.Toast(w,ex.Message);}},true));actions.Children.Add(UI.Button("取消",()=>w.Close()));p.Children.Add(actions);
        w.Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};w.ShowDialog();
    }
}
}
