// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace PeripheralHalo
{
    public enum DeviceKind { Mouse, Headset, Keyboard, Other }
    public enum ChargeState { Unknown, Discharging, Charging, Full, Fault }
    public sealed class DeviceReading
    {
        public string Id = "";
        public string Name = "";
        public string Brand = "";
        public string Connection = "2.4 GHz / USB";
        public DeviceKind Kind;
        public int? Battery;
        public ChargeState Charge;
        public string Source = "";
        public string Detail = "";
        public bool Estimated;
        public bool IsDemo;
        public DateTime ObservedAt = DateTime.Now;
        public string Value { get { return Battery.HasValue ? (Estimated ? "≈" : "") + Battery.Value + "%" : "—"; } }
        public string State { get { return Charge == ChargeState.Charging ? "正在充电" : Charge == ChargeState.Full ? "已充满" : Charge == ChargeState.Fault ? "充电异常" : Battery.HasValue ? (Estimated ? "估算电量" : "电量已读取") : "暂无读数"; } }
        public static DeviceKind InferKind(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (new[] {"headset", "headphone", "g435", "g533", "g535", "g733", "g933", "g935", "kraken", "blackshark", "barracuda", "耳机"}.Any(n.Contains)) return DeviceKind.Headset;
            if (new[] {"keyboard", "keys", "键盘"}.Any(n.Contains)) return DeviceKind.Keyboard;
            if (new[] {"mouse", "viper", "deathadder", "basilisk", "naga", "orochi", "cobra", "superlight", "g pro", "g502", "g903", "g305", "g304", "g703", "g603", "g309", "master", "anywhere", "鼠标"}.Any(n.Contains)) return DeviceKind.Mouse;
            return DeviceKind.Other;
        }
    }
    public static class BatteryPolicy
    {
        public static string Color(int? level) { return !level.HasValue ? "#8995AA" : level >= 75 ? "#63E6B0" : level >= 50 ? "#F3D778" : level > 20 ? "#F4A563" : "#FF7285"; }
        public static string Label(int? level) { return !level.HasValue ? "未知" : level >= 75 ? "充足" : level >= 50 ? "良好" : level > 20 ? "偏低" : "低电量"; }
        public static int VoltagePercent(int mv)
        {
            int[] volts = { 3500, 3579, 3646, 3671, 3717, 3751, 3778, 3811, 3859, 3922, 3989, 4067, 4186 };
            int[] levels = { 0, 2, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
            if (mv <= volts[0]) return 0;
            for (int i = 1; i < volts.Length; i++) if (mv <= volts[i]) return (int)Math.Round(levels[i-1] + (mv-volts[i-1]) * (levels[i]-levels[i-1]) / (double)(volts[i]-volts[i-1]));
            return 100;
        }
        public static string StableId(string value)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value.ToLowerInvariant()))).Replace("-", "").Substring(0, 20);
        }
    }
    [DataContract]
    public sealed class Settings
    {
        [DataMember] public List<string> Pinned = new List<string>();
        [DataMember] public bool PinAll = true;
        [DataMember] public bool Animations = true;
        [DataMember] public bool Notifications = true;
        [DataMember] public int PollSeconds = 30;
        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeripheralHalo"); } }
        public static Settings Load()
        {
            try { using (var f = File.OpenRead(Path.Combine(Folder, "settings.json"))) { var s=(Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(f); s.Pinned=s.Pinned ?? new List<string>(); s.PollSeconds=Math.Max(15, Math.Min(300,s.PollSeconds)); return s; } }
            catch { return new Settings(); }
        }
        public void Save()
        {
            Directory.CreateDirectory(Folder);
            string path=Path.Combine(Folder,"settings.json"), temp=path+".tmp";
            using (var f=File.Create(temp)) new DataContractJsonSerializer(typeof(Settings)).WriteObject(f,this);
            if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
        }
        public static bool StartupEnabled
        {
            get { using (var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key != null && key.GetValue("PeripheralHalo") != null; }
            set { using (var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if(value) key.SetValue("PeripheralHalo", "\""+System.Reflection.Assembly.GetExecutingAssembly().Location+"\" --background"); else key.DeleteValue("PeripheralHalo",false); } }
        }
    }
    public static class DemoDevices
    {
        public static List<DeviceReading> Create()
        {
            return new List<DeviceReading> {
                new DeviceReading {Id="demo-mouse",Name="G PRO X SUPERLIGHT 2",Brand="LOGITECH",Kind=DeviceKind.Mouse,Battery=86,Charge=ChargeState.Discharging,Source="演示数据 · HID++ 0x1004",IsDemo=true},
                new DeviceReading {Id="demo-razer",Name="Razer Viper V3 Pro",Brand="RAZER",Kind=DeviceKind.Mouse,Battery=62,Charge=ChargeState.Charging,Source="演示数据 · Razer HID",IsDemo=true},
                new DeviceReading {Id="demo-low",Name="MX Master 3S",Brand="LOGITECH",Kind=DeviceKind.Mouse,Battery=18,Charge=ChargeState.Discharging,Source="演示数据 · HID++ 0x1004",IsDemo=true},
                new DeviceReading {Id="demo-headset",Name="G435 LIGHTSPEED",Brand="LOGITECH",Kind=DeviceKind.Headset,Source="演示设备 · 无可用电量协议",Detail="G435 的 LIGHTSPEED 精确电量读取暂未实现。可按耳机电源键查看灯色：绿灯 31–100%，红灯 15–30%，闪红灯低于 15%。",IsDemo=true}
            };
        }
    }
}
