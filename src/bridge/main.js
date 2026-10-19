// KnobRing Bridge: a tiny local HTTP server that passes knob turns to ExtendScript.
(function () {
  var statusEl = document.getElementById('s');
  function status(t) { if (statusEl) statusEl.textContent = t; }
  var cep = window.__adobe_cep__;
  var host = 'PPRO';
  try { host = JSON.parse(cep.getHostEnvironment()).appName; } catch (e) {}
  var ext = '';
  try { ext = cep.getExtensionId() || ''; } catch (e) {}
  // KnobRing uses 47321 (Premiere) / 47322 (After Effects); KnobRing for Premiere has its own copy on 47341
  var PORT = ext.indexOf('knobringpremiere') >= 0 ? 47341 : (host === 'AEFT' ? 47322 : 47321);
  var OPS = { get: 1, nudge: 1, scrub: 1 };
  var PROPS = { posx: 1, posy: 1, scale: 1, rotation: 1, opacity: 1, playhead: 1 };

  var http;
  try { http = require('http'); } catch (e) { status('Node.js is not available in this panel.'); return; }

  var server = http.createServer(function (req, res) {
    var body = '';
    req.on('data', function (d) { body += d; if (body.length > 4096) req.destroy(); });
    req.on('end', function () {
      function reply(obj) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(typeof obj === 'string' ? obj : JSON.stringify(obj));
      }
      var m;
      try { m = JSON.parse(body || '{}'); } catch (e) { return reply({ ok: false, msg: 'bad request' }); }
      if (req.url === '/ping') return reply({ ok: true, host: host });
      if (!OPS[m.op] || !PROPS[m.prop]) return reply({ ok: false, msg: 'unknown op' });
      var d = Number(m.delta) || 0;
      if (!isFinite(d) || Math.abs(d) > 100000) d = 0;
      var call = 'KnobRing.run(' + JSON.stringify(m.op) + ',' + JSON.stringify(m.prop) + ',' + d + ')';
      cep.evalScript(call, function (r) {
        if (!r || r === 'EvalScript error.') r = JSON.stringify({ ok: false, msg: 'Script error' });
        reply(r);
      });
    });
  });
  server.on('error', function (e) { status(e.code === 'EADDRINUSE' ? 'Already running.' : 'Error: ' + e.message); });
  server.listen(PORT, '127.0.0.1', function () { status('Connected, listening for KnobRing on port ' + PORT + '.'); });
})();
