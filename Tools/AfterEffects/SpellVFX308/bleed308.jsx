// #308 spell VFX overhaul - the After Effects pass of the bleeding-ink flipbooks (SPEC-SPELL-VFX-OVERHAUL-308, 3rd pass).
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
    var log = new File(ROOT + "/bleed308.log"); log.open("w");
    // per preset: displace amount (px at 512), displace size, whether the displacement may evolve (not for loops / stills), fibre strength (%)
    var LOOK = {
        ink_splat: [16, 30, true, 30], ink_drops: [10, 26, false, 30], wood_splinter: [7, 22, true, 22], fire_tongue: [14, 26, false, 18],
        fire_burst: [18, 28, true, 18], earth_dust: [14, 40, true, 38], metal_streak: [2, 20, true, 10], water_ring: [14, 34, true, 26], water_orb: [12, 34, false, 26],
        wood_stroke: [6, 22, true, 26], fire_lick: [12, 26, false, 18], speed_line: [3, 20, false, 14], earth_heave: [12, 36, true, 36], water_crest: [9, 30, true, 24], ink_pool: [10, 34, true, 30]
    };
    function fx(layer, name) { return layer.property("ADBE Effect Parade").addProperty(name); }
    try {
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
            fibre.property("ADBE Transform Group").property("ADBE Rotate Z").setValue(-28);
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
    app.quit();
})();
