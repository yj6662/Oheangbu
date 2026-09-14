using System;
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    public sealed class SpellCodexDefinition
    {
        public readonly string Id, Label, Description, Letter, BrushExample;
        public readonly SpellKind Kind;
        public readonly Element Element;
        public SpellCodexDefinition(string id, string letter, SpellKind kind, Element element,string description,string brushExample)
        {
            Id = id; Letter = letter; Kind = kind; Element = element;
            Label = letter + " 술식";
            Description = description; BrushExample = brushExample;
        }
    }

    public sealed class RecordDefinition
    {
        public readonly string Id, SourceInteractionId, Title, Body;
        public RecordDefinition(string id, string sourceInteractionId, string title, string body)
        { Id=id; SourceInteractionId=sourceInteractionId; Title=title; Body=body; }
    }

    public sealed class FragmentDefinition
    {
        public readonly string Id, ItemId, Letter, SpellId;
        public FragmentDefinition(string id, string itemId, string letter, string spellId)
        { Id=id; ItemId=itemId; Letter=letter; SpellId=spellId; }
    }

    public sealed class FragmentBundleDefinition
    {
        public readonly string Id, Title, SourceAnchorId;
        public readonly Vector3 Position;
        public readonly IReadOnlyList<FragmentDefinition> Fragments;
        public FragmentBundleDefinition(string id,string title,string sourceAnchorId,Vector3 position,params FragmentDefinition[] fragments)
        { Id=id; Title=title; SourceAnchorId=sourceAnchorId; Position=position; Fragments=fragments; }
    }

    // Static definitions are usable by runtime and editor authoring without loading a scene or ScriptableObject.
    public static class WorldMacroCollectionCatalog
    {
        static readonly SpellCodexDefinition[] Spells =
        {
            Spell("ga","가",SpellKind.AttackSingle,Element.Wood,"단일 대상에 곧게 뻗는 생목 가시.","ㄱ + ㅏ"),
            Spell("go","고",SpellKind.AttackArea,Element.Wood,"지정 영역에서 가시가 산발적으로 솟아 적별 한 번 타격한다.","ㄱ + ㅗ"),
            Spell("na","나",SpellKind.AttackSingle,Element.Fire,"단일 대상에 불덩이를 쏘는 기준 공격.","ㄴ + ㅏ"),
            Spell("no","노",SpellKind.AttackArea,Element.Fire,"지정 영역에 화염을 방사한다.","ㄴ + ㅗ"),
            Spell("ma","마",SpellKind.AttackSingle,Element.Earth,"높은 위력의 바위를 포물선으로 던진다.","ㅁ + ㅏ"),
            Spell("mo","모",SpellKind.AttackArea,Element.Earth,"굵은 주 균열이 여러 갈래 지맥으로 갈라지며 낮은 돌가루와 함께 전진한다.","ㅁ + ㅗ"),
            Spell("sa","사",SpellKind.AttackSingle,Element.Metal,"빠른 금속 송곳을 단일 대상에 쏜다.","ㅅ + ㅏ"),
            Spell("so","소",SpellKind.AttackArea,Element.Metal,"전방에 금속 송곳을 여러 발 빠르게 쏜다.","ㅅ + ㅗ"),
            Spell("a","아",SpellKind.AttackSingle,Element.Water,"각도 한계 안에서 대상을 좇는 느린 물 유도탄.","ㅇ + ㅏ"),
            Spell("o","오",SpellKind.AttackArea,Element.Water,"전진하는 느린 파도가 경로의 적을 타격한다.","ㅇ + ㅗ"),
            Spell("eo","어",SpellKind.Parry,Element.Water,"수막으로 받아친다. 상극 판정과 보상은 전투 규칙을 따른다.","ㅇ + ㅓ"),
            Spell("seo","서",SpellKind.Parry,Element.Metal,"금강 방벽으로 받아친다. 상극 판정과 보상은 전투 규칙을 따른다.","ㅅ + ㅓ"),
            Spell("geo","거",SpellKind.Parry,Element.Wood,"덩굴 방벽으로 받아친다. 상극 판정과 보상은 전투 규칙을 따른다.","ㄱ + ㅓ"),
            Spell("neo","너",SpellKind.Parry,Element.Fire,"화막으로 받아친다. 상극 판정과 보상은 전투 규칙을 따른다.","ㄴ + ㅓ"),
            Spell("meo","머",SpellKind.Parry,Element.Earth,"토벽으로 받아친다. 상극 판정과 보상은 전투 규칙을 따른다.","ㅁ + ㅓ"),
            Spell("gom","곰",SpellKind.Summon,Element.Wood,"목질 사슴을 앞에 잠시 불러 세운다. 현재는 고정형 등장 표현이며 이동·공격·피해 기능은 없다.","ㄱ + ㅗ + ㅁ"),
            Spell("nom","놈",SpellKind.Summon,Element.Fire,"불씨 해태를 앞에 잠시 불러 세운다. 현재는 고정형 등장 표현이며 이동·공격·피해 기능은 없다.","ㄴ + ㅗ + ㅁ"),
            Spell("mom","몸",SpellKind.Summon,Element.Earth,"몽둥이를 든 돌 도깨비를 앞에 잠시 불러 세운다. 현재는 고정형 등장 표현이며 이동·공격·피해 기능은 없다.","ㅁ + ㅗ + ㅁ"),
            Spell("som","솜",SpellKind.Summon,Element.Metal,"은철 호랑이를 앞에 잠시 불러 세운다. 현재는 고정형 등장 표현이며 이동·공격·피해 기능은 없다.","ㅅ + ㅗ + ㅁ"),
            Spell("om","옴",SpellKind.Summon,Element.Water,"물 거북 영물을 앞에 잠시 불러 세운다. 현재는 고정형 등장 표현이며 이동·공격·피해 기능은 없다.","ㅇ + ㅗ + ㅁ")
        };

        static readonly RecordDefinition[] Records =
        {
            new RecordDefinition("record.mine_inquiry","mine_inquiry","폭파 흔적 조사","돌이 안쪽으로 휘어졌다. 지지목에는 불탄 줄의 흔적이 남아 있다."),
            new RecordDefinition("record.logger","logger","벌목꾼의 이야기","산 너머 벌목 마을이 비었다. 금표 비석을 지나면 길목 역참이 나온다."),
            new RecordDefinition("record.herbalist","herbalist","약초꾼의 이야기","광산 쪽에서 왔다면 더 깊이 들어가지 말라. 숲의 뿌리가 길을 삼키고 있다."),
            new RecordDefinition("record.worker_satchel","worker_satchel","작업자의 소지품","해진 주머니에 조선통보가 남아 있었다.")
        };

        static readonly FragmentBundleDefinition[] Bundles =
        {
            Bundle("start","갱도 입구의 석경","mine_inquiry",new Vector3(3406.3513f,195.72368f,1124.7881f),"a","eo"),
            Bundle("exit","작업장 출구의 석경","worker_satchel",new Vector3(3403.0796f,199.8907f,1247.1952f),"ga","geo","na","neo"),
            Bundle("inn","금표 비석의 석경","geumpyo_stone",new Vector3(2022f,136.05891f,835f),"sa","seo","ma","meo"),
            Bundle("pass","산길 고개의 석경","EastPass",new Vector3(1052f,329.91162f,192f),"go","no","so","mo","o"),
            Bundle("capital","남문 앞 석경","capital_notice",new Vector3(-303f,135.2893f,-1355f),"gom","nom","mom","som","om")
        };

        static readonly FragmentDefinition[] Fragments = FlattenFragments();
        public static IReadOnlyList<SpellCodexDefinition> AllSpells => Spells;
        public static IReadOnlyList<RecordDefinition> AllRecords => Records;
        public static IReadOnlyList<FragmentBundleDefinition> AllBundles => Bundles;
        public static IReadOnlyList<FragmentDefinition> AllFragments => Fragments;

        public static bool TryGetSpell(string letter, out SpellCodexDefinition definition)
        { definition=Array.Find(Spells,x=>string.Equals(x.Letter,letter,StringComparison.Ordinal));return definition!=null; }
        public static bool TryGetRecord(string id, out RecordDefinition definition)
        { definition=Array.Find(Records,x=>string.Equals(x.Id,id,StringComparison.Ordinal));return definition!=null; }
        public static bool TryGetRecordForInteraction(string interactionId, out RecordDefinition definition)
        { definition=Array.Find(Records,x=>string.Equals(x.SourceInteractionId,interactionId,StringComparison.Ordinal));return definition!=null; }
        public static bool TryGetBundle(string id, out FragmentBundleDefinition definition)
        { definition=Array.Find(Bundles,x=>string.Equals(x.Id,id,StringComparison.Ordinal));return definition!=null; }

        static SpellCodexDefinition Spell(string id,string letter,SpellKind kind,Element element,string description,string brushExample)
        { return new SpellCodexDefinition("spell."+id,letter,kind,element,description,brushExample); }
        static FragmentBundleDefinition Bundle(string id,string title,string anchor,Vector3 position,params string[] spellIds)
        {
            var fragments=new FragmentDefinition[spellIds.Length];
            for(int i=0;i<spellIds.Length;i++)
            {
                var spell=Array.Find(Spells,x=>string.Equals(x.Id,"spell."+spellIds[i],StringComparison.Ordinal));
                if(spell==null)throw new InvalidOperationException("Unknown fragment spell: "+spellIds[i]);
                fragments[i]=new FragmentDefinition("fragment."+spellIds[i],"fragment."+spellIds[i],spell.Letter,spell.Id);
            }
            return new FragmentBundleDefinition(id,title,anchor,position,fragments);
        }
        static FragmentDefinition[] FlattenFragments()
        {
            var values=new List<FragmentDefinition>();foreach(var bundle in Bundles)values.AddRange(bundle.Fragments);return values.ToArray();
        }
    }
}
