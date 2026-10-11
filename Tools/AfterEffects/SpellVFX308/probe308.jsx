// #308 probe: can After Effects be driven without the GUI? Writes a small log and a project file.
(function () {
    var out = new Folder("C:/Users/yj666/Oheangbu/Art/SpellVFX308/AE");
    var log = new File(out.fsName + "/probe.log");
    try {
        app.beginSuppressDialogs();
        var p = app.newProject();
        var c = app.project.items.addComp("probe", 256, 256, 1, 1, 12);
        c.layers.addSolid([1, 1, 1], "s", 256, 256, 1);
        app.project.save(new File(out.fsName + "/probe.aep"));
        log.open("w"); log.write("ok version " + app.version + " scriptsWrite " + app.preferences.getPrefAsLong("Main Pref Section", "Pref_SCRIPTING_FILE_NETWORK_SECURITY")); log.close();
    } catch (e) { try { log.open("w"); log.write("error " + e.toString()); log.close(); } catch (e2) {} }
    app.quit();
})();
