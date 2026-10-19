// KnobRing panel for Premiere Pro: edits the Premiere wheel, scrubbing and transform steps in %APPDATA%\KnobRing\rings.json.
// The KnobRing app watches that file and picks up every change straight away.
(function () {
  'use strict';
  var ICONS = window.KR_ICONS || {};
  var PREMIERE = 'adobe premiere pro.exe';
  var TYPES = [['keys', 'Shortcut'], ['transform', 'Transforms'], ['text', 'Type text'], ['media', 'Media'], ['run', 'Open'], ['ring', 'Sub-ring']];
  var PROPS = [['posx', 'Position X', [5, 1, 25]], ['posy', 'Position Y', [5, 1, 25]], ['scale', 'Scale', [1, 0.1, 10]],
               ['rotation', 'Rotation', [1, 0.1, 10]], ['opacity', 'Opacity', [1, 0.1, 10]]];
  var PICK = ['play', 'pause', 'cut', 'trim', 'undo', 'redo', 'copy', 'paste', 'delete', 'add', 'remove', 'transform', 'movex', 'movey',
              'scale', 'rotate', 'opacity', 'crop', 'zoomin', 'zoomout', 'layers', 'upload', 'download', 'save', 'video', 'music', 'mic',
              'photo', 'camera', 'color', 'brush', 'pen', 'font', 'flag', 'tag', 'pin', 'star', 'heart', 'check', 'close', 'prev', 'next',
              'back', 'forward', 'up', 'down', 'search', 'settings', 'brightness', 'view', 'lock', 'link', 'folder', 'document', 'list', 'ring', 'more'];

  // ------------------------------------------------------------ file access (Node in CEP; demo data in a plain browser)
  var fs = null, path = null, cp = null, FILE = null;
  try { fs = require('fs'); path = require('path'); cp = require('child_process'); FILE = path.join(process.env.APPDATA, 'KnobRing', 'rings.json'); } catch (e) { fs = null; }

  var DEMO = {
    settings: { accent: '#FF8A1F' },
    premiere: { knobScrub: true, wheelScrub: true, invert: false, scrubFrames: { slow: 1, normal: 5, fast: 25 }, transformSteps: {} },
    rings: [{ name: 'Premiere Pro', apps: [PREMIERE], items: [
      { label: 'Play / Pause', icon: 'play', type: 'keys', value: 'Space' }, { label: 'Add Edit (cut)', icon: 'cut', type: 'keys', value: 'Ctrl+K' },
      { label: 'Ripple Trim End', icon: 'W', type: 'keys', value: 'W' }, { label: 'Mark Out', icon: 'O', type: 'keys', value: 'O' },
      { label: 'Export', icon: 'upload', type: 'keys', value: 'Ctrl+M' }, { label: 'Undo', icon: 'undo', type: 'keys', value: 'Ctrl+Z' },
      { label: 'Mark In', icon: 'I', type: 'keys', value: 'I' }, { label: 'Ripple Trim Start', icon: 'Q', type: 'keys', value: 'Q' },
      { label: 'Transforms', icon: 'transform', type: 'transform', value: '' }] }]
  };

  var cfg = null, ring = null, sel = 0, lastWrite = 0, saveTimer = null, fileError = '';

  function readCfg() {
    if (!fs) return JSON.parse(JSON.stringify(DEMO));
    try { return JSON.parse(fs.readFileSync(FILE, 'utf8')); }
    catch (e) { fileError = e.code === 'ENOENT' ? 'Start the KnobRing app once to create its settings.' : 'Couldn\'t read rings.json: ' + e.message; return null; }
  }

  function premiereRing(c) {
    c.rings = c.rings || [];
    for (var i = 0; i < c.rings.length; i++) {
      var apps = (c.rings[i].apps || []).map(function (a) { return String(a).toLowerCase(); });
      if (apps.indexOf(PREMIERE) >= 0) return c.rings[i];
    }
    var r = { name: 'Premiere Pro', apps: [PREMIERE], items: [{ label: 'Transforms', icon: 'transform', type: 'transform', value: '' }] };
    c.rings.push(r);
    return r;
  }

  function prem(c) {
    c.premiere = c.premiere || {};
    var p = c.premiere;
    if (p.knobScrub === undefined) p.knobScrub = true;
    if (p.wheelScrub === undefined) p.wheelScrub = true;
    if (p.invert === undefined) p.invert = false;
    p.scrubFrames = p.scrubFrames || {};
    if (!(p.scrubFrames.slow > 0)) p.scrubFrames.slow = 1;
    if (!(p.scrubFrames.normal > 0)) p.scrubFrames.normal = 5;
    if (!(p.scrubFrames.fast > 0)) p.scrubFrames.fast = 25;
    p.transformSteps = p.transformSteps || {};
    return p;
  }

  function load() {
    var c = readCfg();
    if (!c) { cfg = null; render(); return; }
    cfg = c; fileError = '';
    ring = premiereRing(cfg);
    prem(cfg);
    if (sel >= ring.items.length) sel = Math.max(0, ring.items.length - 1);
    var acc = (cfg.settings && cfg.settings.accent) || '#2F6BFF';
    document.documentElement.style.setProperty('--accent', acc);
    render();
  }

  // Merge our part into the newest file so edits made in the KnobRing app aren't lost.
  function save() {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(function () {
      if (!fs) { toast('Saved (demo)'); return; }
      var fresh = readCfg();
      if (!fresh) { toast(fileError); return; }
      var r = premiereRing(fresh);
      r.items = ring.items.map(function (it) {
        var o = { label: it.label || '', icon: it.icon || '', type: it.type || 'keys', value: it.value || '' };
        if (it.args) o.args = it.args;
        return o;
      });
      fresh.premiere = cfg.premiere;
      try {
        fs.writeFileSync(FILE, JSON.stringify(fresh, null, 2), 'utf8');
        lastWrite = Date.now();
        cfg = fresh; ring = r;
        toast('Saved');
      } catch (e) { toast('Couldn\'t save: ' + e.message); }
    }, 350);
  }

  function watch() {
    if (!fs) return;
    fs.watchFile(FILE, { interval: 1000 }, function () {
      if (Date.now() - lastWrite < 1500) return;          // our own save
      var a = document.activeElement;
      if (a && (a.tagName === 'INPUT' || a.tagName === 'SELECT')) return;   // don't yank a field while typing
      load();
    });
  }

  // ------------------------------------------------------------ helpers
  var $ = function (id) { return document.getElementById(id); };
  var toastT = null;
  function toast(t) {
    var el = $('toast'); el.textContent = t; el.classList.add('show');
    clearTimeout(toastT); toastT = setTimeout(function () { el.classList.remove('show'); }, 1400);
  }
  function esc(s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }
  function iconSvg(name, color, size, x, y, sw) {
    var d = ICONS[(name || '').toLowerCase()];
    if (!d) return null;
    var s = size / 24;
    return '<g transform="translate(' + (x - size / 2) + ',' + (y - size / 2) + ') scale(' + s + ')"><path d="' + d +
      '" fill="none" stroke="' + color + '" stroke-width="' + (sw || 1.75) + '" stroke-linecap="round" stroke-linejoin="round"/></g>';
  }
  function slotGlyph(it, color, x, y, r) {
    var name = (it.icon || '').trim();
    if (!name) name = it.type === 'transform' ? 'transform' : it.type === 'ring' ? 'more' : (it.label || '?').charAt(0).toUpperCase();
    var svg = iconSvg(name, color, r * 1.05, x, y);
    if (svg) return svg;
    var t = name.length > 3 ? name.slice(0, 3) : name;
    var fs_ = t.length === 1 ? r * 0.9 : t.length === 2 ? r * 0.68 : r * 0.52;
    return '<text x="' + x + '" y="' + (y + fs_ * 0.35) + '" text-anchor="middle" font-family="Segoe UI" font-size="' + fs_ + '" fill="' + color + '">' + esc(t) + '</text>';
  }
  function accent() { return getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#2F6BFF'; }

  // ------------------------------------------------------------ render
  function render() {
    renderLogo();
    $('err').classList.toggle('hidden', !!cfg);
    $('err').textContent = cfg ? '' : fileError;
    if (!cfg) return;
    renderRing();
    renderEditor();
    renderScrub();
    renderSteps();
  }

  function renderLogo() {
    var a = accent(), h = '';
    for (var i = 0; i < 8; i++) {
      var ang = -Math.PI / 2 + i * Math.PI / 4, x = 16 + Math.cos(ang) * 11, y = 16 + Math.sin(ang) * 11;
      h += '<circle cx="' + x + '" cy="' + y + '" r="' + (i === 0 ? 3.6 : 2.6) + '" fill="' + (i === 0 ? a : '#e6e6ea') + '"/>';
    }
    $('logo').innerHTML = h + '<circle cx="16" cy="16" r="3.4" fill="#e6e6ea"/>';
  }

  function renderRing() {
    var items = ring.items, n = Math.max(items.length, 1), a = accent();
    var SR = 25, R = Math.max(98, (SR + 6) / Math.sin(Math.PI / n)), C = 150;
    var scale = R > 112 ? 112 / R : 1;
    var h = '<g transform="translate(' + C + ',' + C + ') scale(' + scale + ')">';
    h += '<circle r="' + R + '" fill="none" stroke="#2c2c2c" stroke-width="2"/>';
    for (var i = 0; i < items.length; i++) {
      var ang = -Math.PI / 2 + i * 2 * Math.PI / n, x = Math.cos(ang) * R, y = Math.sin(ang) * R, on = i === sel;
      var r = on ? SR * 1.16 : SR;
      h += '<g class="slot" data-i="' + i + '">';
      if (on) h += '<circle cx="' + x + '" cy="' + y + '" r="' + (r + 6) + '" fill="' + a + '" opacity=".22"/>';
      h += '<circle cx="' + x + '" cy="' + y + '" r="' + r + '" fill="' + (on ? a : '#f4f4f6') + '"' + (on ? ' stroke="#fff" stroke-width="2"' : '') + '/>';
      h += slotGlyph(items[i], on ? '#fff' : '#1c1c20', x, y, r);
      h += '</g>';
    }
    var cur = items[sel] || { label: '' };
    h += '<circle r="44" fill="#151517" stroke="#2c2c2c"/>';
    h += '<text y="-12" text-anchor="middle" font-size="8.5" letter-spacing="1.2" fill="#8a8a8a">PREMIERE PRO</text>';
    h += '<text y="8" text-anchor="middle" font-size="' + ((cur.label || '').length > 13 ? 10.5 : 12.5) + '" font-weight="700" fill="#fff">' + esc(cur.label || '') + '</text>';
    h += '<text y="24" text-anchor="middle" font-size="8.5" fill="#777">' + (sel + 1) + ' of ' + items.length + '</text>';
    h += '</g>';
    var svg = $('ring');
    svg.innerHTML = h;
    Array.prototype.forEach.call(svg.querySelectorAll('.slot'), function (g) {
      g.addEventListener('click', function () { sel = +g.getAttribute('data-i'); stopRec(); render(); });
    });
    $('mvL').disabled = sel <= 0; $('mvR').disabled = sel >= items.length - 1;
    $('add').disabled = items.length >= 12; $('del').disabled = items.length <= 1;
  }

  function renderEditor() {
    var it = ring.items[sel];
    if (!it) return;
    $('edTitle').textContent = 'Slot ' + (sel + 1);
    if (document.activeElement !== $('name')) $('name').value = it.label || '';
    var tb = '';
    TYPES.forEach(function (t) { tb += '<button data-t="' + t[0] + '" class="' + (it.type === t[0] ? 'on' : '') + '">' + t[1] + '</button>'; });
    $('types').innerHTML = tb;
    Array.prototype.forEach.call($('types').children, function (b) {
      b.addEventListener('click', function () {
        var t = b.getAttribute('data-t');
        if (it.type === t) return;
        it.type = t; it.value = t === 'media' ? 'playpause' : ''; delete it.args;
        if (t === 'transform' && (!it.icon || it.icon === 'add')) it.icon = 'transform';
        if (t === 'transform' && (!it.label || it.label === 'New action')) it.label = 'Transforms';
        stopRec(); save(); render();
      });
    });
    ['keys', 'transform', 'text', 'media', 'run', 'ring'].forEach(function (t) { $('v-' + t).classList.toggle('hidden', it.type !== t); });
    var fields = { keys: 'keys', text: 'text', run: 'run', ring: 'sub' };
    Object.keys(fields).forEach(function (t) { var el = $(fields[t]); if (document.activeElement !== el) el.value = it.type === t ? (it.value || '') : ''; });
    if (it.type === 'media') $('media').value = it.value || 'playpause';
    checkKeys();
    var letters = ICONS[(it.icon || '').toLowerCase()] ? '' : (it.icon || '');
    if (document.activeElement !== $('letters')) $('letters').value = letters;
    var ib = '';
    PICK.forEach(function (k) {
      if (!ICONS[k]) return;
      var on = (it.icon || '').toLowerCase() === k;
      ib += '<button data-k="' + k + '" class="' + (on ? 'on' : '') + '" title="' + k + '"><svg viewBox="0 0 24 24"><path d="' + ICONS[k] +
        '" fill="none" stroke="' + (on ? '#fff' : '#cfcfcf') + '" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg></button>';
    });
    $('icons').innerHTML = ib;
    Array.prototype.forEach.call($('icons').children, function (b) {
      b.addEventListener('click', function () { it.icon = b.getAttribute('data-k'); save(); render(); });
    });
  }

  var KEY_RE = /^\s*((ctrl|control|shift|alt|win)\s*\+\s*)*([a-z0-9]|f([1-9]|1[0-9]|2[0-4])|space|enter|return|tab|esc|escape|backspace|delete|del|insert|ins|home|end|pgup|pageup|pgdn|pagedown|left|up|right|down|plus|minus|comma|period|dot|num[0-9]|[\[\]\\;'`,.\/=\-+])\s*$/i;
  function checkKeys() {
    var it = ring.items[sel];
    var bad = false;
    if (it && it.type === 'keys' && it.value) it.value.split(',').forEach(function (c) { if (c.trim() && !KEY_RE.test(c.trim())) bad = true; });
    var h = $('keysHint');
    h.className = bad ? 'hint warn' : 'hint';
    h.textContent = recording ? 'Press the shortcut now… (Esc to cancel)' : bad ? 'KnobRing may not understand this shortcut. Use names like Ctrl+Shift+E, Space, F5, Left.' : 'Type it, or press Record and then the keys.';
  }

  function renderScrub() {
    var p = cfg.premiere;
    $('knobScrub').checked = !!p.knobScrub; $('wheelScrub').checked = !!p.wheelScrub; $('invert').checked = !!p.invert;
    ['slow', 'normal', 'fast'].forEach(function (k) { if (document.activeElement !== $(k)) $(k).value = p.scrubFrames[k]; });
  }

  function renderSteps() {
    var st = cfg.premiere.transformSteps, h = '';
    PROPS.forEach(function (p) {
      var v = st[p[0]] && st[p[0]].length === 3 ? st[p[0]] : p[2];
      h += '<tr><td>' + p[1] + '</td>';
      for (var i = 0; i < 3; i++) h += '<td><input type="number" min="0.01" step="any" data-p="' + p[0] + '" data-i="' + i + '" value="' + v[i] + '"></td>';
      h += '</tr>';
    });
    var tb = $('steps');
    if (tb.contains(document.activeElement)) return;
    tb.innerHTML = h;
    Array.prototype.forEach.call(tb.querySelectorAll('input'), function (inp) {
      inp.addEventListener('change', function () {
        var id = inp.getAttribute('data-p'), i = +inp.getAttribute('data-i'), val = parseFloat(inp.value);
        if (!(val > 0)) { renderSteps(); return; }
        var def = PROPS.filter(function (p) { return p[0] === id; })[0][2];
        var cur = (st[id] && st[id].length === 3 ? st[id] : def).slice();
        cur[i] = val; st[id] = cur; save();
      });
    });
  }

  // ------------------------------------------------------------ shortcut recording
  var recording = false;
  var KEYNAME = { ' ': 'Space', 'Enter': 'Enter', 'Tab': 'Tab', 'Backspace': 'Backspace', 'Delete': 'Delete', 'Insert': 'Insert', 'Home': 'Home',
                  'End': 'End', 'PageUp': 'PgUp', 'PageDown': 'PgDn', 'ArrowLeft': 'Left', 'ArrowRight': 'Right', 'ArrowUp': 'Up', 'ArrowDown': 'Down' };
  function setKeyInterest(on) {
    try {
      var list = [];
      if (on) {
        var codes = [8, 9, 13, 32, 33, 34, 35, 36, 37, 38, 39, 40, 45, 46, 186, 187, 188, 189, 190, 191, 192, 219, 220, 221, 222];
        for (var c = 48; c <= 57; c++) codes.push(c);
        for (c = 65; c <= 90; c++) codes.push(c);
        for (c = 112; c <= 123; c++) codes.push(c);
        codes.forEach(function (k) {
          for (var m = 0; m < 8; m++) list.push({ keyCode: k, ctrlKey: !!(m & 1), shiftKey: !!(m & 2), altKey: !!(m & 4) });
        });
      }
      window.__adobe_cep__.registerKeyEventsInterest(JSON.stringify(list));
    } catch (e) {}
  }
  function startRec() { recording = true; $('rec').classList.add('rec'); $('rec').textContent = 'Stop'; setKeyInterest(true); $('rec').focus(); checkKeys(); }
  function stopRec() { if (!recording) return; recording = false; $('rec').classList.remove('rec'); $('rec').textContent = 'Record'; setKeyInterest(false); checkKeys(); }
  document.addEventListener('keydown', function (e) {
    if (!recording) return;
    if (['Control', 'Shift', 'Alt', 'Meta'].indexOf(e.key) >= 0) return;
    e.preventDefault(); e.stopPropagation();
    if (e.key === 'Escape' && !e.ctrlKey && !e.shiftKey && !e.altKey) { stopRec(); return; }
    var k = KEYNAME[e.key] || (/^F\d{1,2}$/.test(e.key) ? e.key : null);
    if (!k) {
      if (e.code && /^Key[A-Z]$/.test(e.code)) k = e.code.slice(3);
      else if (e.code && /^Digit\d$/.test(e.code)) k = e.code.slice(5);
      else if (e.key.length === 1) k = e.key.toUpperCase();
    }
    if (!k) return;
    var parts = [];
    if (e.ctrlKey) parts.push('Ctrl'); if (e.shiftKey) parts.push('Shift'); if (e.altKey) parts.push('Alt');
    parts.push(k);
    var it = ring.items[sel];
    it.value = parts.join('+');
    $('keys').value = it.value;
    stopRec(); save(); render();
  }, true);

  // ------------------------------------------------------------ wiring
  function bindInput(id, apply) {
    $(id).addEventListener('input', function () { if (!cfg) return; apply(this.value); save(); renderRing(); checkKeys(); });
  }
  bindInput('name', function (v) { ring.items[sel].label = v; });
  bindInput('keys', function (v) { ring.items[sel].value = v; });
  bindInput('text', function (v) { ring.items[sel].value = v; });
  bindInput('run', function (v) { ring.items[sel].value = v; });
  bindInput('sub', function (v) { ring.items[sel].value = v; });
  bindInput('letters', function (v) { if (v.trim()) ring.items[sel].icon = v.trim(); });
  $('media').addEventListener('change', function () { ring.items[sel].value = this.value; save(); });
  $('rec').addEventListener('click', function () { recording ? stopRec() : startRec(); });

  $('add').addEventListener('click', function () {
    if (ring.items.length >= 12) return;
    ring.items.splice(sel + 1, 0, { label: 'New action', icon: 'add', type: 'keys', value: '' });
    sel++; save(); render(); $('name').focus(); $('name').select();
  });
  $('del').addEventListener('click', function () {
    if (ring.items.length <= 1) return;
    ring.items.splice(sel, 1); sel = Math.min(sel, ring.items.length - 1); save(); render();
  });
  function move(d) {
    var j = sel + d; if (j < 0 || j >= ring.items.length) return;
    var t = ring.items[sel]; ring.items[sel] = ring.items[j]; ring.items[j] = t; sel = j; save(); render();
  }
  $('mvL').addEventListener('click', function () { move(-1); });
  $('mvR').addEventListener('click', function () { move(1); });

  ['knobScrub', 'wheelScrub', 'invert'].forEach(function (k) {
    $(k).addEventListener('change', function () { cfg.premiere[k] = this.checked; save(); });
  });
  ['slow', 'normal', 'fast'].forEach(function (k) {
    $(k).addEventListener('change', function () {
      var v = Math.round(parseFloat(this.value));
      if (!(v >= 1)) { this.value = cfg.premiere.scrubFrames[k]; return; }
      this.value = v; cfg.premiere.scrubFrames[k] = v; save();
    });
  });

  Array.prototype.forEach.call(document.querySelectorAll('.tab'), function (t) {
    t.addEventListener('click', function () {
      Array.prototype.forEach.call(document.querySelectorAll('.tab'), function (x) { x.classList.toggle('on', x === t); });
      ['wheel', 'scrub', 'transform'].forEach(function (id) { $('tab-' + id).classList.toggle('hidden', id !== t.getAttribute('data-tab')); });
      stopRec();
    });
  });

  // ------------------------------------------------------------ is the KnobRing app running?
  function checkApp() {
    if (!cp) { setApp(null); return; }
    cp.exec('tasklist /FI "IMAGENAME eq KnobRing.exe" /NH', { windowsHide: true }, function (err, out) {
      setApp(!err && /KnobRing\.exe/i.test(out || ''));
    });
  }
  function setApp(on) {
    $('appDot').className = 'dot ' + (on === null ? '' : on ? 'on' : 'off');
    $('appTxt').textContent = on === null ? 'Preview' : on ? 'KnobRing running' : 'KnobRing not running';
  }

  load();
  watch();
  checkApp();
  setInterval(checkApp, 5000);
})();
