using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    /// <summary>
    /// Original, deterministic meshes for ink VFX. Longitudinal direction is +Z;
    /// circular faces look along +Z. Build creates mesh data only, never scene objects.
    /// These are effect silhouettes, not replacement character or creature assets.
    /// </summary>
    public static class Vfx120MeshFactory
    {
        public static readonly string[] Families =
        {
            "Thorn", "Vine", "Leaf", "Flame", "Ember", "Cloud", "Sand", "Rock",
            "Shard", "Blade", "Ring", "Ripple", "Ribbon", "Lotus", "Seal", "Beast", "Tree", "WaveCrest", "BridgeSlab"
        };

        public static Mesh Build(string family)
        {
            var b = new Builder();
            switch (family)
            {
                case "Thorn": Thorn(b); break;
                case "Vine": Vine(b); break;
                case "Leaf": Leaf(b); break;
                case "Flame": Flame(b); break;
                case "Ember": Ember(b); break;
                case "Cloud": Cloud(b); break;
                case "Sand": Sand(b); break;
                case "Rock": Rock(b); break;
                case "Shard": Shard(b); break;
                case "Blade": Blade(b); break;
                case "Ring": Ring(b); break;
                case "Ripple": Ripple(b); break;
                case "Ribbon": Ribbon(b); break;
                case "Lotus": Lotus(b); break;
                case "Seal": Seal(b); break;
                case "Beast": Beast(b); break;
                case "Tree": Tree(b); break;
                case "WaveCrest": WaveCrest(b); break;
                case "BridgeSlab": BridgeSlab(b); break;
                default: throw new ArgumentException("Unknown spell mesh family: " + family, nameof(family));
            }

            return b.Finish("SM_Vfx120_" + family);
        }

        static float S(float t) => Mathf.Sin(t * Mathf.PI);

        static void BridgeSlab(Builder b)
        {
            var ring = new[] { new Vector2(-.45f,-.5f),new Vector2(.45f,-.5f),new Vector2(.5f,-.43f),new Vector2(.5f,.43f),new Vector2(.43f,.5f),new Vector2(-.43f,.5f),new Vector2(-.5f,.43f),new Vector2(-.5f,-.43f) };
            int first=b.Vertices.Count;
            for(int level=0;level<4;level++)
            {
                float y = level==0 ? -.12f : level==1 ? -.08f : level==2 ? .08f : .12f;
                float inset = level==0 || level==3 ? .93f : 1;
                for(int i=0;i<8;i++) b.Vertex(new Vector3(ring[i].x*inset,y,ring[i].y*inset),ring[i]+Vector2.one*.5f,Color.Lerp(new Color(.62f,.57f,.48f),Color.white,level/3f));
            }
            for(int level=0;level<3;level++) for(int i=0;i<8;i++)
            { int n=(i+1)%8;b.Quad(first+level*8+i,first+(level+1)*8+i,first+(level+1)*8+n,first+level*8+n); }
            int bottom=b.Vertices.Count;b.Vertex(new Vector3(0,-.12f,0),Vector2.one*.5f,Color.white);
            int top=b.Vertices.Count;b.Vertex(new Vector3(0,.12f,0),Vector2.one*.5f,Color.white);
            for(int i=0;i<8;i++) { int n=(i+1)%8;b.Triangle(bottom,first+i,first+n);b.Triangle(top,first+24+n,first+24+i); }
        }

        static void Tree(Builder b)
        {
            Tube(b, t => new Vector3(.1f * t * t, t - .5f, .03f * S(t)), t => .072f * (1 - t) + .012f, 18, 8, Vector2.one);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 2.39f;
                float height = -.1f + i * .063f;
                var start = new Vector3(.02f, height, 0);
                var reach = new Vector3(Mathf.Cos(a) * .34f, .24f - i * .017f, Mathf.Sin(a) * .34f);
                Tube(b, t => start + reach * t + Vector3.up * .045f * S(t), t => .026f * (1-t) + .003f, 8, 6, Vector2.one);
                for (int j = 0; j < 4; j++)
                {
                    float u = .3f + j * .21f;
                    var stem = start + reach * u;
                    var direction = new Vector3(Mathf.Cos(a + j * 1.1f) * .16f, .075f, Mathf.Sin(a+j*1.1f)*.16f);
                    Petal(b, stem, direction, .055f, .02f, 6, 3);
                }
            }
            for(int i=0;i<5;i++)
            {
                float a=i*Mathf.PI*2/5;
                Tube(b,t=>new Vector3(Mathf.Cos(a)*t*.2f,-.5f+.04f*(1-t),Mathf.Sin(a)*t*.2f),t=>.025f*(1-t)+.003f,5,5,Vector2.one);
            }
        }

        static void WaveCrest(Builder b)
        {
            int first=b.Vertices.Count, nx=42, ny=22;
            for(int x=0;x<=nx;x++)
            for(int j=0;j<=ny;j++)
            {
                float u=x/(float)nx,v=j/(float)ny;
                float across=u-.5f;
                float height=Mathf.Sin(v*Mathf.PI)*(.28f+.055f*Mathf.Cos(u*Mathf.PI*4));
                float curl=.17f*Mathf.Sin(v*Mathf.PI*1.25f);
                var p=new Vector3(across,height-.12f, v*.38f-.23f+curl);
                p.y*=.76f+.24f*Mathf.Sin(u*Mathf.PI);
                b.Vertex(p,new Vector2(u,v),Color.Lerp(new Color(.52f,.61f,.64f,1),Color.white,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.35f,.8f,v))));
            }
            for(int x=0;x<nx;x++)
            for(int y=0;y<ny;y++)
            {
                int a=first+x*(ny+1)+y;
                b.Quad(a,a+1,a+ny+2,a+ny+1);
            }
        }

        static void Thorn(Builder b)
        {
            Tube(b, t => new Vector3(.12f * t * t, .025f * S(t), t - .5f),
                t => .145f * Mathf.Pow(1f - t, .83f) + .0015f, 11, 9, new Vector2(1f, .72f));
            Tube(b, t => new Vector3(-.015f - .18f * t, 0f, -.23f + .25f * t),
                t => .055f * (1f - t) + .001f, 5, 7, Vector2.one);
        }

        static void Vine(Builder b)
        {
            Tube(b, t => new Vector3(.18f * Mathf.Sin(t * 2.4f * Mathf.PI),
                .09f * Mathf.Cos(t * 2.4f * Mathf.PI), t - .5f),
                t => .046f * (1f - .42f * t), 26, 7, Vector2.one);
            for (int n = 0; n < 3; n++)
            {
                float u = .22f + .27f * n;
                var stem = new Vector3(.18f * Mathf.Sin(u * 2.4f * Mathf.PI),
                    .09f * Mathf.Cos(u * 2.4f * Mathf.PI), u - .5f);
                float side = n % 2 == 0 ? 1f : -1f;
                Tube(b, t => stem + new Vector3(side * .18f * t, .025f * S(t), .12f * t),
                    t => .018f * (1f - t) + .0015f, 5, 5, Vector2.one);
                Petal(b, stem, new Vector3(side * .28f, .04f, .22f), .085f, .018f, 7, 4);
            }
        }

        static void Leaf(Builder b)
        {
            Petal(b, new Vector3(0, 0, -.5f), new Vector3(.035f, .055f, 1f), .235f, .065f, 14, 6);
            Tube(b, t => new Vector3(.035f * t, .025f + .08f * S(t), t - .5f),
                t => .009f * (1f - .7f * t), 12, 5, Vector2.one);
        }

        static void Flame(Builder b)
        {
            Tube(b, t => new Vector3(.17f * S(t * 1.1f) * t, .035f * S(t * 2f), t - .5f),
                t => .21f * Mathf.Pow(Mathf.Max(0f, S(t)), .65f) * (1f - .75f * t) + .001f,
                15, 9, new Vector2(1f, .46f));
            Tube(b, t => new Vector3(-.10f - .16f * S(t * .72f), .015f, -.4f + .68f * t),
                t => .11f * S(t) * (1f - .55f * t) + .001f, 11, 7, new Vector2(1f, .40f));
            Tube(b, t => new Vector3(.12f + .16f * S(t), -.01f, -.35f + .49f * t),
                t => .073f * S(t) + .001f, 9, 7, new Vector2(1f, .42f));
        }

        static void Ember(Builder b)
        {
            Tube(b, t => new Vector3(.035f * Mathf.Sin(t * 9f), .02f * Mathf.Cos(t * 8f), t - .5f),
                t => .15f * Mathf.Pow(Mathf.Max(0f, S(t)), .8f) + .002f, 7, 5, new Vector2(1f, .55f));
        }

        static void Cloud(Builder b)
        {
            // A ruyi/cloud head with inward hooks, rather than a row of spheres.
            Extrude(b, new[]
            {
                new Vector2(-.49f,-.13f),new Vector2(-.53f,.00f),new Vector2(-.47f,.14f),
                new Vector2(-.34f,.20f),new Vector2(-.26f,.19f),new Vector2(-.22f,.34f),
                new Vector2(-.10f,.44f),new Vector2(.01f,.47f),new Vector2(.15f,.40f),
                new Vector2(.22f,.25f),new Vector2(.34f,.30f),new Vector2(.48f,.22f),
                new Vector2(.54f,.08f),new Vector2(.49f,-.07f),new Vector2(.38f,-.14f),
                new Vector2(.25f,-.10f),new Vector2(.21f,.00f),new Vector2(.27f,.07f),
                new Vector2(.34f,.04f),new Vector2(.34f,-.01f),new Vector2(.29f,-.01f),
                new Vector2(.31f,-.04f),new Vector2(.38f,-.02f),new Vector2(.40f,.05f),
                new Vector2(.34f,.12f),new Vector2(.23f,.13f),new Vector2(.14f,.06f),
                new Vector2(.07f,-.15f),new Vector2(-.03f,-.27f),new Vector2(-.15f,-.21f),
                new Vector2(-.25f,-.04f),new Vector2(-.32f,.06f),new Vector2(-.40f,.01f),
                new Vector2(-.40f,-.07f),new Vector2(-.34f,-.10f),new Vector2(-.31f,-.04f),
                new Vector2(-.29f,-.11f),new Vector2(-.36f,-.17f)
            }, .035f);
        }

        static void Sand(Builder b)
        {
            Extrude(b, new[] {new Vector2(-.32f,-.48f),new Vector2(.20f,-.39f),
                new Vector2(.40f,-.08f),new Vector2(.21f,.43f),new Vector2(-.06f,.50f),
                new Vector2(-.42f,.11f)}, .12f);
        }

        static void Rock(Builder b)
        {
            Ellipsoid(b, Vector3.zero, new Vector3(.49f,.39f,.43f), 12, 8, .19f, false);
        }

        static void Shard(Builder b)
        {
            DiamondLoft(b, new[] {-0.5f,-.28f,.13f,.50f},
                new[] {.025f,.16f,.115f,.002f},new[] {.025f,.09f,.07f,.001f},
                new[] {-.04f,-.025f,.045f,.1f});
        }

        static void Blade(Builder b)
        {
            // Thin straight double-edged blade with a raised spine and short grip.
            DiamondLoft(b, new[] {-.30f,-.24f,.25f,.43f,.54f},
                new[] {.068f,.082f,.068f,.043f,.001f},new[] {.025f,.027f,.021f,.015f,.001f},null);
            Tube(b,t=>new Vector3(0,0,-.51f+.2f*t),t=>.031f,5,8,Vector2.one);
            Tube(b,t=>new Vector3(-.17f+.34f*t,0,-.285f+.018f*S(t)),
                t=>.025f*(.75f+.25f*S(t)),7,6,new Vector2(1f,.65f));
            Ellipsoid(b,new Vector3(0,0,-.525f),new Vector3(.04f,.03f,.028f),8,4,0,false);
        }

        static void Ring(Builder b)
        {
            // An analytic periodic frame avoids Tube's reference-axis switch at
            // near-vertical tangents. Both UV seams repeat the exact first position.
            const int rows=64,sides=6;
            int first=b.Vertices.Count,stride=sides+1;
            for(int i=0;i<=rows;i++)
            {
                float a=(i%rows)*Mathf.PI*2/rows;
                Vector3 radial=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                for(int j=0;j<=sides;j++)
                {
                    float cross=(j%sides)*Mathf.PI*2/sides;
                    b.Vertex(radial*(.46f+.031f*Mathf.Cos(cross))+
                        Vector3.forward*(.031f*Mathf.Sin(cross)),new Vector2(j/(float)sides,i/(float)rows));
                }
            }
            for(int i=0;i<rows;i++)for(int j=0;j<sides;j++)
            {
                int a=first+i*stride+j;
                b.Quad(a,a+stride,a+stride+1,a+1);
            }
            // Position continuity alone leaves a lighting seam after normal
            // recalculation, because the UV seam vertices have different neighbors.
            for(int i=1;i<rows;i++)b.SmoothNormalSeam(first+i*stride,first+i*stride+sides);
            for(int j=1;j<sides;j++)b.SmoothNormalSeam(first+j,first+rows*stride+j);
            b.SmoothNormalSeam(first,first+sides,first+rows*stride,first+rows*stride+sides);
        }

        static void Ripple(Builder b)
        {
            // Broken offset wavefronts leave negative space for the target silhouette.
            ArcRibbon(b,.48f,.028f,-.14f,1.70f*Mathf.PI,42,.025f);
            ArcRibbon(b,.35f,.020f,.28f,1.54f*Mathf.PI,38,-.018f);
            ArcRibbon(b,.23f,.014f,-.32f,1.82f*Mathf.PI,32,.014f);
        }

        static void Ribbon(Builder b)
        {
            int rows=22, first=b.Vertices.Count;
            for(int i=0;i<=rows;i++)
            {
                float t=i/(float)rows;
                float x=.15f*Mathf.Sin(t*2.7f*Mathf.PI),y=.08f*Mathf.Cos(t*3.3f*Mathf.PI);
                float w=.10f*Mathf.Pow(Mathf.Max(0f,S(t)),.55f)+.002f;
                var axis=new Vector3(Mathf.Cos(t*3f),Mathf.Sin(t*3f),0);
                b.Vertex(new Vector3(x,y,t-.5f)-axis*w,new Vector2(0,t));
                b.Vertex(new Vector3(x,y,t-.5f)+axis*w,new Vector2(1,t));
            }
            for(int i=0;i<rows;i++)b.Quad(first+2*i,first+2*i+1,first+2*i+3,first+2*i+2);
        }

        static void Lotus(Builder b)
        {
            for(int tier=0;tier<2;tier++)
            {
                int petals=tier==0?8:6;
                for(int n=0;n<petals;n++)
                {
                    float a=n*Mathf.PI*2/petals+(tier==0?0:.29f);
                    float radius=tier==0?.50f:.32f;
                    var end=new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,tier==0?.17f:.37f);
                    CupPetal(b,new Vector3(0,0,-.12f),end,tier==0?.14f:.115f,8,4);
                }
            }
            Ellipsoid(b,new Vector3(0,0,-.08f),new Vector3(.12f,.12f,.065f),10,5,0,false);
        }

        static void Seal(Builder b)
        {
            int first=b.Vertices.Count,rows=12;
            for(int i=0;i<=rows;i++)
            {
                float t=i/(float)rows;
                float y=.045f*Mathf.Sin(t*7f)-.016f*Mathf.Sin(t*13f);
                float half=.175f+(i==rows?-.021f:0);
                b.Vertex(new Vector3(-half,y,t-.5f),new Vector2(0,t));
                b.Vertex(new Vector3(half,y+.011f*Mathf.Sin(t*6f),t-.5f),new Vector2(1,t));
            }
            for(int i=0;i<rows;i++)b.Quad(first+2*i,first+2*i+1,first+2*i+3,first+2*i+2);
        }

        static void Beast(Builder b)
        {
            // A compact, round-headed folk-painting tiger spirit. Simple anatomy only;
            // striped vertex color is optional and needs a shader that reads mesh color.
            Ellipsoid(b,new Vector3(0,.06f,-.07f),new Vector3(.18f,.23f,.38f),12,8,.015f,true);
            Ellipsoid(b,new Vector3(0,.07f,.24f),new Vector3(.19f,.24f,.20f),10,7,0,true);
            Ellipsoid(b,new Vector3(0,.20f,.43f),new Vector3(.19f,.175f,.145f),12,8,0,false);
            Ellipsoid(b,new Vector3(0,.13f,.555f),new Vector3(.13f,.075f,.072f),8,5,0,false);
            Ellipsoid(b,new Vector3(0,.15f,.615f),new Vector3(.05f,.028f,.018f),6,4,0,false,new Color(.22f,.20f,.17f));
            for(int side=-1;side<=1;side+=2)
            {
                Ellipsoid(b,new Vector3(side*.137f,.355f,.405f),new Vector3(.058f,.075f,.039f),7,5,0,false);
                Ellipsoid(b,new Vector3(side*.137f,.232f,.548f),new Vector3(.037f,.018f,.015f),6,4,0,false,new Color(.15f,.13f,.11f));
                float sx=side*.12f;
                Tube(b,t=>new Vector3(sx,-.03f-.28f*t,.21f+.06f*t),
                    t=>.064f*(1f-.32f*t),7,7,Vector2.one);
                Ellipsoid(b,new Vector3(sx,-.315f,.30f),new Vector3(.068f,.039f,.105f),7,4,0,false);
                Tube(b,t=>new Vector3(sx,-.05f-.27f*t,-.28f-.08f*S(t)),
                    t=>.077f*(1f-.50f*t),7,7,Vector2.one);
                Ellipsoid(b,new Vector3(sx,-.325f,-.22f),new Vector3(.065f,.037f,.095f),7,4,0,false);
            }
            Tube(b,t=>new Vector3(.03f+.20f*S(t),.09f+.31f*S(t*.8f),-.38f-.36f*t+.16f*t*t),
                t=>.030f*(1f-.50f*t),17,6,Vector2.one);
        }

        static void Tube(Builder b,Func<float,Vector3> center,Func<float,float> radius,int rows,
            int sides,Vector2 aspect,bool closed=false)
        {
            int first=b.Vertices.Count;
            for(int i=0;i<=rows;i++)
            {
                float t=i/(float)rows;
                Vector3 p=center(t);
                Vector3 tangent=(center(Mathf.Min(1,t+.002f))-center(Mathf.Max(0,t-.002f))).normalized;
                Vector3 reference=Mathf.Abs(Vector3.Dot(tangent,Vector3.up))>.93f?Vector3.right:Vector3.up;
                Vector3 right=Vector3.Cross(reference,tangent).normalized;
                Vector3 up=Vector3.Cross(tangent,right).normalized;
                float r=Mathf.Max(radius(t),.0005f);
                for(int j=0;j<=sides;j++)
                {
                    float a=j*Mathf.PI*2/sides;
                    b.Vertex(p+right*(Mathf.Cos(a)*r*aspect.x)+up*(Mathf.Sin(a)*r*aspect.y),new Vector2(j/(float)sides,t));
                }
            }
            int stride=sides+1;
            for(int i=0;i<rows;i++)for(int j=0;j<sides;j++)
                b.Quad(first+i*stride+j,first+i*stride+j+1,first+(i+1)*stride+j+1,first+(i+1)*stride+j);
            if(!closed)
            {
                int a=b.Vertex(center(0),new Vector2(.5f,0)),z=b.Vertex(center(1),new Vector2(.5f,1));
                for(int j=0;j<sides;j++)
                {
                    b.Triangle(a,first+j+1,first+j);
                    b.Triangle(z,first+rows*stride+j,first+rows*stride+j+1);
                }
            }
        }

        static void Ellipsoid(Builder b,Vector3 center,Vector3 scale,int sides,int rows,
            float roughness,bool stripes,Color? tint=null)
        {
            // Each pole is one real vertex. Azimuth-dependent displacement at a
            // duplicated pole produced different heights and opened radial cracks.
            int north=b.Vertex(center+Vector3.Scale(Vector3.up,scale),new Vector2(.5f,0),tint);
            int first=b.Vertices.Count,stride=sides+1;
            for(int i=1;i<rows;i++)for(int j=0;j<=sides;j++)
            {
                float u=j/(float)sides,v=i/(float)rows,a=(j%sides)*Mathf.PI*2/sides,p=v*Mathf.PI;
                float latitude=Mathf.Sin(p);
                float irregular=1f+roughness*latitude*latitude*
                    (Mathf.Sin(a*3f+p*4f)+.45f*Mathf.Sin(a*5f-p*3f));
                var q=new Vector3(Mathf.Sin(p)*Mathf.Cos(a),Mathf.Cos(p),Mathf.Sin(p)*Mathf.Sin(a))*irregular;
                Color c=tint??Color.white;
                if(stripes&&Mathf.Sin(q.z*24f+q.y*5f)>.48f)c=new Color(.26f,.23f,.18f);
                b.Vertex(center+Vector3.Scale(q,scale),new Vector2(u,v),c);
            }
            int south=b.Vertex(center-Vector3.Scale(Vector3.up,scale),new Vector2(.5f,1),tint);
            for(int j=0;j<sides;j++)
            {
                b.Triangle(north,first+j+1,first+j);
                int a=first+(rows-2)*stride+j;
                b.Triangle(a,a+1,south);
            }
            for(int i=0;i<rows-2;i++)for(int j=0;j<sides;j++)
            {
                int a=first+i*stride+j,c=a+stride;
                b.Triangle(a,a+1,c);
                b.Triangle(a+1,c+1,c);
            }
            for(int i=0;i<rows-1;i++)b.SmoothNormalSeam(first+i*stride,first+i*stride+sides);
        }

        static void DiamondLoft(Builder b,float[] z,float[] width,float[] depth,float[] shift)
        {
            int first=b.Vertices.Count;
            for(int i=0;i<z.Length;i++)
            {
                float x=shift==null?0:shift[i];
                b.Vertex(new Vector3(x-width[i],0,z[i]),new Vector2(0,i/(float)(z.Length-1)));
                b.Vertex(new Vector3(x,depth[i],z[i]),new Vector2(.5f,i/(float)(z.Length-1)));
                b.Vertex(new Vector3(x+width[i],0,z[i]),new Vector2(1,i/(float)(z.Length-1)));
                b.Vertex(new Vector3(x,-depth[i],z[i]),new Vector2(.5f,i/(float)(z.Length-1)));
            }
            for(int i=0;i<z.Length-1;i++)for(int j=0;j<4;j++)
                b.Quad(first+i*4+j,first+(i+1)*4+j,first+(i+1)*4+(j+1)%4,first+i*4+(j+1)%4);
            b.Quad(first,first+1,first+2,first+3);
            int end=first+(z.Length-1)*4;b.Quad(end+3,end+2,end+1,end);
        }

        static void Petal(Builder b,Vector3 start,Vector3 direction,float halfWidth,float ridge,int rows,int columns)
        {
            int first=b.Vertices.Count;
            Vector3 along=direction.normalized;
            Vector3 across=Vector3.Cross(Vector3.up,along).normalized;
            if(across.sqrMagnitude<.1f)across=Vector3.right;
            for(int i=0;i<=rows;i++)for(int j=0;j<=columns;j++)
            {
                float t=i/(float)rows,u=j/(float)columns,q=u*2f-1f;
                float width=halfWidth*Mathf.Pow(Mathf.Max(.002f,S(t)),.85f);
                Vector3 p=start+direction*t+across*(q*width)+Vector3.up*(ridge*S(t)*(1f-q*q));
                b.Vertex(p,new Vector2(u,t));
            }
            for(int i=0;i<rows;i++)for(int j=0;j<columns;j++)
            {int a=first+i*(columns+1)+j;b.Quad(a,a+1,a+columns+2,a+columns+1);}
        }

        static void CupPetal(Builder b,Vector3 start,Vector3 end,float halfWidth,int rows,int columns)
        {
            int first=b.Vertices.Count;
            Vector3 outward=new Vector3(end.x,end.y,0).normalized;
            Vector3 across=new Vector3(-outward.y,outward.x,0);
            for(int i=0;i<=rows;i++)for(int j=0;j<=columns;j++)
            {
                float t=i/(float)rows,u=j/(float)columns,q=u*2f-1;
                float width=halfWidth*Mathf.Pow(Mathf.Max(.001f,S(t)),.75f);
                Vector3 p=Vector3.Lerp(start,end,t)+across*(q*width);
                p.z+=.09f*S(t)*(q*q-.6f)+.06f*t*t;
                b.Vertex(p,new Vector2(u,t));
            }
            for(int i=0;i<rows;i++)for(int j=0;j<columns;j++)
            {int a=first+i*(columns+1)+j;b.Quad(a,a+1,a+columns+2,a+columns+1);}
        }

        static void ArcRibbon(Builder b,float radius,float width,float start,float length,int rows,float wave)
        {
            int first=b.Vertices.Count;
            for(int i=0;i<=rows;i++)
            {
                float t=i/(float)rows,a=start+length*t;
                float r=radius+wave*Mathf.Sin(a*5);
                float w=width*Mathf.Pow(Mathf.Max(.004f,S(t)),.35f);
                for(int j=0;j<2;j++)b.Vertex(new Vector3(Mathf.Cos(a)*(r+(j==0?-w:w)),
                    Mathf.Sin(a)*(r+(j==0?-w:w)),.012f*Mathf.Sin(a*3)),new Vector2(j,t));
            }
            for(int i=0;i<rows;i++)b.Quad(first+2*i,first+2*i+1,first+2*i+3,first+2*i+2);
        }

        static float Cross(Vector2 a,Vector2 b,Vector2 c) =>
            (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);

        static void Extrude(Builder b,Vector2[] input,float thickness)
        {
            var polygon=new List<Vector2>(input);float area=0;
            for(int i=0;i<polygon.Count;i++)
            {Vector2 a=polygon[i],c=polygon[(i+1)%polygon.Count];area+=a.x*c.y-c.x*a.y;}
            if(area<0)polygon.Reverse();
            int first=b.Vertices.Count,n=polygon.Count;
            for(int side=0;side<2;side++)for(int i=0;i<n;i++)
            {Vector2 p=polygon[i];b.Vertex(new Vector3(p.x,(side==0?-.5f:.5f)*thickness,p.y),p+Vector2.one*.5f);}
            var remaining=new List<int>();for(int i=0;i<n;i++)remaining.Add(i);
            int safety=n*n;
            while(remaining.Count>2&&safety-->0)
            {
                bool found=false;
                for(int k=0;k<remaining.Count;k++)
                {
                    int a=remaining[(k+remaining.Count-1)%remaining.Count],c=remaining[k],d=remaining[(k+1)%remaining.Count];
                    if(Cross(polygon[a],polygon[c],polygon[d])<=.000001f)continue;
                    bool inside=false;
                    foreach(int q in remaining)
                    {
                        if(q==a||q==c||q==d)continue;
                        if(Cross(polygon[a],polygon[c],polygon[q])>=0&&Cross(polygon[c],polygon[d],polygon[q])>=0&&Cross(polygon[d],polygon[a],polygon[q])>=0)
                        {inside=true;break;}
                    }
                    if(inside)continue;
                    b.Triangle(first+a,first+c,first+d);b.Triangle(first+n+d,first+n+c,first+n+a);
                    remaining.RemoveAt(k);found=true;break;
                }
                if(!found)throw new InvalidOperationException("VFX outline is not a simple triangulable polygon.");
            }
            for(int i=0;i<n;i++){int j=(i+1)%n;b.Quad(first+i,first+n+i,first+n+j,first+j);}
        }

        sealed class Builder
        {
            public readonly List<Vector3> Vertices=new List<Vector3>();
            readonly List<Vector2> uv=new List<Vector2>();
            readonly List<Color> colors=new List<Color>();
            readonly List<int> triangles=new List<int>();
            readonly List<int[]> normalSeams=new List<int[]>();
            public void SmoothNormalSeam(params int[] indices){normalSeams.Add(indices);}
            public int Vertex(Vector3 p,Vector2 tex,Color? color=null)
            {int index=Vertices.Count;Vertices.Add(p);uv.Add(tex);colors.Add(color??Color.white);return index;}
            public void Triangle(int a,int b,int c)
            {
                if(Vector3.Cross(Vertices[b]-Vertices[a],Vertices[c]-Vertices[a]).sqrMagnitude<1e-14f)return;
                triangles.Add(a);triangles.Add(b);triangles.Add(c);
            }
            public void Quad(int a,int b,int c,int d){Triangle(a,b,c);Triangle(a,c,d);}
            public Mesh Finish(string name)
            {
                Vector3 lo=Vertices[0],hi=Vertices[0];
                foreach(Vector3 p in Vertices){lo=Vector3.Min(lo,p);hi=Vector3.Max(hi,p);}
                Vector3 center=(lo+hi)*.5f,extent=hi-lo;
                float size=Mathf.Max(extent.x,Mathf.Max(extent.y,extent.z));
                for(int i=0;i<Vertices.Count;i++)Vertices[i]=(Vertices[i]-center)/size;
                var mesh=new Mesh{name=name};
                mesh.SetVertices(Vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);
                mesh.RecalculateNormals();
                if(normalSeams.Count>0)
                {
                    Vector3[] normals=mesh.normals;
                    foreach(int[] seam in normalSeams)
                    {
                        Vector3 normal=Vector3.zero;
                        foreach(int index in seam)normal+=normals[index];
                        normal.Normalize();
                        foreach(int index in seam)normals[index]=normal;
                    }
                    mesh.normals=normals;
                }
                mesh.RecalculateTangents();mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
