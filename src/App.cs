// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

[assembly: AssemblyTitle("PeripheralHalo")]
[assembly: AssemblyDescription("外设光环 · 无线外设电量托盘")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
namespace PeripheralHalo
{
    internal sealed class TrayEntry : IDisposable
    {
        public Forms.NotifyIcon Icon;
        public System.Drawing.Icon Art;
        public void Dispose() {Icon.Visible=false;var menu=Icon.ContextMenuStrip;Icon.Dispose();if(menu!=null) menu.Dispose();if(Art!=null) Art.Dispose();}
    }
    public sealed class HaloApp : Application
    {
        public Settings Config;
        public List<DeviceReading> Devices=new List<DeviceReading>();
        public bool Demo,Busy,Exiting;
        public DateTime LastRefresh;
        private MainWindow panel;
        private readonly DeviceScanner scanner=new DeviceScanner();
        private readonly Dictionary<string,TrayEntry> tray=new Dictionary<string,TrayEntry>();
        private readonly HashSet<string> lowNotified=new HashSet<string>();
        private readonly HashSet<string> demoHidden=new HashSet<string>();
        private readonly DispatcherTimer poll=new DispatcherTimer(),animation=new DispatcherTimer();
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private int modeVersion;
        private EventWaitHandle showEvent;
        private RegisteredWaitHandle showWait;
        public HaloApp(bool demo,bool background)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;Config=Settings.Load();Demo=demo;
            if(demo) Devices=DemoDevices.Create();
            panel=new MainWindow(this);MainWindow=panel;
            Startup+=async delegate {UpdateTray();if(!background) panel.OpenPanel();if(!demo) await Refresh(false);};
            poll.Tick+=async delegate {await Refresh(false);};RestartPolling();
            animation.Interval=TimeSpan.FromMilliseconds(120);animation.Tick+=delegate {DrawIcons(true);};
            SystemEvents.PowerModeChanged+=PowerChanged;
            DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e) {try {Directory.CreateDirectory(Settings.Folder);File.AppendAllText(Path.Combine(Settings.Folder,"errors.log"),DateTime.Now.ToString("s")+" "+e.Exception+Environment.NewLine);}catch { }e.Handled=true;};
        }
        public void Listen(EventWaitHandle signal)
        {
            showEvent=signal;showWait=ThreadPool.RegisterWaitForSingleObject(showEvent,delegate {if(!Exiting) Dispatcher.BeginInvoke(new Action(delegate {panel.OpenPanel();}));},null,Timeout.Infinite,false);
        }
        private void PowerChanged(object sender,PowerModeChangedEventArgs e)
        {if(e.Mode==PowerModes.Resume && !Exiting) Dispatcher.BeginInvoke(new Action(async delegate {await Refresh(true);}));}
        public bool IsPinned(string id) {return Demo?!demoHidden.Contains(id):Config.PinAll || Config.Pinned.Contains(id);}
        public void Pin(string id,bool value)
        {
            if(Demo) {if(value) demoHidden.Remove(id);else demoHidden.Add(id);UpdateTray();panel.Render();return;}
            if(Config.PinAll) {Config.Pinned=Devices.Select(d=>d.Id).ToList();Config.PinAll=false;}
            if(value && !Config.Pinned.Contains(id)) Config.Pinned.Add(id);if(!value) Config.Pinned.Remove(id);
            Save();UpdateTray();panel.Render();
        }
        public void Save() {try {Config.Save();}catch(Exception ex) {MessageBox.Show(panel,ex.Message,"设置保存失败");}}
        public void RestartPolling() {poll.Stop();poll.Interval=TimeSpan.FromSeconds(Math.Max(15,Config.PollSeconds));poll.Start();}
        public void SetDemo(bool enabled)
        {
            modeVersion++;Demo=enabled;Devices=enabled?DemoDevices.Create():new List<DeviceReading>();LastRefresh=DateTime.MinValue;UpdateTray();panel.Render();if(!enabled) {var pending=Refresh(true);}
        }
        public async Task Refresh(bool clearCache)
        {
            if(Demo || Busy || Exiting) return;
            Busy=true;int version=modeVersion;panel.Render();
            try {
                var readings=await Task.Run(delegate {if(clearCache) scanner.ClearCache();return scanner.Scan();});
                if(!Exiting && !Demo && version==modeVersion) {Devices=readings;LastRefresh=DateTime.Now;UpdateTray();NotifyLow();}
            } catch(Exception ex) {
                if(!Exiting && !Demo && version==modeVersion) {Devices=new List<DeviceReading>();UpdateTray();MessageBox.Show(panel,"设备扫描未完成："+ex.Message,"PeripheralHalo");}
            } finally {Busy=false;if(!Exiting) panel.Render();}
            if(!Demo && !Exiting && version!=modeVersion) await Refresh(true);
        }
        public void UpdateTray()
        {
            if(Exiting) return;
            var desired=Devices.Where(d=>IsPinned(d.Id)).ToList();
            if(desired.Count==0) desired.Add(new DeviceReading {Id="launcher",Name="PeripheralHalo",Kind=DeviceKind.Other,Detail="点击查看所有设备"});
            foreach(var id in tray.Keys.Except(desired.Select(d=>d.Id)).ToList()) {tray[id].Dispose();tray.Remove(id);}
            foreach(var d in desired) {
                TrayEntry entry;
                if(!tray.TryGetValue(d.Id,out entry)) {
                    entry=new TrayEntry {Icon=new Forms.NotifyIcon()};
                    entry.Icon.MouseClick+=delegate(object sender,Forms.MouseEventArgs e) {if(e.Button==Forms.MouseButtons.Left) {if(panel.IsVisible) panel.Hide();else panel.OpenPanel();}};
                    var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开外设光环",null,delegate {panel.OpenPanel();});menu.Items.Add("刷新电量",null,async delegate {await Refresh(true);});menu.Items.Add("显示设置",null,delegate {panel.OpenPanel(true);});menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出",null,delegate {ExitApp();});entry.Icon.ContextMenuStrip=menu;tray[d.Id]=entry;
                }
                string tooltip=(Demo?"[演示] ":"")+d.Name+" · "+d.Value+" · "+d.State;entry.Icon.Text=tooltip.Length>63?tooltip.Substring(0,63):tooltip;
            }
            DrawIcons(false);
            if(Config.Animations && SystemParameters.ClientAreaAnimation && desired.Any(d=>d.Charge==ChargeState.Charging)) animation.Start();else animation.Stop();
        }
        private void DrawIcons(bool chargingOnly)
        {
            foreach(var pair in tray) {
                var d=Devices.FirstOrDefault(x=>x.Id==pair.Key) ?? new DeviceReading {Kind=DeviceKind.Other};
                if(chargingOnly && d.Charge!=ChargeState.Charging) continue;
                double phase=d.Charge==ChargeState.Charging?(Config.Animations && SystemParameters.ClientAreaAnimation?(1-Math.Cos(clock.Elapsed.TotalSeconds*Math.PI/1.3))/2:1):0;
                var art=TrayArt.Create(d,phase);var previous=pair.Value.Art;pair.Value.Icon.Icon=art;pair.Value.Art=art;pair.Value.Icon.Visible=true;if(previous!=null) previous.Dispose();
            }
        }
        private void NotifyLow()
        {
            var seen=new HashSet<string>(Devices.Select(d=>d.Id));lowNotified.RemoveWhere(id=>!seen.Contains(id));
            foreach(var d in Devices.Where(d=>!d.IsDemo && d.Battery.HasValue)) {
                if(d.Charge==ChargeState.Charging || d.Battery>=25) {lowNotified.Remove(d.Id);continue;}
                if(d.Battery<=20 && d.Charge!=ChargeState.Full && Config.Notifications && lowNotified.Add(d.Id)) {
                    var icon=tray.Values.FirstOrDefault();if(icon!=null) icon.Icon.ShowBalloonTip(5000,"外设电量偏低",d.Name+" 剩余 "+d.Value,Forms.ToolTipIcon.Warning);
                }
            }
        }
        public static void WriteDiagnostics(string path,List<DeviceReading> readings)
        {
            var lines=new List<string> {"PeripheralHalo 1.1.0 · 本地诊断",DateTime.Now.ToString("s"),"仅列出 USB 型号、接口能力与电量；不包含序列号或设备路径。",""};
            foreach(var e in HidDiscovery.Enumerate()) lines.Add(String.Format("USB {0:X4}:{1:X4} | {2} | usage {3:X4}:{4:X4} | input {5} output {6} feature {7}",e.Vid,e.Pid,e.Product,e.UsagePage,e.Usage,e.InputLength,e.OutputLength,e.FeatureLength));
            lines.Add("");foreach(var d in readings) lines.Add(d.Name+" | "+d.Value+" | "+d.State+" | "+d.Source+" | "+d.Detail);
            File.WriteAllLines(path,lines,Encoding.UTF8);
        }
        public void ExportDiagnostics()
        {
            var dialog=new SaveFileDialog {FileName="PeripheralHalo-diagnostics.txt",Filter="文本文件 (*.txt)|*.txt"};
            if(dialog.ShowDialog(panel)==true) {try {WriteDiagnostics(dialog.FileName,Demo?new List<DeviceReading>():Devices);MessageBox.Show(panel,"诊断已保存。诊断文件不会自动上传。","PeripheralHalo");}catch(Exception ex) {MessageBox.Show(panel,ex.Message,"导出失败");}}
        }
        public void ExitApp()
        {
            if(Exiting) return;Exiting=true;poll.Stop();animation.Stop();SystemEvents.PowerModeChanged-=PowerChanged;
            if(showWait!=null) showWait.Unregister(null);foreach(var e in tray.Values) e.Dispose();tray.Clear();panel.Close();Shutdown();
        }
        [STAThread]
        public static int Main(string[] args)
        {
            try {
                if(args.Length>1 && args[0]=="--export-icon") {using(var icon=TrayArt.Create(new DeviceReading {Battery=86,Kind=DeviceKind.Other},0)) using(var f=File.Create(args[1])) icon.Save(f);return 0;}
                if(args.Length>0 && args[0]=="--self-test") return SelfTests.Run(args.Length>1?args[1]:"test-results.txt");
                if(args.Length>0 && args[0]=="--diagnose") {WriteDiagnostics(args.Length>1?args[1]:"diagnostics.txt",new DeviceScanner().Scan());return 0;}
                if(args.Length>0 && args[0]=="--render-demo") {
                    var app=new HaloApp(true,true);app.Config.Animations=false;var window=(MainWindow)app.MainWindow;
                    app.Startup+=delegate {window.OpenPanel();var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(200)};timer.Tick+=delegate {timer.Stop();window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(args.Length>1?args[1]:"preview.png")) encoder.Save(f);app.ExitApp();};timer.Start();};
                    app.Run();return 0;
                }
                bool created;string key="Local\\PeripheralHalo-"+BatteryPolicy.StableId(Environment.UserName);
                using(var mutex=new Mutex(true,key,out created))
                using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,key+"-show")) {
                    if(!created) {signal.Set();return 0;}
                    var app=new HaloApp(args.Contains("--demo"),args.Contains("--background"));app.Listen(signal);
                    try {app.Run();}finally {app.ExitApp();mutex.ReleaseMutex();}return 0;
                }
            } catch(Exception ex) {MessageBox.Show("应用未能启动："+ex,"PeripheralHalo");return 1;}
        }
    }
}
