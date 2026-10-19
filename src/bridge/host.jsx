// KnobRing Bridge - ExtendScript side (Premiere Pro and After Effects).
// KnobRing.run(op, prop, delta) -> JSON text {"ok":true,"value":"12.5 deg","msg":""}
// op: "get" reads the value of the first selected clip/layer, "nudge" adds delta to every selected one.
// prop: posx, posy (pixels), scale (%), rotation (degrees), opacity (%).

var KnobRing = (function () {

    function esc(s) { return String(s).replace(/\\/g, "\\\\").replace(/"/g, "\\\"").replace(/[\r\n]/g, " "); }
    function out(ok, value, msg) {
        return '{"ok":' + (ok ? 'true' : 'false') + ',"value":"' + esc(value || "") + '","msg":"' + esc(msg || "") + '"}';
    }
    function fmt(v, unit) {
        var r = Math.round(v * 10) / 10;
        var s = String(r);
        if (s.indexOf(".") < 0) s += ".0";
        return s + unit;
    }
    function clamp(v, lo, hi) { return v < lo ? lo : (v > hi ? hi : v); }
    var UNIT = { posx: "", posy: "", scale: "%", rotation: "\u00B0", opacity: "%", playhead: "" };

    // ================================================================ Premiere Pro
    function pprProp(comp, names, index) {
        var ps = comp.properties;
        for (var i = 0; i < ps.numItems; i++) {
            for (var n = 0; n < names.length; n++)
                if (ps[i].displayName === names[n]) return ps[i];
        }
        return index < ps.numItems ? ps[index] : null;
    }
    function pprComponent(item, matchName, displayName) {
        var cs = item.components;
        for (var i = 0; i < cs.numItems; i++)
            if (cs[i].matchName === matchName || cs[i].displayName === displayName) return cs[i];
        return null;
    }
    function pprKeyTime(seq, item) {
        var t = new Time();
        t.seconds = seq.getPlayerPosition().seconds - item.start.seconds + item.inPoint.seconds;
        return t;
    }
    function pprGet(p, seq, item) {
        if (p.isTimeVarying()) return p.getValueAtTime(pprKeyTime(seq, item));
        return p.getValue();
    }
    function pprSet(p, seq, item, v) {
        if (p.isTimeVarying()) {
            var t = pprKeyTime(seq, item);
            p.addKey(t);
            p.setValueAtKey(t, v, true);
        } else p.setValue(v, true);
    }

    function premiere(op, prop, delta) {
        var seq = app.project.activeSequence;
        if (!seq) return out(false, "", "No sequence open");
        var sel = seq.getSelection();
        var w = Number(seq.frameSizeHorizontal) || 1920, h = Number(seq.frameSizeVertical) || 1080;
        var first = null, count = 0;
        for (var i = 0; i < sel.length; i++) {
            var item = sel[i];
            if (item.mediaType === "Audio") continue;
            var shown;
            if (prop === "opacity") {
                var oc = pprComponent(item, "AE.ADBE Opacity", "Opacity");
                if (!oc) continue;
                var op_ = pprProp(oc, ["Opacity"], 0);
                var ov = Number(pprGet(op_, seq, item));
                if (op === "nudge") { ov = clamp(ov + delta, 0, 100); pprSet(op_, seq, item, ov); }
                shown = fmt(ov, "%");
            } else {
                var mc = pprComponent(item, "AE.ADBE Motion", "Motion");
                if (!mc) continue;
                if (prop === "posx" || prop === "posy") {
                    var pp = pprProp(mc, ["Position"], 0);
                    var pv = pprGet(pp, seq, item);
                    var x = Number(pv[0]), y = Number(pv[1]);
                    if (op === "nudge") {
                        if (prop === "posx") x += delta / w; else y += delta / h;
                        pprSet(pp, seq, item, [x, y]);
                    }
                    shown = fmt(prop === "posx" ? x * w : y * h, "");
                } else if (prop === "scale") {
                    var sp = pprProp(mc, ["Scale", "Scale Height"], 1);
                    var uni = pprProp(mc, ["Uniform Scale"], 3);
                    var sw = pprProp(mc, ["Scale Width"], 2);
                    var sv = Number(pprGet(sp, seq, item));
                    var uniform = true;
                    try { uniform = !!uni.getValue(); } catch (e) {}
                    if (op === "nudge") {
                        sv = clamp(sv + delta, 0, 10000);
                        pprSet(sp, seq, item, sv);
                        if (!uniform && sw) {
                            var wv = clamp(Number(pprGet(sw, seq, item)) + delta, 0, 10000);
                            pprSet(sw, seq, item, wv);
                        }
                    }
                    shown = fmt(sv, "%");
                } else if (prop === "rotation") {
                    var rp = pprProp(mc, ["Rotation"], 4);
                    var rv = Number(pprGet(rp, seq, item));
                    if (op === "nudge") { rv = rv + delta; pprSet(rp, seq, item, rv); }
                    shown = fmt(rv, "\u00B0");
                }
            }
            if (shown === undefined) continue;
            count++;
            if (first === null) first = shown;
            if (op === "get") break;
        }
        if (count === 0) return out(false, "", sel.length ? "Select a video clip" : "Select a clip first");
        return out(true, first, count > 1 ? count + " clips" : "");
    }

    // moves the playhead by a number of frames (Premiere Pro)
    function scrub(frames) {
        var seq = app.project.activeSequence;
        if (!seq) return out(false, "", "No sequence open");
        var tpf = Number(seq.timebase);
        var pos = Number(seq.getPlayerPosition().ticks);
        var np = pos + Math.round(frames) * tpf;
        var end = Number(seq.end);
        if (np < 0) np = 0;
        if (end > 0 && np > end) np = end;
        seq.setPlayerPosition(String(np));
        return out(true, String(np), "");
    }

    // ================================================================ After Effects
    function aeSet(p, comp, v) {
        if (p.numKeys > 0) p.setValueAtTime(comp.time, v); else p.setValue(v);
    }
    function aeGet(p, comp) { return p.numKeys > 0 ? p.valueAtTime(comp.time, false) : p.value; }

    function afterEffects(op, prop, delta) {
        var comp = app.project.activeItem;
        if (!comp || !(comp instanceof CompItem)) return out(false, "", "Open a composition");
        var layers = comp.selectedLayers;
        if (!layers || layers.length === 0) return out(false, "", "Select a layer first");
        var first = null, count = 0;
        if (op === "nudge") app.beginUndoGroup("KnobRing " + prop);
        try {
            for (var i = 0; i < layers.length; i++) {
                var tr = layers[i].property("ADBE Transform Group");
                if (!tr) continue;
                var shown;
                if (prop === "posx" || prop === "posy") {
                    var pos = tr.property("ADBE Position");
                    if (pos.dimensionsSeparated) {
                        var dp = tr.property(prop === "posx" ? "ADBE Position_0" : "ADBE Position_1");
                        var dv = aeGet(dp, comp);
                        if (op === "nudge") { dv += delta; aeSet(dp, comp, dv); }
                        shown = fmt(dv, "");
                    } else {
                        var v = aeGet(pos, comp);
                        var nv = [];
                        for (var k = 0; k < v.length; k++) nv.push(v[k]);
                        if (op === "nudge") { nv[prop === "posx" ? 0 : 1] += delta; aeSet(pos, comp, nv); }
                        shown = fmt(nv[prop === "posx" ? 0 : 1], "");
                    }
                } else if (prop === "scale") {
                    var sp = tr.property("ADBE Scale");
                    var s = aeGet(sp, comp);
                    var ns = [];
                    for (var j = 0; j < s.length; j++) ns.push(s[j]);
                    if (op === "nudge") { ns[0] += delta; ns[1] += delta; aeSet(sp, comp, ns); }
                    shown = fmt(ns[1], "%");
                } else if (prop === "rotation") {
                    var rp = tr.property("ADBE Rotate Z");
                    var rv = aeGet(rp, comp);
                    if (op === "nudge") { rv += delta; aeSet(rp, comp, rv); }
                    shown = fmt(rv, "\u00B0");
                } else if (prop === "opacity") {
                    var opp = tr.property("ADBE Opacity");
                    var ov = aeGet(opp, comp);
                    if (op === "nudge") { ov = clamp(ov + delta, 0, 100); aeSet(opp, comp, ov); }
                    shown = fmt(ov, "%");
                }
                if (shown === undefined) continue;
                count++;
                if (first === null) first = shown;
                if (op === "get") break;
            }
        } finally {
            if (op === "nudge") app.endUndoGroup();
        }
        if (count === 0) return out(false, "", "Nothing to change on this layer");
        return out(true, first, count > 1 ? count + " layers" : "");
    }

    return {
        run: function (op, prop, delta) {
            try {
                if (!UNIT.hasOwnProperty(prop)) return out(false, "", "Unknown property");
                delta = Number(delta) || 0;
                if (op === "scrub") {
                    if (BridgeTalk.appName === "aftereffects") return out(false, "", "Scrubbing is for Premiere Pro");
                    return scrub(delta);
                }
                if (BridgeTalk.appName === "aftereffects") return afterEffects(op, prop, delta);
                return premiere(op, prop, delta);
            } catch (e) {
                return out(false, "", String(e.message || e));
            }
        }
    };
})();
