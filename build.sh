#!/bin/sh
set -e
cd "$(dirname "$0")"
mkdir -p out
mcs -target:winexe -optimize+ -platform:anycpu -sdk:4.8 \
  -out:out/KnobRing.exe -win32icon:src/app.ico \
  -resource:src/rings.default.json,rings.default.json \
  -resource:src/bridge/manifest.xml,bridge.manifest.xml -resource:src/bridge/index.html,bridge.index.html \
  -resource:src/bridge/main.js,bridge.main.js -resource:src/bridge/host.jsx,bridge.host.jsx \
  -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll \
  src/*.cs
