using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 소지품 석경 탭 · 술식 도감 (codex.png, DESIGN §7.4, IMPLEMENTATION §7.6) · 차패 (DESIGN §7.9, D18).
    /// Codex data: the matrix is the game's learnable set (WorldMacroCollectionCatalog: 5 오행 columns x 중성/종성 rows), the
    /// row 분류 / 프레임, 효과, 사거리 and the 받침 table come from the 작도어휘 CSV import (ContentArt304.Vocab), cell names are the
    /// catalog's own noun phrases shortened (ContentVocab304.CellName) - nothing is written here. Locked cells stay focusable
    /// (D16 묵등; D308-27: the state word stands once, in the detail of the selected 칸) and show only the row / column grammar; D17 광곽 (사주쌍변) frames the matrix; D21 flicks mark new
    /// letters; 공백 받침 rows are sealed (D22). Focus returns to the same cell after rebuilds (OnCollected).</summary>
    public sealed partial class PlaytestUiRoot
    {
        readonly List<ContentCell304> content304CodexCells=new List<ContentCell304>();
        string content304CodexShown;

        // ================================================================== 소지품 without equipment / 석경 탭
        void BuildInventory()
        {
            if(Session?.Progress?.ui==null)return;
            Equip308Build();   // #308: one 3-column screen for 장비 and 석경, with or without an equipment catalog
        }

        /// <summary>석경 칩 grid (DESIGN §7.3 / IMPLEMENTATION §7.5): chip 104 with the glyph (Serif900 56 ink), name + count under it.
        /// Selecting a chip shows the fragment in the detail column (no ShowDetail: the menu stays open).
        /// #308: the 3-column 소지품 passes its own pitch and compact = true (the glyph only since D308-27, four rows before it scrolls; the count
        /// is in the detail).</summary>
        void Content304FragmentGrid(float ox,float oy,int cols,float pitchX=Content304PitchX,float pitchY=Content304PitchY,bool compact=false)
        {
            var s=Content304Style;var ui=Session.Progress.ui;
            var held=WorldMacroCollectionCatalog.AllFragments.Where(x=>ui.GetItemCount(x.ItemId)>0).ToArray();
            if(held.Length==0){EmptyPage("빈 봇짐",null,ox,oy);return;}   // D308-27 (소지품 ⑫): the title alone
            if(string.IsNullOrEmpty(selectedItem)||!held.Any(x=>x.ItemId==selectedItem))selectedItem=held[0].ItemId;
            int rows=Mathf.Max(1,Mathf.CeilToInt(held.Length/(float)cols)),shown=compact?4:3;
            float cellW=compact?pitchX-4:128,cellH=compact?pitchY-8:172;
            RectTransform grid=rows>shown?V.Scroll(contentRoot,"FragmentGrid",ox,oy,cols*pitchX+18,shown*pitchY,rows*pitchY)
                :V.Rect("FragmentGrid",contentRoot,ox,oy,cols*pitchX,rows*pitchY);
            for(int i=0;i<held.Length;i++)
            {
                var f=held[i];float gx=i%cols*pitchX,gy=i/cols*pitchY;
                var cell=Content304Cell(grid,"Fragment_"+f.Id,gx,gy,cellW,cellH,compact?Equip308FragmentRing():new Rect(0,0,Content304ChipSize,Content304ChipSize),compact?Equip308FragmentDab():new Rect(-33,121,26,20),
                    ()=>{Content304PickFragment(Content304GearCells("Fragment_"+f.Id),f);Equip308ReadCodex();},c=>Content304PickFragment(c,f),false);   // D308-27 (소지품 ⑦): Enter / click opens the codex at once (the link row is gone)
                cell.Id=f.ItemId;
                // #308: the kept chip (focus on 술식 도감에서 보기) keeps a frame that shows on the veil (ink does not). Theme
                // (D308-15): the chip is a pane of a lattice window (flat paper under it, focus bed + nacre kept frame over it)
                if(compact){var kept=Equip308KeptFrame(s);if(kept!=s.Ink)cell.Init(s,cell.Visual,Equip308FragmentRing(),kept,c=>Content304PickFragment(c,f));Equip308FragmentPane(cell,false);}
                Content304Chip(cell.transform,"Chip",0,0,Content304ChipSize,false);
                if(compact)Equip308FragmentPane(cell,true);
                V.Label(s,cell.transform,"Glyph",f.Letter,UiType304.ChipGlyph56,s.Ink,0,0,Content304ChipSize,Content304ChipSize-4,TextAlignmentOptions.Center);
                // D308-27 (소지품 ⑪): no name line under the chip - the chip's glyph is the name, the detail head names the piece once
                if(!compact)V.Label(s,cell.transform,"Count",ui.GetItemCount(f.ItemId)+"개",UiType304.Meta20,s.Mist,0,144);
                Content304NewMark(cell,"item:"+f.ItemId,Content304ChipSize-22,-8,false);
                content304GearCells.Add(cell);
            }
        }

        GameObject Content304FirstFragment()
        {
            var cell=content304GearCells.FirstOrDefault(c=>c!=null&&c.Id==selectedItem&&c.name.StartsWith("Fragment_",StringComparison.Ordinal))
                ??content304GearCells.FirstOrDefault(c=>c!=null&&c.name.StartsWith("Fragment_",StringComparison.Ordinal));
            return cell!=null?cell.gameObject:null;
        }

        void Content304PickFragment(ContentCell304 cell,FragmentDefinition f)
        {
            if(cell!=null){content304Kept=cell.name;content304Focus=cell.name;}
            selectedItem=f.ItemId;
            equip308InMiddle=false;Equip308Raise(cell);   // #308: the focus is back on the grid (theme: its bed lies over the neighbours)
            Content304RefreshGear();
            if(Page=="소지품")Equip308Legend();
        }

        /// <summary>빈 상태 (DESIGN §7.3 IMPLEMENTATION §7.5): "빈 봇짐" Serif600 26 + meta, where the grid would be.</summary>
        void EmptyPage(string title,string description,float x=800,float y=264)
        {
            var s=Content304Style;
            V.Label(s,contentRoot,"EmptyTitle",title,UiType304.Label26,s.Paper,x,y);
            if(!string.IsNullOrEmpty(description))V.Label(s,contentRoot,"EmptyBody",description,UiType304.Meta20,s.Mist,x,y+42);
        }

        // ================================================================== 술식 도감
        sealed class Content304Spell { public SpellCodexDefinition Def; public string Letter,Initial,Medial,Final; public int Row,Col; }

        void BuildCodex()
        {
            var s=Content304Style;var ui=Session.Progress.ui;var known=ui.knownSpellLetters;var art=Content304Art;
            content304CodexCells.Clear();content304CodexShown=null;

            // ---- data: rows = (중성, 종성) groups of the catalog, columns = 오행
            var spells=new List<Content304Spell>();var groups=new List<(string m,string f)>();
            foreach(var def in WorldMacroCollectionCatalog.AllSpells)
            {
                if(!ContentVocab304.Decompose(def.Letter,out string ini,out string med,out string fin))continue;
                if(!groups.Contains((med,fin)))groups.Add((med,fin));
                spells.Add(new Content304Spell{Def=def,Letter=def.Letter,Initial=ini,Medial=med,Final=fin,Col=Mathf.Clamp((int)def.Element,0,4)});
            }
            groups.Sort((a,b)=>{int r=ContentVocab304.MedialRank(a.m).CompareTo(ContentVocab304.MedialRank(b.m));return r!=0?r:ContentVocab304.FinalRank(a.f).CompareTo(ContentVocab304.FinalRank(b.f));});
            foreach(var sp in spells)sp.Row=groups.IndexOf((sp.Medial,sp.Final));
            int rows=Mathf.Max(1,groups.Count);float pitch=rows<=4?158f:632f/rows;
            var matrix=new HashSet<string>(spells.Select(x=>x.Letter));
            int knownInMatrix=spells.Count(x=>known.Contains(x.Letter));
            var extras=known.Where(l=>!matrix.Contains(l)&&ContentVocab304.Decompose(l,out _,out _,out _)).Distinct().ToList();

            // ---- selection: keep, else the first known letter in matrix order, else the first cell
            bool selectable=matrix.Contains(selectedSpell)||extras.Contains(selectedSpell);
            if(!selectable)
            {
                var first=spells.OrderBy(x=>x.Row).ThenBy(x=>x.Col).FirstOrDefault(x=>known.Contains(x.Letter))??spells.OrderBy(x=>x.Row).ThenBy(x=>x.Col).FirstOrDefault();
                selectedSpell=first!=null?first.Letter:extras.FirstOrDefault()??"";
            }

            // ---- sheet + header
            V.SpriteImage(contentRoot,"Sheet",s.Sprites.SheetCodex,s.Sprites.SheetCodex!=null?Color.white:s.Sheet,40,146,1150,870);
            // mockup: flex row at (96,184), align-items baseline, gap 14; the figure (t-fig, line-height 1) sets the baseline (y221).
            // Box bottoms on the figure's TMP box put the label and "/ 20" 9~14 px low, through the 광곽's outer line.
            var fig=V.Label(s,contentRoot,"KnownCount",knownInMatrix.ToString(),UiType304.Figure40,s.Ink,0,184);
            var head=V.Label(s,contentRoot,"KnownLabel","석경에 남은 술식",UiType304.MetaBold20,s.Ash,96,0);
            var total=V.Label(s,contentRoot,"TotalCount","/ "+spells.Count,UiType304.Label26,s.Ash,0,0);
            float countBaseline=Content304CssBaseline(fig,184,1f);
            Content304OnBaseline(head,96,countBaseline);
            float figX=96+head.rectTransform.sizeDelta.x+14;
            Content304OnBaseline(fig,figX,countBaseline);
            Content304OnBaseline(total,figX+fig.rectTransform.sizeDelta.x+14,countBaseline);
            if(extras.Count>0)Content304CodexExtras(extras,countBaseline);

            // ---- D17 광곽 (사주쌍변, α.5) around the matrix; dry rule under the column heads; 계선 between columns
            float rowsTop=352,rowsH=pitch*rows;
            // frame (lines 3~7 px and 10~11.5 px inside the sliced rect): the outer line clears the count's baseline by 8 px,
            // the inner line stays above the stamps (242); the count sits outside the frame, the matrix inside (D17 행렬 둘레만)
            bool frameDrawn=art!=null&&art.GwangGwak!=null;
            float frameTop=Mathf.Max(226f,countBaseline+5f);
            if(frameDrawn)Content304Ink(V.SpriteImage(contentRoot,"Gwanggwak",art.GwangGwak,UiStyle304SO.A(s.Ink,.5f),70,frameTop,1092,Mathf.Min(rowsTop+rowsH+8,1000)-frameTop),s);
            V.Brush(s,contentRoot,"HeadRule",StrokeClass304.Dry,s.Ink,frameDrawn?88:84,318,frameDrawn?1060:1080,30,.85f);
            for(int c=1;c<5;c++)V.Brush(s,contentRoot,"ColumnRule",StrokeClass304.Line,s.Ink,290+172*c-rowsH*.5f,rowsTop+rowsH*.5f-6,rowsH,12,.18f,90f);

            // ---- column heads: 형상 도장 + 한자 + 이름 + 초성 (+ D29 진행)
            for(int c=0;c<5;c++)
            {
                float x=290+172*c;var e=(Element)c;
                var stamp=V.Element(s,contentRoot,c,x+18,242,58,s.Ink);
                V.Label(s,stamp.transform,"Hanja",Content304Hanja(e),UiType304.Serif900_28,s.Ink,0,0,58,58,TextAlignmentOptions.Center);
                Content304AtCss(V.Label(s,contentRoot,"ColumnName",Content304ElementName(e),UiType304.Title24,s.Ink,x+88,244),x+88,244,1.1f);
                var column=spells.Where(sp=>sp.Col==c).ToList();
                string initial=column.Count>0?column[0].Initial:"";
                Content304AtCss(V.Label(s,contentRoot,"ColumnMeta",initial+"  "+column.Count(sp=>known.Contains(sp.Letter))+" / "+column.Count,UiType304.Meta20,s.Ash,x+89,274),x+89,274,1.35f);
            }

            // ---- row heads (mockup: block at (96, y+30); flex baseline row [중성 44 line-height 1][분류 22], then 프레임 meta
            // margin-top 10 under that line box). Box-bottom placement put all three 10~15 px low.
            for(int r=0;r<groups.Count;r++)
            {
                float y=rowsTop+r*pitch;var g=groups[r];
                var sample=spells.FirstOrDefault(sp=>sp.Row==r);
                var row=art!=null&&sample!=null?art.Row(sample.Letter):null;
                float blockTop=y+30;
                var glyph=V.Label(s,contentRoot,"RowJamo",g.m+g.f,UiType304.Headline44,s.Ink,96,blockTop);
                var kind=V.Label(s,contentRoot,"RowKind",row!=null?row.Category:sample!=null?Content304KindLabel(sample.Def.Kind):"",UiType304.Serif700_22,s.Ink,0,0);
                Content304Serif800Face(kind,UiType304.Serif700_22);   // Serif700 -> Noto 600 printed lighter than codex.png's 700
                float rowBaseline=Mathf.Max(Content304CssBaseline(glyph,blockTop,1f),Content304CssBaseline(kind,blockTop,0f));
                Content304OnBaseline(glyph,96,rowBaseline);
                Content304OnBaseline(kind,96+glyph.rectTransform.sizeDelta.x+12,rowBaseline);
                float lineBottom=Mathf.Max(blockTop+glyph.fontSize,rowBaseline+Content304Descent(kind));
                if(row!=null&&!string.IsNullOrEmpty(row.Frame))
                    Content304AtCss(V.Label(s,contentRoot,"RowFrame",row.Frame,UiType304.Meta20,s.Ash,96,0),96,lineBottom+10,1.35f);
            }

            // ---- cells
            // names: the catalog's own noun phrase; two cells that would read the same (사 / 소 "금속 송곳") keep one more of the
            // catalog's words when it still fits the cell (164 px), so no two cells carry one name
            var cellNames=spells.ToDictionary(x=>x.Letter,x=>ContentVocab304.CellName(x.Def.Description));
            foreach(var sp in spells.OrderBy(x=>x.Row).ThenBy(x=>x.Col))
            {
                float x=290+172*sp.Col,y=rowsTop+sp.Row*pitch;string letter=sp.Letter;bool unlocked=known.Contains(letter);
                var cell=Content304Cell(contentRoot,"Codex_"+letter,x,y,172,pitch,new Rect(8,6,156,pitch-16),new Rect(16,30,28,21),
                    Content304CodexReplay,c=>Content304PickSpell(letter),true);
                cell.Id=letter;
                TMP_Text label;
                if(unlocked)
                {
                    // mockup: glyph at cell +18 with line-height 1.1 (a plain top put it 11 px low, onto the name at +104)
                    label=V.Label(s,cell.transform,"Glyph",letter,UiType304.CellGlyph64,s.Ink,0,18,172,70,TextAlignmentOptions.Top);
                    Content304AtCss(label,0,18,1.1f);
                    string cellName=cellNames[letter];
                    if(cellNames.Values.Count(v=>v==cellName)>1)cellName=ContentVocab304.CellName(sp.Def.Description,1);
                    var nameLabel=V.Label(s,cell.transform,"Name",cellName,UiType304.Meta20,s.Ash,0,104,172,0,TextAlignmentOptions.Top);
                    if(cellName!=cellNames[letter]&&UiText304.Preferred(nameLabel,cellName).x>164f)nameLabel.text=cellNames[letter];
                    Content304NewMark(cell,"spell:"+letter,134,12,true);
                }
                else
                {
                    // D16 묵등 through UI/InkReveal (UI/Default printed α.6 at #9E instead of #64). α.5 = D16's lower bound: at .6 a fresh
                    // save's 20 blocks (#5A) weighed on the grid next to the known glyphs; .5 lands near #70 (D16: 무거워지면 가볍게 / 백광)
                    if(art!=null&&art.MukDeung!=null)Content304Ink(V.SpriteImage(cell.transform,"Mukdeung",art.MukDeung,UiStyle304SO.A(s.Ink,.5f),64,31,44,44),s);
                    else Content304Ink(V.SpriteImage(cell.transform,"Blot",s.Sprites.Blot,UiStyle304SO.A(s.Ink,.22f),50,28,72,56),s);
                    label=null;   // D308-27 answer 1: no state word in the grid; the detail of the selected 칸 carries it once
                }
                if(label!=null)cell.BindLabel(label,label.color,label.color);
                cell.Kept=letter==selectedSpell;
                content304CodexCells.Add(cell);
            }

            content304Detail=V.Rect("CodexDetail304",contentRoot,0,0,1920,1080);
            Content304CodexDetail();
            var target=Content304FindSelectable(contentRoot,"Codex_"+selectedSpell)??content304CodexCells.FirstOrDefault()?.gameObject;
            Content304Select(target);
        }

        /// <summary>Known letters outside the catalog matrix (e.g. 국 from a boss): small focusable glyphs right of the count.</summary>
        void Content304CodexExtras(List<string> extras,float baseline)
        {
            var s=Content304Style;
            var head=V.Label(s,contentRoot,"ExtraLabel","그 밖에 익힌 글자",UiType304.MetaBold20,s.Ash,0,0);
            float x=1130-extras.Count*64-head.rectTransform.sizeDelta.x;
            Content304OnBaseline(head,x,baseline);
            x+=head.rectTransform.sizeDelta.x+28;
            float cellY=baseline-45f;   // 52² glyph cells centred on the count line, clear of the 광곽 (outer line baseline + 8)
            foreach(var letter in extras)
            {
                string l=letter;
                var cell=Content304Cell(contentRoot,"Codex_"+l,x,cellY,52,52,new Rect(2,2,48,48),new Rect(-18,20,22,17),Content304CodexReplay,c=>Content304PickSpell(l),true);
                cell.Id=l;
                var glyph=V.Label(s,cell.transform,"Glyph",l,UiType304.Title30,s.Ink,0,0,52,50,TextAlignmentOptions.Center);
                cell.BindLabel(glyph,s.Ink,s.Ink);cell.Kept=l==selectedSpell;
                Content304NewMark(cell,"spell:"+l,38,-4,true);
                content304CodexCells.Add(cell);
                x+=64;
            }
        }

        void Content304PickSpell(string letter)
        {
            content304Focus="Codex_"+letter;
            if(letter==selectedSpell&&content304CodexShown==letter)return;
            selectedSpell=letter;
            foreach(var c in content304CodexCells)if(c!=null)c.Kept=c.Id==letter;
            Content304CodexDetail();
        }

        /// <summary>Enter / click on a cell = 획 다시 보기 (the film draws again, staggered 1 → 2 → 3).</summary>
        void Content304CodexReplay()
        {
            if(content304Detail==null)return;
            var films=content304Detail.GetComponentsInChildren<CodexStrokeExample>();
            for(int i=0;i<films.Length;i++)films[i].Replay(.08f+i*.6f);
        }

        /// <summary>상세 (장막 위, x1234~1856): tag, 탁본 240 glyph, 조합, 받침 표, 쓰기 예시 필름, 효과 / 사거리, 상생 · 상극.
        /// D308-27: no 초성 / 중성 meta lines, no stroke-rule sentences, no draw hints; the 받침 block closed up 74 px and the block
        /// under the film 110 px. A state word (미발견, 미습득, 미배정) stands at most once in this detail.</summary>
        void Content304CodexDetail()
        {
            if(content304Detail==null)return;
            V.Clear(content304Detail);
            var s=Content304Style;var ui=Session.Progress.ui;var art=Content304Art;var root=content304Detail;
            string letter=selectedSpell;content304CodexShown=letter;
            if(!ContentVocab304.Decompose(letter,out string ini,out string med,out string fin))return;
            bool known=ui.knownSpellLetters.Contains(letter);
            WorldMacroCollectionCatalog.TryGetSpell(letter,out var def);
            var row=art!=null?art.Row(letter):null;
            Element e=def!=null?def.Element:Content304ElementOf(ini);

            // tag line (1244,150): 도장 44 + 한자 · 목 · 프레임 · 분류
            var stamp=V.Element(s,root,(int)e,1244,150,44,s.Paper);
            V.Label(s,stamp.transform,"Hanja",Content304Hanja(e),UiType304.Serif900_22,s.Paper,0,0,44,44,TextAlignmentOptions.Center);
            float tx=1244+44+14;
            var en=V.Label(s,root,"TagElement",Content304ElementName(e),UiType304.Title26,s.Paper,tx,154);tx+=en.rectTransform.sizeDelta.x+14;
            string frame=row!=null?row.Frame:"",category=row!=null?row.Category:def!=null?Content304KindLabel(def.Kind):"";
            if(!string.IsNullOrEmpty(frame)){var fl=V.Label(s,root,"TagFrame",frame,UiType304.Label22,s.Mist,tx,158);tx+=fl.rectTransform.sizeDelta.x+14;}
            if(!string.IsNullOrEmpty(category))V.Label(s,root,"TagCategory",category,UiType304.Label22,s.Mist,tx,158);

            // glyph (탁본) or the 묵등 block; mockup CSS tops with line-height 1 (a plain rect top put the 240 glyph 52 px low)
            if(known)Content304AtCss(V.Label(s,root,"SpellGlyph",letter,UiType304.Glyph240,s.Paper,1234,196),1234,196,1f);
            // on the veil the block is paper at the 빈 칸 weight (α.16, DESIGN §5.6) through UI/InkReveal: UI/Default printed α.1
            // #52 on the #11 veil (a grey tile), InkReveal at α.1 would leave it at #1A (gone); α.16 lands at #26, the empty chips' weight
            else if(art!=null&&art.MukDeung!=null)Content304Ink(V.SpriteImage(root,"SpellMukdeung",art.MukDeung,UiStyle304SO.A(s.Paper,.16f),1252,222,200,200),s);

            // 조합 (D308-27 술식 ②: the tag line above and the matrix heads already say the 초성's element and the 중성's frame)
            string composition=ini+" + "+med+(string.IsNullOrEmpty(fin)?"":" + "+fin);
            Content304AtCss(V.Label(s,root,"Composition",composition,UiType304.Headline44,s.Paper,1512,204),1512,204,1f);

            if(known)
            {
                // 받침 표 (CSV rows sharing 초성 + 중성); 공백 = sealed (D22)
                var finals=art!=null?art.FinalsOf(ini,med):new List<ContentVocabRow304>();
                if(finals.Count>0)
                {
                    // D308-27: the block sits 74 px higher (the two meta lines over it are gone). Answer 1: a state word stands once
                    // in this detail - on the first row of that state; the later rows of the same state keep their dim colour only
                    V.Label(s,root,"FinalHead","받침",UiType304.Meta20,s.Mist,1514,262);
                    bool saidUnlearned=false,saidBlank=false;
                    for(int i=0;i<finals.Count&&i<5;i++)
                    {
                        var fr=finals[i];float y=288+i*28;bool learned=ui.knownSpellLetters.Contains(fr.Letter);
                        Color c=learned?s.Paper:s.Mist;
                        V.Label(s,root,"FinalGlyph",fr.Letter,UiType304.Serif900_22,c,1514,y);
                        var jamoLabel=V.Label(s,root,"FinalJamo",fr.Final,UiType304.Meta20,c,1550,y+1);
                        string text=learned?fr.ShortEffect():fr.Blank?(saidBlank?"":"미배정"):(saidUnlearned?"":"미습득");
                        if(!learned){if(fr.Blank)saidBlank=true;else saidUnlearned=true;}
                        float right=1550+jamoLabel.rectTransform.sizeDelta.x;
                        if(!string.IsNullOrEmpty(text))
                        {
                            var t=V.Label(s,root,"FinalText",text,UiType304.Meta20,c,1582,y+1);
                            if(t.rectTransform.sizeDelta.x>274f){t.rectTransform.sizeDelta=new Vector2(274f,t.rectTransform.sizeDelta.y);t.overflowMode=TextOverflowModes.Ellipsis;}
                            right=1582+t.rectTransform.sizeDelta.x;
                        }
                        if(fr.Blank&&!learned)Content304Sealed(root,1508,y+3,right-1508+8,20,s.Paper);
                    }
                }

                // 쓰기 예시: [Enter] 획 다시 보기 + 획 필름 3칸
                V.Label(s,root,"ExampleLabel","쓰기 예시",UiType304.Meta20,s.Mist,1246,504);
                var replay=V.Hint(s,root,"ReplayHint",new[]{"Enter"},"획 다시 보기",s.Mist,0,498);
                V.Place(replay,1856-replay.sizeDelta.x,498);
                Content304Film(root,letter);

                // D308-27: no stroke-rule sentences (answer 3: the film shows the strokes) and no draw / release hints (술식 ⑤: the
                // 조작 page's 작도 tab is the one place). The block below sits 110 px higher (852 -> 742).
            }

            // 효과 · 사거리 (CSV 효과 괄호 앞 / 괄호 속) or the not-found line
            V.Brush(s,root,"EffectDivider",StrokeClass304.Dry,s.Paper,1240,742,616,26,.3f);
            if(known)
            {
                string effect=def!=null?def.Description.TrimEnd('.'):"",range="";
                if(row!=null){row.SplitEffect(out string head,out range);if(!string.IsNullOrEmpty(head))effect=head;}
                V.Label(s,root,"SpellEffect",effect,UiType304.Body24,s.Paper,1246,776,600,0,TextAlignmentOptions.TopLeft);
                if(!string.IsNullOrEmpty(range))V.Label(s,root,"SpellRange",range,UiType304.Meta20,s.Mist,1246,814);
            }
            else V.Label(s,root,"SpellEffect","미발견",UiType304.Body24,s.Mist,1246,776);   // D308-27 answer 1: the state word, once, here (술식 ③: the sentence is gone)

            // 상생 X → e → Y · 상극 X → e → Y (ElementRelations ring)
            var sheng=V.Label(s,root,"ShengLabel","상생",UiType304.Meta20,s.Mist,1246,852);
            float lx=1246+sheng.rectTransform.sizeDelta.x+8;
            var sg=V.Label(s,root,"Sheng",Content304Hanja(Content304GeneratedBy(e))+" → "+Content304Hanja(e)+" → "+Content304Hanja(Content304Generates(e)),UiType304.Serif700_20,s.Paper,lx,852);
            lx+=sg.rectTransform.sizeDelta.x+28;
            var ke=V.Label(s,root,"KeLabel","상극",UiType304.Meta20,s.Mist,lx,852);
            V.Label(s,root,"Ke",Content304Hanja(Content304OvercomeBy(e))+" → "+Content304Hanja(e)+" → "+Content304Hanja(Content304Overcomes(e)),UiType304.Serif700_20,s.Paper,lx+ke.rectTransform.sizeDelta.x+8,852);
        }

        /// <summary>획 필름 (IMPLEMENTATION §5.3): up to three frames 196x180 at x 1244 / 1450 / 1656, y536. Frame k shows the strokes
        /// before it in 안개 α.75, its own strokes in 한지 (drawn on replay), the next stroke as a 16 % ghost, and a cinnabar number
        /// circle 24 at the start of its first stroke. Reads JamoTemplateLibrarySO only.</summary>
        void Content304Film(Transform root,string letter)
        {
            var s=Content304Style;var library=Theme!=null?Theme.StrokeTemplates:null;
            float[] xs={1244,1450,1656};
            // frame 1 is built first: its template tells how many strokes the letter has
            var first=V.Rect("Film_1",root,xs[0],536,196,180);
            var film1=Content304Ink(V.Rect("StrokeExample",first,0,0,196,180).gameObject.AddComponent<CodexStrokeExample>(),s);   // ghost α.16 keeps its weight
            film1.InitializeFilm(library,letter,0,0,Color.clear,Color.clear,Color.clear,s.Sprites.Ribbon);
            int n=film1.StrokeCount;
            int frames=n<=0?1:Mathf.Min(3,n);
            for(int k=0;k<frames;k++)
            {
                var frame=k==0?first:V.Rect("Film_"+(k+1),root,xs[k],536,196,180);
                Content304Hairline(frame,0,0,196,180,UiStyle304SO.A(s.Paper,.16f),s);
                if(n<=0)
                {
                    V.Label(s,frame,"FallbackGlyph",letter,UiType304.CellGlyph64,s.Paper,0,0,196,176,TextAlignmentOptions.Center);
                    break;
                }
                int from=Mathf.CeilToInt(n*k/(float)frames),to=Mathf.CeilToInt(n*(k+1)/(float)frames);
                var film=k==0?film1:Content304Ink(V.Rect("StrokeExample",frame,0,0,196,180).gameObject.AddComponent<CodexStrokeExample>(),s);
                film.InitializeFilm(library,letter,from,to,UiStyle304SO.A(s.Mist,.75f),s.Paper,UiStyle304SO.A(s.Paper,.16f),s.Sprites.Ribbon);
                film.Replay(.12f+k*.6f);
                if(film.TryGetStrokeStart(from,out Vector2 start))
                {
                    // film rect: pivot top-left, so local (x, y) = (px, -py) from the frame's top-left
                    // the circle sits up-left of the stroke start (codex.png), never covering it; kept inside the frame
                    float cx=Mathf.Clamp(start.x-26f,0f,196f-24f),cy=Mathf.Clamp(-start.y-26f,-4f,180f-24f);
                    V.Disc(s,frame,cx,cy,24,s.Cinnabar,"OrderDisc");
                    var no=V.Label(s,frame,"OrderNo",(from+1).ToString(),UiType304.OrderNo16,s.Paper,cx,cy,24,24,TextAlignmentOptions.Center);
                    UiText304.ApplyRole(no,s.Role(UiType304.OrderNo16),s,false);no.gameObject.AddComponent<UiTextNoScale304>();
                }
            }
        }

        Element Content304ElementOf(string initial)
        {
            foreach(var def in WorldMacroCollectionCatalog.AllSpells)
                if(ContentVocab304.Decompose(def.Letter,out string ini,out _,out _)&&ini==initial)return def.Element;
            return Element.Wood;
        }

        static string Content304KindLabel(SpellKind kind)
        {
            switch(kind){case SpellKind.AttackSingle:case SpellKind.AttackArea:return "공격";case SpellKind.Parry:return "패링";case SpellKind.Summon:return "소환";
                case SpellKind.Field:return "필드";case SpellKind.Buff:return "버프";case SpellKind.Ward:return "방벽";default:return "";}
        }

        static string ElementLabel(Element element)=>Content304ElementName(element)+" "+Content304Hanja(element);

        // ================================================================== 차패
        /// <summary>차패 (DESIGN §7.9, D18, D43): 신분패 쪽지 420x630 (差 牌 세로 60 먹, 소속, 부인 백문 - without the seal the block is
        /// centred on the card), 오덕 5행 x640 pitch 120:
        /// 새긴 덕 = 주문 방인 (관변 주사 테 + 주사 한자 on paper), 못 새긴 덕 = 같은 두께 비활성 테만, 쓰인 仁 = sealed (D22).
        /// D308-27: no [G] hint and no recall rule under the card; no state word per row (the seal says it), a used 仁 says 쓰임.</summary>
        void BuildChapae()
        {
            var s=Content304Style;var art=Content304Art;
            const float cardH=630f;
            var card=V.SpriteImage(contentRoot,"Identity",s.Sprites.SheetSlip,s.Sprites.SheetSlip!=null?Color.white:s.Sheet,96,200,420,cardH);
            var cardTitle=V.Label(s,card.transform,"Title",UiText304.Vertical("差牌"),UiType304.Speaker60,s.Ink,0,64,420,0,TextAlignmentOptions.Top);
            var cardLabel=V.Label(s,card.transform,"IdentityLabel","오행부 소속\n전직 집행관",UiType304.Label24,s.Ink,0,300,420,0,TextAlignmentOptions.Top);
            if(V.Seal(s,card.transform,182,470,56)==null)
            {
                // no seal (UiStyle304SO.ShowSeals off, user request): the 差 牌 + 소속 block alone would hang in the top half with
                // ~270 px of bare paper under it, so the block is centred on the card instead (12 px above centre: the rects carry
                // more room above the first glyph than under the last line). Same gap between the two as with the seal.
                float blockTop=64f,blockBottom=300f+cardLabel.rectTransform.sizeDelta.y;
                float shift=Mathf.Max(0f,(cardH-(blockBottom-blockTop))*.5f-12f-blockTop);
                V.Place(cardTitle.rectTransform,0,64f+shift);
                V.Place(cardLabel.rectTransform,0,300f+shift);
            }

            string[] virtueIds={"仁","禮","義","智","信"};string[] names={"인 · 목","예 · 화","의 · 금","지 · 수","신 · 토"};
            var knownVirtues=Session.Progress.ui.knownVirtues;
            for(int i=0;i<5;i++)
            {
                bool owned=knownVirtues.Contains(virtueIds[i]);float y=220+i*120;
                bool sealedRen=owned&&virtueIds[i]=="仁"&&Session.DemoCampaignActive&&Session.Progress.renUsed;
                var seal=V.Rect("Virtue_"+i,contentRoot,640,y,64,64);
                if(owned)
                {
                    Content304Chip(seal,"SealPaper",0,0,64,false);
                    Content304SealFrame(seal,s.Cinnabar,art);
                    V.Label(s,seal,"Glyph",virtueIds[i],UiType304.Heading44,s.Cinnabar,0,0,64,62,TextAlignmentOptions.Center);
                }
                else Content304SealFrame(seal,s.Off,art);
                // D308-27 (차패 ①④, answer 1): no state word per row - the seal says it (주사 도장 / 빈 테) and the word stands once in
                // the detail of the selected 오덕 칸 on the 소지품 page. The name sits on the seal's middle line; a used 仁 says 쓰임
                var virtueName=V.Label(s,contentRoot,"VirtueName_"+i,names[i],UiType304.Label28,owned?s.Paper:s.Mist,728,y);
                V.Place(virtueName.rectTransform,728,y+(64f-virtueName.rectTransform.sizeDelta.y)*.5f);
                if(sealedRen)
                {
                    var used=V.Label(s,contentRoot,"VirtueState_"+i,"쓰임",UiType304.Meta20,s.Mist,0,0);
                    V.Place(used.rectTransform,728+virtueName.rectTransform.sizeDelta.x+16,y+(64f-used.rectTransform.sizeDelta.y)*.5f);
                    Content304Sealed(contentRoot,630,y+20,190,24);   // D22 across the seal and the name (both on the middle line now)
                }
            }
        }

        /// <summary>관변 seal border 64² (seal_frame.png; 3 px rect lines when content304-setup has not run).</summary>
        void Content304SealFrame(RectTransform seal,Color tint,ContentArt304 art)
        {
            if(art!=null&&art.SealFrame!=null){V.SpriteImage(seal,"SealFrame",art.SealFrame,tint,0,0,64,64);return;}
            V.Image(V.Rect("SealT",seal,0,0,64,3),tint);V.Image(V.Rect("SealB",seal,0,61,64,3),tint);
            V.Image(V.Rect("SealL",seal,0,3,3,58),tint);V.Image(V.Rect("SealR",seal,61,3,3,58),tint);
        }
    }
}
