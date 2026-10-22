using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Oheangbu.Vol2.Rules;
namespace Vol2V3.Tests
{
    public sealed class Report
    {
        public string Suite { get; }
        public int Passed { get; private set; }
        public int Failed { get; private set; }
        public Report(string suite) { Suite=suite; }
        public void Check(string id,bool ok,string detail="")
        { if(ok)Passed++; else { Failed++;Console.WriteLine("FAIL "+Suite+"/"+id+" "+detail); } }
        public void Equal<T>(string id,T expected,T actual) => Check(id,EqualityComparer<T>.Default.Equals(expected,actual),"expected="+expected+" actual="+actual);
        public void Near(string id,decimal expected,decimal actual) => Check(id,Math.Abs(expected-actual)<=0.000000001m,"expected="+expected+" actual="+actual);
        public void Run(string id,Action test) { try {test();}catch(Exception e){Check(id,false,e.ToString());} }
        public void Print() => Console.WriteLine(Suite+": PASS "+Passed+" / FAIL "+Failed);
    }
    public sealed class Context : IDisposable
    {
        public string Root { get; }
        public string Stage { get; }
        public Dictionary<string,byte[]> Sources { get; }
        public JsonDocument Golden { get; }
        public Vol2ContractValidator.Result Import { get; }
        public NumbersV3Dto Numbers => Import.Numbers;
        public Context()
        {
            string dir=AppContext.BaseDirectory;
            while(dir!=null&&!File.Exists(Path.Combine(dir,"Docs/Research/e3/port_contract_v3.md")))dir=Directory.GetParent(dir)?.FullName;
            Root=dir??throw new InvalidOperationException("Repository root not found");
            Stage=Path.Combine(Root,"Tools/SpellVFX120/Pure308/Vol2V3");
            Sources=new Dictionary<string,byte[]> {
                ["numbers"]=File.ReadAllBytes(Path.Combine(Root,"Docs/Research/e0/numbers_v3.csv")),
                ["table"]=File.ReadAllBytes(Path.Combine(Root,"Docs/Research/debate25/vol2_table120_v10.csv")),
                ["markdown"]=File.ReadAllBytes(Path.Combine(Root,"Docs/Research/debate25/vol2_v10.md")),
                ["checker"]=File.ReadAllBytes(Path.Combine(Root,"Docs/Research/debate25/check_table120_v10.py")),
                ["sheet"]=File.ReadAllBytes(Path.Combine(Root,"Docs/Research/debate25/e2_sheet_v10.md")),
                ["contract"]=File.ReadAllBytes(Path.Combine(Root,"Docs/Research/e3/port_contract_v3.md")) };
            Golden=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Stage,"Golden/source.json")));
            Import=new Vol2ContractValidator().Import(Sources);
        }
        public GlyphRulesV3.Plan Plan(char letter,NumbersV3Dto n=null)
        { if(!GlyphRulesV3.TryDecode(letter,out var glyph))throw new ArgumentException("Illegal glyph");return GlyphRulesV3.Build(glyph,n??Numbers); }
        public Dictionary<string,byte[]> Copy() => Sources.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());
        public static byte[] Csv(IEnumerable<IEnumerable<string>> rows) => Encoding.UTF8.GetBytes(string.Join("\n",rows.Select(row=>string.Join(",",row.Select(cell=>"\""+cell.Replace("\"","\"\"")+"\""))))+"\n");
        public static string Text(byte[] bytes)=>Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Replace("\r\n","\n");
        public void Dispose()=>Golden.Dispose();
    }
}
