using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace Oheangbu.App.Prologue
{
    [Serializable] public sealed class PrologueProgress
    {
        public int version=1;
        public string checkpoint="MineStart";
        public Vector3 checkpointPosition,position,dropPosition;
        public float yaw,hp=1,ink=1;
        public int currency,dropCurrency;
        public bool hasPosition;
        public bool renUsed;
        public bool hasVehicle;
        public bool hasEscortCheckpoint;
        public Vector3 escortCheckpointCargo, escortCheckpointVehicle;
        public Quaternion escortCheckpointVehicleRotation=Quaternion.identity;
        public Vector3 vehiclePosition, escortCargoPosition;
        public Quaternion vehicleRotation = Quaternion.identity;
        public Oheangbu.App.Demo.DemoEscortState escort = new Oheangbu.App.Demo.DemoEscortState();
        public List<string> completed=new List<string>();
        public List<string> defeated=new List<string>();
    }
    // A scoped repository, no static gameplay state. Atomic save + recoverable previous version.
    public sealed class PrologueProgressStore
    {
        readonly string path;
        public PrologueProgressStore(string path){this.path=path;}
        public PrologueProgress Load()
        {
            foreach(var p in new[]{path,path+".bak"})
            {
                if(!File.Exists(p))continue;
                try {var s=JsonUtility.FromJson<PrologueProgress>(File.ReadAllText(p));
                    if(s!=null && s.version==1 && s.currency>=0 && s.dropCurrency>=0 && s.completed!=null
                        && float.IsFinite(s.hp)&&s.hp>=0&&s.hp<=1&&float.IsFinite(s.ink)&&s.ink>=0&&s.ink<=1
                        && float.IsFinite(s.yaw)&&Finite(s.checkpointPosition)&&Finite(s.dropPosition)
                        && (!s.hasVehicle || Finite(s.vehiclePosition) && Finite(s.escortCargoPosition) && ValidRotation(s.vehicleRotation))
                        && (!s.hasEscortCheckpoint || s.escort!=null && s.escort.HasCheckpoint && s.checkpoint==s.escort.CheckpointId && Finite(s.escortCheckpointCargo) && Finite(s.escortCheckpointVehicle) && ValidRotation(s.escortCheckpointVehicleRotation))
                        && (s.escort==null || s.escort.IsValid())){s.escort??=new Oheangbu.App.Demo.DemoEscortState();return s;}
                } catch(Exception e){Debug.LogWarning("[Prologue] Save recovery: "+e.Message);}
            }
            return null;
        }
        static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        static bool ValidRotation(Quaternion q)=>float.IsFinite(q.x)&&float.IsFinite(q.y)&&float.IsFinite(q.z)&&float.IsFinite(q.w)&&Mathf.Abs(q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w-1)<.02f;
        public void Save(PrologueProgress state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp=path+".tmp";File.WriteAllText(tmp,JsonUtility.ToJson(state,true));
            if(File.Exists(path))File.Replace(tmp,path,path+".bak");else File.Move(tmp,path);
        }
        public static bool Complete(PrologueProgress state,string id,int reward)
        {
            if(state.completed.Contains(id))return false;
            state.completed.Add(id);state.currency+=Math.Max(0,reward);return true;
        }
        public static void Drop(PrologueProgress state,Vector3 position)
        {state.dropCurrency=state.currency;state.currency=0;state.dropPosition=position;}
        public static int Retrieve(PrologueProgress state)
        {int value=state.dropCurrency;state.currency+=value;state.dropCurrency=0;return value;}
    }
}
