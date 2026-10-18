// SPDX-License-Identifier: GPL-2.0-only
// Windows HFP battery property identified by GearHub (MIT). See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
namespace PeripheralHalo
{
    public static class BluetoothBattery
    {
        [StructLayout(LayoutKind.Sequential)] private struct SearchParams {public int Size,Authenticated,Remembered,Unknown,Connected,Inquiry;public byte Timeout;public IntPtr Radio;}
        [StructLayout(LayoutKind.Sequential)] private struct SystemTime {public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Milliseconds;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct DeviceInfo {
            public int Size;public ulong Address;public uint Class;public int Connected,Remembered,Authenticated;public SystemTime Seen,Used;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=248)] public string Name;
        }
        [DllImport("bthprops.cpl",SetLastError=true)] private static extern IntPtr BluetoothFindFirstDevice(ref SearchParams search,ref DeviceInfo device);
        [DllImport("bthprops.cpl",SetLastError=true)] private static extern bool BluetoothFindNextDevice(IntPtr search,ref DeviceInfo device);
        [DllImport("bthprops.cpl")] private static extern bool BluetoothFindDeviceClose(IntPtr search);
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern uint CM_Get_Device_ID_List_SizeW(out uint size,string filter,uint flags);
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern uint CM_Get_Device_ID_ListW(string filter,char[] buffer,uint size,uint flags);
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern uint CM_Locate_DevNodeW(out uint device,string id,uint flags);
        [DllImport("cfgmgr32.dll",ExactSpelling=true)] private static extern uint CM_Get_DevNode_PropertyW(uint device,ref Native.PropertyKey key,out uint type,byte[] buffer,ref uint size,uint flags);
        public static List<DeviceReading> Scan()
        {
            var result=new List<DeviceReading>();
            try {
                uint length;if(CM_Get_Device_ID_List_SizeW(out length,"BTHENUM",1)!=0 || length==0 || length>1048576) return result;
                var buffer=new char[length];if(CM_Get_Device_ID_ListW("BTHENUM",buffer,length,1)!=0) return result;
                var instances=new string(buffer).Split(new char[]{'\0'},StringSplitOptions.RemoveEmptyEntries).Where(s=>s.IndexOf("{0000111E-0000-1000-8000-00805F9B34FB}",StringComparison.OrdinalIgnoreCase)>=0).ToList();
                var search=new SearchParams {Size=Marshal.SizeOf(typeof(SearchParams)),Connected=1};
                var info=new DeviceInfo {Size=Marshal.SizeOf(typeof(DeviceInfo))};
                IntPtr handle=BluetoothFindFirstDevice(ref search,ref info);if(handle==IntPtr.Zero) return result;
                try {do {
                    if(info.Connected==0) continue;
                    string address=info.Address.ToString("X12"),name=info.Name ?? "Bluetooth headset";
                    foreach(var id in instances.Where(s=>s.IndexOf(address,StringComparison.OrdinalIgnoreCase)>=0)) {
                        uint dev;if(CM_Locate_DevNodeW(out dev,id,0)!=0) continue;
                        var key=new Native.PropertyKey("104EA319-6EE2-4701-BD47-8DDBF425BBE5",2);uint type,size=4;byte[] bytes=new byte[4];
                        if(CM_Get_DevNode_PropertyW(dev,ref key,out type,bytes,ref size,0)!=0) continue;
                        int percentage=type==3 && size>=1?bytes[0]:type==7 && size==4?(int)BitConverter.ToUInt32(bytes,0):-1;
                        if(percentage<0 || percentage>100) continue;
                        var kind=DeviceReading.InferKind(name);
                        result.Add(new DeviceReading {Id="bt-"+BatteryPolicy.StableId(address),Name=name,Brand=name.IndexOf("razer",StringComparison.OrdinalIgnoreCase)>=0?"RAZER":name.IndexOf("logi",StringComparison.OrdinalIgnoreCase)>=0||name.ToLowerInvariant().Contains("g435")?"LOGITECH":"BLUETOOTH",Kind=kind==DeviceKind.Other?DeviceKind.Headset:kind,Battery=percentage,Charge=ChargeState.Unknown,Connection="Bluetooth · HFP",Source="Windows 蓝牙 HFP · 系统报告",Detail="仅显示当前已连接的蓝牙设备。电量由 Windows 缓存提供，更新速度取决于耳机上报；此接口不提供充电状态。"});break;
                    }
                }while(BluetoothFindNextDevice(handle,ref info));}finally {BluetoothFindDeviceClose(handle);}
            }catch(DllNotFoundException) { }catch(EntryPointNotFoundException) { }
            return result;
        }
    }
}
