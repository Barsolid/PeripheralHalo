// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace PeripheralHalo
{
    public static class SelfTests
    {
        public static int Run(string path)
        {
            var results=new List<string>();int failed=0;
            Action<string,bool> check=delegate(string name,bool pass) {results.Add((pass?"PASS ":"FAIL ")+name);if(!pass) failed++;};
            check("20% is red",BatteryPolicy.Color(20)=="#FF7285");check("21% is orange",BatteryPolicy.Color(21)=="#F4A563");check("49% is orange",BatteryPolicy.Color(49)=="#F4A563");check("50% is yellow",BatteryPolicy.Color(50)=="#F3D778");check("74% is yellow",BatteryPolicy.Color(74)=="#F3D778");check("75% is green",BatteryPolicy.Color(75)=="#63E6B0");check("unknown is neutral",BatteryPolicy.Color(null)=="#8995AA");
            check("voltage interpolation endpoints",BatteryPolicy.VoltagePercent(3500)==0 && BatteryPolicy.VoltagePercent(4186)==100 && BatteryPolicy.VoltagePercent(3811)==50);int last=0;bool monotonic=true;for(int mv=3400;mv<=4500;mv++) {int p=BatteryPolicy.VoltagePercent(mv);if(p<last || p>100) monotonic=false;last=p;}check("voltage curve monotonic and bounded",monotonic);
            var d=new DeviceReading();check("reject malformed HID++",!ProtocolDecoder.Logitech(0x1004,new byte[3],d));
            check("unified 86% charging",ProtocolDecoder.Logitech(0x1004,new byte[]{0x11,1,4,0x1B,86,4,1},d) && d.Battery==86 && d.Charge==ChargeState.Charging);
            d=new DeviceReading();check("unified full state",ProtocolDecoder.Logitech(0x1004,new byte[]{0x11,1,4,0x1B,100,8,3},d) && d.Charge==ChargeState.Full);
            d=new DeviceReading();check("reject invalid percentage",!ProtocolDecoder.Logitech(0x1004,new byte[]{0x11,1,4,0x1B,255,4,0},d));
            check("legacy 0 means unknown",!ProtocolDecoder.Logitech(0x1000,new byte[]{0x11,1,4,0x0B,0,0,0},d));
            d=new DeviceReading();check("coarse level explicitly estimated",ProtocolDecoder.Logitech(0x1004,new byte[]{0x11,1,4,0x1B,0,4,0},d) && d.Estimated && d.Battery==60);
            d=new DeviceReading();check("voltage explicitly estimated",ProtocolDecoder.Logitech(0x1001,new byte[]{0x11,1,4,0x0B,0x0E,0xE3,0x80},d) && d.Estimated && d.Battery==50 && d.Charge==ChargeState.Charging);
            d=new DeviceReading();check("headset offline flag rejected",!ProtocolDecoder.Logitech(0x1F20,new byte[]{0x11,1,4,0x0B,0x0E,0xE3,0},d));
            var request=ProtocolDecoder.RazerRequest(0x1F,0x80);check("Razer request layout",request.Length==91 && request[2]==0x1F && request[6]==2 && request[7]==7 && request[8]==0x80 && request[89]==0x85);
            check("unnumbered feature accepts 90-byte native count",HidTransport.IsComplete(3,new byte[91],90));check("feature rejects truncated response",!HidTransport.IsComplete(3,new byte[91],89));check("numbered feature requires full length",!HidTransport.IsComplete(3,new byte[]{1,0,0},2));check("set feature accepts zero-byte completion",HidTransport.IsComplete(2,new byte[91],0));
            var response=(byte[])request.Clone();response[1]=2;response[10]=255;response[89]=ProtocolDecoder.Checksum(response);
            check("Razer validates matched success",ProtocolDecoder.RazerValid(response,0x1F,0x80));check("Razer rejects unrelated transaction",!ProtocolDecoder.RazerValid(response,0x3F,0x80));check("Razer rejects unrelated command",!ProtocolDecoder.RazerValid(response,0x1F,0x84));
            response[10]^=1;check("Razer rejects corrupt checksum",!ProtocolDecoder.RazerValid(response,0x1F,0x80));response[89]=ProtocolDecoder.Checksum(response);response[1]=1;check("Razer rejects busy/stale reply",!ProtocolDecoder.RazerValid(response,0x1F,0x80));
            check("Viper V3 Pro and legacy transactions",RazerProfile.All[0x00C1].Transaction==0x1F && RazerProfile.All[0x007B].Transaction==0xFF && RazerProfile.All[0x007D].Transaction==0x3F);check("AA device charging query excluded",RazerProfile.All[0x0094].ReplaceableBattery);check("69 Razer USB profiles",RazerProfile.All.Count==69);
            check("G435 is headset",DeviceReading.InferKind("G435 LIGHTSPEED")==DeviceKind.Headset);check("unknown never displayed as percentage",new DeviceReading().Value=="—");check("stable ID ignores path case",BatteryPolicy.StableId("ABC")==BatteryPolicy.StableId("abc"));
            foreach(var demo in DemoDevices.Create()) check("demo isolated: "+demo.Name,demo.IsDemo);
            results.Add(String.Format("{0} passed, {1} failed",results.Count-failed,failed));File.WriteAllLines(path,results,Encoding.UTF8);return failed==0?0:1;
        }
    }
}
