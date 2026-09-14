using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    public enum WorldMacroSaveSlotStatus { New, Primary, Backup, Temporary, Invalid }

    public sealed class WorldMacroSaveSlotInfo
    {
        public readonly WorldMacroSaveSlotStatus Status;
        public readonly string PrimaryPath, LoadedPath, Error;
        public readonly WorldMacroProgress Progress;
        public WorldMacroSaveSlotInfo(WorldMacroSaveSlotStatus status,string primaryPath,string loadedPath,WorldMacroProgress progress,string error)
        {Status=status;PrimaryPath=primaryPath;LoadedPath=loadedPath;Progress=progress;Error=error;}
    }

    // Title/menu helper: inspection is read-only; starting new archives every slot artifact before returning.
    public static class WorldMacroSaveSlot
    {
        public static string GetPrimaryPath(string directory,string slotName)
        {
            if(string.IsNullOrWhiteSpace(directory))throw new ArgumentException("Save directory is required",nameof(directory));
            if(string.IsNullOrWhiteSpace(slotName)||slotName.IndexOfAny(Path.GetInvalidFileNameChars())>=0||slotName.Contains("/")||slotName.Contains("\\")||slotName=="."||slotName=="..")
                throw new ArgumentException("Save slot must be a file name",nameof(slotName));
            return Path.Combine(directory,slotName+".json");
        }

        public static WorldMacroSaveSlotInfo Inspect(string directory,string slotName)
        {
            string primary;
            try{primary=GetPrimaryPath(directory,slotName);}catch(Exception e)when(e is ArgumentException||e is NotSupportedException){return new WorldMacroSaveSlotInfo(WorldMacroSaveSlotStatus.Invalid,null,null,null,e.Message);}
            bool found=false;string lastError=null;
            foreach(var candidate in new[]{primary,primary+".bak",primary+".tmp"})
            {
                if(!File.Exists(candidate))continue;found=true;
                try
                {
                    var raw=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(candidate));
                    if(!WorldMacroProgress.Valid(raw)){lastError="저장 데이터 형식이 올바르지 않습니다.";continue;}
                    var migrated=WorldMacroProgress.MigrateToCurrent(raw);
                    var status=candidate==primary?WorldMacroSaveSlotStatus.Primary:
                        candidate==primary+".bak"?WorldMacroSaveSlotStatus.Backup:WorldMacroSaveSlotStatus.Temporary;
                    return new WorldMacroSaveSlotInfo(status,primary,candidate,migrated,null);
                }
                catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException){lastError=e.Message;}
            }
            return found?new WorldMacroSaveSlotInfo(WorldMacroSaveSlotStatus.Invalid,primary,null,null,lastError??"저장 파일을 읽을 수 없습니다."):
                new WorldMacroSaveSlotInfo(WorldMacroSaveSlotStatus.New,primary,null,null,null);
        }

        public static bool TryArchiveForNew(string directory,string slotName,out string archiveDirectory,out string error)
        {return TryArchiveForNew(directory,slotName,DateTime.UtcNow,out archiveDirectory,out error);}

        public static bool TryArchiveForNew(string directory,string slotName,DateTime utcNow,out string archiveDirectory,out string error)
        {
            archiveDirectory=null;error=null;string primary;
            try{primary=GetPrimaryPath(directory,slotName);}catch(Exception e)when(e is ArgumentException||e is NotSupportedException){error=e.Message;return false;}
            var sources=new List<string>();foreach(var path in new[]{primary,primary+".bak",primary+".tmp"})if(File.Exists(path))sources.Add(path);
            if(sources.Count==0)return true;
            archiveDirectory=Path.Combine(directory,slotName+".archive-"+utcNow.ToUniversalTime().ToString("yyyyMMdd-HHmmss-fff"));
            int suffix=1;string baseDirectory=archiveDirectory;while(Directory.Exists(archiveDirectory))archiveDirectory=baseDirectory+"-"+(suffix++);
            var moved=new List<KeyValuePair<string,string>>();
            try
            {
                Directory.CreateDirectory(archiveDirectory);
                foreach(var source in sources){string destination=Path.Combine(archiveDirectory,Path.GetFileName(source));File.Move(source,destination);moved.Add(new KeyValuePair<string,string>(source,destination));}
                return true;
            }
            catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is NotSupportedException)
            {
                error="저장 보관 실패: "+e.Message;
                for(int i=moved.Count-1;i>=0;i--)try{if(File.Exists(moved[i].Value)&&!File.Exists(moved[i].Key))File.Move(moved[i].Value,moved[i].Key);}catch(Exception rollback){Debug.LogError("Save archive rollback failed: "+rollback.Message);}
                try{if(Directory.Exists(archiveDirectory)&&Directory.GetFileSystemEntries(archiveDirectory).Length==0)Directory.Delete(archiveDirectory);}catch(Exception cleanup){Debug.LogWarning("Save archive cleanup failed: "+cleanup.Message);}
                return false;
            }
        }
    }
}
