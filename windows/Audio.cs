using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace HushAlarm {
    public sealed class Headphone {
        public string Id; public string Name; public uint FormFactor;
        public override string ToString() { return Name; }
    }
    public static class AudioDevices {
        public static List<Headphone> List() {
            var result = new List<Headphone>();
            IMMDeviceEnumerator e = (IMMDeviceEnumerator)new DeviceEnumerator(); IMMDeviceCollection list = null;
            try {
                Marshal.ThrowExceptionForHR(e.EnumAudioEndpoints(0, 1, out list));
                uint count; Marshal.ThrowExceptionForHR(list.GetCount(out count));
                for (uint i=0; i<count; i++) {
                    IMMDevice device = null; IPropertyStore props = null;
                    try {
                        Marshal.ThrowExceptionForHR(list.Item(i, out device));
                        string id; Marshal.ThrowExceptionForHR(device.GetId(out id));
                        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out props));
                        PropertyKey form = new PropertyKey(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);
                        PropVariant value; Marshal.ThrowExceptionForHR(props.GetValue(ref form, out value));
                        uint factor = value.UIntValue; value.Clear();
                        if (!AlarmTime.IsHeadphoneFormFactor(factor)) continue;
                        PropertyKey name = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
                        Marshal.ThrowExceptionForHR(props.GetValue(ref name, out value));
                        string label = value.Vt == 31 ? Marshal.PtrToStringUni(value.Pointer) : "イヤホン"; value.Clear();
                        result.Add(new Headphone { Id=id, Name=label, FormFactor=factor });
                    } finally { Release(props); Release(device); }
                }
            } finally { Release(list); Release(e); }
            return result;
        }
        public static bool IsActive(string id) { try { return List().Exists(delegate(Headphone h) { return h.Id == id; }); } catch { return false; } }
        internal static void Release(object obj) { if (obj != null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
    }
    public sealed class HeadphonePlayer : IDisposable {
        private Thread thread; private ManualResetEvent cancel;
        public event Action<string> Failed;
        public void Play(string endpointId, double volume, int sound, double seconds) {
            Stop();
            if (thread != null && thread.IsAlive) throw new InvalidOperationException("Playback is stopping.");
            if (!AudioDevices.IsActive(endpointId)) throw new InvalidOperationException("イヤホンを確認できません");
            var signal = new ManualResetEvent(false); cancel = signal;
            thread = new Thread(delegate() { Render(endpointId, Math.Max(0, Math.Min(0.5, volume)), sound, seconds, signal); });
            thread.IsBackground = true; thread.Name = "HUSH headphone output"; thread.SetApartmentState(ApartmentState.MTA); thread.Start();
        }
        private void Render(string endpointId, double volume, int sound, double seconds, ManualResetEvent signal) {
            IMMDeviceEnumerator e=null; IMMDevice device=null; IAudioClient client=null; IAudioRenderClient render=null; IntPtr format=IntPtr.Zero;
            try {
                e=(IMMDeviceEnumerator)new DeviceEnumerator();
                // Open only the concrete endpoint. Never call GetDefaultAudioEndpoint.
                Marshal.ThrowExceptionForHR(e.GetDevice(endpointId, out device));
                uint state; Marshal.ThrowExceptionForHR(device.GetState(out state)); if (state != 1) throw new InvalidOperationException("イヤホンが切断されました");
                object obj; Guid iid=typeof(IAudioClient).GUID; Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out obj)); client=(IAudioClient)obj;
                var wave=new WaveFormat { Tag=1, Channels=2, Rate=48000, BytesPerSecond=192000, BlockAlign=4, Bits=16, Extra=0 };
                format=Marshal.AllocCoTaskMem(Marshal.SizeOf(wave)); Marshal.StructureToPtr(wave, format, false);
                Marshal.ThrowExceptionForHR(client.Initialize(0, 0x88000000, 1000000, 0, format, IntPtr.Zero));
                uint capacity; Marshal.ThrowExceptionForHR(client.GetBufferSize(out capacity));
                iid=typeof(IAudioRenderClient).GUID; Marshal.ThrowExceptionForHR(client.GetService(ref iid, out obj)); render=(IAudioRenderClient)obj;
                long total=0; Marshal.ThrowExceptionForHR(client.Start());
                while (!signal.WaitOne(8) && total / 48000.0 < seconds) {
                    uint padding; Marshal.ThrowExceptionForHR(client.GetCurrentPadding(out padding));
                    uint frames=capacity-padding; if (frames==0) continue;
                    IntPtr data; Marshal.ThrowExceptionForHR(render.GetBuffer(frames, out data));
                    var samples=new short[(int)frames*2];
                    for (int f=0; f<(int)frames; f++) {
                        double t=(total+f)/48000.0, cycle=t % 1.6;
                        double frequency=sound==1 ? 660 + 100*Math.Sin(t*4) : sound==2 ? (cycle<0.25 ? 523.25 : cycle<0.5 ? 659.25 : 783.99) : 740;
                        double local=cycle % 0.4;
                        double envelope=cycle<1.2 ? Math.Min(1,local/0.015)*Math.Min(1,Math.Max(0,(0.32-local)/0.04)) : 0;
                        double ramp=Math.Min(1,t/5.0);
                        short sample=(short)(Math.Sin(t*frequency*2*Math.PI)*32767*volume*envelope*ramp);
                        samples[f*2]=sample; samples[f*2+1]=sample;
                    }
                    if (signal.WaitOne(0)) { Marshal.ThrowExceptionForHR(render.ReleaseBuffer(frames, 2)); break; }
                    Marshal.Copy(samples,0,data,samples.Length); Marshal.ThrowExceptionForHR(render.ReleaseBuffer(frames,0)); total+=frames;
                }
            } catch (Exception) {
                if (!signal.WaitOne(0) && Failed != null) Failed("イヤホンへの再生が停止しました");
            } finally {
                if (client!=null) client.Stop();
                if (format!=IntPtr.Zero) Marshal.FreeCoTaskMem(format);
                AudioDevices.Release(render); AudioDevices.Release(client); AudioDevices.Release(device); AudioDevices.Release(e);
                signal.Dispose();
            }
        }
        public void Stop() { try { if(cancel!=null) cancel.Set(); } catch(ObjectDisposedException) {} if(thread!=null && thread.IsAlive) thread.Join(1500); }
        public void Dispose() { Stop(); }
    }
    [StructLayout(LayoutKind.Sequential,Pack=2)] struct WaveFormat { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort BlockAlign, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Pid; public PropertyKey(Guid f,uint p) { Format=f; Pid=p; } }
    [StructLayout(LayoutKind.Explicit)] struct PropVariant {
        [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public IntPtr Pointer; [FieldOffset(8)] public uint UIntValue;
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
        public void Clear() { PropVariantClear(ref this); }
    }
    [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumerator {}
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow,uint states,out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow,int role,out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-C0A32B8B5F8A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDeviceCollection {
        [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index,out IMMDevice device);
    }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid,uint context,IntPtr activation,[MarshalAs(UnmanagedType.IUnknown)]out object obj);
        [PreserveSig] int OpenPropertyStore(uint mode,out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index,out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key,out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key,ref PropVariant value); [PreserveSig] int Commit();
    }
    [ComImport,Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioClient {
        [PreserveSig] int Initialize(int mode,uint flags,long duration,long periodicity,IntPtr format,IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames); [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding); [PreserveSig] int IsFormatSupported(int mode,IntPtr format,IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format); [PreserveSig] int GetDevicePeriod(out long normal,out long minimum);
        [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset(); [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid,[MarshalAs(UnmanagedType.IUnknown)]out object service);
    }
    [ComImport,Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioRenderClient {
        [PreserveSig] int GetBuffer(uint frames,out IntPtr data); [PreserveSig] int ReleaseBuffer(uint frames,uint flags);
    }
}
