using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    public sealed class CompactReliefGrid
    {
        readonly float[] values;
        readonly int width,height;
        readonly float cell;
        public CompactReliefGrid(CompactWorldLayoutSO layout)
        {
            if(layout.MountainRelief==null)return;
            width=layout.MountainReliefWidth;height=layout.MountainReliefHeight;cell=layout.MountainReliefCell;
            var bytes=layout.MountainRelief.bytes;
            if(width<2||height<2||cell<=0||bytes.Length!=width*height*4)throw new ArgumentException("Invalid Compact mountain relief grid");
            values=new float[width*height];Buffer.BlockCopy(bytes,0,values,0,bytes.Length);
            for(int i=0;i<values.Length;i++)if(!float.IsFinite(values[i]))throw new ArgumentException("Non-finite mountain relief");
        }
        public float Sample(float x,float z)
        {
            if(values==null)return 0;
            float fx=Mathf.Clamp(x/cell,0,width-1),fz=Mathf.Clamp(z/cell,0,height-1);
            int ix=Mathf.Min((int)fx,width-2),iz=Mathf.Min((int)fz,height-2);
            return Mathf.Lerp(Mathf.Lerp(values[iz*width+ix],values[iz*width+ix+1],fx-ix),
                Mathf.Lerp(values[(iz+1)*width+ix],values[(iz+1)*width+ix+1],fx-ix),fz-iz);
        }
    }
}
