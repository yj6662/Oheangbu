(function () {
    var out = new Folder("C:/Users/yj666/Oheangbu/Art/SpellVFX308/AE");
    var log = new File(out.fsName + "/probe2.log"); log.open("w");
    try {
        app.beginSuppressDialogs();
        app.newProject();
        var c = app.project.items.addComp("probe", 256, 256, 1, 1, 12);
        var l = c.layers.addSolid([1, 1, 1], "s", 256, 256, 1);
        var names = ["ADBE Turbulent Displace", "ADBE Gaussian Blur 2", "ADBE Fractal Noise", "ADBE Roughen Edges", "ADBE Easy Levels2"];
        for (var n = 0; n < names.length; n++) {
            try {
                var e = l.property("ADBE Effect Parade").addProperty(names[n]);
                log.writeln("EFFECT " + names[n] + " = " + e.name);
                for (var i = 1; i <= e.numProperties; i++) log.writeln("  " + i + " " + e.property(i).matchName + " | " + e.property(i).name);
            } catch (x) { log.writeln("NO " + names[n] + " " + x.toString()); }
        }
        var rq = app.project.renderQueue.items.add(c);
        var t = rq.outputModule(1).templates; log.writeln("OM TEMPLATES " + t.join(" ; "));
        log.writeln("RS TEMPLATES " + rq.templates.join(" ; "));
    } catch (e) { log.writeln("error " + e.toString()); }
    log.close();
    app.project.close(CloseOptions.DO_NOT_SAVE_CHANGES);
    app.quit();
})();
