using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Oheangbu.App.Demo;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Demo;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 소지품 3-column screen (SPEC-UI-EQUIPMENT-308, DECISIONS D308-12) on the #304 foundation: LEFT the slot grid
    /// (장비 6 + 오행 마석 5 + 오덕 5 = 16 칸, nothing else), CENTRE the selected slot's detail and the 바꿔 낄 것 list, RIGHT the
    /// read-only 도사 상태 column, BOTTOM one legend line (keycap + verb phrase, the map page's grammar). The page id, the pause
    /// path (OpenPage → Pause.Begin) and GearAction are unchanged; this file only builds PageContent. Every number shown comes
    /// from the catalog, the economy rules / quotes, RuntimePlayerStats, CombatConfigSO (Session.TryGetStatus308) or
    /// EquipmentScreen308SO; texts are composed by EquipmentText308. Instance state only (domain reload is off): the tables
    /// below are immutable. Harness names: Equipped_&lt;slot&gt; (+EquipmentDropSlot), Stone_&lt;track&gt;, VirtueSlot_&lt;n&gt;,
    /// Gear_&lt;id&gt; (+EquipmentDragItem, now a 바꿔 낄 것 row), EquipmentCandidates, SlotKind, SlotItem, SelectedGearSlot / Title /
    /// Level / Effect / Compare / Paper, GearTransactionError, LinkService, LinkChapae, ReadCodex, EquipStatus308 (Status_&lt;key&gt;),
    /// EquipLegend308 (Legend_&lt;n&gt;).
    /// #308 theme (SPEC-UI-THEME-308, D308-15; the region near the end of this file): with EquipmentScreen308SO.Theme bound the
    /// 칸 become lattice windows (one 교창 per row, panes seated in the bars), the rules become bars, the picture frame a
    /// porcelain plaque with a lotus band, the tier dabs nacre petals, the kept 칸 wears a nacre cut-shell frame and the focus
    /// frame lies on a 한지 bed. Without the block (or ThemeOn off) every branch below draws the #304 look. Theme names:
    /// EquipGrid308, Lattice308 (Head / Sill / StileL / StileR / Bar / Board / BoardMotif), Pane, FocusBed, Kept308
    /// (Moat / KeptFrame), FootLine, FootBand.</summary>
    public sealed partial class PlaytestUiRoot
    {
        // layout308-begin : page px (1920x1080, top-left origin), SPEC §10. Read by Tools/Unity/Stage308_equip/check_equip308.py (AC-E6).
        const float E308SafeL=64,E308SafeT=40,E308SafeR=1856,E308SafeB=1016;
        const float E308LeftX=96,E308LeftR=696,E308KindY=232,E308ItemY=258;
        const float E308GridX=96,E308GridY=316,E308Chip=104,E308PitchX=124,E308PitchY=156,E308NameDy=110,E308CellW=120,E308CellH=148;
        const float E308Rule1X=740,E308Rule2X=1440,E308RuleY=232,E308RuleH=708;
        const float E308MidX=776,E308MidR=1404,E308HeadKindY=232,E308HeadNameY=258,E308NameW=420,E308HeadStateY=338,E308HeadDabY=382,E308HeadDabPitch=36;
        const float E308ArtX=1204,E308ArtY=232,E308ArtSize=200,E308ArtInner=168;
        const float E308DividerX=770,E308DividerY=446,E308DividerW=640,E308DividerH=28;
        const float E308SubY=486,E308SubLX=776,E308SubW=300,E308SubRX=1104,E308SubBottom=940;
        const float E308CandH=74,E308CandGap=2,E308ErrorY=880;
        const float E308StatusX=1476,E308StatusR=1856,E308StatusY=232,E308MoneyY=160,E308DropY=202;
        const float E308LegendX=96,E308LegendY=972,E308LegendH=48,E308LegendGap=34;
        // layout308-end
        // layout308t-begin : the same page with the #308 theme (SPEC §10 "테마"). The panes sit IN the bars: pitch = chip + bar.
        // Read by check_equip308.py (AC-E6 on the themed table, and the 8 / 16 px clearances of SPEC-UI-THEME-308 §4.2).
        const float E308TGridX=102,E308TGridY=308,E308TPitchX=108,E308TPitchY=160,E308TNameDy=118,E308TBar=4,E308TFrame=6,E308TCellW=104,E308TCellH=146;
        const float E308TBoardCells=2,E308TChrys=48,E308TRuleW=3;
        const float E308TBedX=14,E308TBedY=12,E308TBedIn=8,E308TRingDx=2,E308TRingDw=-6;                 // 한지 bed + ring rect, pane 104
        const float E308TRowBedX=17,E308TRowBedY=12,E308TRowBedIn=5,E308TRowRingDx=1,E308TRowRingDw=-2;    // the same, 바꿔 낄 것 chip 56
        const float E308TMoat=8,E308TSelectOut=5,E308TCandLabelX=110;
        const float E308TArt=140,E308TArtY=18,E308TStamp=116,E308TStampY=30,E308TSeal=124,E308TSealY=26;  // plaque: picture register
        const float E308TFootY=163,E308TFootInset=13,E308TLotusX=20,E308TLotusY=168,E308TLotusW=160,E308TLotusH=17;   // plaque: foot register
        const float E308TPetalW=12,E308TPetalH=14,E308TStepPitch=13,E308TFragDabX=-27,E308TFragDabDy=5,E308TFragDabW=20,E308TFragDabH=15;
        // layout308t-end
        // small measures inside the blocks above (style, not balance)
        const float E308DabX=-30,E308DabDy=3,E308DabW=24,E308DabH=18;                          // 방점 left of the name under a chip
        const float E308ArtPad=8,E308GhostPad=12,E308StampPad=20,E308SealPad=8;                // chip contents
        const float E308StepGap=8,E308StepDy=7,E308StepW=16,E308StepH=12,E308StepPitch=18;     // 단계 방점 after a 마석 name
        const float E308SectionGap=28,E308MarkSize=24,E308HeadTextDx=32,E308HeadGap=8,E308LineGap=4;
        // 바꿔 낄 것 row = a 칸 (mock sheet (f)): 방점 | 칩 56 | 이름 + 메타. The focus is the 칸 frame on the chip, no underlay stroke
        const float E308CandChipX=30,E308CandChip=56,E308CandArtPad=4,E308CandLabelX=102,E308CandDabX=-6,E308CandDabW=26,E308CandDabH=20;
        // 연결 줄 (정비 / 차패 / 술식 도감에서 보기): the label sits on the column's left edge, a small 방점 between the rule and the label
        const float E308LinkLabelX=36,E308LinkH=72,E308LinkWide=628,E308LinkDabGap=8,E308LinkMetaGap=16,E308LinkStrokeTail=60;
        const float E308LearnPitch=72,E308LearnSize=48,E308LearnBlank=40;
        const float E308StatusHeadDy=32,E308StatusPitchBody=40,E308StatusPitch=36,E308StatusGroupGap=60,E308StatusNameDx=32;
        const float E308GlyphFill=.82f,E308PendingAlpha=.35f,E308GhostAlpha=.2f,E308FrameAlpha=.5f,E308BlankAlpha=.16f,E308DividerAlpha=.38f;
        const int E308FragmentCols=5,E308LearnCols=4,E308ErrorLines=3;

        enum Equip308Kind{Gear,Stone,Virtue}
        readonly struct Equip308Def
        {
            public readonly Equip308Kind Kind;public readonly int Index,Row;
            public Equip308Def(Equip308Kind kind,int index,int row){Kind=kind;Index=index;Row=row;}
        }
        // slots308-begin : the only slots the screen draws (#245 장비 6부위 · CONST-RULES 오행 마석 5 · COMBAT-OSANG 오상 5). AC-E1.
        static readonly Equip308Def[] Equip308Slots=
        {
            new Equip308Def(Equip308Kind.Gear,(int)EquipmentSlot.Brush,0),new Equip308Def(Equip308Kind.Gear,(int)EquipmentSlot.Accessory,0),
            new Equip308Def(Equip308Kind.Gear,(int)EquipmentSlot.Head,1),new Equip308Def(Equip308Kind.Gear,(int)EquipmentSlot.Body,1),
            new Equip308Def(Equip308Kind.Gear,(int)EquipmentSlot.Hands,1),new Equip308Def(Equip308Kind.Gear,(int)EquipmentSlot.Feet,1),
            new Equip308Def(Equip308Kind.Stone,(int)DemoUpgradeTrack.Wood,2),new Equip308Def(Equip308Kind.Stone,(int)DemoUpgradeTrack.Fire,2),
            new Equip308Def(Equip308Kind.Stone,(int)DemoUpgradeTrack.Earth,2),new Equip308Def(Equip308Kind.Stone,(int)DemoUpgradeTrack.Metal,2),
            new Equip308Def(Equip308Kind.Stone,(int)DemoUpgradeTrack.Water,2),
            new Equip308Def(Equip308Kind.Virtue,0,3),new Equip308Def(Equip308Kind.Virtue,1,3),new Equip308Def(Equip308Kind.Virtue,2,3),
            new Equip308Def(Equip308Kind.Virtue,3,3),new Equip308Def(Equip308Kind.Virtue,4,3),
        };
        // slots308-end
        // the 차패 page's names for 仁 禮 義 智 信 (PlaytestUiRoot.Collections.cs BuildChapae), language constants
        static readonly string[] Equip308VirtueNames={"인 · 목","예 · 화","의 · 금","지 · 수","신 · 토"};
        const string Equip308VirtueReadings="인예의지신";   // the label under an 오덕 칸 (the chip already carries the 한자)
        const string Equip308StoneLink="오행 마석 · 속성 위력";   // what the 정비 link leads to (the artefacts, never 마석(원석))

        EquipmentScreen308SO equip308Data;bool equip308DataLoaded;
        int equip308Slot=-1;                    // the page's selected slot (kept across rebuilds and page changes)
        string equip308Candidate;               // 바꿔 낄 것 row under the focus (the detail compares it), else null
        bool equip308InMiddle;                  // the focus sits in the middle column (legend: [Esc] 칸으로)
        string equip308SlotKey,equip308InfoKey,equip308LegendKey,equip308ArtKey;
        float equip308SectionsTop;
        RectTransform equip308Info,equip308LegendRoot;
        TMP_Text equip308KindLabel,equip308ItemLabel;
        readonly ContentCell304[] equip308Cells=new ContentCell304[Equip308Slots.Length];
        readonly List<List<int>> equip308Rows=new List<List<int>>();           // present rows, top to bottom → slot indices
        readonly List<Selectable> equip308Middle=new List<Selectable>();       // 바꿔 낄 것 rows, then the live link row
        readonly Dictionary<ContentCell304,GameObject> equip308KeptMarks=new Dictionary<ContentCell304,GameObject>();   // theme: the kept frame of every 칸

        EquipmentScreen308SO Equip308Data()
        {
            if(!equip308DataLoaded){equip308DataLoaded=true;equip308Data=Resources.Load<EquipmentScreen308SO>(EquipmentScreen308SO.ResourcePath);}
            return equip308Data;
        }
        bool Equip308Fragments=>fragmentTab||Session==null||!Session.EquipmentEnabled;
        static string Equip308Name(Equip308Def d)=>d.Kind==Equip308Kind.Gear?"Equipped_"+(EquipmentSlot)d.Index:d.Kind==Equip308Kind.Stone?"Stone_"+(DemoUpgradeTrack)d.Index:"VirtueSlot_"+d.Index;
        static int Equip308IndexOf(EquipmentSlot slot){for(int i=0;i<Equip308Slots.Length;i++)if(Equip308Slots[i].Kind==Equip308Kind.Gear&&Equip308Slots[i].Index==(int)slot)return i;return -1;}
        static string Equip308GearKind(EquipmentSlot slot)=>slot==EquipmentSlot.Brush||slot==EquipmentSlot.Accessory?Content304SlotNames[(int)slot]:"의복 · "+Content304SlotNames[(int)slot];
        /// <summary>The live cell of slot i, or a real null (never a destroyed object, so ?. is safe on the result).</summary>
        ContentCell304 Equip308Cell(int i){var c=i>=0&&i<equip308Cells.Length?equip308Cells[i]:null;return c!=null?c:null;}

        // ================================================================== page
        void Equip308Build()
        {
            var s=Content304Style;var data=Equip308Data();
            content304GearCells.Clear();content304GearChips.Clear();equip308Rows.Clear();equip308Middle.Clear();equip308KeptMarks.Clear();
            var th=Equip308Theme;
            for(int i=0;i<equip308Cells.Length;i++)equip308Cells[i]=null;
            equip308SlotKey=equip308InfoKey=equip308LegendKey=equip308ArtKey=null;equip308Candidate=null;equip308InMiddle=false;
            bool gear=Session.EquipmentEnabled,fragments=Equip308Fragments;
            Content304SubTabs(gear);
            Content304Currency(contentRoot,"Currency",E308StatusR,E308MoneyY,Session.Progress.ledger.currency);
            if(th!=null)
            {
                // 용자살: the three columns are one door leaf and the rules are its bars (centred on the old 1 px rule)
                Equip308Fill(contentRoot,"DetailRule",E308Rule1X-1,E308RuleY,E308TRuleW,E308RuleH,th.Wood,th);
                Equip308Fill(contentRoot,"StatusRule",E308Rule2X-1,E308RuleY,E308TRuleW,E308RuleH,th.Wood,th);
            }
            else
            {
                Content304Rule(contentRoot,"DetailRule",E308Rule1X,E308RuleY,E308RuleH);
                Content304Rule(contentRoot,"StatusRule",E308Rule2X,E308RuleY,E308RuleH);
            }
            equip308KindLabel=V.Label(s,contentRoot,"SlotKind","",UiType304.Meta20,s.Mist,E308LeftX,E308KindY);
            equip308ItemLabel=V.Label(s,contentRoot,"SlotItem","",UiType304.Label26,s.Paper,E308LeftX,E308ItemY);
            Equip308Status(s,data);
            content304Detail=V.Rect("GearDetail304",contentRoot,0,0,UiPageFit304.Width,UiPageFit304.Height);
            equip308LegendRoot=V.Rect("EquipLegend308",contentRoot,E308LegendX,E308LegendY,E308LeftR-E308LegendX,E308LegendH);

            GameObject fallback=null;
            if(fragments)
            {
                if(th!=null){Content304FragmentGrid(E308TGridX,E308TGridY,E308FragmentCols,E308TPitchX,E308TPitchY,true);Equip308FragmentLattice(th);}
                else Content304FragmentGrid(E308GridX,E308GridY,E308FragmentCols,E308PitchX,E308PitchY,true);
                fallback=Content304FirstFragment();
                if(content304GearCells.All(c=>c.name!=content304Kept))content304Kept="Fragment_"+selectedItem;
            }
            else
            {
                Equip308Grid(s,data);
                Equip308Keys(data);
                if(Equip308Cell(equip308Slot)==null)equip308Slot=equip308Rows.Count>0?equip308Rows[0][0]:-1;
                var cell=Equip308Cell(equip308Slot);
                if(cell!=null){fallback=cell.gameObject;content304Kept=cell.name;Equip308SyncGearSelection(cell);}
            }
            Content304RefreshGear();
            // §6 처음 초점: a build never starts inside the middle column; the slot (or the sub tab the player is on) takes it
            bool onTab=content304Focus=="EquipmentTab"||content304Focus=="FragmentsTab";
            if(!onTab&&fallback!=null)content304Focus=fallback.name;
            if(fallback==null)fallback=contentRoot.Find(fragments?"FragmentsTab":"EquipmentTab")?.gameObject;
            Content304Restore(contentRoot,fallback);
            Equip308Legend();
        }

        // ================================================================== left: 칸 격자
        void Equip308Grid(UiStyle304SO s,EquipmentScreen308SO data)
        {
            var art=Session.Content.EquipmentUiArt;var cart=Content304Art;
            bool gear=Session.EquipmentEnabled,stones=Session.DemoEconomy!=null;
            var catalog=Session.Content.EquipmentCatalog;var state=Session.EquipmentReady?Session.Progress.equipment:null;
            var known=Session.Progress.ui.knownVirtues;var seen=Content304Seen;
            // theme: the 칸 live under their own root (the focused / kept 칸 is raised over its neighbours), the lattice under them
            var th=Equip308Theme;
            Transform gridRoot=contentRoot;RectTransform lattice=null;
            if(th!=null){gridRoot=V.Rect("EquipGrid308",contentRoot,0,0,UiPageFit304.Width,UiPageFit304.Height);lattice=V.Rect("Lattice308",gridRoot,0,0,0,0);}
            float gridX=th!=null?E308TGridX:E308GridX,gridY=th!=null?E308TGridY:E308GridY,pitchX=th!=null?E308TPitchX:E308PitchX,pitchY=th!=null?E308TPitchY:E308PitchY;
            float nameDy=th!=null?E308TNameDy:E308NameDy,cellW=th!=null?E308TCellW:E308CellW,cellH=th!=null?E308TCellH:E308CellH;
            var pane=new Rect(0,0,E308Chip,E308Chip);
            int lastRow=-1,col=0,boardRow=-1;
            for(int i=0;i<Equip308Slots.Length;i++)
            {
                var d=Equip308Slots[i];
                if(d.Kind==Equip308Kind.Gear&&!gear)continue;
                DemoUpgradeQuote quote=default;
                if(d.Kind==Equip308Kind.Stone&&(!stones||!Session.DemoEconomy.TryQuote((DemoUpgradeTrack)d.Index,out quote,out _)))continue;
                if(d.Row!=lastRow){equip308Rows.Add(new List<int>());lastRow=d.Row;col=0;if(d.Kind==Equip308Kind.Gear&&d.Row==0)boardRow=equip308Rows.Count-1;}
                float x=gridX+col*pitchX,y=gridY+(equip308Rows.Count-1)*pitchY;
                int index=i;var frame=Equip308RingRect(pane,false,th);
                var cell=Content304Cell(gridRoot,Equip308Name(d),x,y,cellW,cellH,frame,new Rect(E308DabX,nameDy+E308DabDy,E308DabW,E308DabH),
                    ()=>Equip308SubmitSlot(index),c=>Equip308PickSlot(index),false);
                Color keptFrame=Equip308KeptFrame(s);
                if(keptFrame!=s.Ink)cell.Init(s,cell.Visual,frame,keptFrame,c=>Equip308PickSlot(index));
                TMP_Text name;
                switch(d.Kind)
                {
                    case Equip308Kind.Gear:
                    {
                        var slot=(EquipmentSlot)d.Index;string worn=state!=null?state.Equipped[d.Index]:null;bool has=!string.IsNullOrEmpty(worn);
                        cell.Id=has?worn:"";
                        Image chip;
                        if(th!=null)
                        {
                            chip=Equip308Pane(cell.transform,0,0,E308Chip,!has,s,th);
                            if(has)chip=Content304Chip(cell.transform,"Chip",0,0,E308Chip,false);
                            Equip308States(cell,pane,false,true,s,th);
                        }
                        else chip=Content304Chip(cell.transform,"Chip",0,0,E308Chip,!has);
                        if(has)Content304GearArt(cell.transform,slot,art,E308ArtPad,E308ArtPad,E308Chip-2*E308ArtPad,Color.white);
                        else Content304GearArt(cell.transform,slot,art,E308GhostPad,E308GhostPad,E308Chip-2*E308GhostPad,UiStyle304SO.A(s.Paper,E308GhostAlpha));
                        // 강화 단계 = "+N" after the 부위 name, the item name's own notation (ink dabs on the chip read as part of the picture)
                        int level=has?state.Level(worn):0;
                        name=V.Label(s,cell.transform,"SlotName",level>0?Content304SlotNames[d.Index]+" +"+level:Content304SlotNames[d.Index],UiType304.Meta20,s.Mist,0,nameDy);
                        cell.BindLabel(name,s.Mist,s.Paper);
                        cell.gameObject.AddComponent<EquipmentDropSlot>().Slot=slot;
                        content304GearChips[cell]=chip;
                        // D21: something owned for this slot has not been looked at yet (cleared when the slot is selected)
                        if(state!=null)foreach(var o in state.Owned)
                        {
                            var od=catalog.Find(o.Id);
                            if(od!=null&&od.Slot==slot&&seen.IsNew("gear:"+o.Id)){Content304NewMark(cell,"gear:"+o.Id,E308Chip-22,-8,false);break;}
                        }
                        break;
                    }
                    case Equip308Kind.Stone:
                    {
                        var e=(Element)d.Index;cell.Id="stone:"+d.Index;float size=E308Chip-2*E308StampPad;
                        if(th!=null)Equip308Pane(cell.transform,0,0,E308Chip,false,s,th);
                        Content304Chip(cell.transform,"Chip",0,0,E308Chip,false);
                        if(th!=null)Equip308States(cell,pane,false,true,s,th);
                        var stamp=V.Element(s,cell.transform,d.Index,E308StampPad,E308StampPad,size,s.Ink);
                        V.Label(s,stamp.transform,"Hanja",EquipmentText308.ElementHanja(e),UiType304.Serif900_28,s.Ink,0,0,size,size,TextAlignmentOptions.Center);
                        name=V.Label(s,cell.transform,"SlotName",EquipmentText308.ElementName(e),UiType304.Meta20,s.Mist,0,nameDy);
                        cell.BindLabel(name,s.Mist,s.Paper);
                        float dx=name.rectTransform.sizeDelta.x+E308StepGap;
                        // theme: petals 7 px wide at pitch 13, so name + petals end clear of the next 칸's 방점 (pitch 108)
                        for(int k=0;k<quote.MaximumLevel;k++)
                            if(th!=null)Equip308Pip(s,cell.transform,dx+k*E308TStepPitch,nameDy+E308StepDy,E308TPetalW,E308TPetalH,k<quote.CurrentLevel,1f);
                            else Equip308Pip(s,cell.transform,dx+k*E308StepPitch,nameDy+E308StepDy,E308StepW,E308StepH,k<quote.CurrentLevel,1f);
                        break;
                    }
                    default:
                    {
                        string id=Menu304Virtues[d.Index];bool owned=known!=null&&known.Contains(id);cell.Id="virtue:"+d.Index;
                        float size=E308Chip-2*E308SealPad;
                        if(owned)
                        {
                            if(th!=null)Equip308Pane(cell.transform,0,0,E308Chip,false,s,th);
                            Content304Chip(cell.transform,"Chip",0,0,E308Chip,false);
                            if(th!=null)Equip308States(cell,pane,false,true,s,th);
                            Equip308Seal(cell.transform,E308SealPad,E308SealPad,size,s.Ink,cart);
                            V.Label(s,cell.transform,"Glyph",id,UiType304.ChipGlyph56,s.Ink,0,0,E308Chip,E308Chip-4,TextAlignmentOptions.Center);
                        }
                        else
                        {
                            // 잠긴 칸: no paper. Theme = bare 빗살 (Lattice.Bit) in the bar colour under the 비활성색 관변 테
                            if(th!=null)
                            {
                                var bare=V.SpriteImage(cell.transform,"Pane",th.LatticeBit,th.Wood,0,0,E308Chip,E308Chip);bare.type=Image.Type.Tiled;
                                Equip308States(cell,pane,false,true,s,th);
                            }
                            Equip308Seal(cell.transform,E308SealPad,E308SealPad,size,s.Off,cart);
                        }
                        // the 한글 음 under the chip (the 한자 is on the chip; same grammar as 목 화 토 금 수 one row up)
                        name=V.Label(s,cell.transform,"SlotName",Equip308VirtueReadings.Substring(d.Index,1),UiType304.Meta20,owned?s.Mist:s.Off,0,nameDy);
                        cell.BindLabel(name,name.color,owned?s.Paper:s.Off);
                        if(Equip308RenUsed(d.Index,owned))Content304Sealed(cell.transform,E308SealPad,E308Chip*.5f-11,size);   // D22
                        break;
                    }
                }
                equip308Cells[i]=cell;content304GearCells.Add(cell);equip308Rows[equip308Rows.Count-1].Add(i);col++;
            }
            // theme: one 교창 per present row, closed after its last 칸 (no bar is drawn for a 칸 that does not exist); the first
            // gear row is widened by a lacquer board to the width of the row under it and carries the one najeon figure
            if(th!=null)
                for(int r=0;r<equip308Rows.Count;r++)
                    Equip308Sash(lattice,gridX,gridY+r*pitchY,equip308Rows[r].Count,r==boardRow?(int)E308TBoardCells:0,th,EquipmentScreen308SO.Motifs(data));
            Equip308Navigation();
        }

        bool Equip308RenUsed(int virtue,bool owned)=>owned&&virtue==0&&Session.DemoCampaignActive&&Session.Progress.renUsed;

        /// <summary>Temporary Exception 10: an ink frame cannot be seen on the veil, so the kept 칸 (the focus sits in the
        /// middle column) keeps its frame in 안개 (mock sheet (f)). The data asset can pick another colour or go back to ink.</summary>
        Color Equip308KeptFrame(UiStyle304SO s)
        {
            if(Equip308Theme!=null)return new Color(0f,0f,0f,0f);   // theme: the kept 칸 wears the nacre frame (Equip308States); the brush frame draws nothing
            var data=Equip308Data();
            return data==null?s.Mist:data.KeptFrameOverride?data.KeptFrameOnVeil:s.Ink;
        }

        /// <summary>Mouse: while the focus sits in the middle column the 칸 do not take it on mouse-over (a click still selects
        /// one). Without this the pointer could not cross the grid to reach 바꿔 낄 것 (every 칸 on the way became the selection
        /// and replaced the list), and a row dragged across another 칸 was destroyed with its list before it could be dropped.
        /// The rows of the middle column keep mouse-over = focus.</summary>
        void Equip308HoverLock()
        {
            foreach(var c in content304GearCells)if(c!=null&&c.Visual!=null)c.Visual.SelectOnHover=!equip308InMiddle;
        }

        /// <summary>관변 테 at any size (seal_frame.png; 3 px lines before content304-setup).</summary>
        static void Equip308Seal(Transform parent,float x,float y,float size,Color tint,ContentArt304 art)
        {
            if(art!=null&&art.SealFrame!=null){V.SpriteImage(parent,"SealFrame",art.SealFrame,tint,x,y,size,size);return;}
            V.Image(V.Rect("SealT",parent,x,y,size,3),tint);V.Image(V.Rect("SealB",parent,x,y+size-3,size,3),tint);
            V.Image(V.Rect("SealL",parent,x,y+3,3,size-6),tint);V.Image(V.Rect("SealR",parent,x+size-3,y+3,3,size-6),tint);
        }

        /// <summary>§6 explicit navigation table (never Automatic). Left / right = the neighbour in the row; a row's last cell
        /// goes right into the middle column (Equip308WireMiddle). Up / down = the same column, or the row's last cell when
        /// the row is shorter; the first row goes up to the sub tab; the last row has nothing below.</summary>
        void Equip308Navigation()
        {
            var tabGo=Content304FindSelectable(contentRoot,"EquipmentTab");
            var tab=tabGo!=null?tabGo.GetComponent<Selectable>():null;
            for(int r=0;r<equip308Rows.Count;r++)
            {
                var row=equip308Rows[r];
                for(int c=0;c<row.Count;c++)
                {
                    var button=equip308Cells[row[c]].GetComponent<Button>();
                    button.navigation=new Navigation
                    {
                        mode=Navigation.Mode.Explicit,
                        selectOnLeft=c>0?Equip308Button(row[c-1]):null,
                        selectOnRight=c<row.Count-1?Equip308Button(row[c+1]):null,
                        selectOnUp=r>0?Equip308Button(Equip308InRow(equip308Rows[r-1],c)):tab,
                        selectOnDown=r<equip308Rows.Count-1?Equip308Button(Equip308InRow(equip308Rows[r+1],c)):null,
                    };
                }
            }
        }
        static int Equip308InRow(List<int> row,int column)=>row[Mathf.Min(column,row.Count-1)];
        Selectable Equip308Button(int i){var c=Equip308Cell(i);return c!=null?c.GetComponent<Button>():null;}

        /// <summary>The middle column's rows: up / down between them, left back to the kept slot; every row end of the grid
        /// goes right to the first of them (nothing when the list is empty).</summary>
        void Equip308WireMiddle()
        {
            equip308Middle.RemoveAll(m=>m==null||!m.interactable);
            Selectable first=equip308Middle.Count>0?equip308Middle[0]:null;
            foreach(var row in equip308Rows)
            {
                var end=Equip308Button(row[row.Count-1]);if(end==null)continue;
                var nav=end.navigation;nav.selectOnRight=first;end.navigation=nav;
            }
            Selectable kept;
            if(Equip308Fragments){var chip=Content304FirstFragment();kept=chip!=null?chip.GetComponent<Selectable>():null;}   // 석경 탭: back to the chip
            else kept=Equip308Button(equip308Slot);
            for(int i=0;i<equip308Middle.Count;i++)
                equip308Middle[i].navigation=new Navigation
                {
                    mode=Navigation.Mode.Explicit,selectOnLeft=kept,
                    selectOnUp=i>0?equip308Middle[i-1]:null,selectOnDown=i<equip308Middle.Count-1?equip308Middle[i+1]:null,
                };
        }

        // ================================================================== selection
        void Equip308SyncGearSelection(ContentCell304 cell)
        {
            var d=Equip308Slots[equip308Slot];
            if(d.Kind!=Equip308Kind.Gear){gearSelection="";content304EmptySlot=null;return;}
            gearSelection=cell.Id??"";
            content304EmptySlot=string.IsNullOrEmpty(cell.Id)?(EquipmentSlot?)(EquipmentSlot)d.Index:null;
        }

        /// <summary>A slot got the focus (keys, pad, mouse-over): it becomes the page selection and the middle column shows it.</summary>
        void Equip308PickSlot(int i)
        {
            var cell=Equip308Cell(i);if(cell==null)return;
            if(i!=equip308Slot)gearError="";
            equip308Slot=i;equip308Candidate=null;equip308InMiddle=false;
            content304Kept=cell.name;content304Focus=cell.name;
            Equip308Raise(cell);
            Equip308SyncGearSelection(cell);
            var d=Equip308Slots[i];
            if(d.Kind==Equip308Kind.Gear&&Session.EquipmentReady)
            {
                var catalog=Session.Content.EquipmentCatalog;var seen=Content304Seen;
                foreach(var o in Session.Progress.equipment.Owned){var od=catalog.Find(o.Id);if(od!=null&&(int)od.Slot==d.Index)seen.MarkSeen("gear:"+o.Id);}
            }
            Content304RefreshGear();
            Equip308Legend();
        }

        void Equip308PickCandidate(string id)
        {
            if(id!=equip308Candidate)gearError="";
            equip308Candidate=id;equip308InMiddle=true;content304Focus="Gear_"+id;
            Equip308Refresh();
            Equip308Legend();
        }

        void Equip308FocusSlot(int i)
        {
            var cell=Equip308Cell(i);if(cell==null)return;
            equip308Slot=i;
            Content304Select(cell.gameObject);
        }

        /// <summary>Enter / click on a slot. 장비: the focus moves into 바꿔 낄 것 (first item not worn, else the worn one).
        /// 오행 마석: the 정비 page beside a shelter, nothing elsewhere. 오덕: the 차패 page. Nothing is bought here.</summary>
        void Equip308SubmitSlot(int i)
        {
            if(Equip308Cell(i)==null)return;
            if(equip308Slot!=i||equip308SlotKey==null)Equip308PickSlot(i);
            var d=Equip308Slots[i];
            if(d.Kind==Equip308Kind.Stone){Equip308OpenService(d.Index);return;}
            if(d.Kind==Equip308Kind.Virtue){OpenPage("차패");return;}
            if(!Session.EquipmentReady||equip308Middle.Count==0)return;
            string worn=Session.Progress.equipment.Equipped[d.Index];
            var target=equip308Middle.FirstOrDefault(m=>m!=null&&m.name!="Gear_"+worn)??equip308Middle[0];
            if(target!=null)Content304Select(target.gameObject);
        }

        void Equip308OpenService(int track)
        {
            if(Session==null||!Session.AtDemoShop)return;
            content304Focus="Buy_"+(DemoUpgradeTrack)track;   // BuildDemoShop restores the row by name
            OpenPage("정비");
        }

        void Equip308ReadCodex()
        {
            var f=Equip308SelectedFragment();if(f==null)return;
            selectedSpell=f.Letter;content304Focus="Codex_"+f.Letter;OpenPage("술식 도감");
        }

        FragmentDefinition Equip308SelectedFragment()
        {
            var ui=Session.Progress.ui;
            return WorldMacroCollectionCatalog.AllFragments.FirstOrDefault(x=>x.ItemId==selectedItem&&ui.GetItemCount(x.ItemId)>0);
        }

        /// <summary>Enter / click on a 바꿔 낄 것 row: equip (or unequip the worn one) at once, or move to the 착용 / 해제 row when
        /// EquipmentScreen308SO.DirectEquipFromCandidate is off.</summary>
        void Equip308SubmitCandidate(string id)
        {
            if(!Session.EquipmentReady)return;
            var item=Session.Content.EquipmentCatalog.Find(id);if(item==null)return;
            if(!EquipmentScreen308SO.Direct(Equip308Data()))
            {
                if(equip308Candidate!=id)Equip308PickCandidate(id);
                var go=Content304FindSelectable(content304Detail,"GearEquip");
                if(go!=null){Content304Select(go);return;}
            }
            Equip308Commit(item);
        }

        void Equip308Commit(EquipmentDefinition item)
        {
            var state=Session.Progress.equipment;bool worn=state.Equipped[(int)item.Slot]==item.Id;
            // the rebuild restores the selection by name (Menu304AfterBuild): hand it the slot, not the row that goes away
            Equip308FocusSlot(Equip308IndexOf(item.Slot));
            GearAction(worn?EquipmentAction.Unequip:EquipmentAction.Equip,item.Id,item.Slot,state.Revision);
        }

        void Equip308Dropped(string id,EquipmentSlot slot,long revision)
        {
            Equip308FocusSlot(Equip308IndexOf(slot));
            GearAction(EquipmentAction.Equip,id,slot,revision);
        }

        /// <summary>[X] 빼기 (the map's [X] 표식 지우기 key): the focused 장비 칸 takes off what it wears.</summary>
        void Equip308Remove(bool fromLegend)
        {
            if(Page!="소지품"||Busy||confirmationRoot!=null||Session==null||!Session.EquipmentReady||Equip308Fragments)return;
            var cell=Equip308Cell(equip308Slot);if(cell==null)return;
            var d=Equip308Slots[equip308Slot];
            if(d.Kind!=Equip308Kind.Gear||string.IsNullOrEmpty(cell.Id))return;
            if(fromLegend)Equip308FocusSlot(equip308Slot);
            else{var es=EventSystem.current;if(es==null||es.currentSelectedGameObject!=cell.gameObject)return;}
            GearAction(EquipmentAction.Unequip,cell.Id,(EquipmentSlot)d.Index,Session.Progress.equipment.Revision);
        }

        // the key arrives inside the Input System's own update: run the action from the player loop instead
        void Equip308RemovePressed(){Equip308RemoveNextUpdate().Forget();}
        async UniTaskVoid Equip308RemoveNextUpdate()
        {
            if(await UniTask.Yield(PlayerLoopTiming.Update,this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow())return;
            Equip308Remove(false);
        }

        void Equip308Keys(EquipmentScreen308SO data)
        {
            string key=EquipmentScreen308SO.RemoveBinding(data),pad=EquipmentScreen308SO.RemovePadBinding(data);
            if(string.IsNullOrEmpty(key)&&string.IsNullOrEmpty(pad))return;
            V.Rect("EquipKeys308",contentRoot,0,0,1,1).gameObject.AddComponent<EquipmentKeys308>().Bind(key,pad,Equip308RemovePressed);
        }

        /// <summary>Esc while the focus is in the middle column: back to the slot (the menu and the pause depth stay). Called
        /// by PlaytestUiRoot.Back; false = not handled (Esc closes the menu as before).</summary>
        bool Equipment308Back()
        {
            if(content304Detail==null||Session==null)return false;
            var es=EventSystem.current;var sel=es!=null?es.currentSelectedGameObject:null;
            if(sel==null||!sel.activeInHierarchy||!sel.transform.IsChildOf(content304Detail))return false;
            GameObject back=Equip308Fragments?Content304FirstFragment():Equip308Cell(equip308Slot)?.gameObject;
            if(back==null)return false;
            PlayUi(Theme.BackSound,.3f);
            equip308InMiddle=false;
            Content304Select(back);
            Equip308Legend();
            return true;
        }

        // ================================================================== centre: detail
        /// <summary>Content304RefreshGear's 소지품 branch. Two keys: the slot (rebuilds the whole column: info + 바꿔 낄 것 / link
        /// rows) and the info (rebuilds only the left part, so the list row holding the focus survives).</summary>
        void Equip308Refresh()
        {
            if(content304Detail==null||Session==null)return;
            var s=Content304Style;bool fragments=Equip308Fragments;
            long revision=Session.EquipmentReady?Session.Progress.equipment.Revision:-1;
            string slotKey=content304Detail.GetInstanceID()+"|"+fragments+"|"+(fragments?selectedItem:equip308Slot.ToString())+"|"+revision;
            if(slotKey!=equip308SlotKey)
            {
                equip308SlotKey=slotKey;equip308InfoKey=null;equip308Middle.Clear();
                V.Clear(content304Detail);
                equip308Info=V.Rect("EquipInfo308",content304Detail,0,0,UiPageFit304.Width,UiPageFit304.Height);
            }
            string infoKey=slotKey+"|"+equip308Candidate+"|"+gearError;
            if(infoKey==equip308InfoKey)return;
            bool slotBuild=equip308InfoKey==null;
            equip308InfoKey=infoKey;
            V.Clear(equip308Info);
            equip308SectionsTop=E308SubY;
            float bottom=fragments?Equip308FragmentInfo(s):Equip308SlotInfo(s);
            if(!slotBuild)return;
            Equip308SlotParts(s,bottom);
            Equip308WireMiddle();
            Equip308Head();
        }

        /// <summary>The two lines over the grid: what kind of slot, and what it holds NOW (the comparison's base).</summary>
        void Equip308Head()
        {
            string kind="",item="";
            if(Equip308Fragments)
            {
                var f=Equip308SelectedFragment();
                if(f!=null){kind="석경 조각";item=f.Letter+"의 석경";}
            }
            else if(equip308Slot>=0)
            {
                var d=Equip308Slots[equip308Slot];
                if(d.Kind==Equip308Kind.Gear)
                {
                    kind=Equip308GearKind((EquipmentSlot)d.Index);item="비어 있음";
                    if(Session.EquipmentReady)
                    {
                        var state=Session.Progress.equipment;var worn=Session.Content.EquipmentCatalog.Find(state.Equipped[d.Index]);
                        if(worn!=null)item=EquipmentText308.GearName(worn,state);
                    }
                }
                else if(d.Kind==Equip308Kind.Stone){var e=(Element)d.Index;kind="오행 마석 · "+EquipmentText308.ElementName(e);item=EquipmentText308.StoneName(e);}
                else{kind="오덕 · "+Menu304Virtues[d.Index];item=Equip308VirtueNames[d.Index];}
            }
            Equip308SetText(equip308KindLabel,kind);Equip308SetText(equip308ItemLabel,item);
        }
        static void Equip308SetText(TMP_Text t,string value)
        {
            if(t==null)return;
            t.text=value??"";var p=UiText304.Preferred(t,t.text);
            t.rectTransform.sizeDelta=new Vector2(Mathf.Max(1f,Mathf.Ceil(p.x)),Mathf.Max(1f,Mathf.Ceil(p.y)));
        }

        Sprite Equip308Mark(int which)
        {
            var d=Equip308Data();if(d==null)return null;
            switch(which){case 0:return d.MarkEffect;case 1:return d.MarkCompare;case 2:return d.MarkUpgrade;case 3:return d.MarkCandidates;
                case 4:return d.MarkBody;case 5:return d.MarkPower;case 6:return d.MarkTier;default:return d.MarkLearned;}
        }

        /// <summary>Kind, name (60 → 44 → two 어절 lines → ellipsis), state line, step dabs, the dry divider. A long name or
        /// state pushes everything under it down; the sections start at equip308SectionsTop.</summary>
        void Equip308Header(UiStyle304SO s,Transform root,string kind,string title,Color titleColor,string state,int steps,int stepsOn,string titleName="SelectedGearTitle")
        {
            V.Label(s,root,"SelectedGearSlot",kind,UiType304.Meta20,s.Mist,E308MidX,E308HeadKindY);
            var t=V.Label(s,root,titleName,title,UiType304.Speaker60,titleColor,E308MidX-4,E308HeadNameY);
            Content304AtCss(t,E308MidX-4,E308HeadNameY,1.1f);
            float extra=0f;
            if(t.rectTransform.sizeDelta.x>E308NameW)
            {
                UiText304.ApplyRole(t,s.Role(UiType304.Heading44),s);
                var one=UiText304.Preferred(t,title);float line=Mathf.Ceil(UiText304.Preferred(t,"가").y);
                if(one.x<=E308NameW)t.rectTransform.sizeDelta=new Vector2(Mathf.Ceil(one.x),Mathf.Ceil(one.y));
                else
                {
                    t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Ellipsis;t.maxVisibleLines=2;
                    float h=Mathf.Min(Mathf.Ceil(UiText304.Preferred(t,title,E308NameW).y),line*2f);
                    t.rectTransform.sizeDelta=new Vector2(E308NameW,h);extra+=Mathf.Max(0f,h-line);
                }
                Content304AtCss(t,E308MidX-4,E308HeadNameY+12,1.2f);
            }
            if(!string.IsNullOrEmpty(state))
            {
                var st=V.Label(s,root,"SelectedGearLevel",state,UiType304.Label26,s.Mist,E308MidX,E308HeadStateY,E308NameW,0,TextAlignmentOptions.TopLeft,true);
                Content304AtCss(st,E308MidX,E308HeadStateY+extra,1.25f);
                extra+=Mathf.Max(0f,st.rectTransform.sizeDelta.y-Mathf.Ceil(UiText304.Preferred(st,"가").y));
            }
            for(int k=0;k<steps;k++)Equip308Pip(s,root,E308MidX+2+k*E308HeadDabPitch,E308HeadDabY+extra,28,21,k<stepsOn,2f);
            V.Brush(s,root,"DetailDivider",StrokeClass304.Dry,s.Paper,E308DividerX,E308DividerY+extra,E308DividerW,E308DividerH,E308DividerAlpha);
            equip308SectionsTop=E308SubY+extra;
        }

        /// <summary>테 두른 그림 200² (paper = a full chip under an ink frame; else the empty-chip weight under a paper frame).
        /// The picture fades in only when it is another picture than the last one shown.</summary>
        RectTransform Equip308ArtBox(UiStyle304SO s,Transform root,bool paper,string artKey)
        {
            var box=V.Rect("ItemArt",root,E308ArtX,E308ArtY,E308ArtSize,E308ArtSize);
            var data=Equip308Data();var cart=Content304Art;var th=Equip308Theme;
            if(paper&&th!=null)
            {
                // 백자 상감 판 (Plaque.Porcelain, inlaid double line + foot): the picture register over a foot register that
                // carries the lotus band (Sanggam.Lotus, 10 petals of 16 px) between an inlaid line and the frame's inner line.
                // The empty-slot ghost and the 탁본 glyph keep the #304 frame (light on dark needs the dark chip).
                V.SpriteImage(box,"SelectedArtPaper",th.Plaque,Color.white,0,0,E308ArtSize,E308ArtSize);
                if(EquipmentScreen308SO.Motifs(data)&&th.Lotus!=null)
                {
                    Equip308Fill(box,"FootLine",E308TFootInset,E308TFootY,E308ArtSize-2*E308TFootInset,1,th.InlayDark,th);
                    var band=V.SpriteImage(box,"FootBand",th.Lotus,th.InlayDark,E308TLotusX,E308TLotusY,E308TLotusW,E308TLotusH);band.type=Image.Type.Tiled;
                }
            }
            else
            {
                var chip=Content304Chip(box,paper?"SelectedArtPaper":"SelectedArtEmpty",0,0,E308ArtSize,!paper);
                chip.raycastTarget=false;
                var sprite=data!=null&&data.ItemFrame!=null?data.ItemFrame:cart!=null?cart.GwangGwak:null;
                Color tint=UiStyle304SO.A(paper?s.Ink:s.Paper,E308FrameAlpha);
                if(sprite!=null)Content304Ink(V.SpriteImage(box,"ItemFrame",sprite,tint,0,0,E308ArtSize,E308ArtSize),s);
                else Content304Hairline(box,0,0,E308ArtSize,E308ArtSize,tint,s);
            }
            var content=V.Rect("Art",box,0,0,E308ArtSize,E308ArtSize);
            if(artKey!=equip308ArtKey)
            {
                bool first=equip308ArtKey==null;equip308ArtKey=artKey;
                if(!first)
                {
                    var group=content.gameObject.AddComponent<CanvasGroup>();group.alpha=0f;group.blocksRaycasts=false;
                    UiTween304.Alpha(group,1f,s.Motion.Sec(s.Motion.StrokeMs,UiTween304.ReducedMotion),null,UiTween304.Token(group)).Forget();
                }
            }
            return content;
        }

        float Equip308Section(UiStyle304SO s,Transform root,string name,Sprite mark,string head,float x,float y)
        {
            float tx=x;
            if(mark!=null){V.SpriteImage(root,name+"Mark",mark,s.Mist,x,y-2,E308MarkSize,E308MarkSize);tx+=E308HeadTextDx;}
            var h=V.Label(s,root,name,head,UiType304.Meta20,s.Mist,tx,y);
            return y+Mathf.Max(E308MarkSize,h.rectTransform.sizeDelta.y)+E308HeadGap;
        }

        float Equip308Line(UiStyle304SO s,Transform root,string name,string text,UiType304 role,Color color,float x,float y,float width=E308SubW)
        {
            var t=V.Label(s,root,name,text,role,color,x,y,width,0,TextAlignmentOptions.TopLeft,true);
            return y+t.rectTransform.sizeDelta.y+E308LineGap;
        }

        float Equip308Error(UiStyle304SO s,Transform root,float y)
        {
            if(string.IsNullOrEmpty(gearError))return y;
            float top=Mathf.Max(y,E308ErrorY);
            var t=V.Label(s,root,"GearTransactionError",gearError,UiType304.MetaBold20,s.CinnabarLift,E308SubLX,top,E308SubW,0,TextAlignmentOptions.TopLeft,true);
            t.maxVisibleLines=E308ErrorLines;
            return top+t.rectTransform.sizeDelta.y;
        }

        float Equip308SlotInfo(UiStyle304SO s)
        {
            if(equip308Slot<0)return E308SubY;
            var d=Equip308Slots[equip308Slot];
            if(d.Kind==Equip308Kind.Stone)return Equip308StoneInfo(s,equip308Info,d.Index);
            if(d.Kind==Equip308Kind.Virtue)return Equip308VirtueInfo(s,equip308Info,d.Index);
            return Equip308GearInfo(s,equip308Info,(EquipmentSlot)d.Index);
        }

        float Equip308GearInfo(UiStyle304SO s,Transform root,EquipmentSlot slot)
        {
            string slotName=Content304SlotNames[(int)slot];
            if(!Session.EquipmentReady)
            {
                V.Label(s,root,"GearError","장비 저장을 확인해야 한다.",UiType304.Body24,s.CinnabarLift,E308MidX,E308HeadNameY,E308NameW,0,TextAlignmentOptions.TopLeft,true);
                return E308SubY;
            }
            var catalog=Session.Content.EquipmentCatalog;var state=Session.Progress.equipment;var art=Session.Content.EquipmentUiArt;
            string worn=state.Equipped[(int)slot];
            var item=catalog.Find(!string.IsNullOrEmpty(equip308Candidate)?equip308Candidate:worn);
            float pad=(E308ArtSize-E308ArtInner)*.5f;
            if(item==null||item.Slot!=slot)
            {
                bool any=state.Owned.Any(o=>{var x=catalog.Find(o.Id);return x!=null&&x.Slot==slot;});
                Equip308Header(s,root,slotName,"비어 있음",s.Mist,any?"":"맞는 장비 없음",0,0);
                Content304GearArt(Equip308ArtBox(s,root,false,"empty:"+slot),slot,art,pad,pad,E308ArtInner,UiStyle304SO.A(s.Paper,E308GhostAlpha));
                return Equip308Error(s,root,equip308SectionsTop);
            }
            bool isWorn=worn==item.Id;int max=catalog.UpgradeCosts!=null?catalog.UpgradeCosts.Length:0;
            Equip308Header(s,root,slotName,item.Name,s.Paper,EquipmentText308.GearState(state,item,isWorn),max,state.Level(item.Id));
            var artRoot=Equip308ArtBox(s,root,true,"gear:"+slot);   // one picture per slot (Temporary Exception 1)
            var gearRect=Equip308ArtRect(0);
            Content304GearArt(artRoot,slot,art,gearRect.x,gearRect.y,gearRect.width,Color.white);
            float y=equip308SectionsTop;
            y=Equip308Section(s,root,"EffectHead",Equip308Mark(0),"효과",E308SubLX,y);
            y=Equip308Line(s,root,"SelectedGearEffect",EquipmentText308.GearEffect(catalog,state,item),UiType304.Title32,s.Paper,E308SubLX,y);
            y=Equip308Line(s,root,"EffectParts",EquipmentText308.GearParts(catalog,state,item),UiType304.Meta20,s.Mist,E308SubLX,y)+E308SectionGap;
            if(!isWorn)
            {
                y=Equip308Section(s,root,"CompareHead",Equip308Mark(1),"견줌",E308SubLX,y);
                y=Equip308Line(s,root,"SelectedGearCompare",EquipmentText308.Compare(catalog,state,item),UiType304.ValueBold24,s.Paper,E308SubLX,y);
                y=Equip308Line(s,root,"CompareAgainst",EquipmentText308.CompareAgainst(catalog,state,item,slotName),UiType304.Body22,s.Mist,E308SubLX,y)+E308SectionGap;
            }
            y=Equip308Section(s,root,"UpgradeHead",Equip308Mark(2),"다음 강화",E308SubLX,y);
            if(EquipmentText308.NextUpgrade(catalog,state,item,out string step,out int cost))
            {
                y=Equip308Line(s,root,"UpgradeNext",step,UiType304.Body24,s.Paper,E308SubLX,y);
                y=Equip308Line(s,root,"UpgradeWhere",EquipmentText308.UpgradeWhere(Content304Artisan,cost),UiType304.Meta20,s.Mist,E308SubLX,y)+E308SectionGap;
            }
            else y=Equip308Line(s,root,"UpgradeNext","강화 완료",UiType304.Body24,s.Mist,E308SubLX,y)+E308SectionGap;
            if(!EquipmentScreen308SO.Direct(Equip308Data())&&!string.IsNullOrEmpty(equip308Candidate))
            {
                // three-step variant (question 3): the row keeps the old harness name GearEquip
                var target=item;
                var row=V.FocusRow(s,root,"GearEquip",isWorn?"해제":"착용",E308SubLX,y,E308SubW,E308LinkH,()=>Equip308Commit(target),new FocusRowSpec304
                    {Role=UiType304.Label26,LabelX=E308LinkLabelX,Key="Enter",KeyMode=FocusKeyMode304.FilledWhenFocused,SoundTheme=Theme});
                Equip308NoteMiddle(row.Button);
                row.Button.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=Equip308Button(equip308Slot),selectOnRight=equip308Middle.FirstOrDefault(m=>m!=null&&m.name=="Gear_"+target.Id)};
                y+=E308LinkH+E308LineGap;
            }
            return Equip308Error(s,root,y);
        }

        float Equip308StoneInfo(UiStyle304SO s,Transform root,int track)
        {
            var e=(Element)track;
            if(Session.DemoEconomy==null||!Session.DemoEconomy.TryQuote((DemoUpgradeTrack)track,out var q,out _))return E308SubY;
            Equip308Header(s,root,"오행 마석",EquipmentText308.StoneName(e),s.Paper,EquipmentText308.StoneLevel(q.CurrentLevel),q.MaximumLevel,q.CurrentLevel);
            var artRoot=Equip308ArtBox(s,root,true,"stone:"+track);
            var stampRect=Equip308ArtRect(1);float size=stampRect.width;Color ink=Equip308PlaqueInk(s);
            var stamp=V.Element(s,artRoot,track,stampRect.x,stampRect.y,size,ink);
            V.Label(s,stamp.transform,"Hanja",EquipmentText308.ElementHanja(e),UiType304.Speaker60,ink,0,0,size,size,TextAlignmentOptions.Center);
            float y=equip308SectionsTop;
            y=Equip308Section(s,root,"EffectHead",Equip308Mark(0),"효과",E308SubLX,y);
            y=Equip308Line(s,root,"SelectedGearEffect",EquipmentText308.StoneEffect(e,q.CurrentTotalBonus),UiType304.Title32,s.Paper,E308SubLX,y)+E308SectionGap;
            y=Equip308Section(s,root,"UpgradeHead",Equip308Mark(2),"다음 강화",E308SubLX,y);
            if(!q.AtMaximum)
            {
                y=Equip308Line(s,root,"UpgradeNext",EquipmentText308.StoneNext(q.CurrentLevel+1,q.NextTotalBonus),UiType304.Body24,s.Paper,E308SubLX,y);
                y=Equip308Line(s,root,"UpgradeWhere",EquipmentText308.Coins(q.Cost),UiType304.Meta20,q.CanAfford?s.Mist:s.Off,E308SubLX,y)+E308SectionGap;
            }
            else y=Equip308Line(s,root,"UpgradeNext","강화 완료",UiType304.Body24,s.Mist,E308SubLX,y)+E308SectionGap;

            // right: 익힌 <속성> 술식 a / b (read only; the codex owns the detail)
            var spells=WorldMacroCollectionCatalog.AllSpells.Where(x=>x.Element==e).ToList();var known=Session.Progress.ui.knownSpellLetters;
            var cart=Content304Art;
            float ry=Equip308Section(s,root,"LearnedHead",Equip308Mark(7),"익힌 "+EquipmentText308.ElementName(e)+" 술식 "+EquipmentText308.Fraction(spells.Count(x=>known.Contains(x.Letter)),spells.Count),E308SubRX,equip308SectionsTop);
            for(int i=0;i<spells.Count;i++)
            {
                float gx=E308SubRX+i%E308LearnCols*E308LearnPitch,gy=ry+i/E308LearnCols*E308LearnPitch;
                if(known.Contains(spells[i].Letter))V.Label(s,root,"LearnedGlyph",spells[i].Letter,UiType304.Heading44,s.Paper,gx,gy,E308LearnSize,E308LearnSize,TextAlignmentOptions.Center);
                else
                {
                    float o=(E308LearnSize-E308LearnBlank)*.5f;
                    var blank=cart!=null&&cart.MukDeung!=null?cart.MukDeung:s.Sprites.Blot;
                    Content304Ink(V.SpriteImage(root,"LearnedBlank",blank,UiStyle304SO.A(s.Paper,E308BlankAlpha),gx+o,gy+o,E308LearnBlank,E308LearnBlank),s);
                }
            }
            return y;
        }

        float Equip308VirtueInfo(UiStyle304SO s,Transform root,int index)
        {
            string id=Menu304Virtues[index];var known=Session.Progress.ui.knownVirtues;bool owned=known!=null&&known.Contains(id);
            bool used=Equip308RenUsed(index,owned);
            // the 차패 page's own state wording
            string state=used?"쓰였다 · 쉼터에서 쉬면 돌아온다":owned?"새겨짐":"새기지 못함";
            Equip308Header(s,root,"오덕",Equip308VirtueNames[index],owned?s.Paper:s.Mist,state,0,0);
            var artRoot=Equip308ArtBox(s,root,owned,"virtue:"+index+owned);
            // an owned 오덕 sits on the plaque (seal + 한자 in the inlay colour); a locked one keeps the #304 frame, full size
            float pad=(E308ArtSize-E308ArtInner)*.5f;
            var sealRect=owned?Equip308ArtRect(2):new Rect(pad,pad,E308ArtInner,E308ArtInner);Color ink=owned?Equip308PlaqueInk(s):s.Off;
            Equip308Seal(artRoot,sealRect.x,sealRect.y,sealRect.width,ink,Content304Art);
            if(owned)V.Label(s,artRoot,"Glyph",id,UiType304.Region72,ink,sealRect.x,sealRect.y,sealRect.width,sealRect.height-6,TextAlignmentOptions.Center);
            if(used)Content304Sealed(artRoot,sealRect.x,sealRect.y+sealRect.height*.5f-14,sealRect.width,28);
            return equip308SectionsTop;
        }

        float Equip308FragmentInfo(UiStyle304SO s)
        {
            var root=equip308Info;var ui=Session.Progress.ui;
            var f=Equip308SelectedFragment();if(f==null)return E308SubY;
            Equip308Header(s,root,"석경 조각",f.Letter+"의 석경 조각",s.Paper,"지닌 수 "+ui.GetItemCount(f.ItemId),0,0,"ItemTitle");
            var artRoot=Equip308ArtBox(s,root,false,"fragment:"+f.Id);
            var glyph=V.Label(s,artRoot,"ItemGlyph",f.Letter,UiType304.Glyph240,s.Paper,0,0,E308ArtSize,E308ArtSize,TextAlignmentOptions.Center);
            glyph.gameObject.AddComponent<UiTextNoScale304>();glyph.fontSize=E308ArtInner*E308GlyphFill;
            float y=equip308SectionsTop;
            var sources=WorldMacroCollectionCatalog.AllBundles.Where(b=>b.Fragments.Any(x=>x.Id==f.Id)).Select(b=>b.Title).ToArray();
            if(sources.Length>0)
            {
                y=Equip308Section(s,root,"SourceHead",Equip308Mark(7),"나온 곳",E308SubLX,y);
                for(int i=0;i<sources.Length&&i<3;i++)y=Equip308Line(s,root,"Source",sources[i],UiType304.Body24,s.Paper,E308SubLX,y,E308MidR-E308SubLX);
                y+=E308SectionGap;
            }
            return y;
        }

        /// <summary>Slot-level rows of the middle column (built once per slot, they hold the focus): 바꿔 낄 것, or the link
        /// row (정비 / 차패), or the fragment's 술식 도감에서 보기.</summary>
        void Equip308SlotParts(UiStyle304SO s,float y)
        {
            var root=content304Detail;
            if(Equip308Fragments)
            {
                if(Equip308SelectedFragment()==null)return;
                Equip308Link(s,root,"ReadCodex","술식 도감에서 보기",y,E308LinkWide,Equip308ReadCodex,false,null);
                return;
            }
            if(equip308Slot<0)return;
            var d=Equip308Slots[equip308Slot];
            if(d.Kind==Equip308Kind.Stone)
            {
                if(Session.DemoEconomy==null)return;
                int track=d.Index;
                Equip308Link(s,root,"LinkService","정비",y,E308SubW,()=>Equip308OpenService(track),!Session.AtDemoShop,"쉼터 곁에서만 열린다",Equip308StoneLink);
                return;
            }
            if(d.Kind==Equip308Kind.Virtue){Equip308Link(s,root,"LinkChapae","차패",y,E308SubW,()=>OpenPage("차패"),false,null);return;}
            if(!Session.EquipmentReady)return;

            var slot=(EquipmentSlot)d.Index;var catalog=Session.Content.EquipmentCatalog;var state=Session.Progress.equipment;var art=Session.Content.EquipmentUiArt;
            // worn first, then the order of ownership (OrderBy is stable)
            var fits=state.Owned.Select(o=>catalog.Find(o.Id)).Where(x=>x!=null&&x.Slot==slot).OrderByDescending(x=>state.Equipped[(int)slot]==x.Id).ToList();
            if(fits.Count==0)return;
            float top=Equip308Section(s,root,"CandidatesHead",Equip308Mark(3),"바꿔 낄 것 "+fits.Count,E308SubRX,equip308SectionsTop);
            // 줄 높이 = max(74, 이름 + 메타 + 8): measured, so 본문 크기 1.25 keeps the two lines inside the row
            float nameH=Equip308TextSize(s,UiType304.Label24,"가").y,metaH=Equip308TextSize(s,UiType304.Meta20,"가").y;
            float rowH=Mathf.Max(E308CandH,nameH+metaH+8f),pitch=rowH+E308CandGap;
            int visible=Mathf.Max(1,Mathf.FloorToInt((E308SubBottom-top)/pitch));
            RectTransform list=fits.Count>visible
                ?V.Scroll(root,"EquipmentCandidates",E308SubRX,top,E308SubW+18,visible*pitch,fits.Count*pitch)
                :V.Rect("EquipmentCandidates",root,E308SubRX,top,E308SubW,fits.Count*pitch);
            // theme: the row's focus frame lies on a 한지 bed too; the label stands 8 px further right, clear of the bed
            var th=Equip308Theme;float labelX=th!=null?E308TCandLabelX:E308CandLabelX;
            float room=E308SubW-labelX,chipY=(rowH-E308CandChip)*.5f,labelY=(rowH-nameH-metaH)*.5f;
            var rowChip=new Rect(E308CandChipX,chipY,E308CandChip,E308CandChip);
            foreach(var item in fits)
            {
                string id=item.Id;bool worn=state.Equipped[(int)slot]==id;int n=equip308Middle.Count;
                // a 칸, not a focus row: the focus is the 주사 테 on the chip + the 방점 (an underlay stroke would run across the
                // second rule into the status column). It is NOT in content304GearCells: no kept frame, and mouse-over stays focus
                var cell=Content304Cell(list,"Gear_"+id,0,n*pitch,E308SubW,rowH,Equip308RingRect(rowChip,true,th),
                    new Rect(E308CandDabX,chipY+(E308CandChip-E308CandDabH)*.5f,E308CandDabW,E308CandDabH),()=>Equip308SubmitCandidate(id),c=>Equip308PickCandidate(id),false);
                cell.Id=id;
                Content304Chip(cell.transform,"Chip",E308CandChipX,chipY,E308CandChip,false);
                if(th!=null)Equip308States(cell,rowChip,true,false,s,th);
                Content304GearArt(cell.transform,slot,art,E308CandChipX+E308CandArtPad,chipY+E308CandArtPad,E308CandChip-2*E308CandArtPad,Color.white);
                var name=V.Label(s,cell.transform,"Label",EquipmentText308.GearName(item,state),UiType304.Label24,s.Paper,labelX,labelY);
                var meta=V.Label(s,cell.transform,"Meta",worn?"착용 중":EquipmentText308.GearEffect(catalog,state,item),UiType304.Meta20,s.Mist,labelX,labelY+nameH);
                Equip308Clip(name,room);Equip308Clip(meta,room);
                cell.Visual.Meta=meta;cell.Visual.MetaNormal=s.Mist;cell.Visual.MetaFocused=s.Paper;
                cell.BindLabel(name,s.Paper,s.Paper);
                var drag=cell.gameObject.AddComponent<EquipmentDragItem>();drag.ItemId=id;drag.Revision=state.Revision;drag.Dropped=Equip308Dropped;
                equip308Middle.Add(cell.GetComponent<Button>());
            }
        }

        static void Equip308Clip(TMP_Text t,float width)
        {
            if(t==null||t.rectTransform.sizeDelta.x<=width)return;
            t.rectTransform.sizeDelta=new Vector2(width,t.rectTransform.sizeDelta.y);t.overflowMode=TextOverflowModes.Ellipsis;
        }

        /// <summary>연결 줄 (보조 버튼 문법, mock sheet (a) / (g)): the label on the column's left edge, an inline meta (where it
        /// leads, or why it is closed) and a dry under-stroke while unfocused; focused = the stock #304 focus row with [Enter]
        /// after the meta. The row starts E308LinkLabelX left of the column so the small 방점 sits between the rule and the label.</summary>
        void Equip308Link(UiStyle304SO s,Transform root,string name,string label,float y,float width,UnityEngine.Events.UnityAction clicked,bool disabled,string reason,string meta=null)
        {
            string shown=disabled&&!string.IsNullOrEmpty(reason)?reason:meta;
            float labelW=Equip308TextSize(s,UiType304.Label26,label).x,metaX=E308LinkLabelX+labelW+E308LinkMetaGap;
            float keyX=string.IsNullOrEmpty(shown)?float.NaN:metaX+Equip308TextSize(s,UiType304.Meta20,shown).x+E308LinkMetaGap;
            var row=V.FocusRow(s,root,name,label,E308SubLX-E308LinkLabelX,y,width+E308LinkLabelX,E308LinkH,clicked,new FocusRowSpec304
            {
                Role=UiType304.Label26,LabelX=E308LinkLabelX,DabGap=E308LinkDabGap,DabSize=new Vector2(E308DabW,E308DabH),
                Key="Enter",KeyMode=FocusKeyMode304.FilledWhenFocused,KeyX=keyX,
                Meta=meta,MetaX=metaX,UnderStroke=true,UnderStrokeW=labelW+E308LinkStrokeTail,
                Disabled=disabled,DisabledReason=reason,SoundTheme=Theme,
            });
            Equip308NoteMiddle(row.Button);
            if(!disabled)equip308Middle.Add(row.Button);
        }

        void Equip308NoteMiddle(Component row)
        {
            row.gameObject.AddComponent<ContentFocusNote304>().Selected=go=>{content304Focus=go.name;equip308InMiddle=true;Equip308Legend();};
        }

        /// <summary>Size of `text` in `role` at the current 본문 크기 (a probe label, removed at once).</summary>
        Vector2 Equip308TextSize(UiStyle304SO s,UiType304 role,string text)
        {
            var probe=V.Label(s,contentRoot,"SizeProbe",text,role,Color.white,0,0);
            Vector2 size=probe.rectTransform.sizeDelta;
            probe.gameObject.SetActive(false);Destroy(probe.gameObject);
            return size;
        }

        // ================================================================== right: 도사 상태 (read only, no focus)
        void Equip308Status(UiStyle304SO s,EquipmentScreen308SO data)
        {
            float w=E308StatusR-E308StatusX;
            var root=V.Rect("EquipStatus308",contentRoot,E308StatusX,0,w,UiPageFit304.Height);
            var progress=Session.Progress;
            if(progress.ledger.dropCurrency>0)
            {
                var drop=V.Label(s,root,"Status_drop","남긴 통보 "+progress.ledger.dropCurrency.ToString("N0"),UiType304.Meta20,s.Mist,0,E308DropY);
                V.Place(drop.rectTransform,w-drop.rectTransform.sizeDelta.x,E308DropY);
            }
            float y=E308StatusY;
            // a value that cannot be read is left out, never printed as 0 (SPEC §4)
            if(Session.TryGetStatus308(out var st))
            {
                float scale=EquipmentScreen308SO.InkScale(data);
                y=Equip308StatusHead(s,root,"StatusBody",Equip308Mark(4),"몸과 먹",y);
                Equip308StatusRow(s,root,"hp","체력",EquipmentText308.Fraction(Mathf.RoundToInt(st.Hp),Mathf.RoundToInt(st.MaxHp)),y,w);y+=E308StatusPitchBody;
                Equip308StatusRow(s,root,"ink","먹",EquipmentText308.Fraction(Mathf.RoundToInt(st.Ink01*st.InkCapacity*scale),Mathf.RoundToInt(st.InkCapacity*scale)),y,w);y+=E308StatusPitchBody;
                if(EquipmentScreen308SO.RegenShown(data)&&st.InkRegenPerSecond>0f)
                {Equip308StatusRow(s,root,"inkRegen","먹 회복",EquipmentText308.InkRegen(st.InkRegenPerSecond,scale),y,w);y+=E308StatusPitchBody;}
                y+=E308StatusGroupGap-E308StatusPitchBody;
                if(st.HasStats)
                {
                    y=Equip308StatusHead(s,root,"StatusPower",Equip308Mark(5),"속성 위력",y);
                    for(int i=0;i<5;i++)
                    {
                        var e=(Element)i;
                        var row=Equip308StatusRow(s,root,"power_"+(DemoUpgradeTrack)i,EquipmentText308.ElementHanja(e)+" "+EquipmentText308.ElementName(e),
                            EquipmentText308.Power(st.Stats.DamageMultiplier(e)),y,w,E308StatusNameDx);
                        V.Element(s,row,i,0,2,E308MarkSize,s.Paper);
                        y+=E308StatusPitch;
                    }
                    y+=E308StatusGroupGap-E308StatusPitch;
                }
            }
            var tiers=new List<DemoUpgradeQuote>();
            if(Session.DemoEconomy!=null)
                foreach(var track in new[]{DemoUpgradeTrack.Health,DemoUpgradeTrack.Ink})
                    if(Session.DemoEconomy.TryQuote(track,out var quote,out _))tiers.Add(quote);
            if(tiers.Count>0)
            {
                y=Equip308StatusHead(s,root,"StatusTier",Equip308Mark(6),"보강",y);
                foreach(var q in tiers)
                {
                    var row=Equip308StatusRow(s,root,"tier_"+q.Track,q.Track==DemoUpgradeTrack.Health?"체력 보강":"먹 용량 보강","+"+EquipmentText308.Pct(q.CurrentTotalBonus)+"%",y,w);
                    var nameLabel=row.Find("Name") as RectTransform;float dx=(nameLabel!=null?nameLabel.sizeDelta.x:0f)+E308StepGap+4;
                    for(int k=0;k<q.MaximumLevel;k++)Equip308Pip(s,row,dx+k*E308StepPitch,E308StepDy+2,E308StepW,E308StepH,k<q.CurrentLevel,1.5f);
                    y+=E308StatusPitch;
                }
                y+=E308StatusGroupGap-E308StatusPitch;
            }
            var ui=progress.ui;
            y=Equip308StatusHead(s,root,"StatusLearned",Equip308Mark(7),"익힌 것",y);
            Equip308StatusRow(s,root,"spells","술식",EquipmentText308.Fraction(ui.knownSpellLetters!=null?ui.knownSpellLetters.Count:0,WorldMacroCollectionCatalog.AllSpells.Count),y,w);y+=E308StatusPitch;
            Menu304VirtueText(s,out string virtues,out _,out int owned);
            var vrow=Equip308StatusRow(s,root,"virtues","오덕",EquipmentText308.Fraction(owned,Menu304Virtues.Length),y,w);
            var vname=vrow.Find("Name") as RectTransform;
            V.Label(s,vrow,"Glyphs",virtues,UiType304.Serif800_20,Color.white,(vname!=null?vname.sizeDelta.x:0f)+E308StepGap+4,2);
        }

        float Equip308StatusHead(UiStyle304SO s,Transform root,string name,Sprite mark,string head,float y)
        {
            float bottom=Equip308Section(s,root,name,mark,head,0,y);
            return Mathf.Max(y+E308StatusHeadDy,bottom-E308HeadGap+4);
        }

        /// <summary>One status line "Status_&lt;key&gt;": Name left (Serif 24), Value right-aligned (Sans 24) on the same baseline.</summary>
        RectTransform Equip308StatusRow(UiStyle304SO s,Transform root,string key,string name,string value,float y,float w,float nameX=0f)
        {
            var row=V.Rect("Status_"+key,root,0,y,w,E308StatusPitch);
            var v=V.Label(s,row,"Value",value,UiType304.Body24,s.Paper,0,0);
            float baseline=Content304Ascent(v);
            Content304OnBaseline(v,w-v.rectTransform.sizeDelta.x,baseline);
            var n=V.Label(s,row,"Name",name,UiType304.Label24,s.Paper,nameX,0);
            Content304OnBaseline(n,nameX,baseline);
            return row;
        }

        // ================================================================== #308 theme (SPEC-UI-THEME-308, DECISIONS D308-15)
        // Drawn with the kit's sprites and tokens held by EquipmentScreen308SO.Theme (bound by EquipmentSetup308 from the kit's
        // own json files: no theme colour is typed in code). Bars, boards, beds and moats are fills of the kit's White cell, so
        // they share the atlas batch. Nothing here glows, moves or polls: the two states are FocusVisual304's show / hide lists.
        EquipTheme308 Equip308Theme=>EquipmentScreen308SO.Themed(Equip308Data());

        static Image Equip308Fill(Transform parent,string name,float x,float y,float w,float h,Color color,EquipTheme308 th)
            =>V.Image(V.Rect(name,parent,x,y,w,h),color,th.White);

        /// <summary>A hollow band round `box`: from ox / oy px outside its edge to `inside` px inside it (four fills).</summary>
        static RectTransform Equip308Band(Transform parent,string name,Rect box,float ox,float oy,float inside,Color color,EquipTheme308 th)
        {
            var root=V.Rect(name,parent,0,0,0,0);
            float tx=ox+inside,ty=oy+inside,x=box.x,y=box.y,w=box.width,h=box.height;
            Equip308Fill(root,"T",x-ox,y-oy,w+2*ox,ty,color,th);Equip308Fill(root,"B",x-ox,y+h-inside,w+2*ox,ty,color,th);
            Equip308Fill(root,"L",x-ox,y-oy+ty,tx,h+2*oy-2*ty,color,th);Equip308Fill(root,"R",x+w-inside,y-oy+ty,tx,h+2*oy-2*ty,color,th);
            return root;
        }

        /// <summary>One 교창 (transom window): a plain frame round n panes seated in the bars, plus `board` pane widths of flat
        /// black-lacquer board (궁판) with the screen's one najeon figure (Motif.Chrys) in its middle: a place with no text.</summary>
        static void Equip308Sash(Transform root,float x,float y,int n,int board,EquipTheme308 th,bool motif)
        {
            float f=E308TFrame,b=E308TBar,p=E308TPitchX,c=E308Chip,w=(n+board)*p-b;
            Equip308Fill(root,"Head",x-f,y-f,w+2*f,f,th.Wood,th);Equip308Fill(root,"Sill",x-f,y+c,w+2*f,f,th.Wood,th);
            Equip308Fill(root,"StileL",x-f,y,f,c,th.Wood,th);Equip308Fill(root,"StileR",x+w,y,f,c,th.Wood,th);
            for(int i=1;i<n+(board>0?1:0);i++)Equip308Fill(root,"Bar",x+p*i-b,y,b,c,th.Wood,th);
            if(board<=0)return;
            float bx=x+p*n,bw=board*p-b;
            Equip308Fill(root,"Board",bx,y,bw,c,th.Lacquer,th);
            if(motif&&th.Chrys!=null)V.SpriteImage(root,"BoardMotif",th.Chrys,Color.white,bx+(bw-E308TChrys)*.5f,y+(c-E308TChrys)*.5f,E308TChrys,E308TChrys);
        }

        /// <summary>The pane's paper, seated in the bars: flat 창호지 to the bar edge (the torn chip laid on it only adds its
        /// grain: its ragged edge has the flat value). Empty = the same paper at α.16, pre-composited on the veil.</summary>
        Image Equip308Pane(Transform cell,float x,float y,float size,bool empty,UiStyle304SO s,EquipTheme308 th)
            =>Equip308Fill(cell,"Pane",x,y,size,size,empty?Content304Precomposite(s.Veil,s.Chip,E308BlankAlpha):s.Chip,th);

        /// <summary>The two states of a themed 칸 (`pane` in the cell's own px; row = a 바꿔 낄 것 chip). Focus: a 한지 band masks
        /// the bars round the pane and the cinnabar frame lies wholly on it, the strongest mark of the page (measured 4.4:1,
        /// 3.9:1 at worst under colour-vision simulation; on a bar it was 1.75:1). Kept: the nacre cut-shell frame
        /// (Frame.Select, pane + 10 = 24 + 10 n) in an 8 px lacquer moat, while the 칸 stays the selection with the focus
        /// elsewhere (Equip308KeptSync). Call it after the pane's paper and before its picture.</summary>
        void Equip308States(ContentCell304 cell,Rect pane,bool row,bool keepable,UiStyle304SO s,EquipTheme308 th)
        {
            var bed=Equip308Band(cell.transform,"FocusBed",pane,row?E308TRowBedX:E308TBedX,row?E308TRowBedY:E308TBedY,row?E308TRowBedIn:E308TBedIn,s.Paper,th);
            bed.gameObject.SetActive(false);cell.Visual.ShowWhenFocused.Add(bed.gameObject);
            if(!keepable)return;
            var holder=V.Rect("Kept308",cell.transform,0,0,0,0);
            var mark=Equip308Band(holder,"Moat",pane,E308TMoat,E308TMoat,0,th.Lacquer,th);
            float o=E308TSelectOut;
            var frame=V.SpriteImage(mark,"KeptFrame",th.FrameSelect,Color.white,pane.x-o,pane.y-o,pane.width+2*o,pane.height+2*o);
            frame.type=Image.Type.Tiled;frame.fillCenter=false;
            cell.Visual.HideWhenFocused.Add(mark.gameObject);
            holder.gameObject.SetActive(false);equip308KeptMarks[cell]=holder.gameObject;
        }

        /// <summary>The rect the cinnabar brush frame is laid on. Theme: narrowed so every stroke end stays on the 한지 bed.</summary>
        static Rect Equip308RingRect(Rect pane,bool row,EquipTheme308 th)
            =>th==null?pane:new Rect(pane.x+(row?E308TRowRingDx:E308TRingDx),pane.y,pane.width+(row?E308TRowRingDw:E308TRingDw),pane.height);

        void Equip308KeptSync()
        {
            foreach(var pair in equip308KeptMarks)
                if(pair.Key!=null&&pair.Value!=null&&pair.Value.activeSelf!=pair.Key.Kept)pair.Value.SetActive(pair.Key.Kept);
        }

        /// <summary>The focused / kept 칸 draws over its neighbours: its bed and its moat mask their bars and paper edges.</summary>
        void Equip308Raise(ContentCell304 cell){if(cell!=null&&Equip308Theme!=null)cell.transform.SetAsLastSibling();}

        /// <summary>강화 단계 한 칸 in the 방점's box. Theme: a nacre petal (Pip.Petal 12 x 14 px x scale) centred in it, reached =
        /// the baked shell, not reached = the same petal brought to 안개 α.35 on the veil. No theme: the #304 방점.</summary>
        void Equip308Pip(UiStyle304SO s,Transform parent,float x,float y,float w,float h,bool on,float scale)
        {
            var th=Equip308Theme;Color pending=Content304Precomposite(s.Veil,s.Mist,E308PendingAlpha);
            if(th==null){V.SpriteImage(parent,"LevelDab",s.Sprites.Dab,on?s.Paper:pending,x,y,w,h,s.DabRotation);return;}
            float pw=E308TPetalW*scale,ph=E308TPetalH*scale;
            V.SpriteImage(parent,"LevelDab",th.Petal,on?Color.white:th.Toward(pending),x+(w-pw)*.5f,y+(h-ph)*.5f,pw,ph);
        }

        /// <summary>Where the picture sits in the 200 box (0 gear art, 1 형상 도장, 2 관변 테). Theme with motifs = the plaque's
        /// picture register above the lotus band; else the #304 sizes.</summary>
        Rect Equip308ArtRect(int kind)
        {
            if(Equip308Theme!=null&&EquipmentScreen308SO.Motifs(Equip308Data()))
            {
                float size=kind==0?E308TArt:kind==1?E308TStamp:E308TSeal,top=kind==0?E308TArtY:kind==1?E308TStampY:E308TSealY;
                return new Rect((E308ArtSize-size)*.5f,top,size,size);
            }
            float n=kind==1?E308ArtInner-2*E308StampPad:E308ArtInner,pad=(E308ArtSize-n)*.5f;
            return new Rect(pad,pad,n,n);
        }

        /// <summary>형상 도장 · 관변 테 · 한자 on the picture frame: the dark inlay on porcelain, ink on the #304 chip.</summary>
        Color Equip308PlaqueInk(UiStyle304SO s){var th=Equip308Theme;return th!=null?th.InlayDark:s.Ink;}

        // 석경 탭 (Content304FragmentGrid, compact): the same windows, five panes to a row
        Rect Equip308FragmentRing()=>Equip308RingRect(new Rect(0,0,E308Chip,E308Chip),false,Equip308Theme);
        Rect Equip308FragmentDab()=>Equip308Theme!=null?new Rect(E308TFragDabX,E308TNameDy+E308TFragDabDy,E308TFragDabW,E308TFragDabH):new Rect(E308DabX,E308NameDy+E308DabDy,E308DabW,E308DabH);
        float Equip308FragmentNameDy=>Equip308Theme!=null?E308TNameDy:E308NameDy;
        /// <summary>Called by Content304FragmentGrid round its chip: states = false before the chip (the flat paper), true after
        /// it (focus bed + kept frame). Does nothing without the theme.</summary>
        void Equip308FragmentPane(ContentCell304 cell,bool states)
        {
            var th=Equip308Theme;if(th==null||cell==null)return;
            var s=Content304Style;
            if(!states)Equip308Pane(cell.transform,0,0,E308Chip,false,s,th);
            else Equip308States(cell,new Rect(0,0,E308Chip,E308Chip),false,true,s,th);
        }
        void Equip308FragmentLattice(EquipTheme308 th)
        {
            if(content304GearCells.Count==0||content304GearCells[0]==null)return;
            var grid=content304GearCells[0].transform.parent;
            var lattice=V.Rect("Lattice308",grid,0,0,0,0);lattice.SetAsFirstSibling();
            int count=content304GearCells.Count;
            for(int r=0;r*E308FragmentCols<count;r++)
                Equip308Sash(lattice,0,r*E308TPitchY,Mathf.Min(E308FragmentCols,count-r*E308FragmentCols),0,th,false);
        }

        /// <summary>Phase 2 (menu-wide, off by default): a filled keycap of this page becomes the porcelain button (Key.Porcelain,
        /// the #304 keycap's slices) with its letter in the inlay colour.</summary>
        static void Equip308PorcelainKeys(Transform hint,EquipTheme308 th)
        {
            foreach(var face in hint.GetComponentsInChildren<Image>(true))
            {
                if(face.name!="Face"||face.sprite==null)continue;
                face.sprite=th.Keycap;
                var label=face.transform.parent.Find("Label");var text=label!=null?label.GetComponent<TMP_Text>():null;
                if(text!=null)text.color=th.InlayDark;
            }
        }

        // ================================================================== bottom: 조작 범례
        /// <summary>One line at y972 in the map page's grammar (small filled keycap + verb phrase, at most three). It follows
        /// the page's own state (slot / list row), not the live selection: a mouse press on an entry clears the selection
        /// before the click arrives. Entries can be clicked; arrows never reach them (they are not Selectables).</summary>
        void Equip308Legend()
        {
            if(equip308LegendRoot==null||Session==null)return;
            Equip308HoverLock();   // every change of the page's focus state passes through here
            Equip308KeptSync();
            var s=Content304Style;var data=Equip308Data();
            var entries=new List<(string key,string label,Action act)>();
            if(Equip308Fragments)
            {
                if(equip308InMiddle)entries.Add(("Esc","칸으로",Equip308LegendBack));
                else if(Equip308SelectedFragment()!=null)entries.Add(("Enter","술식 도감에서 보기",Equip308ReadCodex));
            }
            else if(Equip308Cell(equip308Slot)!=null)
            {
                var d=Equip308Slots[equip308Slot];int slot=equip308Slot;
                if(!string.IsNullOrEmpty(equip308Candidate))
                {
                    string id=equip308Candidate;
                    bool worn=Session.EquipmentReady&&d.Kind==Equip308Kind.Gear&&Session.Progress.equipment.Equipped[d.Index]==id;
                    entries.Add(("Enter",worn?"해제":"착용",()=>Equip308SubmitCandidate(id)));
                    entries.Add(("Esc","칸으로",Equip308LegendBack));
                }
                else if(equip308InMiddle)entries.Add(("Esc","칸으로",Equip308LegendBack));
                else if(d.Kind==Equip308Kind.Gear)
                {
                    int n=equip308Middle.Count;bool has=!string.IsNullOrEmpty(equip308Cells[slot].Id);
                    bool removeKey=!string.IsNullOrEmpty(EquipmentScreen308SO.RemoveBinding(data))||!string.IsNullOrEmpty(EquipmentScreen308SO.RemovePadBinding(data));
                    if(n>0)entries.Add(("Enter","바꿔 끼기",()=>Equip308SubmitSlot(slot)));
                    if(has&&removeKey)entries.Add((EquipmentScreen308SO.RemoveLabel(data),"빼기",()=>Equip308Remove(true)));
                    if(n>0)entries.Add(("끌기","칸에 놓아 착용",null));
                }
                else if(d.Kind==Equip308Kind.Stone){int track=d.Index;if(Session.AtDemoShop)entries.Add(("Enter","정비",()=>Equip308OpenService(track)));}
                else entries.Add(("Enter","차패",()=>OpenPage("차패")));
            }
            string key=equip308LegendRoot.GetInstanceID()+"|"+string.Join("|",entries.Select(e=>e.key+":"+e.label));
            if(key==equip308LegendKey)return;
            equip308LegendKey=key;
            V.Clear(equip308LegendRoot);
            float x=0f,top=(E308LegendH-s.Keycap.SmallHeight)*.5f;
            for(int i=0;i<entries.Count;i++)
            {
                // the map page's control row: small filled keycap + Label22 in 한지 (WorldMapPresenter.Page304 ControlRow304)
                var hint=V.Hint(s,equip308LegendRoot,"Legend_"+i,new[]{entries[i].key},entries[i].label,s.Paper,x,top,true,true,false,UiType304.Label22);
                if(EquipmentScreen308SO.Phase2(data))Equip308PorcelainKeys(hint,data.Theme);
                if(entries[i].act!=null)
                {
                    var hit=V.Image(hint,new Color(0f,0f,0f,0f),null,true);hit.canvasRenderer.cullTransparentMesh=true;
                    hint.gameObject.AddComponent<EquipLegendClick308>().Clicked=entries[i].act;
                }
                x+=hint.sizeDelta.x+E308LegendGap;
            }
        }

        void Equip308LegendBack()
        {
            equip308InMiddle=false;
            if(Equip308Fragments){var go=Content304FirstFragment();if(go!=null)Content304Select(go);Equip308Legend();return;}
            Equip308FocusSlot(equip308Slot);
            Equip308Legend();
        }
    }

    /// <summary>#308 legend entry: a mouse click runs the entry's action. Not a Selectable (no focus, no navigation).</summary>
    public sealed class EquipLegendClick308:MonoBehaviour,IPointerClickHandler
    {
        public Action Clicked;
        public void OnPointerClick(PointerEventData eventData){if(eventData.button==PointerEventData.InputButton.Left)Clicked?.Invoke();}
    }

    /// <summary>#308 the 소지품 page's one extra key ([X] 빼기; pad X). Lives on a child of the page content, so the action is
    /// enabled only while the page exists (V.Clear disables then destroys it). Event driven: no Update polling.</summary>
    public sealed class EquipmentKeys308:MonoBehaviour
    {
        InputAction remove;Action pressed;
        public void Bind(string key,string pad,Action onRemove)
        {
            Release();pressed=onRemove;
            remove=new InputAction("Equip308Remove",InputActionType.Button);
            if(!string.IsNullOrEmpty(key))remove.AddBinding(key);
            if(!string.IsNullOrEmpty(pad))remove.AddBinding(pad);
            remove.performed+=OnPerformed;
            if(isActiveAndEnabled)remove.Enable();
        }
        void OnPerformed(InputAction.CallbackContext context){pressed?.Invoke();}
        void OnEnable(){if(remove!=null)remove.Enable();}
        void OnDisable(){if(remove!=null)remove.Disable();}
        void OnDestroy(){Release();}
        void Release()
        {
            if(remove==null)return;
            remove.performed-=OnPerformed;remove.Disable();remove.Dispose();remove=null;
        }
    }
}
