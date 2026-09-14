using System;
using System.Threading;
using UnityEngine;

namespace Oheangbu.App.World
{
    // Temporary, explicitly armed review component. Never changes the audio buffer.
    // The audio thread only copies into one preallocated array; no Unity calls, allocation or IO.
    [DisallowMultipleComponent]
    public sealed class WorldMacroAudioOutputProbe : MonoBehaviour
    {
        private float[] samples;
        private volatile bool armed;
        private int writing, count, channels, callbacks;
        private long lastTimestamp, maxIntervalTicks;
        public int Samples => Volatile.Read(ref count);
        public int Channels => Volatile.Read(ref channels);
        public int Callbacks => Volatile.Read(ref callbacks);
        public double MaximumCallbackIntervalSeconds => Interlocked.Read(ref maxIntervalTicks)/(double)System.Diagnostics.Stopwatch.Frequency;
        public int SampleRate { get; private set; }
        public void Arm(int sampleRate,int seconds)
        {
            if(armed)throw new InvalidOperationException("Probe is already armed");
            SampleRate=sampleRate; samples=new float[checked(sampleRate*Mathf.Clamp(seconds,1,30)*8)];
            count=channels=callbacks=0;lastTimestamp=maxIntervalTicks=0;armed=true;
        }
        public bool StopAndCopy(out float[] result)
        {
            armed=false;
            if(Volatile.Read(ref writing)!=0){result=null;return false;}
            result=new float[Samples];Array.Copy(samples,result,result.Length);return true;
        }
        private void OnAudioFilterRead(float[] data,int channelCount)
        {
            if(!armed||samples==null)return;
            Interlocked.Exchange(ref writing,1);
            try
            {
                if(!armed)return;
                long now=System.Diagnostics.Stopwatch.GetTimestamp();
                if(lastTimestamp!=0&&now-lastTimestamp>maxIntervalTicks)Interlocked.Exchange(ref maxIntervalTicks,now-lastTimestamp);
                lastTimestamp=now;
                int copied=Math.Min(data.Length,samples.Length-count);
                if(copied>0){Array.Copy(data,0,samples,count,copied);count+=copied;channels=channelCount;callbacks++;}
            }
            finally{Volatile.Write(ref writing,0);}
        }
        private void OnDisable(){armed=false;}
    }
}
