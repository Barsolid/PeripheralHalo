// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PeripheralHalo
{
    public sealed class HidEndpoint
    {
        public string Path, Product, Container;
        public ushort Vid, Pid, UsagePage, Usage, InputLength, OutputLength, FeatureLength;
        public string Key { get { return BatteryPolicy.StableId(Container ?? Path); } }
    }
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct InterfaceData { public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct DevInfo { public int Size; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct Attributes { public int Size; public ushort VendorId, ProductId, Version; }
        [StructLayout(LayoutKind.Sequential)] internal struct Caps {
            public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
            public ushort LinkCollectionNodes, InputButtonCaps, InputValueCaps, InputDataIndices, OutputButtonCaps, OutputValueCaps, OutputDataIndices, FeatureButtonCaps, FeatureValueCaps, FeatureDataIndices;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid Format; public uint Id; public PropertyKey(string format,uint id){Format=new Guid(format);Id=id;} }
        [StructLayout(LayoutKind.Sequential)] internal struct Overlapped { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
        [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetAttributes(SafeFileHandle handle,ref Attributes attributes);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetProductString(SafeFileHandle handle,byte[] buffer,int length);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out IntPtr data);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr data,out Caps caps);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern IntPtr SetupDiGetClassDevs(ref Guid guid,IntPtr enumerator,IntPtr parent,uint flags);
        [DllImport("setupapi.dll",SetLastError=true)] internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr info,IntPtr dev,ref Guid guid,uint index,ref InterfaceData data);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr info,ref InterfaceData data,IntPtr detail,uint size,out uint needed,ref DevInfo dev);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool SetupDiGetDeviceProperty(IntPtr info,ref DevInfo dev,ref PropertyKey key,out uint type,byte[] buffer,uint size,out uint needed,uint flags);
        [DllImport("setupapi.dll")] internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr info);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool ReadFile(SafeFileHandle handle,IntPtr buffer,uint length,out uint done,IntPtr overlapped);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool WriteFile(SafeFileHandle handle,IntPtr buffer,uint length,out uint done,IntPtr overlapped);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool DeviceIoControl(SafeFileHandle handle,uint code,IntPtr input,uint inLength,IntPtr output,uint outLength,out uint done,IntPtr overlapped);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool GetOverlappedResult(SafeFileHandle handle,IntPtr overlapped,out uint done,bool wait);
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool CancelIoEx(SafeFileHandle handle,IntPtr overlapped);
        [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr CreateEvent(IntPtr security,bool manual,bool initial,string name);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    }
    public static class HidDiscovery
    {
        public static List<HidEndpoint> Enumerate()
        {
            var found=new List<HidEndpoint>(); Guid guid; Native.HidD_GetHidGuid(out guid);
            IntPtr info=Native.SetupDiGetClassDevs(ref guid,IntPtr.Zero,IntPtr.Zero,0x12);
            if(info==new IntPtr(-1)) return found;
            try {
                for(uint i=0;i<4096;i++) {
                    var data=new Native.InterfaceData {Size=Marshal.SizeOf(typeof(Native.InterfaceData))};
                    if(!Native.SetupDiEnumDeviceInterfaces(info,IntPtr.Zero,ref guid,i,ref data)) break;
                    uint needed; var dev=new Native.DevInfo {Size=Marshal.SizeOf(typeof(Native.DevInfo))};
                    Native.SetupDiGetDeviceInterfaceDetail(info,ref data,IntPtr.Zero,0,out needed,ref dev);
                    if(needed<8 || needed>65536) continue;
                    IntPtr detail=Marshal.AllocHGlobal((int)needed);
                    try {
                        Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                        if(!Native.SetupDiGetDeviceInterfaceDetail(info,ref data,detail,needed,out needed,ref dev)) continue;
                        string path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                        if(path==null || (!path.ToLowerInvariant().Contains("vid_046d") && !path.ToLowerInvariant().Contains("vid_1532"))) continue;
                        using(var handle=Native.CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                            if(handle.IsInvalid) continue;
                            var a=new Native.Attributes {Size=Marshal.SizeOf(typeof(Native.Attributes))};
                            if(!Native.HidD_GetAttributes(handle,ref a) || (a.VendorId!=0x046D && a.VendorId!=0x1532)) continue;
                            IntPtr parsed;
                            if(!Native.HidD_GetPreparsedData(handle,out parsed)) continue;
                            Native.Caps caps;
                            try { if(Native.HidP_GetCaps(parsed,out caps)!=0x110000) continue; }
                            finally {Native.HidD_FreePreparsedData(parsed);}
                            byte[] name=new byte[512]; Native.HidD_GetProductString(handle,name,name.Length);
                            var key=new Native.PropertyKey("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C",2);
                            uint type,size; byte[] container=new byte[16];
                            string containerId=Native.SetupDiGetDeviceProperty(info,ref dev,ref key,out type,container,16,out size,0) && type==0x0D && size==16 ? new Guid(container).ToString() : path.Split('#').Length>2?path.Split('#')[2]:path;
                            found.Add(new HidEndpoint {Path=path,Product=Encoding.Unicode.GetString(name).TrimEnd('\0'),Container=containerId,Vid=a.VendorId,Pid=a.ProductId,UsagePage=caps.UsagePage,Usage=caps.Usage,InputLength=caps.InputLength,OutputLength=caps.OutputLength,FeatureLength=caps.FeatureLength});
                        }
                    } finally {Marshal.FreeHGlobal(detail);}
                }
            } finally {Native.SetupDiDestroyDeviceInfoList(info);}
            return found;
        }
    }
    // Overlapped I/O with cancellation: no permanently blocked HID worker or pinned buffer on timeout.
    public sealed class HidTransport : IDisposable
    {
        private readonly SafeFileHandle handle;
        public readonly HidEndpoint Endpoint;
        public HidTransport(HidEndpoint endpoint,bool featureOnly=false) {
            Endpoint=endpoint;
            handle=Native.CreateFile(endpoint.Path,featureOnly?0u:0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
            if(handle.IsInvalid) {handle.Dispose();throw new InvalidOperationException("设备接口无法打开（可能被驱动占用）");}
        }
        private bool Transfer(byte[] data,int operation,int timeout)
        {
            if(data.Length==0 || data.Length>4096) return false;
            IntPtr ev=Native.CreateEvent(IntPtr.Zero,true,false,null);
            if(ev==IntPtr.Zero) return false;
            IntPtr ov=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.Overlapped)));
            var pinned=GCHandle.Alloc(data,GCHandleType.Pinned);
            try {
                Marshal.StructureToPtr(new Native.Overlapped {Event=ev},ov,false); uint done;
                bool ok=operation==0?Native.ReadFile(handle,pinned.AddrOfPinnedObject(),(uint)data.Length,out done,ov):operation==1?Native.WriteFile(handle,pinned.AddrOfPinnedObject(),(uint)data.Length,out done,ov):operation==2?Native.DeviceIoControl(handle,0x000B0191,pinned.AddrOfPinnedObject(),(uint)data.Length,IntPtr.Zero,0,out done,ov):Native.DeviceIoControl(handle,0x000B0192,pinned.AddrOfPinnedObject(),(uint)data.Length,pinned.AddrOfPinnedObject(),(uint)data.Length,out done,ov);
                if(!ok) {
                    if(Marshal.GetLastWin32Error()!=997) return false;
                    if(Native.WaitForSingleObject(ev,(uint)timeout)!=0) {Native.CancelIoEx(handle,ov);Native.GetOverlappedResult(handle,ov,out done,true);return false;}
                    if(!Native.GetOverlappedResult(handle,ov,out done,false)) return false;
                }
                return IsComplete(operation,data,done);
            } finally {pinned.Free();Marshal.FreeHGlobal(ov);Native.CloseHandle(ev);}
        }
        internal static bool IsComplete(int operation,byte[] data,uint done) {return operation==2 || (operation==3 && data[0]==0?done+1:done)>=data.Length;}
        public bool Write(byte[] data) {return Transfer(data,1,350);}
        public byte[] Read(int timeout) {var data=new byte[Endpoint.InputLength]; return Transfer(data,0,timeout)?data:null;}
        public bool FeatureSet(byte[] data) {return Transfer(data,2,500);}
        public bool FeatureGet(byte[] data) {return Transfer(data,3,500);}
        public byte[] Hidpp(byte slot,byte feature,byte function,params byte[] parameters)
        {
            if(Endpoint.OutputLength<7 || Endpoint.InputLength<7) return null;
            int logical=Endpoint.OutputLength>=20?20:7;
            if(parameters.Length>logical-4) return null;
            var packet=new byte[Endpoint.OutputLength]; packet[0]=(byte)(logical==20?0x11:0x10);packet[1]=slot;packet[2]=feature;packet[3]=(byte)((function<<4)|0x0B);
            Array.Copy(parameters,0,packet,4,parameters.Length);
            return Exchange(packet);
        }
        public byte[] Register(byte slot,byte register)
        {
            if(Endpoint.OutputLength<7 || Endpoint.InputLength<7) return null;
            var packet=new byte[Endpoint.OutputLength];packet[0]=0x10;packet[1]=slot;packet[2]=0x81;packet[3]=register;
            return Exchange(packet);
        }
        private byte[] Exchange(byte[] packet)
        {
            if(!Write(packet)) return null;
            var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<350) {
                byte[] response=Read(Math.Max(1,350-(int)watch.ElapsedMilliseconds));
                if(response==null) return null;
                if(response.Length<7 || (response[0]!=0x10 && response[0]!=0x11) || response[1]!=packet[1]) continue;
                if(response[2]==packet[2] && response[3]==packet[3]) return response;
                if((response[2]==0xFF || response[2]==0x8F) && response[3]==packet[2] && response[4]==packet[3]) return null;
            }
            return null;
        }
        public void Dispose() {handle.Dispose();}
    }
}
