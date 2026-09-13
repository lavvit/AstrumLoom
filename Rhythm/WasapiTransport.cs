using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AstrumLoom.Rhythm;

namespace AstrumLoom.Rhythm;

/// <summary>Shared-mode stereo float PCM transport. All COM calls are owned by one audio thread.</summary>
public sealed class WasapiTransport : IAudioTransport
{
    private readonly float[] samples;
    private readonly int rate;
    private readonly Thread worker;
    private readonly BlockingCollection<Action> commands = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ClockSegment? snapshot;
    private volatile TransportState state;
    private long generation=1, interval, startTick, offset, written;
    private ulong frequency, lastPosition;
    private nint enumerator, endpoint, client, render, clock;
    private uint capacity;
    private readonly List<(long Frame, float[] Pcm)> voices = new();
    private volatile bool disposed;
    public Exception? Failure { get; private set; }
    public TransportState State => state;
    public long Generation => Interlocked.Read(ref generation);
    public long LengthUs => RhythmTime.Convert(samples.Length/2,rate,1_000_000);
    public WasapiTransport(float[] stereoPcm, int sampleRate)
    {
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if(sampleRate is < 8000 or > 192000 || stereoPcm.Length == 0 || stereoPcm.Length%2 != 0 || stereoPcm.Any(x=>!float.IsFinite(x))) throw new ArgumentException("Expected finite stereo float PCM.");
        samples=(float[])stereoPcm.Clone(); rate=sampleRate;
        worker=new Thread(Run) { Name="Rhythm WASAPI", IsBackground=true }; worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        try { ready.Task.GetAwaiter().GetResult(); } catch { worker.Join(); commands.Dispose(); throw; }
    }
    private static T Method<T>(nint obj,int slot) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size));
    private static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
    private void Invoke(Action action)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Add(()=> { try { action(); completion.SetResult(); } catch(Exception ex) { Fault(ex); completion.SetException(ex); } });
        completion.Task.GetAwaiter().GetResult();
    }
    private void Fault(Exception ex) { Failure=ex; state=TransportState.Faulted; Volatile.Write(ref snapshot,null); }
    public void Play() => Invoke(()=>
    {
        if(state==TransportState.Faulted) throw new InvalidOperationException("Transport faulted.",Failure);
        if(state==TransportState.Playing || offset >= samples.Length/2) return;
        if(state!=TransportState.Paused) Fill();
        Check(Method<Simple>(client,10)(client));
        startTick=Stopwatch.GetTimestamp(); interval++; state=TransportState.Playing;
    });
    public void Pause() => Invoke(()=> { if(state!=TransportState.Playing)return; Check(Method<Simple>(client,11)(client)); state=TransportState.Paused; interval++; Volatile.Write(ref snapshot,null); });
    public void Seek(long songUs) => Invoke(()=>
    {
        if(songUs<0 || songUs>LengthUs) throw new ArgumentOutOfRangeException(nameof(songUs));
        Check(Method<Simple>(client,11)(client)); Check(Method<Simple>(client,12)(client));
        offset=RhythmTime.Convert(songUs,1_000_000,rate); written=0; lastPosition=0; voices.Clear();
        Interlocked.Increment(ref generation); interval++; state=TransportState.Stopped; Volatile.Write(ref snapshot,null);
    });
    public bool TryGetClock(out ClockSegment segment) { segment=Volatile.Read(ref snapshot)!; return segment!=null && state==TransportState.Playing; }
    /// <summary>Returns lateness in frames. Past requests start at the next writable frame.</summary>
    public long Schedule(float[] stereoPcm, long songFrame)
    {
        if(stereoPcm.Length%2!=0 || stereoPcm.Any(x=>!float.IsFinite(x)))throw new ArgumentException("Invalid key sound.");
        var copy=(float[])stereoPcm.Clone(); long late=0;
        Invoke(()=> { long first=offset+written; late=Math.Max(0,first-songFrame); voices.Add((Math.Max(first,songFrame),copy)); });
        return late;
    }
    private void Run()
    {
        bool initialized=false;
        try
        {
            Check(CoInitializeEx(0,0)); initialized=true;
            Guid cls=new("BCDE0395-E52F-467C-8E3D-C4579291692E"), iid=new("A95664D2-9614-4F35-A746-DE8DB63617E6");
            Check(CoCreateInstance(ref cls,0,23,ref iid,out enumerator));
            Check(Method<Endpoint>(enumerator,4)(enumerator,0,0,out endpoint));
            iid=new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"); Check(Method<Activate>(endpoint,3)(endpoint,ref iid,23,0,out client));
            var format=new Format { Tag=3,Channels=2,Rate=(uint)rate,Bytes=(uint)rate*8,Align=8,Bits=32 };
            // AUTOCONVERTPCM | SRC_DEFAULT_QUALITY; output rate conversion is owned by the audio engine.
            Check(Method<Initialize>(client,3)(client,0,0x88000000,1_000_000,0,ref format,0));
            Check(Method<GetUInt>(client,4)(client,out capacity));
            iid=new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"); Check(Method<Service>(client,14)(client,ref iid,out render));
            iid=new("CD63314F-3FBA-4a1b-812C-EF96358728E7"); Check(Method<Service>(client,14)(client,ref iid,out clock));
            Check(Method<GetULong>(clock,3)(clock,out frequency));
            if(frequency==0) throw new InvalidOperationException("Zero audio clock frequency.");
            ready.SetResult();
            while(!commands.IsCompleted)
            {
                if(commands.TryTake(out var command,2)) command();
                if(state!=TransportState.Playing) continue;
                try { Observe(); Fill(); } catch(Exception ex) { Fault(ex); }
            }
        }
        catch(Exception ex) { Fault(ex); ready.TrySetException(ex); }
        finally
        {
            if(client!=0) Method<Simple>(client,11)(client);
            foreach(nint obj in new[]{clock,render,client,endpoint,enumerator}) if(obj!=0) Marshal.Release(obj);
            if(initialized) CoUninitialize();
        }
    }
    private void Observe()
    {
        int hr=Method<Position>(clock,4)(clock,out ulong position,out ulong qpc100ns); Check(hr);
        if(position<lastPosition) throw new InvalidOperationException("Audio clock moved backwards.");
        lastPosition=position;
        long song=checked(RhythmTime.Convert(offset,rate,1_000_000)+RhythmTime.DivideRounded((System.Numerics.BigInteger)position*1_000_000,frequency));
        long tick=RhythmTime.DivideRounded((System.Numerics.BigInteger)qpc100ns*Stopwatch.Frequency,10_000_000);
        if(tick < startTick || position==0) return; // no stable device anchor yet
        Volatile.Write(ref snapshot,new(Generation,interval,startTick,long.MaxValue,tick,song,Stopwatch.Frequency,1,1,Stopwatch.Frequency/4,hr==0?ClockQuality.Valid:ClockQuality.Degraded,state));
        // Keep the stream alive with silence after the song for late-window commitment.
    }
    private void Fill()
    {
        Check(Method<GetUInt>(client,6)(client,out uint padding));
        if(state==TransportState.Playing && padding==0 && written>0) throw new InvalidOperationException("Audio underrun; timing invalidated.");
        uint frames=capacity-padding; if(frames==0)return;
        Check(Method<Buffer>(render,3)(render,frames,out nint data));
        var block=new float[checked((int)frames*2)];
        long source=offset+written;
        int available=(int)Math.Min(frames,Math.Max(0,samples.Length/2-source));
        if(available>0) Array.Copy(samples,checked((int)source*2),block,0,available*2);
        foreach(var voice in voices)
        {
            long begin=Math.Max(source,voice.Frame), end=Math.Min(source+frames,voice.Frame+voice.Pcm.Length/2);
            for(long frame=begin;frame<end;frame++) for(int channel=0;channel<2;channel++)
                block[checked((int)(frame-source)*2+channel)] += voice.Pcm[checked((int)(frame-voice.Frame)*2+channel)];
        }
        voices.RemoveAll(v=>v.Frame+v.Pcm.Length/2<=source+frames);
        for(int i=0;i<block.Length;i++)block[i]=Math.Clamp(block[i],-1,1);
        Marshal.Copy(block,0,data,block.Length);
        Check(Method<ReleaseBuffer>(render,4)(render,frames,0)); written+=frames;
    }
    public void Dispose()
    {
        if(disposed)return; disposed=true; commands.CompleteAdding(); worker.Join(); commands.Dispose(); state=TransportState.Stopped;
    }
    [StructLayout(LayoutKind.Sequential,Pack=2)] private struct Format { public ushort Tag,Channels; public uint Rate,Bytes; public ushort Align,Bits,Extra; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Endpoint(nint self,int flow,int role,out nint endpoint);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Activate(nint self,ref Guid iid,uint context,nint parameters,out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Initialize(nint self,int mode,uint flags,long duration,long period,ref Format format,nint session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Service(nint self,ref Guid iid,out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Simple(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetUInt(nint self,out uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetULong(nint self,out ulong value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Position(nint self,out ulong position,out ulong qpc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Buffer(nint self,uint frames,out nint data);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReleaseBuffer(nint self,uint frames,uint flags);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved,uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid cls,nint outer,uint context,ref Guid iid,out nint result);
}
