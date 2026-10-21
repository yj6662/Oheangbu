using System;
using System.Linq;
using TMPro;
using UnityEngine;
using Oheangbu.Data.World;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>D308-117 B2 (SPEC-FIELD-FIRST-HOUR-308 §3): 소지품 > 문서. A paper the player carries (the commission letter) is
    /// read again here — a thing in the world, not a HUD or a goal list. The tab rides the 석경 column (Equip308Fragments is true
    /// while documentTab is on), so the back / kept-chip / legend paths of the 석경 tab serve it unchanged; only the grid, the
    /// detail and the Enter action differ. Enter opens the full text on the 상세 page (ShowDetail). Harness names: DocumentsTab,
    /// Document_&lt;id&gt;, DocumentTitle.</summary>
    public sealed partial class PlaytestUiRoot
    {
        const string Document308Glyph="문";

        /// <summary>Configured documents the bundle holds, in the content order.</summary>
        WorldMacroPlaytestSO.DocumentSpec[] Documents308Held()
        {
            var ui=Session?.Progress?.ui;var docs=Session?.Content?.Documents;
            if(ui==null||docs==null)return Array.Empty<WorldMacroPlaytestSO.DocumentSpec>();
            return docs.Where(d=>d!=null&&d.IsConfigured&&ui.GetItemCount(d.Id)>0).ToArray();
        }

        WorldMacroPlaytestSO.DocumentSpec Equip308SelectedDocument()=>Documents308Held().FirstOrDefault(d=>d.Id==selectedDocument);

        /// <summary>The 문서 chips: the 석경 grid's compact cell, the glyph 문 on the chip; the title is in the detail head.</summary>
        void Content308DocumentGrid(float ox,float oy,int cols,float pitchX,float pitchY)
        {
            var s=Content304Style;var held=Documents308Held();
            if(held.Length==0){EmptyPage("빈 봇짐",null,ox,oy);return;}
            if(string.IsNullOrEmpty(selectedDocument)||!held.Any(d=>d.Id==selectedDocument))selectedDocument=held[0].Id;
            int rows=Mathf.Max(1,Mathf.CeilToInt(held.Length/(float)cols));const int shown=4;
            float cellW=pitchX-4,cellH=pitchY-8;
            RectTransform grid=rows>shown?V.Scroll(contentRoot,"DocumentGrid",ox,oy,cols*pitchX+18,shown*pitchY,rows*pitchY)
                :V.Rect("DocumentGrid",contentRoot,ox,oy,cols*pitchX,rows*pitchY);
            for(int i=0;i<held.Length;i++)
            {
                var d=held[i];float gx=i%cols*pitchX,gy=i/cols*pitchY;
                var cell=Content304Cell(grid,"Document_"+d.Id,gx,gy,cellW,cellH,Equip308FragmentRing(),Equip308FragmentDab(),
                    ()=>{Content308PickDocument(Content304GearCells("Document_"+d.Id),d);Equip308ReadDocument();},c=>Content308PickDocument(c,d),false);
                cell.Id=d.Id;
                var kept=Equip308KeptFrame(s);if(kept!=s.Ink)cell.Init(s,cell.Visual,Equip308FragmentRing(),kept,c=>Content308PickDocument(c,d));
                Equip308FragmentPane(cell,false);
                Content304Chip(cell.transform,"Chip",0,0,Content304ChipSize,false);
                Equip308FragmentPane(cell,true);
                V.Label(s,cell.transform,"Glyph",Document308Glyph,UiType304.ChipGlyph56,s.Ink,0,0,Content304ChipSize,Content304ChipSize-4,TextAlignmentOptions.Center);
                Content304NewMark(cell,"item:"+d.Id,Content304ChipSize-22,-8,false);
                content304GearCells.Add(cell);
            }
        }

        GameObject Content308FirstDocument()
        {
            var cell=content304GearCells.FirstOrDefault(c=>c!=null&&c.Id==selectedDocument&&c.name.StartsWith("Document_",StringComparison.Ordinal))
                ??content304GearCells.FirstOrDefault(c=>c!=null&&c.name.StartsWith("Document_",StringComparison.Ordinal));
            return cell!=null?cell.gameObject:null;
        }

        void Content308PickDocument(ContentCell304 cell,WorldMacroPlaytestSO.DocumentSpec d)
        {
            if(cell!=null){content304Kept=cell.name;content304Focus=cell.name;}
            selectedDocument=d.Id;
            equip308InMiddle=false;Equip308Raise(cell);
            Content304RefreshGear();
            if(Page=="소지품")Equip308Legend();
        }

        /// <summary>Notes on the back whose stage is done (campaign order of the content, max 3 — SPEC B2).</summary>
        string[] Document308Notes(WorldMacroPlaytestSO.DocumentSpec d)
        {
            var done=Session?.Progress?.campaign?.Completed;
            if(d.Notes==null||done==null)return Array.Empty<string>();
            return d.Notes.Where(n=>n!=null&&!string.IsNullOrWhiteSpace(n.Text)&&!string.IsNullOrEmpty(n.AfterStage)&&done.Contains(n.AfterStage)).Select(n=>n.Text).Take(3).ToArray();
        }

        /// <summary>Centre column: head (문서 / title / 덧적음 수), the glyph in the art box, the pages, then the notes.</summary>
        float Equip308DocumentInfo(UiStyle304SO s)
        {
            var root=equip308Info;var d=Equip308SelectedDocument();if(d==null)return E308SubY;
            var notes=Document308Notes(d);
            Equip308Header(s,root,"문서",d.Title,s.Paper,notes.Length>0?"덧적음 "+notes.Length:null,0,0,"DocumentTitle");
            var artRoot=Equip308ArtBox(s,root,false,"document:"+d.Id);
            var glyph=V.Label(s,artRoot,"ItemGlyph",Document308Glyph,UiType304.Glyph240,s.Paper,0,0,E308ArtSize,E308ArtSize,TextAlignmentOptions.Center);
            glyph.gameObject.AddComponent<UiTextNoScale304>();glyph.fontSize=E308ArtInner*E308GlyphFill;
            float y=equip308SectionsTop;
            y=Equip308Section(s,root,"PagesHead",Equip308Mark(7),"적힌 글",E308SubLX,y);
            foreach(var page in d.Pages)if(!string.IsNullOrWhiteSpace(page))y=Equip308Line(s,root,"Page",page,UiType304.Body24,s.Paper,E308SubLX,y,E308MidR-E308SubLX);
            if(notes.Length>0)
            {
                y+=E308SectionGap;
                y=Equip308Section(s,root,"NotesHead",Equip308Mark(7),"뒷면",E308SubLX,y);
                foreach(var n in notes)y=Equip308Line(s,root,"Note",n,UiType304.Body24,s.Mist,E308SubLX,y,E308MidR-E308SubLX);
            }
            return y;
        }

        /// <summary>Enter: the whole paper on the 상세 page (pages, then the notes on the back).</summary>
        void Equip308ReadDocument()
        {
            var d=Equip308SelectedDocument();if(d==null)return;
            var notes=Document308Notes(d);
            string body=string.Join("\n\n",d.Pages.Where(p=>!string.IsNullOrWhiteSpace(p)));
            if(notes.Length>0)body+="\n\n"+string.Join("\n",notes);
            content304Focus="Document_"+d.Id;
            ShowDetail(d.Title,body);
        }
    }
}
