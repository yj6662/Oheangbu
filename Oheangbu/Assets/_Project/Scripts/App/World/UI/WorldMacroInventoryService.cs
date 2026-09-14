using System;
using System.Collections.Generic;

namespace Oheangbu.App.World.UI
{
    public sealed class WorldMacroCollectionNotice
    {
        public readonly string BundleId, Title, Message;
        public readonly IReadOnlyList<string> Letters;
        public WorldMacroCollectionNotice(string bundleId,string title,string message,IReadOnlyList<string> letters)
        { BundleId=bundleId;Title=title;Message=message;Letters=letters; }
    }

    public static class WorldMacroInventoryService
    {
        public static bool IsBundleCollected(UiProgress progress, FragmentBundleDefinition bundle)
        {
            if(progress==null||bundle==null)return false;
            foreach(var fragment in bundle.Fragments)
                if(!progress.HasItem(fragment.ItemId)||progress.knownSpellLetters==null||!progress.knownSpellLetters.Contains(fragment.Letter))return false;
            return true;
        }

        public static bool TryCollectBundle(UiProgress progress,string bundleId,out WorldMacroCollectionNotice notice)
        {
            notice=null;
            if(progress==null||!WorldMacroCollectionCatalog.TryGetBundle(bundleId,out var bundle))return false;
            var added=new List<string>();
            foreach(var fragment in bundle.Fragments)
            {
                bool changed=false;
                if(!progress.HasItem(fragment.ItemId)){progress.AddItem(fragment.ItemId);changed=true;}
                if(progress.LearnSpellLetter(fragment.Letter))changed=true;
                if(changed)added.Add(fragment.Letter);
            }
            if(added.Count==0)return false;
            notice=new WorldMacroCollectionNotice(bundle.Id,bundle.Title,"글자 조각 "+string.Join(" · ",added)+"을 찾았다.",added.ToArray());
            return true;
        }
    }
}
