using System;
using System.IO;
using UnityEngine;

namespace Oheangbu.App.World
{
    // File mechanics only. Each scene owns its schema, slot and validation policy.
    public sealed class AtomicJsonStore<T> where T:class
    {
        readonly string path;
        readonly Func<T,bool> valid;
        public string LoadStatus {get;private set;}
        public AtomicJsonStore(string path,Func<T,bool> valid){this.path=path;this.valid=valid;}
        public T Load()
        {
            bool found=false;
            foreach(string candidate in new[]{path,path+".bak",path+".tmp"}){
                if(!File.Exists(candidate))continue;found=true;
                try{
                    var state=JsonUtility.FromJson<T>(File.ReadAllText(candidate));
                    if(state!=null&&valid(state)){LoadStatus=candidate==path?"primary":candidate==path+".bak"?"backup":"temporary";return state;}
                }catch(Exception e)when(e is IOException||e is ArgumentException||e is UnauthorizedAccessException){Debug.LogWarning("Save recovery: "+e.Message);}
            }
            LoadStatus=found?"invalid":"new";return null;
        }
        public void Save(T state)
        {
            if(state==null||!valid(state))throw new InvalidDataException("Refusing invalid save state");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+".tmp";
            using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){
                using(var writer=new StreamWriter(stream,System.Text.Encoding.UTF8,1024,true)){writer.Write(JsonUtility.ToJson(state,true));writer.Flush();}
                stream.Flush(true);
            }
            // A recovered backup must not be replaced by a corrupt primary.
            bool primaryValid=false;
            if(File.Exists(path))try{var previous=JsonUtility.FromJson<T>(File.ReadAllText(path));primaryValid=previous!=null&&valid(previous);}catch(ArgumentException){}
            if(File.Exists(path))File.Replace(temporary,path,primaryValid?path+".bak":null);
            else File.Move(temporary,path);
            LoadStatus="primary";
        }
    }
}
