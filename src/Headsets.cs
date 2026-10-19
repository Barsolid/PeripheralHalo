// SPDX-License-Identifier: GPL-2.0-only
// Independent C# implementation of the wire formats documented by HaloBattery (MIT).
// See THIRD_PARTY_NOTICES.md and docs/protocols.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace PeripheralHalo
{
    public interface IHidTransport : IDisposable
    {
        HidEndpoint Endpoint { get; }
        bool Write(byte[] data);
        byte[] Read(int timeout);
        bool FeatureSet(byte[] data);
        bool FeatureGet(byte[] data);
    }
    public static class HeadsetCatalog
    {
        public static readonly Dictionary<ushort,string> Logitech=new Dictionary<ushort,string> {
            {0x0A66,"Logitech G533"},{0x0AC4,"Logitech G535"},{0x0A5C,"Logitech G633"},
            {0x0A89,"Logitech G635"},{0x0A5B,"Logitech G933"},{0x0A87,"Logitech G935"},
            {0x0AB5,"Logitech G733"},{0x0AFE,"Logitech G733"},{0x0B1F,"Logitech G733"},
            {0x0AA7,"Logitech G PRO"},{0x0AAA,"Logitech G PRO X"},{0x0ABA,"Logitech G PRO X"},
            {0x0AFB,"Logitech G PRO X 2"},{0x0AFC,"Logitech G PRO X 2"},
            {0x0AF7,"Logitech G PRO X 2 LIGHTSPEED"}
        };
        public static readonly Dictionary<ushort,string> Razer=new Dictionary<ushort,string> {
            {0x0528,"Razer BlackShark V2 Pro (2020)"},
            {0x0555,"Razer BlackShark V2 Pro (2023)"},
            {0x0556,"Razer BlackShark V2 Pro (2023)"},
            {0x053A,"Razer Barracuda Pro"}
        };
        public static bool LogitechCandidate(HidEndpoint e)
        {
            return (e.UsagePage==0xFF00 || e.UsagePage==0xFF43 ||
                (e.Pid==0x0AC4 && e.UsagePage==0x000C && e.Usage==1)) &&
                e.OutputLength>=7 && e.OutputLength<=64 && e.InputLength>=7 && e.InputLength<=64;
        }
        public static bool RazerCandidate(HidEndpoint e)
        {
            if(e.Pid==0x0528) return e.UsagePage==0xFF00 && e.Usage==1 && e.FeatureLength>=64 && e.FeatureLength<=65;
            return (e.UsagePage==0xFF00 || (e.Pid!=0x053A && e.UsagePage==0xFF14)) &&
                e.OutputLength>=64 && e.OutputLength<=65 && e.InputLength>=64 && e.InputLength<=65;
        }
    }
    public static class HeadsetCodec
    {
        public static byte[] PaFrame(bool barracuda,byte command,bool remote,bool on)
        {
            var b=new byte[64];b[0]=(byte)(barracuda?1:2);b[1]=0x80;b[2]=(byte)(remote?7:8);
            int marker=barracuda?3:5;b[marker]=0x50;b[marker+1]=0x41;b[marker+2]=(byte)(remote?0x0E:8);
            if(barracuda) b[6]=8;
            int type=barracuda?7:9;b[type]=(byte)(remote?2:3);b[type+1]=remote?(byte)0xE1:command;
            if(remote) b[type+2]=(byte)(on?1:0);
            return b;
        }
        public static byte[] PaPayload(byte[] r,bool barracuda,byte command)
        {
            if(r==null || r.Length<17 || r.Length>65 || r[0]!=(barracuda?1:2) || r[1]!=0x80) return null;
            if(barracuda && (r[3]!=0x50 || r[4]!=0x49)) return null;
            int c=barracuda?13:12;
            if(r[c]!=command || (r[c+1]!=1 && (!barracuda || r[c+1]!=2))) return null;
            int n=r[c+2];if(n<1 || n>32 || c+3+n>r.Length) return null;
            return r.Skip(c+3).Take(n).ToArray();
        }
        public static byte[] BlackShark2020Request(int length)
        {
            var b=new byte[length];byte[] header={0xFF,0x0A,0,0xFD,4,0x12,0xF1,2,5};
            Array.Copy(header,b,header.Length);return b;
        }
        public static bool BlackShark2020(byte[] r,DeviceReading d)
        {
            byte[] prefix={0xFF,0x0F,5,0xFE,0x12,4,0x1F,8,5};
            if(r==null || r.Length<64 || r.Length>65 || !r.Take(prefix.Length).SequenceEqual(prefix)) return false;
            int mv=(r[12]<<8)|r[13];if(mv==0) return false;
            byte state=r[11];d.Charge=state==9?ChargeState.Charging:state==6?ChargeState.Full:
                (state==1||state==2||state==3)?ChargeState.Discharging:ChargeState.Unknown;
            d.Battery=state!=9 && r[14]<=100?(int?)r[14]:null;
            d.Estimated=d.Battery.HasValue;
            d.Source="BlackShark 2020 · 设备电量档位";
            d.Detail=state==9?"已确认充电；此型号充电时的电量数值不可靠，因此隐藏百分比。":
                "设备以较粗档位上报电量，数值为近似值；不是精细百分比。";
            return true;
        }
        public static byte[] CenturionFrame(params byte[] payload)
        {
            if(payload.Length>61) throw new ArgumentException("Centurion payload too large");
            var b=new byte[64];b[0]=0x51;b[1]=(byte)(payload.Length+1);Array.Copy(payload,0,b,3,payload.Length);return b;
        }
        public static byte[] CenturionPayload(byte[] r)
        {
            if(r==null || r.Length<4 || r[0]!=0x51 || r[1]<2 || r[1]>62 || r[1]+2>r.Length) return null;
            return r.Skip(3).Take(r[1]-1).ToArray();
        }
        public static bool CenturionBattery(byte[] data,DeviceReading d)
        {
            if(data==null || data.Length<3 || data[0]>100) return false;
            d.Battery=data[0];d.Charge=data[2]==3?ChargeState.Full:
                (data[2]==1||data[2]==2)?ChargeState.Charging:data[2]==0?ChargeState.Discharging:ChargeState.Unknown;
            d.Estimated=false;d.Source="Logitech Centurion · 0x0104";d.Detail="设备直接报告电量。";return true;
        }
        public static bool CenturionLegacy(byte[] r,DeviceReading d)
        {
            if(r==null || r.Length<13 || r[0]!=0x51 || r[1]!=0x0B || r[8]!=4 || r[10]>100) return false;
            d.Battery=r[10];d.Charge=r[12]==2?ChargeState.Charging:r[12]==3?ChargeState.Full:
                r[12]==0?ChargeState.Discharging:ChargeState.Unknown;
            d.Source="Logitech Centurion · 旧固件查询";d.Detail="设备直接报告电量。";return true;
        }
    }
    public sealed class HeadsetReader
    {
        private readonly Action<int> delay;
        private readonly Dictionary<string,byte[]> centurion=new Dictionary<string,byte[]>();
        public HeadsetReader(Action<int> wait=null) {delay=wait ?? new Action<int>(Thread.Sleep);}
        public void ClearCache() {centurion.Clear();}
        private static bool Send(IHidTransport t,byte[] packet)
        {
            if(t.Endpoint.OutputLength<packet.Length || t.Endpoint.OutputLength>65) return false;
            var padded=new byte[t.Endpoint.OutputLength];Array.Copy(packet,padded,packet.Length);return t.Write(padded);
        }
        private static void Drain(IHidTransport t) {for(int n=0;n<8;n++) if(t.Read(5)==null) break;}
        private byte[] QueryPa(IHidTransport t,bool barracuda,byte command)
        {
            for(int attempt=0;attempt<3;attempt++) {
                Drain(t);
                if(!barracuda) {if(!Send(t,HeadsetCodec.PaFrame(false,0,true,true))) return null;delay(35);}
                if(!Send(t,HeadsetCodec.PaFrame(barracuda,command,false,false))) return null;
                for(int read=0;read<4;read++) {
                    byte[] payload=HeadsetCodec.PaPayload(t.Read(75),barracuda,command);
                    if(payload!=null) return payload;
                }
            }
            return null;
        }
        public bool ReadPa(IHidTransport t,bool barracuda,DeviceReading d)
        {
            bool remote=false;
            try {
                Drain(t);
                remote=Send(t,HeadsetCodec.PaFrame(barracuda,0,true,true));if(!remote) return false;
                delay(40);
                var battery=QueryPa(t,barracuda,0x21);
                if(battery==null || battery[0]>100) return false;
                var charge=QueryPa(t,barracuda,0x2A);
                d.Battery=battery[0];d.Estimated=false;
                d.Charge=charge==null||charge[0]>1?ChargeState.Unknown:charge[0]==1?ChargeState.Charging:ChargeState.Discharging;
                d.Source=barracuda?"Razer Barracuda PA · 0x21":"Razer BlackShark PA · 0x21";
                d.Detail="设备报告电量；未收到充电回复时保留充电状态未知。";return true;
            } finally {if(remote) Send(t,HeadsetCodec.PaFrame(barracuda,0,true,false));}
        }
        public bool ReadBlackShark2020(IHidTransport t,DeviceReading d)
        {
            if(!t.FeatureSet(HeadsetCodec.BlackShark2020Request(t.Endpoint.FeatureLength))) return false;
            for(int attempt=0;attempt<4;attempt++) {
                delay(100);var r=new byte[t.Endpoint.FeatureLength];r[0]=0xFF;
                if(t.FeatureGet(r) && HeadsetCodec.BlackShark2020(r,d)) return true;
            }
            return false;
        }
        private static byte[] Direct(IHidTransport t,byte feature,byte function,params byte[] args)
        {
            byte fn=(byte)((function&0xF0)|1);
            if(!Send(t,HeadsetCodec.CenturionFrame(new byte[]{feature,fn}.Concat(args).ToArray()))) return null;
            for(int n=0;n<12;n++) {
                var p=HeadsetCodec.CenturionPayload(t.Read(100));if(p==null || p.Length<2) continue;
                if(p[0]==0xFF && p.Length>=3 && p[1]==feature && p[2]==fn) return null;
                if(p[0]==feature && p[1]==fn) return p.Skip(2).ToArray();
            }
            return null;
        }
        private static byte[] Bridge(IHidTransport t,byte bridge,byte feature,byte function,params byte[] args)
        {
            byte fn=(byte)((function&0xF0)|1);var sub=new byte[]{0,feature,fn}.Concat(args).ToArray();
            var payload=new byte[]{bridge,0x11,0,(byte)sub.Length}.Concat(sub).ToArray();
            if(!Send(t,HeadsetCodec.CenturionFrame(payload))) return null;
            for(int n=0;n<15;n++) {
                var p=HeadsetCodec.CenturionPayload(t.Read(100));
                if(p==null || p.Length<7 || p[0]!=bridge || p[1]!=0x10 || p[4]!=0) continue;
                int length=(p[2]<<8)|p[3];if(length<3 || length>p.Length-4) continue;
                if(p[5]==0xFF && p[6]==feature) return null;
                if(p[5]==feature && p[6]==fn) return p.Skip(7).Take(length-3).ToArray();
            }
            return null;
        }
        public bool ReadCenturion(IHidTransport t,DeviceReading d)
        {
            Drain(t);byte[] known;
            if(!centurion.TryGetValue(t.Endpoint.Key,out known)) {
                var bridge=Direct(t,0,0,0,3);
                if(bridge==null || bridge.Length<1 || bridge[0]==0) return Legacy(t,d);
                var battery=Bridge(t,bridge[0],0,0,1,4);
                if(battery==null) return false;
                known=new byte[]{bridge[0],battery.Length==0?(byte)0:battery[0]};
                centurion[t.Endpoint.Key]=known;
            }
            if(known[1]==0) return Legacy(t,d);
            return HeadsetCodec.CenturionBattery(Bridge(t,known[0],known[1],0),d);
        }
        private static bool Legacy(IHidTransport t,DeviceReading d)
        {
            byte[] request={0x51,8,0,3,0x1A,0,3,0,4,0x0A};var packet=new byte[64];Array.Copy(request,packet,request.Length);
            if(!Send(t,packet)) return false;
            for(int n=0;n<8;n++) {
                var r=t.Read(150);if(r==null) continue;
                if(r.Length>=7 && r[0]==0x51 && r[1]==5 && r[6]==0) return false;
                if(HeadsetCodec.CenturionLegacy(r,d)) return true;
            }
            return false;
        }
        public DeviceReading Razer(List<HidEndpoint> endpoints,Func<HidEndpoint,DeviceReading> create)
        {
            var first=endpoints[0];var d=create(first);d.Name=HeadsetCatalog.Razer[first.Pid];d.Kind=DeviceKind.Headset;
            d.Source="Razer 耳机 · "+first.Pid.ToString("X4");
            d.Detail="已检测到接收器；尚未获得有效电量回复，可能休眠、关机或驱动占用。";
            foreach(var e in endpoints.Where(HeadsetCatalog.RazerCandidate)) {
                try {using(var t=new HidTransport(e,first.Pid==0x0528)) {
                    bool ok=first.Pid==0x0528?ReadBlackShark2020(t,d):ReadPa(t,first.Pid==0x053A,d);
                    if(ok) return d;
                }} catch(InvalidOperationException) { }
            }
            return d;
        }
        public DeviceReading LogitechCenturion(List<HidEndpoint> endpoints,Func<HidEndpoint,DeviceReading> create)
        {
            var d=create(endpoints[0]);d.Name=HeadsetCatalog.Logitech[0x0AF7];d.Kind=DeviceKind.Headset;
            foreach(var e in endpoints.Where(x=>x.UsagePage==0xFFA0 && x.Usage==1 && x.OutputLength>=64 && x.OutputLength<=65 && x.InputLength>=64 && x.InputLength<=65)) {
                try {using(var t=new HidTransport(e)) {if(ReadCenturion(t,d)) return d;}} catch(InvalidOperationException) { }
            }
            d.Source="Logitech Centurion";d.Detail="接收器已检测到，但耳机未返回有效电量；不沿用历史电量。";return d;
        }
    }
}
