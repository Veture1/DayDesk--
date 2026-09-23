using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace DayDesk {
public static class UI {
    public static Brush Ink = Brush("#252D35"), Muted = Brush("#78817F"), Accent = Brush("#356858"), Surface = Brush("#FFFFFF"), Canvas = Brush("#F5F6F2");
    public static Brush Brush(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
    public static TextBlock Text(string text,double size=14,Brush brush=null,bool bold=false) {
        return new TextBlock { Text=text, FontSize=size, Foreground=brush??Ink, FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal, TextWrapping=TextWrapping.Wrap, VerticalAlignment=VerticalAlignment.Center, LineHeight=size*1.45 };
    }
    public static Button Button(string text,Action onClick,bool primary=false) {
        var b=new Button { Content=text, Padding=new Thickness(12,8,12,8), Margin=new Thickness(0,0,6,0), Background=primary?Accent:Brush("#EEF1EC"), Foreground=primary?Brush("#FFFFFF"):Ink, BorderThickness=new Thickness(0), Cursor=Cursors.Hand, FontSize=12, MinHeight=32 };
        var border=new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty,new CornerRadius(7)); border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
        var content=new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center); content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center); border.AppendChild(content);
        b.Template=new ControlTemplate(typeof(Button)){VisualTree=border};
        b.IsEnabledChanged+=(s,e)=>b.Opacity=b.IsEnabled?1:.42;
        b.MouseEnter+=(s,e)=>b.Opacity=b.IsEnabled?.8:.42; b.MouseLeave+=(s,e)=>b.Opacity=b.IsEnabled?1:.42;
        b.Click+=(s,e)=>{try{onClick();}catch(Exception ex){MessageBox.Show(ex.Message,"未能完成操作",MessageBoxButton.OK,MessageBoxImage.Warning);}};
        return b;
    }
    public static TextBox Input(string text="",bool multiline=false) {
        return new TextBox { Text=text??"", AcceptsReturn=multiline, TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap, FontSize=14, Padding=new Thickness(10,8,10,8), Foreground=Ink, Background=Surface, BorderBrush=Brush("#DCE2DB"), BorderThickness=new Thickness(1), VerticalContentAlignment=VerticalAlignment.Center, MinHeight=36, VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden };
    }
    public static Border Card(UIElement child,string bg="#FFFFFF",double padding=16) { return new Border { Child=child,Background=Brush(bg),CornerRadius=new CornerRadius(14),Padding=new Thickness(padding),BorderBrush=Brush("#E7EBE3"),BorderThickness=new Thickness(1) }; }
    public static StackPanel Stack(params UIElement[] children) { var p=new StackPanel(); foreach(var c in children) p.Children.Add(c); return p; }
    public static void Toast(Window owner,string message) { MessageBox.Show(owner,message,"日程 · DayDesk",MessageBoxButton.OK,MessageBoxImage.Information); }
    public static Window Dialog(Window owner,string title,double width=600,double height=640) {
        var w=new Window {Owner=owner,Title=title,Width=Math.Min(width,Math.Max(420,owner.ActualWidth-32)),Height=Math.Min(height,SystemParameters.WorkArea.Height-60),MinWidth=380,MinHeight=260,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Canvas,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=14,ShowInTaskbar=false};
        w.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape)w.Close();};return w;
    }
    public static string Prompt(Window owner,string title,string label,string initial="") {
        var w=Dialog(owner,title,500,240); var input=Input(initial); var p=Stack(Text(label,14,Muted),input); p.Margin=new Thickness(24); input.Margin=new Thickness(0,16,0,20);
        string result=null; var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        buttons.Children.Add(Button("取消",()=>w.Close())); var ok=Button("保存",()=>{result=input.Text.Trim();w.Close();},true);ok.IsDefault=true;buttons.Children.Add(ok);p.Children.Add(buttons);w.Content=p;w.Loaded+=(s,e)=>{input.Focus();input.SelectAll();};w.ShowDialog();return result;
    }
}
}
