using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Data.Demo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 소지품 · 장비 (inventory.png, DESIGN §7.3, IMPLEMENTATION §7.5): 장착 칸 표 around the portrait, 보유 격자 4 x 3
    /// with names and 착용 중, detail column x1410 (이름 60, 강화 단계, 효과 32, 교체 +N%p, 다음 강화, 착용 + Enter). Focus on a chip
    /// updates the detail in place; Enter / click on a chip moves focus into the detail (the chip keeps an ink frame).
    /// Harness names kept: InkPlayerSilhouette, Equipped_&lt;slot&gt; (+EquipmentDropSlot), Gear_&lt;id&gt; (+EquipmentDragItem),
    /// EquipmentTab, FragmentsTab, EquipmentOwnedGrid, SelectedGearPaper, SelectedGearTitle, SelectedGearEffect, GearEquip,
    /// GearTransactionError; GearAction(action, id, slot, revision, price) is invoked by reflection (CompactSoundChecks255).</summary>
    public sealed partial class PlaytestUiRoot
    {
        string gearSelection="",gearError="";bool fragmentTab;
        RectTransform content304Detail;
        string content304Kept;                  // name of the chip that owns the page selection (ink frame while unfocused)
        string content304DetailKey;             // what the detail column currently shows
        EquipmentSlot? content304EmptySlot;     // focused empty 장착 칸: the detail shows the slot instead of an item
        readonly List<ContentCell304> content304GearCells=new List<ContentCell304>();
        readonly Dictionary<ContentCell304,Image> content304GearChips=new Dictionary<ContentCell304,Image>();

        // EquipmentSlot order: Brush, Head, Body, Hands, Feet, Accessory (DESIGN §7.3 coordinates)
        static readonly Vector2[] Content304SlotXY={new Vector2(96,580),new Vector2(620,264),new Vector2(620,494),new Vector2(96,380),new Vector2(620,750),new Vector2(96,780)};
        static readonly string[] Content304SlotNames={"붓","머리","몸","손","발","장신구"};
        const float Content304GridX=800,Content304GridY=264,Content304PitchX=136,Content304PitchY=186;
        const string Content304Artisan="목공 장인",Content304Merchant="장비 상인";

        void OpenEquipmentService(string id){gearSelection="";gearError="";OpenPage(id=="village_shop"?"장비 상점":"장비 강화");}

        void GearAction(EquipmentAction action,string id,EquipmentSlot slot,long revision,int expectedPrice=-1){
            bool accepted=Session.TryEquipment(action,id,slot,revision,Guid.NewGuid().ToString("N"),out gearError,expectedPrice);
            string cue=!accepted?"ui_error":action==EquipmentAction.Buy?"purchase":action==EquipmentAction.Upgrade?"upgrade":action==EquipmentAction.Unequip?"unequip":"equip";
            if(!PlayNamedSound(cue,.65f))PlayUi(accepted?Theme.ConfirmSound:Theme.BackSound,.4f);
            suppressNextPageSound255=Theme.SoundPalette!=null;
            OpenPage(Page);
        }

        // ------------------------------------------------------------------ page
        void BuildEquipmentInventory(){
            var s=Content304Style;var catalog=Session.Content.EquipmentCatalog;var state=Session.Progress.equipment;
            content304GearCells.Clear();content304GearChips.Clear();
            Content304SubTabs(true);
            Content304Currency(contentRoot,"Currency",1856,160,Session.Progress.ledger.currency);
            if(!Session.EquipmentReady){V.Label(s,contentRoot,"GearError","장비 저장을 확인해야 한다.",UiType304.Body24,s.CinnabarLift,800,264);return;}
            var art=Session.Content.EquipmentUiArt;

            // portrait (the paper doll): exactly one EquipmentInkGraphic with Symbol < 0
            var portrait=V.Rect("InkPlayerSilhouette",contentRoot,196,250,440,660).gameObject.AddComponent<EquipmentInkGraphic>();
            portrait.Artwork=art!=null?art.Portrait:null;portrait.raycastTarget=false;
            portrait.color=portrait.Artwork!=null?Color.white:UiStyle304SO.A(s.Paper,.22f);

            // default selection: keep the current one, else what the brush slot wears, else the first owned item
            if(string.IsNullOrEmpty(gearSelection)||!state.Has(gearSelection))
                gearSelection=(state.Equipped.FirstOrDefault(x=>!string.IsNullOrEmpty(x))??state.Owned.FirstOrDefault()?.Id)??"";
            if(fragmentTab){Content304FragmentGrid(Content304GridX,Content304GridY,4);}
            else{
                // 장착 칸 표 (6 drop targets)
                for(int i=0;i<6;i++){
                    var slot=(EquipmentSlot)i;string worn=state.Equipped[i];var p=Content304SlotXY[i];
                    bool has=!string.IsNullOrEmpty(worn);var d=has?catalog.Find(worn):null;
                    var cell=Content304Cell(contentRoot,"Equipped_"+slot,p.x,p.y,128,166,new Rect(0,0,Content304ChipSize,Content304ChipSize),new Rect(-33,142,26,20),
                        ()=>Content304FocusDetailPrimary(),c=>Content304PickGear(c,worn??"",has?(EquipmentSlot?)null:slot),false);
                    cell.Id=worn??"";
                    var chip=Content304Chip(cell.transform,"Chip",0,0,Content304ChipSize,!has);
                    if(has)Content304GearArt(cell.transform,slot,art,8,8,88,Color.white);
                    else Content304GearArt(cell.transform,slot,art,12,12,80,UiStyle304SO.A(s.Paper,.2f));
                    V.Label(s,cell.transform,"SlotName",Content304SlotNames[i],UiType304.Meta20,s.Mist,0,112);
                    var name=V.Label(s,cell.transform,"Name",has&&d!=null?Content304GearName(d,state):"비어 있음",UiType304.Label22,has?s.Paper:s.Mist,0,138);
                    cell.BindLabel(name,has?s.Paper:s.Mist,has?s.Paper:s.Mist);
                    var drop=cell.gameObject.AddComponent<EquipmentDropSlot>();drop.Slot=slot;
                    content304GearCells.Add(cell);content304GearChips[cell]=chip;
                }
                Content304OwnedGrid();
            }
            Content304Rule(contentRoot,"DetailRule",1368,250,740);
            content304Detail=V.Rect("GearDetail304",contentRoot,0,0,1920,1080);
            if(content304GearCells.All(c=>c.name!=content304Kept))content304Kept=fragmentTab?"Fragment_"+selectedItem:"Gear_"+gearSelection;
            Content304RefreshGear();

            var fallback=fragmentTab?Content304FirstFragment():content304GearCells.FirstOrDefault(c=>c.name=="Gear_"+gearSelection)?.gameObject;
            if(fallback==null)fallback=content304GearCells.FirstOrDefault(c=>c.name.StartsWith("Gear_",StringComparison.Ordinal))?.gameObject;
            if(fallback==null)fallback=contentRoot.Find(fragmentTab?"FragmentsTab":"EquipmentTab")?.gameObject;
            Content304Restore(contentRoot,fallback);
        }

        void Content304OwnedGrid(){
            var s=Content304Style;var catalog=Session.Content.EquipmentCatalog;var state=Session.Progress.equipment;var art=Session.Content.EquipmentUiArt;
            int n=state.Owned.Count,rows=Mathf.Max(3,Mathf.CeilToInt(n/4f));
            RectTransform grid=rows>3?V.Scroll(contentRoot,"EquipmentOwnedGrid",Content304GridX,Content304GridY,4*Content304PitchX+18,3*Content304PitchY,rows*Content304PitchY)
                :V.Rect("EquipmentOwnedGrid",contentRoot,Content304GridX,Content304GridY,4*Content304PitchX,rows*Content304PitchY);
            for(int i=0;i<n;i++){
                var item=state.Owned[i];var d=catalog.Find(item.Id);if(d==null)continue;
                string id=item.Id;float gx=i%4*Content304PitchX,gy=i/4*Content304PitchY;
                var cell=Content304Cell(grid,"Gear_"+id,gx,gy,128,172,new Rect(0,0,Content304ChipSize,Content304ChipSize),new Rect(-33,121,26,20),
                    ()=>{Content304PickGear(Content304GearCells("Gear_"+id),id,null);Content304FocusDetailPrimary();},c=>Content304PickGear(c,id,null),false);
                cell.Id=id;
                var chip=Content304Chip(cell.transform,"Chip",0,0,Content304ChipSize,false);
                Content304GearArt(cell.transform,d.Slot,art,8,8,88,Color.white);
                var name=V.Label(s,cell.transform,"Name",Content304GearName(d,state),UiType304.Label22,s.Paper,0,114);
                if(state.Equipped.Contains(id))V.Label(s,cell.transform,"Worn","착용 중",UiType304.Meta20,s.Mist,0,144);
                cell.BindLabel(name,s.Paper,s.Paper);
                Content304NewMark(cell,"gear:"+id,Content304ChipSize-22,-8,false);
                var drag=cell.gameObject.AddComponent<EquipmentDragItem>();drag.ItemId=id;drag.Revision=state.Revision;drag.Dropped=(gid,slot,rev)=>GearAction(EquipmentAction.Equip,gid,slot,rev);
                content304GearCells.Add(cell);content304GearChips[cell]=chip;
            }
            for(int i=n;i<rows*4;i++)Content304Chip(grid,"EmptyCapacity",i%4*Content304PitchX,i/4*Content304PitchY,Content304ChipSize,true);
            bool lastRowUsed=n>(rows-1)*4;
            float hintY=Content304GridY+(lastRowUsed?Mathf.Min(rows,3)*Content304PitchY+8:(Mathf.Min(rows,3)-1)*Content304PitchY+Content304ChipSize+24);
            V.Hint(s,contentRoot,"DragHint",new[]{"끌기"},"칸에 놓아 착용",s.Mist,Content304GridX,hintY);
        }

        ContentCell304 Content304GearCells(string name)=>content304GearCells.FirstOrDefault(c=>c!=null&&c.name==name);

        /// <summary>Chip focus / click: the chip becomes the page selection and the detail column shows it (no page rebuild).</summary>
        void Content304PickGear(ContentCell304 cell,string id,EquipmentSlot? emptySlot){
            if(cell!=null){content304Kept=cell.name;content304Focus=cell.name;}
            if(id!=gearSelection||emptySlot!=content304EmptySlot)gearError="";
            gearSelection=id??"";content304EmptySlot=emptySlot;
            Content304RefreshGear();
        }

        void Content304RefreshGear(){
            foreach(var c in content304GearCells){
                if(c==null)continue;bool kept=c.name==content304Kept;c.Kept=kept;
                if(content304GearChips.TryGetValue(c,out var chip)&&chip!=null)chip.name=kept&&!string.IsNullOrEmpty(c.Id)?"SelectedGearPaper":"Chip";
            }
            if(content304Detail==null)return;
            // focus moves call this every time; rebuild only when what the column shows changed (a page rebuild = new root)
            string key=content304Detail.GetInstanceID()+"|"+Page+"|"+fragmentTab+"|"+gearSelection+"|"+content304EmptySlot+"|"+selectedItem+"|"+gearError;
            if(key==content304DetailKey)return;
            content304DetailKey=key;
            V.Clear(content304Detail);
            if(Page=="소지품"&&(fragmentTab||!Session.EquipmentEnabled)){Content304FragmentDetail();return;}
            bool shop=Page=="장비 상점",forge=Page=="장비 강화";
            DrawGearDetails(shop,forge);
        }

        /// <summary>Enter / click on a chip: focus moves to the detail's primary action (the chip keeps the ink frame, C17).</summary>
        void Content304FocusDetailPrimary(){
            if(content304Detail==null)return;
            foreach(var name in new[]{"GearEquip","GearBuy","GearUpgrade","ReadCodex"}){
                var go=Content304FindSelectable(content304Detail,name);
                if(go!=null){Content304Select(go);return;}
            }
        }

        // ------------------------------------------------------------------ sub-tabs (장비 8 / 석경 12)
        void Content304SubTabs(bool equipment){
            var ui=Session.Progress.ui;var seen=Content304Seen;
            var held=WorldMacroCollectionCatalog.AllFragments.Where(x=>ui.GetItemCount(x.ItemId)>0).ToArray();
            var owned=Session.Progress.equipment?.Owned;
            bool newGear=owned!=null&&owned.Any(o=>o!=null&&seen.IsNew("gear:"+o.Id)),newFragment=held.Any(f=>seen.IsNew("item:"+f.ItemId));
            float x=64;
            if(equipment)x=Content304SubTab("EquipmentTab","장비",owned?.Count??0,!fragmentTab,newGear,x,()=>{if(!fragmentTab)return;fragmentTab=false;content304Focus="EquipmentTab";OpenPage("소지품");})+44;
            Content304SubTab("FragmentsTab","석경",held.Length,fragmentTab||!equipment,newFragment,x,()=>{if(fragmentTab)return;fragmentTab=true;content304Focus="FragmentsTab";OpenPage("소지품");});
        }

        /// <summary>하위 탭 (DESIGN §5.4): selected = Title32 Paper + swell, other = Label24 Mist; count meta 20. Focus = 방점 only (rail grammar).
        /// Returns the right edge. Label and count share the mockup's baseline y189 (flex-end row at 156: line box 38.4 + padding 12,
        /// the Title32 baseline 5.4 above the line bottom); TMP box bottoms on y206 put the glyphs 8 px low, onto the swell.</summary>
        float Content304SubTab(string name,string text,int count,bool selected,bool fresh,float x,UnityEngine.Events.UnityAction click){
            var s=Content304Style;const float top=150,baseline=189;
            var rect=V.Rect(name,contentRoot,x-4,top,10,64);
            var hit=V.Image(rect,new Color(0,0,0,0),null,true);hit.canvasRenderer.cullTransparentMesh=true;
            var button=rect.gameObject.AddComponent<Button>();button.transition=Selectable.Transition.None;button.targetGraphic=hit;button.onClick.AddListener(click);
            if(Theme.SoundPalette!=null)rect.gameObject.AddComponent<CompactUiSound255>().Theme=Theme;
            var label=V.Label(s,rect,"Label",text,selected?UiType304.Title32:UiType304.Label24,selected?s.Paper:s.Mist,4,0);
            var ls=label.rectTransform.sizeDelta;Content304OnBaseline(label,4,baseline-top);
            float labelTop=-label.rectTransform.anchoredPosition.y;
            var meta=V.Label(s,rect,"Count",count.ToString(),UiType304.Meta20,s.Mist,0,0);
            var ms=meta.rectTransform.sizeDelta;Content304OnBaseline(meta,4+ls.x+6,baseline-top);
            float w=4+ls.x+6+ms.x+4;rect.sizeDelta=new Vector2(w,64);
            if(selected){var sw=V.Swell(s,rect,-16,42,Mathf.Max(150,w+36),30,null,.9f);sw.transform.SetAsFirstSibling();}
            if(fresh){var art=Content304Art;var c=s.Mist;   // D21 on the tab label (Mist on the veil)
                var mark=art!=null&&art.Flick!=null?V.SpriteImage(rect,"NewMark",art.Flick,c,w-4,labelTop-8,26,10,-18f):V.Brush(s,rect,"NewMark",StrokeClass304.Short,c,w-4,labelTop-8,26,7,1f,-18f);
                mark.raycastTarget=false;}
            var dab=V.DabAnchor(rect,4-14-34,labelTop+ls.y*.5f-13+3,34,26,s);
            var visual=rect.gameObject.AddComponent<FocusVisual304>();visual.Bind(s,null,label,selected?s.Paper:s.Mist,s.Paper,dab);
            Content304Note(button);
            return x-4+w;
        }

        // ------------------------------------------------------------------ chip art
        EquipmentInkGraphic Content304GearArt(Transform parent,EquipmentSlot slot,Oheangbu.Data.World.EquipmentUiArtSO art,float x,float y,float size,Color tint){
            var g=V.Rect("GearSymbol",parent,x,y,size,size).gameObject.AddComponent<EquipmentInkGraphic>();
            var sprite=art!=null?art.ForSlot(slot):null;
            g.Symbol=(int)slot;g.Artwork=sprite;g.raycastTarget=false;
            g.color=sprite!=null?tint:UiStyle304SO.A(Content304Style.Ink,tint.a*.8f);
            return g;
        }

        static string Content304GearName(EquipmentDefinition d,EquipmentState state){int level=state.Level(d.Id);return level>0?d.Name+" +"+level:d.Name;}
        static string EffectName(EquipmentEffect effect)=>effect==EquipmentEffect.MaxHealth?"최대 체력":effect==EquipmentEffect.MaxInk?"최대 먹":effect==EquipmentEffect.WoodDamage?"목 속성 위력":"모든 속성 위력";
        static string Content304Pct(float v)=>(v*100).ToString("0");

        // ------------------------------------------------------------------ detail column (inventory x1410; trade windows shift it by dx)
        void DrawGearDetails(bool shop,bool forge){
            var s=Content304Style;var catalog=Session.Content.EquipmentCatalog;var state=Session.Progress.equipment;var art=Session.Content.EquipmentUiArt;
            Transform root=content304Detail!=null?(Transform)content304Detail:contentRoot;
            float dx=shop||forge?-648:0;
            var d=catalog.Find(gearSelection);
            if(d==null){if(!shop&&!forge&&content304EmptySlot.HasValue)Content304EmptySlotDetail(root,content304EmptySlot.Value);return;}
            int level=state.Level(d.Id),max=catalog.UpgradeCosts!=null?catalog.UpgradeCosts.Length:3;
            float bonus=catalog.Bonus(state,d.Id);var old=state.Equipped[(int)d.Slot];bool worn=old==d.Id,owned=state.Has(d.Id);

            Content304GearArt(root,d.Slot,art,1630+dx,236,230,Color.white);
            V.Label(s,root,"SelectedGearSlot",Content304SlotNames[(int)d.Slot],UiType304.Meta20,s.Mist,1410+dx,262);
            // mockup CSS tops: name 290 (line-height 1.1), +1 강화 370 (t-label 1.25), effect 554 (t-title 1.2)
            var title=V.Label(s,root,"SelectedGearTitle",d.Name,UiType304.Speaker60,s.Paper,1406+dx,290);
            Content304AtCss(title,1406+dx,290,1.1f);
            if(title.rectTransform.sizeDelta.x>1630-1406-8){UiText304.ApplyRole(title,s.Role(UiType304.Heading44),s);title.rectTransform.sizeDelta=UiText304.Preferred(title,d.Name);Content304AtCss(title,1406+dx,302,1.2f);}
            var levelLabel=V.Label(s,root,"SelectedGearLevel",owned&&level>0?"+"+level+" 강화":owned?"강화 전":"새 물건",UiType304.Label26,s.Mist,1410+dx,370);
            Content304AtCss(levelLabel,1410+dx,370,1.25f);
            // pending steps = Mist α.35 over the veil, pre-composited (a linear blend printed them #63 vs the mockup's #42)
            for(int k=0;k<max;k++)V.SpriteImage(root,"LevelDab",s.Sprites.Dab,k<level?s.Paper:Content304Precomposite(s.Veil,s.Mist,.35f),1412+dx+k*36,414,28,21,s.DabRotation);
            V.Brush(s,root,"DetailDivider",StrokeClass304.Dry,s.Paper,1400+dx,482,456,28,.38f);
            V.Label(s,root,"EffectHead","효과",UiType304.Meta20,s.Mist,1410+dx,526);
            string effect=EffectName(d.Effect)+" +"+Content304Pct(bonus)+"%";
            if(forge&&level<max)effect+=" → +"+Content304Pct(bonus+catalog.UpgradeStep)+"%";
            var effectLabel=V.Label(s,root,"SelectedGearEffect",effect,UiType304.Title32,s.Paper,1410+dx,554);
            Content304AtCss(effectLabel,1410+dx,554,1.2f);
            if(!forge){
                if(worn)V.Label(s,root,"SelectedGearCompare","착용 중",UiType304.ValueBold24,s.Paper,1410+dx,604);
                else{
                    float oldBonus=string.IsNullOrEmpty(old)?0:catalog.Bonus(state,old);float delta=bonus-oldBonus;
                    var cmp=V.Label(s,root,"SelectedGearCompare","교체 "+(delta>=0?"+":"")+Content304Pct(delta)+"%p",UiType304.ValueBold24,s.Paper,1410+dx,604);
                    var od=string.IsNullOrEmpty(old)?null:catalog.Find(old);
                    string against=od!=null?"착용 중인 "+od.Name+Content304Josa(od.Name,"과","와")+" 비교":"비어 있는 "+Content304SlotNames[(int)d.Slot]+" 칸과 비교";
                    V.Label(s,root,"CompareAgainst",against,UiType304.Body22,s.Mist,1410+dx+cmp.rectTransform.sizeDelta.x+12,606);
                }
            }
            long revision=state.Revision;int quotedPrice=shop?d.Price:forge&&level<max?catalog.UpgradeCosts[level]:0;
            if(shop){
                V.Label(s,root,"PriceHead","값",UiType304.Meta20,s.Mist,1410+dx,676);
                var price=V.Label(s,root,"Price","조선통보 "+d.Price.ToString("N0"),UiType304.Body24,owned?s.Mist:s.Paper,1410+dx,704);
                V.Label(s,root,"PriceNote",owned?"이미 지닌 장비":"지닌 통보 "+Session.Progress.ledger.currency.ToString("N0"),UiType304.Meta20,s.Mist,1410+dx,740);
            }else{
                V.Label(s,root,"UpgradeHead",forge?"강화 비용":"다음 강화",UiType304.Meta20,s.Mist,1410+dx,676);
                if(level<max){
                    int cost=catalog.UpgradeCosts[level];
                    V.Label(s,root,"UpgradeNext",forge?"조선통보 "+cost.ToString("N0"):"+"+(level+1)+" 강화",UiType304.Body24,s.Paper,1410+dx,704);
                    V.Label(s,root,"UpgradeWhere",forge?"+"+(level+1)+" 강화":Content304Artisan+"에게서 조선통보 "+cost.ToString("N0"),UiType304.Meta20,s.Mist,1410+dx,740);
                }else V.Label(s,root,"UpgradeNext","강화 완료",UiType304.Body24,s.Mist,1410+dx,704);
            }
            FocusRow304 primary;
            var spec=new FocusRowSpec304{Role=UiType304.Title36,LabelX=40,Underlay=StrokeClass304.WetM,UnderlayH=122,UnderlayX=0,Key="Enter",KeySmall=false,
                UnderStroke=true,UnderStrokeW=300,SoundTheme=Theme};
            if(shop){
                spec.Disabled=owned;spec.DisabledReason="보유 중";spec.MetaX=210;
                primary=V.FocusRow(s,root,"GearBuy","구매",1370+dx,846,480,92,()=>GearAction(EquipmentAction.Buy,d.Id,d.Slot,revision,quotedPrice),spec);
            }else if(forge){
                spec.Disabled=level>=max;spec.DisabledReason="강화 완료";spec.MetaX=210;
                primary=V.FocusRow(s,root,"GearUpgrade","강화",1370+dx,846,480,92,()=>GearAction(EquipmentAction.Upgrade,d.Id,d.Slot,revision,quotedPrice),spec);
            }else{
                primary=V.FocusRow(s,root,"GearEquip",worn?"해제":"착용",1370+dx,846,480,92,()=>GearAction(worn?EquipmentAction.Unequip:EquipmentAction.Equip,d.Id,d.Slot,revision),spec);
            }
            Content304Note(primary.Button);
            if(!string.IsNullOrEmpty(gearError))
                V.Label(s,root,"GearTransactionError",gearError,UiType304.MetaBold20,s.CinnabarLift,1410+dx,962,440,0,TextAlignmentOptions.TopLeft,true);
        }

        void Content304EmptySlotDetail(Transform root,EquipmentSlot slot){
            var s=Content304Style;var catalog=Session.Content.EquipmentCatalog;var state=Session.Progress.equipment;
            V.Label(s,root,"SelectedGearSlot",Content304SlotNames[(int)slot],UiType304.Meta20,s.Mist,1410,262);
            Content304AtCss(V.Label(s,root,"SelectedGearTitle","비어 있음",UiType304.Speaker60,s.Mist,1406,290),1406,290,1.1f);
            var fits=state.Owned.Select(o=>catalog.Find(o.Id)).Where(x=>x!=null&&x.Slot==slot).Select(x=>x.Name).ToArray();
            Content304AtCss(V.Label(s,root,"SlotFits",fits.Length>0?"맞는 장비  "+string.Join(", ",fits):"맞는 장비 없음",UiType304.Label26,s.Mist,1410,370),1410,370,1.25f);
        }

        /// <summary>받침 있는 말 뒤 withFinal (과, 은, 을), 없으면 withoutFinal (와, 는, 를).</summary>
        static string Content304Josa(string word,string withFinal,string withoutFinal){
            if(string.IsNullOrEmpty(word))return withoutFinal;
            int code=word[word.Length-1]-0xAC00;
            return code>=0&&code<11172&&code%28!=0?withFinal:withoutFinal;
        }
    }
}
