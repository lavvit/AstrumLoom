using System.Diagnostics;
using AstrumLoom;
using AstrumLoom.Rhythm;


namespace Sandbox;

internal sealed class RhythmDemoScene : Scene
{
    private sealed record View(long SongUs, int Hits, int Misses, string Status);
    private View view=new(0,0,0,"Starting");
    private CancellationTokenSource? stop;
    private Thread? worker;
    private readonly RhythmChart chart=new("demo-v1",Enumerable.Range(0,24).Select(i=>new RhythmNote(i,i%4,2_000_000+i*500_000)));
    public override void Enable()
    {
        stop=new(); worker=new Thread(()=>Run(stop.Token)) { IsBackground=true, Name="Rhythm judge demo" }; worker.Start();
    }
    private void Run(CancellationToken token)
    {
        try
        {
            const int rate=48000;
            var pcm=new float[rate*16*2];
            foreach(var note in chart.Notes)
            {
                int start=(int)RhythmTime.Convert(note.TimeUs,1_000_000,rate);
                for(int n=0;n<1200;n++) { float value=(float)(.12*Math.Sin(2*Math.PI*880*n/rate)*(1-n/1200.0)); pcm[(start+n)*2]=pcm[(start+n)*2+1]=value; }
            }
            using var raw=new WindowsRawInputSource();
            using var audio=new WasapiTransport(pcm,rate);
            var session=new RhythmSession(raw,audio,chart,new Dictionary<int,int>{{0x20,0},{0x21,1},{0x24,2},{0x25,3}},50_000);
            audio.Play();
            int traceCount=0;
            var keySound=new float[2400];
            for(int n=0;n<1200;n++)keySound[n*2]=keySound[n*2+1]=(float)(.12*Math.Sin(2*Math.PI*1320*n/rate)*(1-n/1200.0));
            while(!token.IsCancellationRequested)
            {
                session.Pump();
                while(traceCount<session.InputTrace.Count)
                {
                    var entry=session.InputTrace[traceCount++];
                    if(entry.Mapped.Action==InputAction.Down)audio.Schedule(keySound,RhythmTime.Convert(entry.Mapped.SongUs,1_000_000,rate));
                }
                long song=0;
                if(audio.TryGetClock(out var c))c.TryMap(Stopwatch.GetTimestamp(),out song);
                var results=session.Judge.Results;
                Volatile.Write(ref view,new(song,results.Count(r=>r.Kind==Judgement.Hit),results.Count(r=>r.Kind==Judgement.Miss),session.Judge.Failure ?? (session.Judge.Complete?"Complete":"D F J K")));
                if(session.Judge.Failure!=null || session.Judge.Complete)break;
                token.WaitHandle.WaitOne(2);
            }
        }
        catch(Exception ex) { Volatile.Write(ref view,new(0,0,0,ex.Message)); }
    }
    public override void Draw()
    {
        var current=Volatile.Read(ref view);
        Drawing.Box(0,0,AstrumCore.Width,AstrumCore.Height,new Color(16,20,35));
        Drawing.Text(680,180,"Rhythm timing / D F J K",Color.White);
        Drawing.Text(680,220,$"Hit {current.Hits}   Miss {current.Misses}",Color.White);
        Drawing.Text(680,260,current.Status,Color.White);
        for(int lane=0;lane<4;lane++)Drawing.Box(100+lane*140,120,100,450,new Color(35,40,65));
        Drawing.Box(100,500,520,3,Color.White);
        foreach(var note in chart.Notes)
        {
            double y=500-(note.TimeUs-current.SongUs)/5000.0;
            if(y>=120 && y<=570)Drawing.Box(100+note.Lane*140,y,100,12,new Color(80,210,230));
        }
    }
    public override void Disable() { stop?.Cancel(); worker?.Join(); stop?.Dispose(); stop=null; worker=null; }
}
