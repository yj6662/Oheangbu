using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Oheangbu.Vol2.Rules;
namespace Vol2V3.Tests
{
    public static class Cases_Import
    {
        public static void Run(Report r,Context c)
        {
            r.Check("canonical",c.Import.CanCast,string.Join(" | ",c.Import.Errors));
            if(!c.Import.CanCast)return;
            r.Equal("numbers",414,c.Numbers.Rows.Count);r.Equal("values",409,c.Numbers.Rows.Count(x=>x.Value.HasValue));
            r.Equal("nulls",5,c.Numbers.Rows.Count(x=>!x.Value.HasValue));r.Equal("candidates",57,c.Numbers.Rows.Count(x=>x.Candidate!=""));
            r.Equal("spells",120,c.Import.Spells.Count);
            foreach(var pair in c.Sources)r.Equal("source-hash/"+pair.Key,c.Golden.RootElement.GetProperty("hashes").GetProperty(pair.Key).GetString(),Vol2ImportManifestDto.Sha256(pair.Value));
            r.Check("raw-line",c.Import.Spells.All(x=>x.Line>=2&&x.RawText.Length>0&&x.Fields.Count==10));
            r.Equal("groggy-units","그로기단위",c.Numbers.Row("combat.groggy.totalCap").Unit);
            r.Equal("candidate-not-current",0.3m,c.Numbers.Get("action.eo.second"));
            var candidate=c.Numbers.CreateEoCandidateProfile();
            r.Check("candidate-profile",candidate.Get("action.eo.second")==0.25m&&candidate.Get("action.normal.g.shared")==0.85m&&candidate.Get("peak.섯.rearMultiplier")==0.45m&&candidate.Get("timing.drawing.eoMax")==0.6m);
            r.Equal("candidate-does-not-mutate",0.3m,c.Numbers.Get("action.eo.second"));
            using(var frozen=JsonDocument.Parse(System.IO.File.ReadAllBytes(System.IO.Path.Combine(c.Stage,"Golden/import_manifest.json"))))
            {
                r.Equal("independent-python-dto-hash",frozen.RootElement.GetProperty("dtoHash").GetString(),c.Import.Manifest.DtoHash);
                var manifest=new Vol2ImportManifestDto(frozen.RootElement.GetProperty("schema").GetString(),frozen.RootElement.GetProperty("importer").GetString(),frozen.RootElement.GetProperty("sourceHashes").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()),frozen.RootElement.GetProperty("dtoHash").GetString());
                r.Check("frozen-manifest-load",new Vol2ContractValidator().Import(c.Sources,manifest,manifest.DtoHash).CanCast);
            }
            var roundTrip=new NumbersV3Dto(c.Numbers.Rows.Reverse());
            string dto=Vol2ImportManifestDto.HashText(Vol2ImportManifestDto.CanonicalDto(roundTrip,c.Import.Spells.Reverse()));
            r.Equal("canonical-order",c.Import.Manifest.DtoHash,dto);
            var copiedRows=c.Import.Spells.Select(x=>new Vol2SpellRowDto(x.Line,x.RawText,x.Fields,x.Glyph,x.Cost,x.EffectTerms.Select(t=>new EffectTerm(t.Kind,t.Amount,t.Count)))).ToArray();
            var loaded=new Vol2ContractValidator().LoadDto(roundTrip,copiedRows,c.Import.Manifest,c.Import.Manifest);
            r.Check("so-reconstructed-dto-load",loaded.CanCast&&loaded.Numbers.IsValidated&&!roundTrip.IsValidated);
            r.Check("so-reconstructed-plan",GlyphRulesV3.Build(copiedRows[0].Glyph,loaded.Numbers).Cost==copiedRows[0].Cost);
            var tamperedNumbers=new NumbersV3Dto(roundTrip.Rows.Select(x=>x.Key=="action.instant"?x.WithValue(0.8m):x));
            r.Check("so-number-tamper",!new Vol2ContractValidator().LoadDto(tamperedNumbers,copiedRows,c.Import.Manifest,c.Import.Manifest).CanCast);
            var tamperedRows=copiedRows.ToArray();var first=copiedRows[0];
            tamperedRows[0]=new Vol2SpellRowDto(first.Line,first.RawText,first.Fields,first.Glyph,first.Cost+1,first.EffectTerms);
            r.Check("so-parsed-field-tamper",!new Vol2ContractValidator().LoadDto(roundTrip,tamperedRows,c.Import.Manifest,c.Import.Manifest).CanCast);
            r.Check("so-roundtrip",new Vol2ContractValidator().Import(c.Sources,c.Import.Manifest,dto).CanCast);
            Reject("so-hash-required",c.Copy(),"ManifestMismatch",c.Import.Manifest);
            Reject("so-tamper",c.Copy(),"ManifestMismatch",c.Import.Manifest,"bad");
            var wrongVersion=new Vol2ImportManifestDto("old",Vol2ImportManifestDto.Importer,c.Import.Manifest.SourceHashes.ToDictionary(p=>p.Key,p=>p.Value),dto);
            Reject("schema-version",c.Copy(),"ManifestMismatch",wrongVersion,dto);
            var changed=c.Copy();changed["checker"]=changed["checker"].Concat(new byte[]{10}).ToArray();Reject("source-byte-change",changed,"ManifestMismatch",c.Import.Manifest,dto);
            changed=c.Copy();changed.Remove("sheet");Reject("missing-source",changed,"MissingSource");
            var numberRows=CsvV3.Parse(c.Sources["numbers"]).Select(x=>x.Fields.ToArray()).ToList();
            var tableRows=CsvV3.Parse(c.Sources["table"]).Select(x=>x.Fields.ToArray()).ToList();
            void Number(string id,string key,int column,string value,string code)
            {var copy=numberRows.Select(x=>x.ToArray()).ToList();copy.Single(x=>x[0]==key)[column]=value;var input=c.Copy();input["numbers"]=Context.Csv(copy);Reject(id,input,code);}
            Number("decimal-invariant","action.instant",1,"0,7","Decimal");
            Number("decimal-nan","action.instant",1,"NaN","Decimal");
            Number("decimal-exponent","action.instant",1,"7e-1","Decimal");
            Number("unit","combat.groggy.totalCap",2,"s","Unit");
            Number("percent","e7.body.max",1,"101","PercentRange");
            Number("negative","space.ring.radius",1,"-1","Range");
            Number("null-preserved","combat.parry.airMissLockout",1,"0","NullContract");
            Number("status","action.instant",3,"LOCKED","Status");
            Number("candidate-condition","action.eo.second",6,"","CandidateContract");
            Number("candidate-value","action.eo.second",5,"0.2","CandidateContract");
            Number("numeric-overflow","body.earth.adjust",1,"79228162514264337593543950335","NumericRange");
            Number("derived-return","action.return.amount",1,"0.4","Derived");
            Number("shared-contract","action.normal.g.perTarget",1,"1.6","SharedContract");
            Number("groggy-series","combat.groggy.decay",1,"0.7","GroggySeries");
            Number("unknown-key","action.instant",0,"unrecognized","RequiredKey");
            var dup=numberRows.Select(x=>x.ToArray()).ToList();dup[2]=dup[1].ToArray();changed=c.Copy();changed["numbers"]=Context.Csv(dup);Reject("duplicate-key",changed,"DuplicateKey");
            changed=c.Copy();changed["numbers"]=Encoding.UTF8.GetBytes("Key,Value\n\"unterminated");Reject("malformed-csv",changed,"Format");
            changed=c.Copy();changed["numbers"]=new byte[]{0xff,0xfe};Reject("invalid-utf8",changed,"Utf8");
            var special=numberRows.Select(x=>x.ToArray()).ToList();special[1][7]="메모, \"인용\"\n둘째 줄";changed=c.Copy();changed["numbers"]=new byte[]{0xef,0xbb,0xbf}.Concat(Context.Csv(special)).ToArray();
            var quoted=new Vol2ContractValidator().Import(changed);r.Check("bom-quotes-newline",quoted.CanCast,string.Join(" | ",quoted.Errors));
            if(quoted.CanCast)r.Equal("quoted-value",special[1][7],quoted.Numbers.Row("ink.capacity").Note);
            void Table(string id,Action<List<string[]>> mutate,string code)
            {var copy=tableRows.Select(x=>x.ToArray()).ToList();mutate(copy);var input=c.Copy();input["table"]=Context.Csv(copy);Reject(id,input,code);}
            Table("legacy-header",x=>x[0][0]="Index","TableHeader");
            Table("duplicate-glyph",x=>x[2]=x[1].ToArray(),"DuplicateLetter");
            Table("blank",x=>x[1][5]="","Blank");
            Table("illegal-jamo",x=>x[1][1]="ㄴ=화","JamoName");
            Table("omitted-term",x=>x[1][5]+="…","Ellipsis");
            Table("unknown-narrative",x=>x[1][5]+=" 새 효과.","UnparsedNarrative");
            string[] expected={"PeakDeclaration","ParryPayment","NoInkFormula","BorrowedScope","PlayGate","Efficiency","GroggySeries","SharedContract","NoInkScope","SeotDocument","EffectTargetCap","EffectTargetCap","EffectTargetCap"};
            int index=0;
            foreach(var test in c.Golden.RootElement.GetProperty("negative").EnumerateArray())
            {
                var copy=tableRows.Select(x=>x.ToArray()).ToList();string doc=Context.Text(c.Sources["markdown"]);
                foreach(var cell in test.GetProperty("cells").EnumerateArray())copy[cell.GetProperty("row").GetInt32()+1][cell.GetProperty("column").GetInt32()]=cell.GetProperty("value").GetString();
                foreach(var patch in test.GetProperty("document").EnumerateArray())
                {string old=patch.GetProperty("old").GetString();r.Check("mutation-applied/"+(index+1),doc.Contains(old));doc=doc.Replace(old,patch.GetProperty("new").GetString());}
                var input=c.Copy();input["table"]=Context.Csv(copy);input["markdown"]=Encoding.UTF8.GetBytes(doc);
                Reject("C6/"+(index+1)+"/"+test.GetProperty("name").GetString(),input,expected[index]);index++;
            }
            r.Equal("negative-count",13,index);
            void Reject(string id,Dictionary<string,byte[]> input,string code,Vol2ImportManifestDto manifest=null,string roundTripHash=null)
            {
                var result=new Vol2ContractValidator().Import(input,manifest,roundTripHash);
                r.Check(id,!result.CanCast&&result.Numbers==null&&result.Errors.Any(x=>x.Code==code),"expected="+code+" actual="+string.Join(" | ",result.Errors));
            }
        }
    }
}
