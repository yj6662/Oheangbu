// #308 spell VFX overhaul - the After Effects pass for the 6th-pass sheets only (ink_throw / ink_wisp / ink_gather).
// Same steps as bleed308.jsx; it refuses to run over an open project and leaves After Effects running.
//   "C:\Program Files\Adobe\Adobe After Effects 2026\Support Files\AfterFX.exe" -r C:\Users\yj666\Oheangbu\Tools\AfterEffects\SpellVFX308\bleed308.jsx
// (the GUI opens, renders and quits by itself; -noui does not run scripts on this install. Needs Preferences > Scripting &
//  Expressions > "Allow Scripts to Write Files and Access Network".)
// In : Art/SpellVFX308/AE/in/<preset>/d_####.png  (bleed308.py --sim; R heat, G wet heart, B wash density, 16 cels at 12 fps)
// Out: Art/SpellVFX308/AE/out/<preset>/wash_####.tif  - turbulent displace (finer wandering strands) + brush / paper fibre
//      Art/SpellVFX308/AE/out/<preset>/rim_####.tif   - the wash minus its own blur: the dark rim where ink pools
// Then: python Tools/Art/SpellVFX308/bleed308.py --pack
// Effects are addressed by matchName (this After Effects runs in Korean; display names differ per language).
(function () {
    var ROOT = "C:/Users/yj666/Oheangbu/Art/SpellVFX308/AE";
    var log = new File(ROOT + "/bleed308b.log"); log.open("w");
    // per preset: displace amount (px at 512), displace size, whether the displacement may evolve (not for loops / stills), fibre strength (%)
    // 6th pass (D308-83): only the three new sheets. Fifth value = fibre angle (0 = along the throw).
    var LOOK = { ink_throw: [5, 22, true, 34, 0], ink_wisp: [12, 26, false, 22, -12], ink_gather: [10, 26, true, 26, -28] };
    function fx(layer, name) { return layer.property("ADBE Effect Parade").addProperty(name); }
    try {
        // the user's own After Effects may be the one running this: never throw away a project that has anything in it
        if (app.project != null && (app.project.file != null || app.project.numItems > 0)) { log.writeln("abort: a project is open (" + app.project.numItems + " items)"); log.close(); return; }
        app.beginSuppressDialogs();
        app.newProject();
        var count = 0;
        for (var name in LOOK) {
            var first = new File(ROOT + "/in/" + name + "/d_0000.png");
            if (!first.exists) { log.writeln("skip " + name); continue; }
            var io = new ImportOptions(first); io.sequence = true; io.forceAlphabetical = true;
            var footage = app.project.importFile(io);
            footage.mainSource.conformFrameRate = 12;
            var frames = Math.round(footage.duration * 12), dur = frames / 12, look = LOOK[name];
            // comp A: the wash
            var a = app.project.items.addComp(name + "_wash", 512, 512, 1, dur, 12);
            var base = a.layers.add(footage);
            var td = fx(base, "ADBE Turbulent Displace");
            td.property("ADBE Turbulent Displace-0002").setValue(look[0]);       // amount
            td.property("ADBE Turbulent Displace-0003").setValue(look[1]);       // size
            td.property("ADBE Turbulent Displace-0005").setValue(3);             // complexity
            if (look[2]) { var ev = td.property("ADBE Turbulent Displace-0006"); ev.setValueAtTime(0, 0); ev.setValueAtTime(dur, 150); }
            // fibre: a long thin fractal grain multiplied into everything (heart, heat and wash keep their registration)
            var fibre = a.layers.addSolid([1, 1, 1], "fibre", 512, 512, 1, dur);
            var fn = fx(fibre, "ADBE Fractal Noise");
            fn.property("ADBE Fractal Noise-0004").setValue(170);                // contrast
            fn.property("ADBE Fractal Noise-0005").setValue(38);                 // brightness: mostly white, dark fibres
            fn.property("ADBE Fractal Noise-0009").setValue(0);                  // non-uniform scale
            fn.property("ADBE Fractal Noise-0011").setValue(260);                // long
            fn.property("ADBE Fractal Noise-0012").setValue(7);                  // thin
            fn.property("ADBE Fractal Noise-0015").setValue(4);                  // complexity
            fibre.blendingMode = BlendingMode.MULTIPLY;
            fibre.property("ADBE Transform Group").property("ADBE Opacity").setValue(look[3]);
            fibre.property("ADBE Transform Group").property("ADBE Rotate Z").setValue(look.length > 4 ? look[4] : -28);
            fibre.property("ADBE Transform Group").property("ADBE Scale").setValue([160, 160]);
            // comp B: the pooled rim = wash - blurred wash
            var b = app.project.items.addComp(name + "_rim", 512, 512, 1, dur, 12);
            b.layers.add(a);
            var soft = b.layers.add(a);
            fx(soft, "ADBE Gaussian Blur 2").property("ADBE Gaussian Blur 2-0001").setValue(11);
            soft.blendingMode = BlendingMode.SUBTRACT;
            var outDir = new Folder(ROOT + "/out/" + name); if (!outDir.exists) outDir.create();
            var comps = [[a, "wash"], [b, "rim"]];
            for (var c = 0; c < comps.length; c++) {
                var item = app.project.renderQueue.items.add(comps[c][0]);
                var om = item.outputModule(1), t = om.templates, pick = null;
                for (var k = 0; k < t.length; k++) if (t[k].indexOf("TIFF") >= 0) pick = t[k];
                if (pick == null) throw new Error("no TIFF sequence output template");
                om.applyTemplate(pick);
                om.file = new File(outDir.fsName + "/" + comps[c][1] + "_[####].tif");
            }
            count++;
        }
        app.project.renderQueue.render();
        log.writeln("ok " + count + " presets, After Effects " + app.version);
    } catch (e) { log.writeln("error " + e.toString() + " line " + e.line); }
    log.close();
    app.project.close(CloseOptions.DO_NOT_SAVE_CHANGES);
    app.newProject();      // After Effects is left running (the user opened it)
})();
