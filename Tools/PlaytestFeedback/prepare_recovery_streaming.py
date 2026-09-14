"""Preserve candidate selection exactly while exposing a resumable iterator."""
from pathlib import Path
p=Path('Oheangbu/Assets/_Project/Scripts/App/World/Dressing/WorldMacroDressingRenderer.cs')
s=p.read_text(encoding='utf-8-sig')
if 'GenerateSteps(' not in s:
    a=s.index('        Item[] Generate('); b=s.index('        static long FixedId',a)
    old=s[a:b]
    body=old[old.index('            var output='):]
    body=body.replace('var output=new List<Item>();Vector2 origin=Origin(cell);','Vector2 origin=Origin(cell);')
    body=body.replace('            for(int iz=sz;iz<ez;iz++)for(int ix=sx;ix<ex;ix++)\n            {','''            // Bound the candidate grid without changing its global coordinates or hash.
            sx=Mathf.Max(sx,Mathf.FloorToInt(min.x/spacing)-1);sz=Mathf.Max(sz,Mathf.FloorToInt(min.y/spacing)-1);
            ex=Mathf.Min(ex,Mathf.CeilToInt(max.x/spacing)+1);ez=Mathf.Min(ez,Mathf.CeilToInt(max.y/spacing)+1);
            for(int iz=sz;iz<ez;iz++)for(int ix=sx;ix<ex;ix++)
            {
                yield return null; // Every candidate, including rejected ones, is budgeted.''')
    body=body.replace('if(x<origin.x||z<origin.y||x>=origin.x+256||z>=origin.y+256)continue;','if(x<origin.x||z<origin.y||x>=origin.x+256||z>=origin.y+256||x<min.x||z<min.y||x>=max.x||z>=max.y)continue;')
    body=body.replace('output.Add(new Item{','yield return new Item{').replace('Position=pos,Scale=scale,Prototype=proto});','Position=pos,Scale=scale,Prototype=proto};')
    body=body.replace('            return output.ToArray();\n','')
    replacement='''        Item[] Generate(WorldMacroDressingSheetSO.Cell cell,int category,bool far,bool cover=false,bool low=false)
        {
            var output=new List<Item>();var origin=Origin(cell);
            foreach(var item in GenerateSteps(cell,category,far,cover,low,origin,origin+Vector2.one*256))
                if(item.HasValue)output.Add(item.Value);
            return output.ToArray();
        }
        IEnumerable<Item?> GenerateSteps(WorldMacroDressingSheetSO.Cell cell,int category,bool far,bool cover,bool low,Vector2 min,Vector2 max)
        {
'''+body
    s=s[:a]+replacement+s[b:]
    s=s.replace('            live.Clear();cameraCaches.Clear();','            ResetStreaming();live.Clear();cameraCaches.Clear();')
    s=s.replace('            guard?.Invoke();\n            // Geometry', '            if(UseStreaming){PrepareStreaming(eye,budget,guard);return;}\n            guard?.Invoke();\n            // Geometry',1)
    s=s.replace('            if(!SystemInfo.supportsInstancing)return;', '''            if(Application.isPlaying&&!AllowDiagnosticCameras&&camera!=Observer)return;
            if(!SystemInfo.supportsInstancing)return;''',1)
    s=s.replace('            if(!Application.isPlaying)PrepareView(camera.transform.position,4);','''            if(!Application.isPlaying)PrepareView(camera.transform.position,4);
            if(UseStreaming){DrawStreaming(camera);return;}''',1)
    s=s.replace('{PrepareView(collisionFocus,12);collisionInitialized=true;}', '{if(!UseStreaming)PrepareView(collisionFocus,12);collisionInitialized=true;}')
    s=s.replace('            foreach(var cell in live.Values)\n            {\n                // Candidate', '''            if(UseStreaming)CollectStreamingCollision(collisionFocus,collisionEnd,radius,candidates);
            foreach(var cell in live.Values)
            {
                // Candidate''',1)
    s=s.replace('{nextCollision=Time.unscaledTime+.12f;UpdateColliders();}', '{nextCollision=Time.unscaledTime+.12f;long start=System.Diagnostics.Stopwatch.GetTimestamp();UpdateColliders();LastCollisionMilliseconds=ElapsedMs(start);}')
    p.write_text(s,encoding='utf-8')
print(p)
