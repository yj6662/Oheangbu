using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace Oheangbu.Vol2.Rules
{
    public sealed class Vol2ContractValidator
    {
        public sealed class Issue
        {
            public string Code { get; }
            public string Location { get; }
            public string Detail { get; }
            internal Issue(string code, string location, string detail) { Code = code; Location = location; Detail = detail; }
            public override string ToString() => Code + " " + Location + ": " + Detail;
        }
        public sealed class Result
        {
            public bool CanCast => Errors.Count == 0;
            public NumbersV3Dto Numbers { get; }
            public ReadOnlyCollection<Vol2SpellRowDto> Spells { get; }
            public Vol2ImportManifestDto Manifest { get; }
            public ReadOnlyCollection<Issue> Errors { get; }
            internal Result(NumbersV3Dto numbers, IEnumerable<Vol2SpellRowDto> spells, Vol2ImportManifestDto manifest, List<Issue> errors)
            { Numbers = errors.Count == 0 ? numbers.Validated() : null; Spells = RuleTypesV3.Freeze(spells); Manifest = manifest; Errors = errors.AsReadOnly(); }
        }
        public Result Import(IDictionary<string, byte[]> sources, Vol2ImportManifestDto expected = null, string roundTripDtoHash = null)
        {
            var errors = new List<Issue>(); var rows = new List<Vol2SpellRowDto>(); NumbersV3Dto numbers = null; Vol2ImportManifestDto manifest = null;
            void Error(string code, string where, string detail) => errors.Add(new Issue(code, where, detail));
            foreach (string name in new[] { "numbers", "table", "markdown", "checker", "sheet", "contract" })
                if (!sources.TryGetValue(name, out var bytes) || bytes == null || bytes.Length == 0) Error("MissingSource", name, "Required source bytes");
            if (errors.Count != 0) return new Result(null, rows, null, errors);
            try
            {
                numbers = ReadNumbers(sources["numbers"], Error);
                ValidateNumbers(numbers, Error);
                if (errors.Count == 0)
                {
                    rows = ReadSpells(sources["table"], numbers, Error);
                    ValidateDocument(Text(sources["markdown"]), Text(sources["sheet"]), numbers, Error);
                    manifest = Vol2ImportManifestDto.Create(sources, numbers, rows);
                    if (expected != null && !expected.Matches(manifest, roundTripDtoHash)) Error("ManifestMismatch", "manifest", "Schema, importer, source or DTO/SO hash differs");
                }
            }
            catch (FormatException e) { Error("Format", "input", e.Message); }
            catch (OverflowException e) { Error("NumericRange", "numbers", e.Message); }
            catch (DivideByZeroException e) { Error("NumericRange", "numbers", e.Message); }
            catch (DecoderFallbackException e) { Error("Utf8", "input", e.Message); }
            return new Result(numbers, rows, manifest, errors);
        }
        public Result LoadDto(NumbersV3Dto numbers, IEnumerable<Vol2SpellRowDto> spells,
            Vol2ImportManifestDto stored, Vol2ImportManifestDto expected)
        {
            var errors = new List<Issue>();
            var rows = spells == null ? new List<Vol2SpellRowDto>() : new List<Vol2SpellRowDto>(spells);
            if (numbers == null || rows.Count != 120 || stored == null || expected == null)
                errors.Add(new Issue("DtoInput", "SO/DTO", "Numbers, 120 spells and both manifests required"));
            else
            {
                string hash = Vol2ImportManifestDto.HashText(Vol2ImportManifestDto.CanonicalDto(numbers, rows));
                if (!stored.Matches(expected, hash)) errors.Add(new Issue("ManifestMismatch", "SO/DTO", "Reconstructed DTO differs from the trusted import manifest"));
            }
            return new Result(numbers, rows, stored, errors);
        }
        static string Text(byte[] bytes) => new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n");
        static decimal D(string s) => decimal.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        static string Match(string s, string pattern, int group = 1) { var m = Regex.Match(s, pattern); return m.Success ? m.Groups[group].Value : ""; }
        static decimal? Num(string s, string pattern, int group = 1) { var v = Match(s, pattern, group); return v.Length == 0 ? null : D(v); }
        static NumbersV3Dto ReadNumbers(byte[] bytes, Action<string,string,string> error)
        {
            var parsed = CsvV3.Parse(bytes); var rows = new List<NumbersV3RowDto>();
            string header = "Key,Value,Unit,Status,Source,Candidate,CandidateTrigger,Note";
            if (parsed.Count == 0 || string.Join(",", parsed[0].Fields) != header) { error("NumbersHeader", "numbers:1", header); return new NumbersV3Dto(rows); }
            foreach (var r in parsed.Skip(1))
            {
                var f = r.Fields;
                if (f.Length != 8) { error("Columns", "numbers:" + r.Line, "Expected 8 columns"); continue; }
                decimal? value = null;
                if (f[1] != "")
                {
                    if (!decimal.TryParse(f[1], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal v)) { error("Decimal", f[0] + ":" + r.Line, f[1]); continue; }
                    value = v;
                }
                rows.Add(new NumbersV3RowDto(f[0],value,f[2],f[3],f[4],f[5],f[6],f[7],r.Line));
            }
            return new NumbersV3Dto(rows);
        }
        static void ValidateNumbers(NumbersV3Dto n, Action<string,string,string> error)
        {
            void Check(bool ok, string code, string key, string detail) { if (!ok) error(code,key,detail); }
            Check(n.Rows.Count == 414, "NumbersCount", "numbers", "Expected 414 rows");
            Check(n.Rows.Count(r => r.Value.HasValue) == 409, "ValueCount", "numbers", "Expected 409 values / 5 nulls");
            Check(n.Rows.Count(r => r.Candidate != "") == 57, "CandidateCount", "numbers", "Expected 57 candidate rows");
            var schema = ContractSchemaV3.Numbers.Replace("\r", "").Split('\n').Select(s => s.Split('|')).ToDictionary(p => p[0], StringComparer.Ordinal);
            foreach (var group in n.Rows.GroupBy(r => r.Key)) Check(group.Count() == 1,"DuplicateKey",group.Key,"Key must be unique");
            foreach (var r in n.Rows)
            {
                string loc = r.Key + ":" + r.Line;
                if (!schema.TryGetValue(r.Key,out var s)) { error("UnknownKey",loc,"Not in v3 schema"); continue; }
                Check(r.Status == "TEST","Status",loc,"All rows are TEST");
                Check(r.Unit == s[1],"Unit",loc,"Expected " + s[1]);
                Check(r.Value.HasValue == (s[2] == "value"),"NullContract",loc,"Undefined values stay null");
                Check(!r.Value.HasValue || r.Value >= 0,"Range",loc,"Nonnegative value required");
                Check(!r.Value.HasValue || !r.Unit.Contains("%") || r.Value <= 100,"PercentRange",loc,"Percent must be 0..100");
                if (r.Value.HasValue && new[] { "명", "개", "회", "행", "자", "칸", "종", "획", "묶음", "위", "몸", "받침", "세대깊이" }.Contains(r.Unit))
                    Check(decimal.Truncate(r.Value.Value) == r.Value.Value,"IntegerRange",loc,"Integral count required");
                Check(r.Candidate == s[3] && r.CandidateTrigger == s[4],"CandidateContract",loc,"Candidate and condition remain separate from Value");
                Check(r.Source.Length > 0 && r.Note.Length > 0,"Metadata",loc,"Source and Note required");
            }
            foreach (var key in schema.Keys) Check(n.Has(key),"RequiredKey",key,"Missing v3 key");
            if (n.Rows.Count != 414 || schema.Keys.Any(k => !n.Has(k)) || n.Rows.Any(r => schema.TryGetValue(r.Key,out var s) && s[2] == "value" && !r.Value.HasValue)) return;
            void Eq(string key, decimal value, string code = "Derived") => Check(n.Get(key) == value,code,key,"Expected " + Vol2ImportManifestDto.Number(value));
            foreach (var kv in new Dictionary<string,decimal> {
                ["action.eo.second"]=0.3m,["action.normal.g.shared"]=0.9m,["action.effectTargetCap"]=3,["timing.eo.secondDelay"]=0.3m,["timing.eo.secondCount"]=1,
                ["combat.parry.failureCost"]=14,["combat.parry.successCost"]=0,["combat.parry.refund"]=0,["timing.drawing.eoMax"]=0.7m,["combat.noInk.targets"]=1,["combat.noInk.multiplier"]=1,
                ["action.instant"]=0.7m,["action.seed.activation"]=1.2m,["action.seed.instant"]=0,["action.seed.second"]=0,["action.seed.activationCount"]=1,["action.spread.total"]=1,
                ["action.strip.utility"]=0.4m,["action.return.count"]=1,["action.eo.return"]=0,["combat.force.min"]=1,["combat.force.max"]=1.15m,["combat.counter.multiplier"]=1.25m,["combat.peak.structureForceCap"]=1.5m,
                ["peak.엉.reflectionCount"]=3,["combat.borrowed.reflectTotalCap"]=1,["combat.borrowed.multiplier"]=0.5m }) Eq(kv.Key,kv.Value,"CurrentContract");
            Eq("body.earth.instant",n.Get("body.earth.base")*n.Get("body.earth.adjust"));
            foreach (string body in new[] { "wood", "fire", "earth", "metal", "water" })
                Eq("body."+body+".total", n.Get("body."+body+".instant") + (body=="metal" ? n.Get("body.metal.speedUtility")+n.Get("body.metal.wetUtility") : n.Get("body."+body+".utility")));
            Eq("combat.noInk.damage",n.Get("body.wood.instant")*n.Get("combat.noInk.woodRatio"),"NoInkFormula");
            decimal decay=n.Get("combat.groggy.decay");
            Check(decay>=0 && decay<1,"GroggySeries","combat.groggy.decay","Must be below 1");
            if(decay<1) Check(n.Get("combat.groggy.first")/(1-decay)<=n.Get("combat.groggy.totalCap"),"GroggySeries","combat.groggy.totalCap","Geometric series exceeds cap");
            Eq("combat.groggy.third",n.Get("combat.groggy.first")*decay*decay);
            Eq("action.return.amount",n.Get("action.instant")*n.Get("action.return.multiplier"));
            Eq("peak.섯.rearAmount",n.Get("action.instant")*n.Get("peak.섯.rearMultiplier"));
            Eq("peak.삿.instant",n.Get("action.instant")*n.Get("peak.삿.firstHitMultiplier"));
            Eq("peak.앙.returnAmount",n.Get("action.instant")*n.Get("action.return.peakMultiplier"));
            Eq("peak.spread.peakTotal",n.Get("peak.spread.amountPerTarget")*n.Get("peak.spread.peakTargets"));
            Eq("combat.peak.criticalMax",n.Get("combat.peak.structureForceCap")*n.Get("combat.counter.multiplier"));
            Eq("combat.normal.criticalMax",n.Get("combat.force.max")*n.Get("combat.counter.multiplier"));
            Eq("combat.strip.peakDuration",n.Get("combat.strip.duration")*n.Get("peak.nonDamageAxisCap"));
            Eq("ink.regen.fullRefill",n.Get("ink.capacity")/Math.Max(0.000001m,n.Get("ink.regen.outside")));
            Eq("ink.regen.combatSampleInk",n.Get("ink.regen.combatSample")*n.Get("ink.regen.combat"));
            Eq("ink.regen.combatSampleCasts",n.Get("ink.regen.combatSampleInk")/Math.Max(0.000001m,n.Get("ink.normal.a")));
            foreach (string f in new[] {"a","eo","o","u"})
            {
                Eq("ink.peak."+f,n.Get("ink.normal."+f)*n.Get("ink.peak.multiplier"));
                if(f!="eo") Eq("ink.bare."+f,n.Get("ink.normal."+f)-n.Get("ink.bare.discount"));
            }
            foreach (Verb v in Enum.GetValues(typeof(Verb)))
            {
                string key=RuleTypesV3.VerbKey(v);
                decimal per=v switch { Verb.Once=>1,Verb.Seed=>n.Get("action.seed.activation"),Verb.Return=>n.Get("action.instant")+n.Get("action.return.amount"),_=>n.Get("action.instant") };
                decimal shared=v switch { Verb.Leave=>n.Get("action.normal.g.shared"),Verb.Spread=>n.Get("action.spread.total"),Verb.Split=>n.Get("action.strip.utility"),_=>0 };
                foreach(string prefix in new[]{"action.normal.","action.eo."})
                {
                    if(prefix=="action.eo." && v==Verb.Once)continue;
                    decimal p=prefix=="action.eo." ? (v==Verb.Seed?n.Get("action.seed.activation"):n.Get("action.instant")+n.Get("action.eo.second")):per;
                    Eq(prefix+key+".perTarget",p,"SharedContract"); Eq(prefix+key+".shared",shared,"SharedContract"); Eq(prefix+key+".total",p+shared);
                    if(v!=Verb.Once) Eq((prefix=="action.eo."?"peak.eoCap.":"peak.standardCap.")+key,(p+shared)*n.Get("combat.peak.structureForceCap"));
                }
            }
            for(int b=0;b<5;b++) for(int f=0;f<4;f++)
            {
                TryPlan((Body)b,(Frame)f,(Verb)(b+1),n,out var plan);
                Eq("peak."+plan.Glyph.Letter+".total",plan.Total);
                string cap=(f==1?"peak.eoCap.":"peak.standardCap.")+RuleTypesV3.VerbKey(plan.Glyph.Verb);
                Eq("peak."+plan.Glyph.Letter+".cap",n.Get(cap));
                Check(plan.Total<=n.Get(cap),"PeakCap",plan.Glyph.Letter.ToString(),"Structural sum exceeds cap");
            }
            Eq("structure.letters",n.Get("structure.bodies")*n.Get("structure.frames")*n.Get("structure.finalOptions"));
            Eq("structure.peakLetters",n.Get("structure.bodies")*n.Get("structure.frames"));
            Eq("structure.normalLetters",n.Get("structure.letters")-n.Get("structure.peakLetters")-n.Get("structure.bareLetters"));
            Eq("action.effectOverflowStart",n.Get("action.effectTargetCap")+1);
            Check(n.Get("action.eo.n.total")/(n.Get("ink.normal.eo")/Math.Max(0.000001m,n.Get("ink.regen.combat"))+n.Get("timing.drawing.eoMax")) > n.Get("combat.noInk.damage")/Math.Max(0.000001m,n.Get("combat.noInk.cooldown")),"DrawingGate","timing.drawing.eoMax","Drawing-adjusted sustained damage must beat no-ink");
        }
        static void TryPlan(Body b, Frame f, Verb v, NumbersV3Dto n, out GlyphRulesV3.Plan plan)
        { GlyphRulesV3.TryDecode(GlyphRulesV3.Encode(b,f,v),out var g); plan=GlyphRulesV3.BuildCore(g,n); }
        static List<Vol2SpellRowDto> ReadSpells(byte[] bytes, NumbersV3Dto n, Action<string,string,string> error)
        {
            var parsed=CsvV3.Parse(bytes); var result=new List<Vol2SpellRowDto>();
            const string header="글자,초성,중성,종성,구분,한줄정의,자국윤곽,몸거동,반응재료,비고";
            if(parsed.Count==0 || string.Join(",",parsed[0].Fields)!=header) {error("TableHeader","table:1","Expected v10 header, not legacy Builder");return result;}
            var dialect=ContractSchemaV3.Narratives.Replace("\r", "").Split('\n').Select(s=>s.Split('|')).ToDictionary(x=>x[0]);
            foreach(var r in parsed.Skip(1))
            {
                var f=r.Fields; string loc="table:"+r.Line;
                if(f.Length!=10){error("Columns",loc,"Expected 10 columns");continue;}
                loc=f[0]+":"+r.Line;
                if(f.Any(string.IsNullOrWhiteSpace)){error("Blank",loc,"No blank fields");continue;}
                if(f.Any(s=>s.Contains("…")||s.Contains("...")))error("Ellipsis",loc,"No omitted terms");
                if(f[0].Length!=1 || !GlyphRulesV3.TryDecode(f[0][0],out var g)){error("Glyph",loc,"Illegal syllable");continue;}
                string cho="ㄱㄴㅁㅅㅇ"[(int)g.Body]+"="+"목화토금수"[(int)g.Body];
                string jung="ㅏㅓㅗㅜ"[(int)g.Frame]+"="+new[]{"경로","부착","면","둘레"}[(int)g.Frame];
                string jong=new[]{"없음","ㄱ","ㄴ","ㅁ","ㅅ","ㅇ"}[(int)g.Verb]+"="+new[]{"한 번","남긴다","번진다","심는다","가른다","돌아온다"}[(int)g.Verb];
                if(f[1]!=cho||f[2]!=jung||f[3]!=jong)error("JamoName",loc,"Unicode and jamo/name pairs disagree");
                if(f[4]!=new[]{"무받침","일반","극치"}[(int)g.Kind])error("Kind",loc,"Initial/final identity defines peak");
                var p=GlyphRulesV3.BuildCore(g,n);
                decimal cost=Num(f[9],@"먹\s*(\d+)\s*\(TEST\)")??0;
                if(cost!=p.Cost)error("Cost",loc,"Cost differs from E0");
                if(g.IsParry && (!f[9].Contains("먹 후불")||!f[9].Contains("환급 없음")||Num(f[9],@"실패\s*(\d+)\s*고정")!=14))error("ParryPayment",loc,"Postpaid success 0/failure 14; refund 0");
                if(p.Limited && Num(f[9],@"시전당 효과 대상 상한\s*(\d+)")!=n.Get("action.effectTargetCap"))error("EffectTargetCap",loc,"One common set of first three targets");
                if(g.Frame==Frame.Attachment && g.Verb!=Verb.Once && g.Verb!=Verb.Seed && (!f[9].Contains("대상 ID)마다 최대 1회")||!f[9].Contains("0.3 s")))error("SecondContact",loc,"Per cast/target once after 0.3s on separate swing");
                if(g.Frame==Frame.Attachment && g.Verb==Verb.Seed && (!f[9].Contains("둘째 접촉 없음")||!f[5].Contains("피해를 받을 때")))error("SeedTrigger",loc,"Damage received, no second contact");
                if(g.Letter!='엉' && string.Join(" ",f).Contains("빌린 위력"))error("BorrowedScope",loc,"Only 엉 may borrow projectile power");
                var terms=g.Kind==SpellKind.Peak?ParsePeak(g,f[5],f[8],n,error,loc):p.EffectTerms.ToList();
                if(g.Kind==SpellKind.Peak)
                {
                    decimal computed=terms.Sum(t=>t.Total);
                    var m=Regex.Match(f[9],@"총 작용량\s*(\d+(?:\.\d+)?)\s*\(상한\s*(\d+(?:\.\d+)?)\)\s*=\s*([^·]+)");
                    if(!m.Success||D(m.Groups[1].Value)!=computed||D(m.Groups[2].Value)!=n.Get("peak."+g.Letter+".cap"))error("PeakDeclaration",loc,"Declaration/cap differs from narrative calculation");
                    if(m.Success)
                    {
                        var declared=new List<decimal>();
                        foreach(string part in m.Groups[3].Value.Trim().Split(new[]{" + "},StringSplitOptions.None))
                        {
                            var t=Regex.Match(part.Trim(),@"^(.+?)\s*(\d+(?:\.\d+)?)(?:×(\d+))?$");
                            if(!t.Success){error("UnparsedTerm",loc,part);continue;}
                            string name=t.Groups[1].Value.Trim();
                            if(!new[]{"즉시","둘째 접촉","몸 재료","뿌리 재료","재료","번짐","되번짐","작동","벗김","뒤쪽 가름","귀환"}.Contains(name))error("UnparsedTerm",loc,name);
                            declared.Add(D(t.Groups[2].Value)*(t.Groups[3].Success?D(t.Groups[3].Value):1));
                        }
                        if(!declared.OrderBy(x=>x).SequenceEqual(terms.Select(t=>t.Total).OrderBy(x=>x)))error("PeakTerms",loc,"Formula terms differ from narrative terms");
                    }
                    if(computed!=p.Total||terms.Where(t=>t.Shared).Sum(t=>t.Total)!=p.Shared)error("PeakTerms",loc,"Narrative differs from E0-derived plan");
                }
                if(dialect.TryGetValue(f[0],out var known))
                    for(int i=5;i<10;i++)if(Vol2ImportManifestDto.HashText(f[i])!=known[i-4])error("UnparsedNarrative",loc+"/"+i,"Text outside the recognized v10 dialect");
                result.Add(new Vol2SpellRowDto(r.Line,r.Raw,f,g,cost,terms));
            }
            if(result.Count!=120)error("TableCount","table","Expected 120 rows");
            foreach(var group in result.GroupBy(r=>r.Letter))if(group.Count()!=1)error("DuplicateLetter",group.Key.ToString(),"Repeated glyph");
            foreach(var group in result.GroupBy(r=>Regex.Replace(r.Definition,@"\s+"," ")))if(group.Count()!=1)error("DuplicateDefinition",string.Join(",",group.Select(r=>r.Letter)),"Repeated definition");
            foreach(var k in new[]{SpellKind.Bare,SpellKind.Normal,SpellKind.Peak})if(result.Count(r=>r.Kind==k)!=(k==SpellKind.Normal?80:20))error("KindCount",k.ToString(),"Expected 20/80/20");
            return result;
        }
        static List<EffectTerm> ParsePeak(Glyph g,string definition,string material,NumbersV3Dto n,Action<string,string,string> error,string loc)
        {
            var terms=new List<EffectTerm>();
            void Add(EffectKind k,decimal a,int count=1){if(a>0)terms.Add(new EffectTerm(k,a,count));}
            if(g.Verb!=Verb.Seed)
            {
                Add(EffectKind.Instant,n.Get("action.instant")*(definition.Contains("벰은 첫 대상에만 ×1.5")?1.5m:1));
                if(g.Frame==Frame.Attachment)Add(EffectKind.Second,n.Get("action.eo.second"));
            }
            if(g.Verb!=Verb.Spread)
                foreach(string part in material.Split(new[]{" + "},StringSplitOptions.None))
                { decimal? v=Num(part,@"작용량\s*(\d+(?:\.\d+)?)"); if(v.HasValue)Add(EffectKind.Residue,v.Value,(int)(Num(part,@"(?:누적|동시)\s*(\d+)")??1)); }
            if(g.Verb==Verb.Spread)
            {
                if(definition.Contains("작용량 0.5 고정"))
                { string count=Match(definition,@"번짐 대상\s*(?:상한\s*)?(\d+|하나|둘|셋|넷|다섯)"); int value=count switch{"하나"=>1,"둘"=>2,"셋"=>3,"넷"=>4,"다섯"=>5,_=>int.TryParse(count,out int x)?x:0}; if(value==0)error("UnparsedTerm",loc,"Spread count"); Add(EffectKind.Spread,0.5m,value); }
                else foreach(Match m in Regex.Matches(definition,@"(되번짐|번짐) 합\s*(\d+(?:\.\d+)?)"))Add(m.Groups[1].Value=="번짐"?EffectKind.Spread:EffectKind.ReturnSpread,D(m.Groups[2].Value));
            }
            if(g.Verb==Verb.Seed)Add(EffectKind.Activation,Num(definition,@"작용량\s*(\d+(?:\.\d+)?)")??n.Get("action.seed.activation"));
            if(g.Verb==Verb.Split){Add(EffectKind.Strip,n.Get("action.strip.utility"));if(definition.Contains("뒤쪽은 ×0.5"))Add(EffectKind.Rear,0.35m,g.Frame==Frame.Attachment?2:1);}
            if(g.Verb==Verb.Return && g.Frame!=Frame.Attachment)Add(EffectKind.Return,n.Get("action.instant")*(Num(definition,@"귀환 작용 ×\s*(\d+(?:\.\d+)?)")??Num(definition,@"올 때 ×\s*(\d+(?:\.\d+)?)")??0.5m));
            return terms;
        }
        static string Section(string doc,string prefix)
        {
            var lines=doc.Split('\n');int start=Array.FindIndex(lines,s=>s.StartsWith(prefix,StringComparison.Ordinal)); if(start<0)return "";
            int end=start+1;while(end<lines.Length&&!Regex.IsMatch(lines[end],@"^#{1,3} "))end++;
            return string.Join("\n",lines.Skip(start+1).Take(end-start-1));
        }
        static string Row(string section,string prefix) => section.Split('\n').FirstOrDefault(s=>s.StartsWith("| "+prefix,StringComparison.Ordinal))??"";
        static void ValidateDocument(string doc,string sheet,NumbersV3Dto n,Action<string,string,string> error)
        {
            void Check(bool ok,string code,string where,string detail){if(!ok)error(code,where,detail);}
            string e1=Section(doc,"### E-1"),e2=Section(doc,"### E-2"),e4=Section(doc,"### E-4");
            string payment=Row(e1,"패링 먹");
            Check(payment.Contains("후불")&&payment.Contains("성공 0")&&payment.Contains("실패 14")&&payment.Contains("환급 없음"),"ParryPayment","E-1","Postpaid contract");
            string bare=Row(e2,"무먹 붓 휘두르기");
            var bareMatch=Regex.Match(bare,@"목 즉시 (\d+(?:\.\d+)?) × (\d+(?:\.\d+)?) = (\d+(?:\.\d+)?)");
            Check(bareMatch.Success&&D(bareMatch.Groups[1].Value)*D(bareMatch.Groups[2].Value)==D(bareMatch.Groups[3].Value)&&D(bareMatch.Groups[3].Value)==n.Get("combat.noInk.damage"),"NoInkFormula","E-2","0.6 × 0.5 = 0.3");
            Check(Num(bare,@"대상 (\d+) · 배율") == 1 && Num(bare,@"대상 \d+ · 배율 (\d+(?:\.\d+)?)\(고정\)")==1,"NoInkScope","E-2","One target, multiplier one");
            string groggy=Row(e2,"같은 적 패링 그로기 반복");decimal? decay=Num(groggy,@"×(\d+(?:\.\d+)?)\^"),cap=Num(groggy,@"합 ≤ (\d+(?:\.\d+)?) 단위");
            Check(decay.HasValue&&cap.HasValue&&decay<1&&1/(1-decay.Value)<=cap.Value,"GroggySeries","E-2","Geometric sum must fit cap");
            Check(Regex.IsMatch(doc,@"(?m)^\| ⑫[^\n]*80[^\n]*급소창"),"PlayGate","E7/⑫","Critical-window play gate missing");
            foreach(Match m in Regex.Matches(e4,@"정의 계산\((\d+(?:\.\d+)?)\), 상한 = ㅓ 열 ㅅ 상한\((\d+(?:\.\d+)?)\)"))
                Check(D(m.Groups[1].Value)==n.Get("peak.섯.total")&&D(m.Groups[2].Value)==n.Get("peak.섯.cap"),"SeotDocument","E-4","Document calculation must equal DTO");
            Check(Regex.IsMatch(e4,@"정의 계산\("),"SeotDocument","E-4","Required calculation missing");
            string eo=Row(e2,"ㅓ 열 일반 총 작용량");
            Check(Num(eo,@"시전 하나당 최대 (\d+) 대상")==3,"EffectTargetCap","E-2","Maximum three affected targets");
            Check(Num(eo,@"둘째 접촉\s*\**\s*(\d+(?:\.\d+)?)")==n.Get("action.eo.second"),"SecondContact","E-2","Second contact must agree");
            string area=Row(e2,"광역 가격 기준");
            var per=Regex.Match(area,@"대상별 항: 없음 ([^/]+) / 공유 항: 없음 ([^(]+)");
            Check(per.Success && Num(per.Groups[1].Value,@"ㄱ (\d+(?:\.\d+)?)")==0.7m && Num(per.Groups[2].Value,@"ㄱ (\d+(?:\.\d+)?)")==0.9m,"SharedContract","E-2","Residue is lifetime-shared, not per target");
            string g=Section(doc,"## G.").Trim(),gs=Section(sheet,"## G.").Trim();
            Check(g.Length>0&&g==gs,"SheetMismatch","G","One-sheet text must match document G");
            foreach(string f in new[]{"a","o","u"})
            {
                decimal cost=f=="a"?(Num(Row(e1,"무받침 ㅏ"),@"\| \*\*(\d+)\*\* \|")??0):n.Get("ink.bare."+f);
                if(cost<=0){error("Cost","E-1","Missing bare price");continue;}
                decimal mean=0;foreach(string v in new[]{"g","n","m","s","ng"})mean+=n.Get("action.normal."+v+".total")/5;
                decimal ratio=(1/cost)/(mean/n.Get("ink.normal."+f));
                Check(ratio>=n.Get("efficiency.one.min")&&ratio<=n.Get("efficiency.one.max"),"Efficiency","E-1/"+f,"Bare/suffix efficiency outside allowed band");
            }
        }
    }
}
