using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace DayDesk {
public sealed class AppInstance : IDisposable {
    readonly Mutex mutex;
    readonly string property;
    readonly uint message;
    bool owned;
    MainWindow window;
    HwndSource source;
    IntPtr handle;
    public bool IsOwner { get { return owned; } }
    public AppInstance(string dataDirectory) {
        string path=Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        string key=BitConverter.ToString(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()))).Replace("-","");
        mutex=new Mutex(false,"Local\\DayDesk_"+key);
        try { owned=mutex.WaitOne(0); } catch(AbandonedMutexException) { owned=true; }
        property="DayDesk.Activate."+key;message=RegisterWindowMessage(property);
    }
    public void Attach(MainWindow main) {
        if(!owned)throw new InvalidOperationException("Only the running instance may register its window.");
        window=main;window.SourceInitialized+=OnSourceInitialized;
        if(new WindowInteropHelper(window).Handle!=IntPtr.Zero)Register();
    }
    void OnSourceInitialized(object sender,EventArgs args){Register();}
    void Register() {
        if(source!=null)return;
        handle=new WindowInteropHelper(window).Handle;source=HwndSource.FromHwnd(handle);source.AddHook(Receive);
        if(!SetProp(handle,property,new IntPtr(1)))throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    IntPtr Receive(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled) {
        if((uint)msg==message){handled=true;window.Dispatcher.BeginInvoke(new Action(window.Reveal));}
        return IntPtr.Zero;
    }
    public bool ActivateExisting() {
        var timer=Stopwatch.StartNew();
        do {
            IntPtr target=IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd,IntPtr unused){if(GetProp(hwnd,property)!=IntPtr.Zero){target=hwnd;return false;}return true;},IntPtr.Zero);
            if(target!=IntPtr.Zero) {
                uint pid;GetWindowThreadProcessId(target,out pid);AllowSetForegroundWindow(pid);
                if(PostMessage(target,message,IntPtr.Zero,IntPtr.Zero))return true;
            }
            Thread.Sleep(80);
        }while(timer.ElapsedMilliseconds<3000);
        return false;
    }
    public void Dispose() {
        if(window!=null)window.SourceInitialized-=OnSourceInitialized;
        if(handle!=IntPtr.Zero)RemoveProp(handle,property);
        if(source!=null&&!source.IsDisposed)source.RemoveHook(Receive);
        if(owned){owned=false;mutex.ReleaseMutex();}mutex.Dispose();
    }
    delegate bool EnumProc(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetProp(IntPtr hwnd,string name,IntPtr value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr hwnd,string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr hwnd,string name);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
    [DllImport("user32.dll",SetLastError=true)] static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wp,IntPtr lp);
}
public sealed class DesktopTray : IDisposable {
    readonly Forms.NotifyIcon tray;
    readonly Forms.ContextMenuStrip menu;
    readonly System.Drawing.Icon icon;
    public DesktopTray(MainWindow window) {
        icon=System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName);
        menu=new Forms.ContextMenuStrip();
        menu.Items.Add("展开日程",null,(s,e)=>window.Dispatcher.BeginInvoke(new Action(window.Reveal)));
        menu.Items.Add("退出日程",null,(s,e)=>window.Dispatcher.BeginInvoke(new Action(()=>window.Close())));
        tray=new Forms.NotifyIcon{Text="日程 · DayDesk（点击展开）",Icon=icon,ContextMenuStrip=menu,Visible=true};
        tray.MouseClick+=(s,e)=>{if(e.Button==Forms.MouseButtons.Left)window.Dispatcher.BeginInvoke(new Action(window.Reveal));};
    }
    public void Dispose(){tray.Visible=false;tray.Dispose();menu.Dispose();if(icon!=null)icon.Dispose();}
}
}
