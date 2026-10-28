using System.Runtime.InteropServices;

namespace Cyclop.Windows;

internal sealed record DisplayInfo(string DeviceName,int Left,int Top,int Width,int Height,bool IsPrimary);

internal static class DisplayInfoProvider
{
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Auto)]private struct MonitorInfo{public int Size;public Rect Monitor;public Rect Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    private delegate bool MonitorEnumProc(nint monitor,nint dc,ref Rect rect,nint data);
    [DllImport("user32.dll")]private static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorEnumProc callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Auto)]private static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var result=new List<DisplayInfo>();MonitorEnumProc callback=(nint handle,nint _,ref Rect __,nint ___)=>
        {
            var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>(),Device=string.Empty};
            if(GetMonitorInfo(handle,ref info))result.Add(new DisplayInfo(info.Device,info.Monitor.Left,info.Monitor.Top,info.Monitor.Right-info.Monitor.Left,info.Monitor.Bottom-info.Monitor.Top,(info.Flags&1)!=0));
            return true;
        };
        EnumDisplayMonitors(0,0,callback,0);GC.KeepAlive(callback);
        return result.GroupBy(x=>(x.Left,x.Top,x.Width,x.Height)).Select(group=>group.OrderByDescending(x=>x.IsPrimary).First()).ToArray();
    }
}
