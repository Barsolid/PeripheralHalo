// SPDX-License-Identifier: GPL-2.0-only
// Protocol references and attribution: docs/protocols.md and THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace PeripheralHalo
{
    public static class ProtocolDecoder
    {
        private static ChargeState Charge(byte status) { return status==0?ChargeState.Discharging:status==3?ChargeState.Full:(status==1||status==2||status==4)?ChargeState.Charging:status==5||status==6?ChargeState.Fault:ChargeState.Unknown; }
        public static bool Logitech(ushort feature,byte[] p,DeviceReading d)
        {
            if(p==null || p.Length<7) return false;
            d.Detail="";
            if(feature==0x1004 || feature==0x1000) {
                if(p[4]>100) return false;
                d.Charge=Charge(p[6]);
                if(p[4]==0 && feature==0x1000) return false; // HID++ 0x1000: 0 is unknown.
                if(p[4]==0 && feature==0x1004 && p[5]!=0) {
                    int? coarse=p[5]==8?(int?)100:p[5]==4?60:p[5]==2?20:p[5]==1?5:(int?)null;
                    if(!coarse.HasValue) return false;
                    d.Battery=coarse; d.Estimated=true;
                } else d.Battery=p[4];
                d.Source="HID++ 0x"+feature.ToString("X4")+(d.Estimated?" · 档位估算":" · 设备报告");
                return true;
            }
            if(feature==0x1001 || feature==0x1F20) {
                int mv=p[4]*256+p[5];
                if(mv<3000 || mv>4500 || (feature==0x1F20 && (p[6]&1)==0)) return false;
                d.Battery=BatteryPolicy.VoltagePercent(mv);d.Estimated=true;
                d.Charge=feature==0x1F20?((p[6]&8)!=0?ChargeState.Fault:(p[6]&4)!=0?ChargeState.Full:(p[6]&2)!=0?ChargeState.Charging:ChargeState.Discharging):((p[6]&0x80)!=0?((p[6]&3)==3?ChargeState.Full:ChargeState.Charging):ChargeState.Discharging);
                d.Source="HID++ 0x"+feature.ToString("X4")+" · 电压估算";
                d.Detail=mv+" mV · 电压换算的百分比为估算值，可能与厂商软件不同。";
                return true;
            }
            return false;
        }
        public static byte[] RazerRequest(byte transaction,byte command)
        {
            var b=new byte[91];b[2]=transaction;b[6]=2;b[7]=7;b[8]=command;
            b[89]=Checksum(b);return b;
        }
        public static byte Checksum(byte[] b) {byte xor=0;for(int i=3;i<89;i++) xor^=b[i];return xor;}
        public static bool RazerValid(byte[] b,byte transaction,byte command)
        {
            return b!=null && b.Length==91 && b[0]==0 && b[1]==2 && b[2]==transaction && b[3]==0 && b[4]==0 && b[5]==0 && b[6]==2 && b[7]==7 && b[8]==command && b[89]==Checksum(b);
        }
    }
    internal sealed class LogitechBinding
    {
        public byte Slot, Index;
        public ushort Feature;
        public string Name;
        public DeviceKind Kind;
    }
    public sealed class DeviceScanner
    {
        private readonly Dictionary<string,List<LogitechBinding>> cache=new Dictionary<string,List<LogitechBinding>>();
        private readonly HeadsetReader headsets=new HeadsetReader();
        private readonly Dictionary<string,DateTime> cacheAge=new Dictionary<string,DateTime>();
        private static byte FeatureIndex(HidTransport t,byte slot,ushort feature)
        {
            byte[] r=t.Hidpp(slot,0,0,(byte)(feature>>8),(byte)feature,0);return r==null?(byte)0:r[4];
        }
        private static string ReadName(HidTransport t,byte slot,byte index,string fallback)
        {
            byte[] r=t.Hidpp(slot,index,0);if(r==null || r[4]==0 || r[4]>100) return fallback;
            int length=r[4];var bytes=new List<byte>();
            while(bytes.Count<length) {r=t.Hidpp(slot,index,1,(byte)bytes.Count);if(r==null) return fallback;bytes.AddRange(r.Skip(4).Take(Math.Min(length-bytes.Count,r.Length-4)));}
            return Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\0');
        }
        private static DeviceReading Base(HidEndpoint e)
        {
            string headsetName;
            string name=HeadsetCatalog.Logitech.TryGetValue(e.Pid,out headsetName) && e.Vid==0x046D?headsetName:String.IsNullOrWhiteSpace(e.Product)?(e.Vid==0x046D?"Logitech":"Razer")+" USB "+e.Pid.ToString("X4"):e.Product;
            return new DeviceReading {Id=e.Key,Name=name,Brand=e.Vid==0x046D?"LOGITECH":"RAZER",Kind=DeviceReading.InferKind(name),Source="USB HID · "+e.Vid.ToString("X4")+":"+e.Pid.ToString("X4"),Detail="已检测到 USB 接口；无法确认无线设备是否在线。可能处于休眠、未配对，或当前型号尚未适配。"};
        }
        private List<DeviceReading> LogitechDevice(List<HidEndpoint> endpoints)
        {
            HidEndpoint root=endpoints[0];var devices=new List<DeviceReading>();
            var fallback=Base(root);
            if(fallback.Name.ToLowerInvariant().Contains("g435") || root.Pid==0x0ACB) {
                fallback.Name="G435 LIGHTSPEED";fallback.Kind=DeviceKind.Headset;
                fallback.Detail="已检测到 G435 接收器，不能据此判断耳机是否在线。LIGHTSPEED 精确电量读取暂未实现。按耳机电源键：绿灯 31–100%，红灯 15–30%，闪红灯低于 15%。";
                fallback.Source="G435 · 暂无可用电量协议";devices.Add(fallback);return devices;
            }
            if(root.Pid==0x0AF7) return new List<DeviceReading> {headsets.LogitechCenturion(endpoints,Base)};
            if(HeadsetCatalog.Logitech.ContainsKey(root.Pid)) fallback.Kind=DeviceKind.Headset;
            var candidates=endpoints.Where(HeadsetCatalog.LogitechCandidate).OrderByDescending(e=>e.OutputLength>=20).ToList();
            List<LogitechBinding> bindings;
            if(cache.TryGetValue(root.Key,out bindings) && DateTime.UtcNow-cacheAge[root.Key]<TimeSpan.FromMinutes(5)) {
                foreach(var binding in bindings) {
                    foreach(var e in candidates) {
                        try {using(var t=new HidTransport(e)) {var d=Base(root);d.Id=root.Key+"-"+binding.Slot;d.Name=binding.Name;d.Kind=binding.Kind;
                            byte[] r=binding.Feature==0x000D || binding.Feature==0x0007?t.Register(binding.Slot,(byte)binding.Feature):t.Hidpp(binding.Slot,binding.Index,(byte)(binding.Feature==0x1004?1:0));
                            if(Decode(binding.Feature,r,d)) {devices.Add(d);break;}
                        }} catch(InvalidOperationException) { }
                    }
                }
                if(devices.Count==bindings.Count && devices.Count>0) return devices;
                devices.Clear();cache.Remove(root.Key);
            }
            bindings=new List<LogitechBinding>();
            foreach(var e in candidates) {
                try {using(var t=new HidTransport(e)) {
                    foreach(byte slot in HeadsetCatalog.Logitech.ContainsKey(root.Pid)?new byte[]{0xFF,1}:new byte[] {1,2,3,4,5,6,0xFF}) {
                        if(bindings.Any(b=>b.Slot==slot)) continue;
                        // Only probe battery features after a successful root response; sleeping slots cost one timeout.
                        byte[] ping=t.Hidpp(slot,0,1,0,0,0x5A);
                        if(ping==null) {
                            if(e.OutputLength<20) foreach(ushort reg in new ushort[]{0x000D,0x0007}) {
                                var d=Base(root);byte[] r=t.Register(slot,(byte)reg);
                                if(Decode(reg,r,d)) {d.Id=root.Key+"-"+slot;devices.Add(d);bindings.Add(new LogitechBinding {Slot=slot,Feature=reg,Name=d.Name,Kind=d.Kind});break;}
                            }
                            continue;
                        }
                        foreach(ushort f in new ushort[] {0x1004,0x1000,0x1001,0x1F20}) {
                            byte index=FeatureIndex(t,slot,f);if(index==0) continue;
                            var d=Base(root);byte[] r=t.Hidpp(slot,index,(byte)(f==0x1004?1:0));if(!Decode(f,r,d)) continue;
                            byte nameIndex=FeatureIndex(t,slot,0x0005);
                            if(nameIndex!=0) {
                                d.Name=ReadName(t,slot,nameIndex,d.Name);byte[] kind=t.Hidpp(slot,nameIndex,2);
                                d.Kind=kind!=null?(kind[4]==3?DeviceKind.Mouse:kind[4]==0||kind[4]==2?DeviceKind.Keyboard:DeviceReading.InferKind(d.Name)):DeviceReading.InferKind(d.Name);
                            }
                            if(HeadsetCatalog.Logitech.ContainsKey(root.Pid)) d.Kind=DeviceKind.Headset;
                            d.Id=root.Key+"-"+slot;devices.Add(d);bindings.Add(new LogitechBinding {Slot=slot,Index=index,Feature=f,Name=d.Name,Kind=d.Kind});break;
                        }
                    }
                }} catch(InvalidOperationException) { }
            }
            if(bindings.Count>0) {cache[root.Key]=bindings;cacheAge[root.Key]=DateTime.UtcNow;}
            else devices.Add(fallback);
            return devices;
        }
        private static bool Decode(ushort f,byte[] r,DeviceReading d)
        {
            if(f>0x000D) return ProtocolDecoder.Logitech(f,r,d);
            if(r==null || r.Length<7) return false;
            if(f==0x000D) {
                if(r[4]==0 || r[4]>100) return false;
                d.Battery=r[4];d.Charge=r[6]==0x50?ChargeState.Charging:r[6]==0x90?ChargeState.Full:r[6]==0x30?ChargeState.Discharging:ChargeState.Unknown;
            } else {
                int? coarse=r[4]==7?(int?)100:r[4]==5?60:r[4]==3?20:r[4]==1?5:(int?)null;
                if(!coarse.HasValue) return false;d.Battery=coarse;d.Estimated=true;d.Charge=(r[5]&0x21)!=0?ChargeState.Charging:ChargeState.Unknown;
            }
            d.Source="HID++ 1.0 · 寄存器 0x"+f.ToString("X2")+(d.Estimated?" · 档位估算":"");d.Detail="";return true;
        }
        private static byte[] RazerQuery(HidTransport t,byte transaction,byte command)
        {
            if(t.Endpoint.FeatureLength!=91) return null;
            byte[] request=ProtocolDecoder.RazerRequest(transaction,command);
            if(!t.FeatureSet(request)) return null;
            Thread.Sleep(80);
            for(int attempt=0;attempt<3;attempt++) {
                var response=new byte[91];
                if(t.FeatureGet(response) && ProtocolDecoder.RazerValid(response,transaction,command)) return response;
                Thread.Sleep(80);
            }
            return null;
        }
        private DeviceReading RazerDevice(List<HidEndpoint> endpoints)
        {
            if(HeadsetCatalog.Razer.ContainsKey(endpoints[0].Pid)) return headsets.Razer(endpoints,Base);
            var d=Base(endpoints[0]);RazerProfile profile;
            if(!RazerProfile.All.TryGetValue(endpoints[0].Pid,out profile)) {d.Detail="当前雷蛇 USB 型号尚无已登记电量协议。设备 HID 编号：1532:"+endpoints[0].Pid.ToString("X4");return d;}
            d.Name=profile.Name;d.Kind=profile.Kind;d.Connection=profile.Connection;
            foreach(var e in endpoints.Where(e=>e.FeatureLength==91).OrderBy(e=>profile.PreferredInterface>=0 && e.InterfaceNumber==profile.PreferredInterface?0:1)) {
                try {using(var t=new HidTransport(e,true)) {
                    byte[] battery=RazerQuery(t,profile.Transaction,0x80);if(battery==null) continue;
                    d.Battery=(int)Math.Round(battery[10]*100.0/255);d.Source="Razer HID · 0x07 / 0x80";d.Detail="设备报告电量，0–255 数值换算为百分比。";
                    byte[] charge=profile.ReplaceableBattery?null:RazerQuery(t,profile.Transaction,0x84);
                    d.Charge=profile.ReplaceableBattery?ChargeState.Discharging:charge==null||charge[10]>1?ChargeState.Unknown:charge[10]==1?ChargeState.Charging:ChargeState.Discharging;
                    return d;
                }} catch(InvalidOperationException) { }
            }
            return d;
        }
        public List<DeviceReading> Scan()
        {
            var all=HidDiscovery.Enumerate();var result=new List<DeviceReading>();
            foreach(var group in all.GroupBy(e=>e.Key)) {
                try {if(group.First().Vid==0x046D) result.AddRange(LogitechDevice(group.ToList()));else result.Add(RazerDevice(group.ToList()));}
                catch(Exception ex) {var d=Base(group.First());d.Detail="读取失败："+ex.GetType().Name;result.Add(d);}
            }
            result.AddRange(BluetoothBattery.Scan());
            return result.OrderByDescending(d=>d.Battery.HasValue).ThenBy(d=>d.Name).ToList();
        }
        public void ClearCache() {cache.Clear();cacheAge.Clear();headsets.ClearCache();}
    }
    public sealed class RazerProfile
    {
        public byte Transaction;
        public string Name;
        public bool ReplaceableBattery;
        public DeviceKind Kind;
        public int PreferredInterface=-1;
        public string Connection="2.4 GHz / USB";
        private static Dictionary<ushort,RazerProfile> all;
        public static Dictionary<ushort,RazerProfile> All {get {
            if(all==null) {all=new Dictionary<ushort,RazerProfile>();Load("razer-models.txt",DeviceKind.Mouse);Load("razer-keyboards.txt",DeviceKind.Keyboard);}
            return all;
        }}
        private static void Load(string resource,DeviceKind kind)
        {
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            using(var reader=new StreamReader(stream,Encoding.UTF8)) {
                string line;while((line=reader.ReadLine())!=null) {
                    var p=line.Split('|');if(p.Length<3) continue;
                    ushort pid=Convert.ToUInt16(p[0].Substring(2),16);byte tid=Convert.ToByte(p[1].Substring(2),16);
                    string n=p[2];
                    string clean=n.Replace("_WIRELESS","").Replace("_WIRED","").Replace("_RECEIVER","").Replace("_BLUETOOTH","").Replace("_ALT","").Replace('_',' ');
                    string title=System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(clean.ToLowerInvariant())
                        .Replace("Deathadder","DeathAdder").Replace("Blackwidow","BlackWidow").Replace("Deathstalker","DeathStalker")
                        .Replace("Hyperspeed","HyperSpeed").Replace("Tkl","TKL");
                    bool aa=new[] {"ATHERIS","BASILISK_X_HYPERSPEED","OROCHI_V2","DEATHADDER_V2_X","NAGA_V2_HYPERSPEED","VIPER_V3_HYPERSPEED","BASILISK_V3_X","BASILISK_MOBILE"}.Any(n.Contains);
                    all[pid]=new RazerProfile {Transaction=tid,Name="Razer "+title,ReplaceableBattery=aa,Kind=kind,
                        PreferredInterface=p.Length>=4?Int32.Parse(p[3]):-1,
                        Connection=n.Contains("_WIRELESS")||n.Contains("_RECEIVER")?"2.4 GHz":n.Contains("_BLUETOOTH")?"Bluetooth":n.Contains("_WIRED")?"USB 有线":"2.4 GHz / USB"};
                }
            }
        }
    }
}