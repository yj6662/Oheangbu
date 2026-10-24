// NB20: probe the property matchNames of Cell Pattern / Minimax / Matte Choker on this (Korean) After Effects. Refuses an open project, leaves AE running.
(function () {
    var log = new File("C:/Users/yj666/Oheangbu/Art/SpellVFX308/Vol2Vfx/nb20/src/ae/probe.log"); log.parent.create(); log.open("w");
    try {
        if (app.project != null && (app.project.file != null || app.project.numItems > 0)) { log.writeln("abort: a project is open (" + app.project.numItems + " items)"); log.close(); return; }
        app.beginSuppressDialogs();
        app.newProject();
        var c = app.project.items.addComp("probe", 256, 256, 1, 1, 30);
        var l = c.layers.addSolid([1, 1, 1], "s", 256, 256, 1);
        var names = ["ADBE Cell Pattern", "ADBE Minimax", "ADBE Matte Choker", "ADBE Simple Choker", "ADBE Box Blur2"];
        for (var n = 0; n < names.length; n++) {
            try {
                var e = l.property("ADBE Effect Parade").addProperty(names[n]);
                log.writeln("EFFECT " + names[n] + " = " + e.name);
                for (var i = 1; i <= e.numProperties; i++) log.writeln("  " + i + " " + e.property(i).matchName + " | " + e.property(i).name + " | " + (function (p) { try { return p.value; } catch (x) { return "-"; } })(e.property(i)));
            } catch (x) { log.writeln("NO " + names[n] + " " + x.toString()); }
        }
    } catch (e) { log.writeln("error " + e.toString()); }
    log.close();
    app.project.close(CloseOptions.DO_NOT_SAVE_CHANGES);
    app.newProject();
})();
