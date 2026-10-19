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
        public bool IsEnglish { get { return Config.Language=="en"; } }
        public string Ui(string chinese,string english) { return IsEnglish?english:chinese; }
        public string DeviceText(string text)
        {
            if(!IsEnglish || String.IsNullOrEmpty(text)) return text;
            var pairs=new[] {
                new[] {"设备报告电量，0–255 数值换算为百分比。","Battery reported by the device; raw value converted to a percentage."},
                new[] {"已检测到 USB 接口；无法确认无线设备是否在线。可能处于休眠、未配对，或当前型号尚未适配。","USB interface detected. The wireless device may be asleep, unpaired, or unsupported; its online status cannot be confirmed."},
                new[] {"已检测到 G435 接收器，不能据此判断耳机是否在线。LIGHTSPEED 精确电量读取暂未实现。按耳机电源键：绿灯 31–100%，红灯 15–30%，闪红灯低于 15%。","G435 receiver detected; this does not confirm the headset is online. Exact LIGHTSPEED battery reporting is not implemented. Press the headset power button: green 31–100%, red 15–30%, flashing red below 15%."},
                new[] {"当前雷蛇 USB 型号尚无已登记电量协议。设备 HID 编号：","No battery protocol is registered for this Razer USB model. HID ID: "},
                new[] {"设备以较粗档位上报电量，数值为近似值；不是精细百分比。","The device reports a coarse battery level. This is an estimate, not an exact percentage."},
                new[] {"已确认充电；此型号充电时的电量数值不可靠，因此隐藏百分比。","Charging detected. This model reports unreliable battery values while charging, so the percentage is hidden."},
                new[] {"设备直接报告电量。","Battery reported directly by the device."},
                new[] {"设备报告电量；未收到充电回复时保留充电状态未知。","Battery reported by the device. Charging status remains unknown when no charging response is received."},
                new[] {"已检测到接收器；尚未获得有效电量回复，可能休眠、关机或驱动占用。","Receiver detected, but no valid battery response was received. The device may be asleep, off, or claimed by another driver."},
                new[] {"接收器已检测到，但耳机未返回有效电量；不沿用历史电量。","Receiver detected, but the headset returned no valid battery reading. Previous values are not reused."},
                new[] {"仅显示当前已连接的蓝牙设备。电量由 Windows 缓存提供，更新速度取决于耳机上报；此接口不提供充电状态。","Only currently connected Bluetooth devices are shown. Windows provides a cached battery level; update frequency depends on the headset. This interface does not report charging status."},
                new[] {"电压换算的百分比为估算值，可能与厂商软件不同。","Voltage-based percentage is an estimate and may differ from the manufacturer's software."},
                new[] {"当前型号尚未适配","This model is not supported yet"},
                new[] {"读取失败：","Read failed: "},
                new[] {"档位估算","coarse-level estimate"},
                new[] {"设备报告","device report"},
                new[] {"电压估算","voltage estimate"},
                new[] {"暂无可用电量协议","no battery protocol available"},
                new[] {"演示数据","Demo data"},
                new[] {"雷蛇 键盘 HID","Razer keyboard HID"},
                new[] {"雷蛇 耳机 PA","Razer headset PA"},
                new[] {"耳机电量","Headset battery"},
                new[] {"USB 有线","Wired USB"},
new[] {"G435 的 LIGHTSPEED 精确电量读取暂未实现。可按耳机电源键查看灯色：绿灯 31–100%，红灯 15–30%，闪红灯低于 15%。","Exact G435 LIGHTSPEED battery reporting is not implemented. Press the headset power button to check its LED: green 31–100%, red 15–30%, flashing red below 15%."},
                new[] {"连接的无线设备尚无可读电量。","The connected wireless device has no readable battery value."},
                new[] {"没有检测到支持的外设。","No supported peripherals detected."},
                new[] {"点击查看所有设备","Click to view all devices"},
                new[] {"电量暂不可用","Battery unavailable"},
                new[] {"充电状态未知","Charging status unknown"}
            };
            foreach(var pair in pairs) text=text.Replace(pair[0],pair[1]);
            return text;
        }
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
        public void Save() {try {Config.Save();}catch(Exception ex) {MessageBox.Show(panel,ex.Message,Ui("设置保存失败","Settings save failed"));}}
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
                if(!Exiting && !Demo && version==modeVersion) {Devices=new List<DeviceReading>();UpdateTray();MessageBox.Show(panel,Ui("设备扫描未完成：","Device scan failed: ")+ex.Message,"PeripheralHalo");}
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
                    var menu=new Forms.ContextMenuStrip();menu.Items.Add(Ui("打开外设光环","Open PeripheralHalo"),null,delegate {panel.OpenPanel();});menu.Items.Add(Ui("刷新电量","Refresh batteries"),null,async delegate {await Refresh(true);});menu.Items.Add(Ui("显示设置","Settings"),null,delegate {panel.OpenPanel(true);});menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add(Ui("退出","Exit"),null,delegate {ExitApp();});entry.Icon.ContextMenuStrip=menu;tray[d.Id]=entry;
                }
                entry.Icon.ContextMenuStrip.Items[0].Text=Ui("打开外设光环","Open PeripheralHalo");entry.Icon.ContextMenuStrip.Items[1].Text=Ui("刷新电量","Refresh batteries");entry.Icon.ContextMenuStrip.Items[2].Text=Ui("显示设置","Settings");entry.Icon.ContextMenuStrip.Items[4].Text=Ui("退出","Exit");string tooltip=(Demo?Ui("[演示] ","[Demo] "):"")+d.Name+" · "+d.Value+" · "+d.StateFor(IsEnglish);entry.Icon.Text=tooltip.Length>63?tooltip.Substring(0,63):tooltip;
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
                    var icon=tray.Values.FirstOrDefault();if(icon!=null) icon.Icon.ShowBalloonTip(5000,Ui("外设电量偏低","Low peripheral battery"),d.Name+Ui(" 剩余 "," · ")+d.Value,Forms.ToolTipIcon.Warning);
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
            if(dialog.ShowDialog(panel)==true) {try {WriteDiagnostics(dialog.FileName,Demo?new List<DeviceReading>():Devices);MessageBox.Show(panel,Ui("诊断已保存。诊断文件不会自动上传。","Diagnostics saved locally. The file is not uploaded."),"PeripheralHalo");}catch(Exception ex) {MessageBox.Show(panel,ex.Message,Ui("导出失败","Export failed"));}}
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
                    var app=new HaloApp(true,true);if(args.Length>2 && args[2]=="en") app.Config.Language="en";app.Config.Animations=false;var window=(MainWindow)app.MainWindow;
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
