// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
namespace PeripheralHalo
{
    internal sealed class ScriptedHid : IHidTransport
    {
        public HidEndpoint Endpoint {get;set;}
        public readonly List<byte[]> Writes=new List<byte[]>();
        public readonly Queue<byte[]> Replies=new Queue<byte[]>();
        public readonly Queue<byte[]> Features=new Queue<byte[]>();
        public Action<byte[]> Written;
        public bool RejectWrites,ThrowRead;
        public int Reads,FeatureWrites;
        public ScriptedHid() {Endpoint=new HidEndpoint {Path="fake",Container="fake",OutputLength=64,InputLength=64,FeatureLength=64};}
        public bool Write(byte[] b) {Writes.Add((byte[])b.Clone());if(RejectWrites) return false;if(Written!=null) Written(b);return true;}
        public byte[] Read(int timeout) {Reads++;if(ThrowRead) throw new InvalidOperationException("Disconnected");return Replies.Count==0?null:Replies.Dequeue();}
        public bool FeatureSet(byte[] b) {FeatureWrites++;Writes.Add((byte[])b.Clone());return !RejectWrites;}
        public bool FeatureGet(byte[] b) {if(Features.Count==0) return false;var f=Features.Dequeue();Array.Copy(f,b,Math.Min(f.Length,b.Length));return true;}
        public void Dispose() { }
    }
    public static class AdapterTests
    {
        private static byte[] Captured(string hex) {var b=new byte[64];var a=hex.Split(' ').Select(s=>Convert.ToByte(s,16)).ToArray();Array.Copy(a,b,a.Length);return b;}
        private static byte[] PaReply(bool barracuda,byte cmd,byte value)
        {
            var b=new byte[64];b[0]=(byte)(barracuda?1:2);b[1]=0x80;b[2]=10;
            if(barracuda) {b[3]=0x50;b[4]=0x49;b[5]=8;}
            int c=barracuda?13:12;b[c]=cmd;b[c+1]=1;b[c+2]=1;b[c+3]=value;return b;
        }
        private static ScriptedHid PaBus(bool barracuda,byte level,bool chargeReply)
        {
            var bus=new ScriptedHid();
            bus.Written=delegate(byte[] b) {
                int type=barracuda?7:9;
                if(b[type]==3 && b[type+1]==0x21) {
                    bus.Replies.Enqueue(PaReply(barracuda,0x2A,0)); // unrelated response must be ignored
                    bus.Replies.Enqueue(PaReply(barracuda,0x21,level));
                }
                if(chargeReply && b[type]==3 && b[type+1]==0x2A) bus.Replies.Enqueue(PaReply(barracuda,0x2A,1));
            };
            return bus;
        }
        public static void Run(Action<string,bool> check)
        {
            check("BlackWidow wireless keyboard uses 9F",RazerProfile.All[0x025C].Kind==DeviceKind.Keyboard && RazerProfile.All[0x025C].Transaction==0x9F && RazerProfile.All[0x025C].PreferredInterface==2);
            check("DeathStalker receiver/cable use distinct transactions",RazerProfile.All[0x0296].Transaction==0x9F && RazerProfile.All[0x0298].Transaction==0x1F && RazerProfile.All[0x0298].PreferredInterface==3);
            check("13 battery keyboard USB profiles",RazerProfile.All.Values.Count(p=>p.Kind==DeviceKind.Keyboard)==13);
            check("wired-only Basilisk excluded from battery probing",!RazerProfile.All.ContainsKey(0x00CB));
            check("Joro keyboard included",RazerProfile.All.Values.Any(p=>p.Kind==DeviceKind.Keyboard && p.Name.Contains("Joro")));
            var consumer=new HidEndpoint {Pid=0x0AC4,UsagePage=12,Usage=1,OutputLength=20,InputLength=20};
            check("G535 consumer collection is accepted",HeadsetCatalog.LogitechCandidate(consumer));
            consumer.Pid=0x0A66;check("consumer exception limited to G535",!HeadsetCatalog.LogitechCandidate(consumer));
            check("headset class mapped separately",HeadsetCatalog.Logitech.Count==15 && HeadsetCatalog.Razer.Count==4);
            var b=HeadsetCodec.PaFrame(true,0x21,false,false);
            check("Barracuda request wire fixture",b.Take(9).SequenceEqual(new byte[]{1,0x80,8,0x50,0x41,8,8,3,0x21}));
            b=HeadsetCodec.PaFrame(false,0x21,false,false);
            check("BlackShark request wire fixture",b.Take(11).SequenceEqual(new byte[]{2,0x80,8,0,0,0x50,0x41,8,0,3,0x21}));
            foreach(bool barracuda in new[]{false,true}) {
                string name=barracuda?"Barracuda":"BlackShark PA";var reader=new HeadsetReader(delegate(int ms){});
                var bus=PaBus(barracuda,77,true);var d=new DeviceReading();
                check(name+" reads level and charging; ignores unrelated reply",reader.ReadPa(bus,barracuda,d) && d.Battery==77 && d.Charge==ChargeState.Charging);
                int type=barracuda?7:9;
                check(name+" releases remote session",bus.Writes.Last()[type+1]==0xE1 && bus.Writes.Last()[type+2]==0);
                bus=PaBus(barracuda,77,false);d=new DeviceReading();
                check(name+" missing charging reply remains unknown",reader.ReadPa(bus,barracuda,d) && d.Battery==77 && d.Charge==ChargeState.Unknown);
                bus=PaBus(barracuda,255,true);d=new DeviceReading();
                check(name+" invalid battery does not become 100%",!reader.ReadPa(bus,barracuda,d) && !d.Battery.HasValue && !bus.Writes.Any(x=>x[type]==3 && x[type+1]==0x2A));
                bus=new ScriptedHid();d=new DeviceReading();
                check(name+" offline has bounded reads and releases remote",!reader.ReadPa(bus,barracuda,d) && bus.Reads<=30 && bus.Writes.Last()[type+2]==0);
                bus=new ScriptedHid {RejectWrites=true};
                check(name+" failed remote acquisition sends no query",!reader.ReadPa(bus,barracuda,new DeviceReading()) && bus.Writes.Count==1);
                bus=new ScriptedHid();bus.Written=delegate(byte[] x) {if(x[type]==3) bus.ThrowRead=true;};
                bool threw=false;try {reader.ReadPa(bus,barracuda,new DeviceReading());}catch(InvalidOperationException) {threw=true;}
                check(name+" disconnect still releases remote",threw && bus.Writes.Last()[type+1]==0xE1 && bus.Writes.Last()[type+2]==0);
                var reply=PaReply(barracuda,0x21,80);reply[barracuda?15:14]=60;
                check(name+" rejects truncated payload length",HeadsetCodec.PaPayload(reply,barracuda,0x21)==null);
                reply=PaReply(barracuda,0x21,80);reply[0]^=3;
                check(name+" rejects wrong report ID",HeadsetCodec.PaPayload(reply,barracuda,0x21)==null);
            }
            var bat=Captured("ff 0f 05 fe 12 04 1f 08 05 03 05 01 0e c0 50");var readout=new DeviceReading();
            check("BlackShark 2020 captured discharge reports coarse 80",HeadsetCodec.BlackShark2020(bat,readout) && readout.Battery==80 && readout.Estimated && readout.Charge==ChargeState.Discharging);
            var charging=Captured("ff 0f 05 fe 12 04 1f 08 05 05 03 09 10 80 50");readout=new DeviceReading();
            check("BlackShark 2020 charging hides unreliable percentage",HeadsetCodec.BlackShark2020(charging,readout) && !readout.Battery.HasValue && readout.Charge==ChargeState.Charging);
            var full=Captured("ff 0f 05 fe 12 04 1f 08 05 06 05 06 10 88 64");readout=new DeviceReading();
            check("BlackShark 2020 full state",HeadsetCodec.BlackShark2020(full,readout) && readout.Battery==100 && readout.Charge==ChargeState.Full);
            var stale=Captured("ff 01 00 fe 12 04 1f 08 05 05 03 09 10 88 50");
            check("BlackShark 2020 rejects stale retained payload",!HeadsetCodec.BlackShark2020(stale,new DeviceReading()));
            check("BlackShark 2020 rejects request echo",!HeadsetCodec.BlackShark2020(HeadsetCodec.BlackShark2020Request(64),new DeviceReading()));
            bat[12]=bat[13]=0;check("BlackShark 2020 zero voltage is not connected",!HeadsetCodec.BlackShark2020(bat,new DeviceReading()));
            var fbus=new ScriptedHid();fbus.Features.Enqueue(HeadsetCodec.BlackShark2020Request(64));fbus.Features.Enqueue(full);readout=new DeviceReading();
            check("2020 waits through echo for fresh feature response",new HeadsetReader(delegate(int ms){}).ReadBlackShark2020(fbus,readout) && readout.Battery==100 && fbus.FeatureWrites==1);
            fbus=new ScriptedHid();for(int n=0;n<4;n++) fbus.Features.Enqueue(stale);
            check("2020 never reuses stale charging or percentage",!new HeadsetReader(delegate(int ms){}).ReadBlackShark2020(fbus,new DeviceReading()));
            CenturionTests(check);
        }
        private static void CenturionTests(Action<string,bool> check)
        {
            var bus=new ScriptedHid();bool offline=false;
            bus.Written=delegate(byte[] b) {
                var p=HeadsetCodec.CenturionPayload(b);if(p==null) return;
                if(p[0]==0 && p[1]==1 && p.Length>=4) bus.Replies.Enqueue(HeadsetCodec.CenturionFrame(0,1,3));
                else if(p[0]==3 && p[1]==0x11) {
                    bus.Replies.Enqueue(HeadsetCodec.CenturionFrame(3,0x11)); // receiver ack is not a headset reading
                    if(offline) return;
                    if(p[5]==0) bus.Replies.Enqueue(HeadsetCodec.CenturionFrame(3,0x10,0,4,0,0,1,4));
                    else {
                        bus.Replies.Enqueue(HeadsetCodec.CenturionFrame(3,0x10,0,6,0,4,2,50,0,0)); // wrong echoed function
                        bus.Replies.Enqueue(HeadsetCodec.CenturionFrame(3,0x10,0,6,0,4,1,77,0,3));
                    }
                }
            };
            var reader=new HeadsetReader(delegate(int ms){});var d=new DeviceReading();
            check("Centurion discovers bridge and battery; matches headset response",reader.ReadCenturion(bus,d) && d.Battery==77 && d.Charge==ChargeState.Full);
            int writes=bus.Writes.Count;d=new DeviceReading();
            check("Centurion cached feature needs one request",reader.ReadCenturion(bus,d) && bus.Writes.Count==writes+1 && d.Battery==77);
            offline=true;d=new DeviceReading();
            check("Centurion ack-only offline never keeps old battery",!reader.ReadCenturion(bus,d) && !d.Battery.HasValue);
            check("Centurion rejects malformed frame length",HeadsetCodec.CenturionPayload(new byte[]{0x51,63,0,0})==null);
            check("Centurion rejects invalid percentage",!HeadsetCodec.CenturionBattery(new byte[]{255,0,1},new DeviceReading()));
            d=new DeviceReading();check("Centurion unknown charge state remains unknown",HeadsetCodec.CenturionBattery(new byte[]{60,0,42},d) && d.Charge==ChargeState.Unknown);
            var legacy=Captured("51 0b 00 00 00 00 00 00 04 00 37 00 02");d=new DeviceReading();
            check("Centurion fixed legacy captured layout",HeadsetCodec.CenturionLegacy(legacy,d) && d.Battery==55 && d.Charge==ChargeState.Charging);
            var payload=HeadsetCodec.CenturionPayload(HeadsetCodec.CenturionFrame(0,1,0,3));
            check("Centurion length excludes report padding",payload.SequenceEqual(new byte[]{0,1,0,3}));
            reader.ClearCache();offline=false;writes=bus.Writes.Count;
            check("Centurion manual refresh repeats discovery",reader.ReadCenturion(bus,new DeviceReading()) && bus.Writes.Count>writes+1);
        }
    }
}
