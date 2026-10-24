// NB20 (D308-150 V6): the After Effects pass over the Blender ink elements.
//   "C:\Program Files\Adobe\Adobe After Effects 2026\Support Files\AfterFX.exe" -r C:\Users\yj666\Oheangbu\Tools\Art\SpellVFX308\nb20\nb20_ae_build.jsx
// Runs in the user's own After Effects (it was open): refuses when a project is open, renders in the render queue, saves the project for aerender re-runs,
// closes it and leaves After Effects running on an empty project. Effects by matchName (this AE runs in Korean).
// In : Art/SpellVFX308/Vol2Vfx/nb20/src/blender/<element>.png   (R tone, G pigment, B reveal order, A coverage; straight alpha)
// Out: Art/SpellVFX308/Vol2Vfx/nb20/src/ae/bleed/<element>_0000.tif  - Roughen Edges (Cut: the ink edge eaten by the paper fibre) + a small Turbulent Displace
//                                                                        (the wet ink wanders into the paper) on every channel together
//      Art/SpellVFX308/Vol2Vfx/nb20/src/ae/dry/plates_0000.tif        - Cell Pattern "static plates": one random value per fragment (the order fragments fall)
//      Art/SpellVFX308/Vol2Vfx/nb20/src/ae/dry/cracks_0000.tif        - Cell Pattern "crystals" pushed through Turbulent Displace: the crack network
//      Art/SpellVFX308/Vol2Vfx/nb20/src/ae/dry/fibre_0000.tif         - long thin Fractal Noise: the paper fibre the drying edge follows
// Then: python Tools/Art/SpellVFX308/nb20/nb20_pack.py
(function () {
    var ROOT = "C:/Users/yj666/Oheangbu/Art/SpellVFX308/Vol2Vfx/nb20/src";
    var log = new File(ROOT + "/ae/build.log"); log.parent.create(); log.open("w");
    function fx(layer, name) { return layer.property("ADBE Effect Parade").addProperty(name); }
    function out(comp, dir, name) {
        var d = new Folder(ROOT + "/ae/" + dir); if (!d.exists) d.create();
        var item = app.project.renderQueue.items.add(comp);
        var om = item.outputModule(1), t = om.templates, pick = null;
        for (var k = 0; k < t.length; k++) if (t[k].indexOf("TIFF") >= 0) pick = t[k];
        if (pick == null) throw new Error("no TIFF sequence output template");
        om.applyTemplate(pick);
        try { om.setSettings({ "Color": "Straight (Unmatted)" }); } catch (x) { }
        om.file = new File(d.fsName + "/" + name + "_[####].tif");
        return om;
    }
    try {
        if (app.project != null && (app.project.file != null || app.project.numItems > 0)) { log.writeln("abort: a project is open (" + app.project.numItems + " items)"); log.close(); return; }
        app.beginSuppressDialogs();
        app.newProject();
        var src = new Folder(ROOT + "/blender");
        var files = src.getFiles("*.png");
        var count = 0, firstOm = null;
        for (var i = 0; i < files.length; i++) {
            var f = files[i];
            var name = decodeURI(f.name).replace(/\.png$/i, "");
            var io = new ImportOptions(f);
            var footage = app.project.importFile(io);
            footage.mainSource.alphaMode = AlphaMode.STRAIGHT;
            var w = footage.width, h = footage.height, k = w / 2048.0;
            var c = app.project.items.addComp("bleed_" + name, w, h, 1, 1 / 30, 30);
            var lay = c.layers.add(footage);
            var isFleck = name.indexOf("fleck") == 0 || name.indexOf("ignite") == 0;
            // the paper eats the ink edge: Roughen Edges, Cut (alpha only), a fine fractal border
            var re = fx(lay, "ADBE Roughen Edges");
            re.property("ADBE Roughen Edges-0001").setValue(1);                 // edge type: cut
            re.property("ADBE Roughen Edges-0002").setValue((isFleck ? 3.0 : 4.5) * k);   // border (px)
            re.property("ADBE Roughen Edges-0003").setValue(1.6);               // edge sharpness
            re.property("ADBE Roughen Edges-0004").setValue(0.85);              // fractal influence
            re.property("ADBE Roughen Edges-0005").setValue(14 * k + 6);        // scale
            re.property("ADBE Roughen Edges-0008").setValue(5);                 // complexity
            re.property("ADBE Roughen Edges-0014").setValue(i * 7 + 3);         // random seed
            // the wet ink wanders a little into the paper (every channel moves together: the data stays registered)
            var td = fx(lay, "ADBE Turbulent Displace");
            td.property("ADBE Turbulent Displace-0002").setValue(isFleck ? 4 : 7);          // amount
            td.property("ADBE Turbulent Displace-0003").setValue(22 * k + 8);               // size
            td.property("ADBE Turbulent Displace-0005").setValue(2.5);                      // complexity
            td.property("ADBE Turbulent Displace-0010").setValue(i * 13 + 5);               // random seed
            var om = out(c, "bleed", name);
            if (firstOm == null) firstOm = om;
            count++;
        }
        // the drying maps (shared, sampled in the packer at a fixed world scale so a fragment is the same size on every element)
        var S = 2048;
        var dry = app.project.items.addComp("dry_plates", S, S, 1, 1 / 30, 30);
        var pl = dry.layers.addSolid([1, 1, 1], "plates", S, S, 1);
        var cp = fx(pl, "ADBE Cell Pattern");
        cp.property("ADBE Cell Pattern-0001").setValue(4);                       // static plates: one flat random value per cell
        cp.property("ADBE Cell Pattern-0006").setValue(70);                      // cell size (px)
        cp.property("ADBE Cell Pattern-0017").setValue(11);                      // seed
        var t2 = fx(pl, "ADBE Turbulent Displace");
        t2.property("ADBE Turbulent Displace-0002").setValue(28); t2.property("ADBE Turbulent Displace-0003").setValue(90); t2.property("ADBE Turbulent Displace-0005").setValue(3);
        out(dry, "dry", "plates");
        var cr = app.project.items.addComp("dry_cracks", S, S, 1, 1 / 30, 30);
        var cl = cr.layers.addSolid([1, 1, 1], "cracks", S, S, 1);
        var cp2 = fx(cl, "ADBE Cell Pattern");
        cp2.property("ADBE Cell Pattern-0001").setValue(2);                      // crystals: bright cell centres, dark borders = the cracks
        cp2.property("ADBE Cell Pattern-0006").setValue(70);
        cp2.property("ADBE Cell Pattern-0017").setValue(11);                     // the SAME cells as the plates
        var t3 = fx(cl, "ADBE Turbulent Displace");
        t3.property("ADBE Turbulent Displace-0002").setValue(28); t3.property("ADBE Turbulent Displace-0003").setValue(90); t3.property("ADBE Turbulent Displace-0005").setValue(3);
        out(cr, "dry", "cracks");
        var fb = app.project.items.addComp("dry_fibre", S, S, 1, 1 / 30, 30);
        var fl = fb.layers.addSolid([1, 1, 1], "fibre", S, S, 1);
        var fn = fx(fl, "ADBE Fractal Noise");
        fn.property("ADBE Fractal Noise-0004").setValue(140);                    // contrast
        fn.property("ADBE Fractal Noise-0009").setValue(0);                      // non-uniform scale
        fn.property("ADBE Fractal Noise-0011").setValue(300);                    // long
        fn.property("ADBE Fractal Noise-0012").setValue(12);                     // thin
        fn.property("ADBE Fractal Noise-0015").setValue(5);
        out(fb, "dry", "fibre");
        try { log.writeln("om settings keys: " + firstOm.getSettings(GetSettingsFormat.STRING).toSource().substring(0, 900)); } catch (x) { log.writeln("om settings: " + x.toString()); }
        app.project.renderQueue.render();
        app.project.save(new File(ROOT + "/ae/nb20_ae.aep"));
        log.writeln("ok " + count + " elements + 3 dry maps, After Effects " + app.version);
    } catch (e) { log.writeln("error " + e.toString() + " line " + e.line); }
    log.close();
    try { app.project.close(CloseOptions.DO_NOT_SAVE_CHANGES); } catch (x) { }
    app.newProject();      // After Effects is left running (the user opened it)
})();
