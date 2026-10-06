using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Oheangbu.App.World
{
    // File mechanics only. Each scene owns its schema, slot and validation policy.
    // #307 phase 1 item 3 (Tools/Unity/Plan307/PERF_DESIGN.md C.1): Save = Serialize (main thread, the same pretty JSON) + Commit
    // (tmp, fsync, Replace), byte-identical to the former single method. SaveInBackground hands an already serialized state to one
    // writer task per store (one in flight + one pending, newest wins); a synchronous Save first waits for it, so commits to this
    // path never overlap or reorder. Results are collected on the main thread (TryTakeBackgroundResult); nothing Unity-bound runs
    // on the writer except JsonUtility.FromJson + the pure validity delegate (both thread-safe for a freshly parsed object).
    public sealed class AtomicJsonStore<T> where T:class
    {
        readonly string path;
        readonly Func<T,bool> valid;
        public string LoadStatus {get;private set;}
        public string FilePath=>path;   // #308 read-only: an isolated test store is recognised by the file it was opened on
        public AtomicJsonStore(string path,Func<T,bool> valid){this.path=path;this.valid=valid;}
        public T Load()
        {
            WaitForBackground();
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
            string json=Serialize(state);
            WaitForBackground();
            lock(commitGate)Commit(json,false);
        }
        // Validation + the exact text Save writes. Throws InvalidDataException for an invalid state, as Save always did.
        public string Serialize(T state)
        {
            if(state==null||!valid(state))throw new InvalidDataException("Refusing invalid save state");
            return JsonUtility.ToJson(state,true);
        }

        // ---- commit (shared by Save and the background writer; always under commitGate) ----
        readonly object commitGate=new object();
        // Last primary this store wrote: when the file still has that length and write time, the previous-primary validity is the
        // validity of that text (computed lazily from memory) instead of a disk re-read + parse. Any other writer changes the stamp.
        bool writtenKnown;long writtenLength;DateTime writtenTimeUtc;string writtenJson;int writtenValid=-1;
        void Commit(string json,bool background)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+".tmp";
            using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){
                using(var writer=new StreamWriter(stream,System.Text.Encoding.UTF8,1024,true)){writer.Write(json);writer.Flush();}
                stream.Flush(true);
            }
            // A recovered backup must not be replaced by a corrupt primary.
            bool primaryValid=false;
            if(File.Exists(path))
            {
                if(UnchangedSinceLastWrite())primaryValid=WrittenValid();
                else try{var previous=JsonUtility.FromJson<T>(File.ReadAllText(path));primaryValid=previous!=null&&valid(previous);}catch(ArgumentException){}
            }
            writtenKnown=false;writtenJson=null;writtenValid=-1;
            if(File.Exists(path))File.Replace(temporary,path,primaryValid?path+".bak":null);
            else File.Move(temporary,path);
            LoadStatus="primary";
            try{var info=new FileInfo(path);if(info.Exists){writtenLength=info.Length;writtenTimeUtc=info.LastWriteTimeUtc;writtenJson=json;writtenKnown=true;}}
            catch(Exception e)when(e is IOException||e is UnauthorizedAccessException){writtenKnown=false;}
            // Off the main thread the parse is free; on a synchronous Save it waits until the next commit actually needs it.
            if(background&&writtenKnown)WrittenValid();
        }
        bool UnchangedSinceLastWrite()
        {
            if(!writtenKnown)return false;
            try{var info=new FileInfo(path);return info.Exists&&info.Length==writtenLength&&info.LastWriteTimeUtc==writtenTimeUtc;}
            catch(Exception e)when(e is IOException||e is UnauthorizedAccessException){return false;}
        }
        bool WrittenValid()
        {
            if(writtenValid<0)
            {
                bool ok=false;
                try{var previous=JsonUtility.FromJson<T>(writtenJson);ok=previous!=null&&valid(previous);}catch(ArgumentException){}
                writtenValid=ok?1:0;
            }
            return writtenValid==1;
        }

        // ---- background writer: one in flight + one pending (newest wins) ----
        sealed class Job{public string Json;public object Tag;}
        public readonly struct BackgroundResult
        {
            public readonly object Tag;public readonly Exception Error;
            public BackgroundResult(object tag,Exception error){Tag=tag;Error=error;}
        }
        readonly object queueGate=new object();
        Job inFlight,pending;
        readonly Queue<BackgroundResult> results=new Queue<BackgroundResult>();
        int resultCount;
        public bool BackgroundBusy{get{lock(queueGate)return inFlight!=null||pending!=null;}}
        public bool HasBackgroundResults=>Volatile.Read(ref resultCount)>0;
        // json must come from Serialize (validated on the caller's thread). A pending job not yet started is superseded (dropped
        // without a result): only the newest state matters and the in-flight commit still reports its own result.
        public void SaveInBackground(string json,object tag)
        {
            if(json==null)throw new ArgumentNullException(nameof(json));
            var job=new Job{Json=json,Tag=tag};
            lock(queueGate)
            {
                if(inFlight!=null){pending=job;return;}
                inFlight=job;
            }
            Task.Run(Drain);
        }
        void Drain()
        {
            while(true)
            {
                Job job;lock(queueGate)job=inFlight;
                Exception error=null;
                try{lock(commitGate)Commit(job.Json,true);}
                catch(Exception e){error=e;}
                lock(queueGate)
                {
                    results.Enqueue(new BackgroundResult(job.Tag,error));Volatile.Write(ref resultCount,results.Count);
                    inFlight=pending;pending=null;
                    if(inFlight==null){Monitor.PulseAll(queueGate);return;}
                }
            }
        }
        // Blocks until the writer is idle (nothing in flight or pending). Results stay queued for TryTakeBackgroundResult.
        public void WaitForBackground()
        {
            lock(queueGate)while(inFlight!=null||pending!=null)Monitor.Wait(queueGate);
        }
        public bool TryTakeBackgroundResult(out BackgroundResult result)
        {
            lock(queueGate)
            {
                if(results.Count==0){result=default;return false;}
                result=results.Dequeue();Volatile.Write(ref resultCount,results.Count);return true;
            }
        }
    }
}
