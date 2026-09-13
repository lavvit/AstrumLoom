using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AstrumLoom.Rhythm;

/// <summary>Explicit opt-in keyboard registration owned by the application, never created by GameRunner.</summary>
public sealed class WindowsRawInputSource : ITimedInputSource
{
    private readonly TimedInputQueue queue;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly WndProc callback;
    private readonly HashSet<(nint, int)> held = new();
    private readonly Dictionary<nint,string> devices = new();
    private readonly uint processId = (uint)Environment.ProcessId;
    private nint window;
    private Exception? failure;
    private bool wasFocused;
    public long TickFrequency => queue.TickFrequency;
    public InputLoss? Loss => queue.Loss;
    public Exception? Failure => failure;
    public WindowsRawInputSource(int capacity = 4096)
    {
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        queue=new(capacity); callback=HandleMessage;
        thread=new Thread(Run) { IsBackground=true, Name="Rhythm raw keyboard" };
        thread.Start(); ready.Wait();
        if(failure != null) { thread.Join(); ready.Dispose(); throw new InvalidOperationException("Raw input initialization failed.",failure); }
    }
    public bool TryRead(out TimedInputEvent input) => queue.TryRead(out input);
    private void Run()
    {
        string name="AstrumRhythm-"+Guid.NewGuid().ToString("N");
        nint instance=GetModuleHandleW(null);
        try
        {
            uint count=0;
            if(GetRegisteredRawInputDevices(null,ref count,(uint)Marshal.SizeOf<RawDevice>()) == uint.MaxValue) throw new Win32Exception();
            var registrations=new RawDevice[count];
            if(count > 0 && GetRegisteredRawInputDevices(registrations,ref count,(uint)Marshal.SizeOf<RawDevice>()) == uint.MaxValue) throw new Win32Exception();
            if(registrations.Any(r=>r.Page==1 && r.Usage==6)) throw new InvalidOperationException("Keyboard raw input is already registered in this process.");
            var wc=new WindowClass { Size=(uint)Marshal.SizeOf<WindowClass>(), Procedure=Marshal.GetFunctionPointerForDelegate(callback), Instance=instance, Name=name };
            if(RegisterClassExW(ref wc)==0) throw new Win32Exception();
            window=CreateWindowExW(0,name,name,0,0,0,0,0,new nint(-3),0,instance,0);
            if(window==0) throw new Win32Exception();
            var device=new RawDevice { Page=1,Usage=6,Flags=0x2100,Target=window }; // INPUTSINK | DEVNOTIFY; preserve legacy UI messages.
            if(!RegisterRawInputDevices([device],1,(uint)Marshal.SizeOf<RawDevice>())) throw new Win32Exception();
            SetTimer(window,1,10,0);
            ready.Set();
            int status;
            while((status=GetMessageW(out var msg,0,0,0))>0) { TranslateMessage(ref msg); DispatchMessageW(ref msg); }
            if(status<0) throw new Win32Exception();
        }
        catch(Exception ex) { failure=ex; }
        finally
        {
            if(window!=0) { DestroyWindow(window); window=0; }
            UnregisterClassW(name,instance); ready.Set();
        }
    }
    private bool Focused()
    {
        GetWindowThreadProcessId(GetForegroundWindow(),out uint id); return id==processId;
    }
    private nint HandleMessage(nint hwnd,uint message,nuint w,nint l)
    {
        try
        {
            long tick=Stopwatch.GetTimestamp();
            if(message==0x10) { PostQuitMessage(0); return 0; }
            if(message==0x113)
            {
                bool focused=Focused();
                if(wasFocused && !focused) { held.Clear(); queue.Publish(tick,"",0,InputAction.Reset,InputOrigin.Synthetic); }
                wasFocused=focused;
            }
            if(message==0xFE && w==2)
            {
                devices.Remove(l); held.RemoveWhere(k=>k.Item1==l);
                queue.Publish(tick,"",0,InputAction.Reset,InputOrigin.Synthetic);
            }
            if(message==0xFF && Focused())
            {
                uint size=0, hs=(uint)Marshal.SizeOf<RawHeader>();
                if(GetRawInputData(l,0x10000003,0,ref size,hs)==uint.MaxValue) throw new Win32Exception();
                nint buffer=Marshal.AllocHGlobal(checked((int)size));
                try
                {
                    if(GetRawInputData(l,0x10000003,buffer,ref size,hs)!=size) throw new Win32Exception();
                    var h=Marshal.PtrToStructure<RawHeader>(buffer);
                    if(h.Type==1 && size>=hs+16)
                    {
                        ushort scan=(ushort)Marshal.ReadInt16(buffer,(int)hs), flags=(ushort)Marshal.ReadInt16(buffer,(int)hs+2);
                        int code=scan | ((flags & 6)<<8);
                        if(!devices.TryGetValue(h.Device,out string? id)) devices[h.Device]=id=Guid.NewGuid().ToString("N");
                        bool up=(flags&1)!=0;
                        if(up ? held.Remove((h.Device,code)) : held.Add((h.Device,code))) queue.Publish(tick,id,code,up?InputAction.Up:InputAction.Down);
                        wasFocused=true;
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        catch(Exception ex) { failure=ex; PostQuitMessage(1); }
        return DefWindowProcW(hwnd,message,w,l);
    }
    public void Dispose()
    {
        if(thread.IsAlive) { PostMessageW(window,0x10,0,0); thread.Join(); }
        ready.Dispose();
    }
    private delegate nint WndProc(nint h,uint m,nuint w,nint l);
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page,Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawHeader { public uint Type,Size; public nint Device; public nuint W; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct WindowClass { public uint Size,Style; public nint Procedure; public int ClassExtra,WindowExtra; public nint Instance,Icon,Cursor,Background; public string? Menu,Name; public nint SmallIcon; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint H; public uint M; public nuint W; public nint L; public uint Time; public int X,Y; public uint Private; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint GetRegisteredRawInputDevices([Out] RawDevice[]? devices,ref uint count,uint size);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterRawInputDevices(RawDevice[] devices,uint count,uint size);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint GetRawInputData(nint input,uint command,nint data,ref uint size,uint headerSize);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern ushort RegisterClassExW(ref WindowClass wc);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool UnregisterClassW(string name,nint instance);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint h,uint m,nuint w,nint l);
    [DllImport("user32.dll")] private static extern int GetMessageW(out Message m,nint h,uint min,uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message m);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message m);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint h,uint m,nuint w,nint l);
    [DllImport("user32.dll")] private static extern nuint SetTimer(nint h,nuint id,uint ms,nint callback);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint h,out uint id);
}
