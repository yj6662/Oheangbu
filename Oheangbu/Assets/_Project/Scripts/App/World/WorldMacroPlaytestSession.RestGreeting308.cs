using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 road inn (D308-16c; SPEC-CONTENT-PACING-308 가도 주막) [TEST]: a Rest talk row may carry Lines = what the keeper says on
    // the way to the rest. Data switch: PointService306.Lines of a Rest row (empty = exactly the previous behaviour: the view closes
    // and the rest runs in silence). Page n is said on the n-th pick of that row from that speaker in this session and the last page
    // repeats; it is shown in place through the one dialogue surface, the view closes after it, and TickDialogueRest306 rests as before.
    // Session memory only: instance fields (nothing static, nothing saved), so a reload starts from the first page again.
    public sealed partial class WorldMacroPlaytestSession
    {
        readonly Dictionary<string,string[]> restGreetingPages308=new Dictionary<string,string[]>();
        readonly Dictionary<string,int> restGreetingSaid308=new Dictionary<string,int>();

        void KeepRestGreeting308(string restId,string[] pages)
        {
            if(string.IsNullOrEmpty(restId))return;
            if(pages==null||pages.Length==0)restGreetingPages308.Remove(restId);else restGreetingPages308[restId]=pages;
        }
        void SayRestGreeting308(Talk306 t,string restId)
        {
            if(string.IsNullOrEmpty(restId)||!restGreetingPages308.TryGetValue(restId,out var pages))return;
            string key=t.Request.SourceId+">"+restId;restGreetingSaid308.TryGetValue(key,out int said);restGreetingSaid308[key]=said+1;
            FollowUp306(t,new[]{pages[Mathf.Min(said,pages.Length-1)]},false);
        }
    }
}
