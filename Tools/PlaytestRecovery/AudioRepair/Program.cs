using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Oheangbu.App.World;

class Program
{
    static void Main()
    {
        var checks=new List<object>();
        void Check(bool okay,string name,object evidence) { checks.Add(new{name,pass=okay,evidence});if(!okay)throw new Exception(name); }
        const int rate=48000;
        // DC input isolates envelope discontinuities from a sound's own transient.
        float[] Render(int block,bool loop,out WorldMacroAudioEnvelopeState state)
        {
            state=new WorldMacroAudioEnvelopeState();state.Start(rate,.7,.018,.06,.75f,loop);
            var data=new float[rate];int at=0;
            while(at<data.Length)
            {
                int count=Math.Min(block,data.Length-at);var chunk=new float[count];Array.Fill(chunk,1f);
                state.Process(chunk,1);Array.Copy(chunk,0,data,at,count);at+=count;
            }
            return data;
        }
        var a=Render(256,false,out var stateA);var b=Render(1024,false,out var stateB);
        double difference=0,maxStep=0;for(int i=1;i<a.Length;i++){difference=Math.Max(difference,Math.Abs(a[i]-b[i]));maxStep=Math.Max(maxStep,Math.Abs(a[i]-a[i-1]));}
        Check(difference==0,"Block size and main frame independence",new{maximumDifference=difference,blocks="256/1024",unityApiUsed=false});
        Check(stateA.IsSilent&&stateB.IsSilent&&maxStep<.0014,"Natural endpoint and continuous attack",new{maxStep,a0=a[0],last=a[^1]});
        var release=new WorldMacroAudioEnvelopeState();release.Start(rate,5,.018,.06,.75f,true);
        var before=new float[4800];Array.Fill(before,1);release.Process(before,1);release.Release(.025);
        var after=new float[4800];Array.Fill(after,1);release.Process(after,1);
        double releaseStep=Math.Abs(after[0]-before[^1]);for(int i=1;i<after.Length;i++)releaseStep=Math.Max(releaseStep,Math.Abs(after[i]-after[i-1]));
        Check(release.IsSilent&&after[1201]==0&&releaseStep<.001,"25 ms release completes without a main-thread tick",new{maximumStep=releaseStep,simulatedNextTickSeconds=.1});
        // Compared with the former 18 ms fade evaluated only on a 10 Hz Update.
        Check(.75/maxStep>500,"Previous 10 Hz attack step reduction",new{beforeStep=.75,afterStep=maxStep,reduction=.75/maxStep});
        release.Start(rate,5,.018,.06,.6f,true);var fresh=new float[1024];Array.Fill(fresh,1);release.Process(fresh,1);
        Check(!release.IsSilent&&fresh[0]==0&&fresh[^1]>.59,"Stolen slot resets release state with a new attack",new{first=fresh[0],last=fresh[^1]});
        release.Release(.04);release.Start(rate,5,.018,.06,.5f,true);release.Release(.02);
        var immediate=new float[2048];Array.Fill(immediate,1);release.Process(immediate,1);
        Check(release.IsSilent&&Array.TrueForAll(immediate,x=>x==0),"Start and release before first DSP callback stays silent",new{samples=immediate.Length});
        var stereo=new WorldMacroAudioEnvelopeState();stereo.Start(rate,1,.018,.06,.5f,true);var pairs=new float[4000];Array.Fill(pairs,1);stereo.Process(pairs,2);
        bool equal=true;for(int i=0;i<pairs.Length;i+=2)equal&=pairs[i]==pairs[i+1];
        Check(equal,"Stereo channel gains remain identical",new{frames=2000});
        var voices=new WorldMacroAudioEnvelopeState[16];var blocks=new float[16][];
        for(int i=0;i<16;i++){voices[i]=new WorldMacroAudioEnvelopeState();voices[i].Start(rate,10,.018,.06,.5f,true);blocks[i]=new float[2048];Array.Fill(blocks[i],1);}
        for(int warm=0;warm<100;warm++)for(int i=0;i<16;i++)voices[i].Process(blocks[i],2);
        var watch=new System.Diagnostics.Stopwatch();long allocated=GC.GetAllocatedBytesForCurrentThread();watch.Start();
        for(int block=0;block<300;block++)for(int i=0;i<16;i++)voices[i].Process(blocks[i],2);
        watch.Stop();long processAllocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Check(processAllocated<=0,"DSP process allocates no memory",new{processAllocated,voices=16,blockFrames=1024,meanMillisecondsPerSixteenVoices=watch.Elapsed.TotalMilliseconds/300,scope="Offline managed processor only; excludes Unity mixer and hardware driver."});
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
        var output=Path.Combine(root,"Art/PlaytestRecovery/AudioRepair");Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"envelope_contracts.json"),JsonSerializer.Serialize(new{status="PASS",scope="Actual production DSP processor, offline PCM. Not Unity routing, game playback, device or listening approval.",checks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS {checks.Count} DSP processor contracts; {output}");
    }
}
