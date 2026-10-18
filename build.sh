#!/bin/sh
# Builds KnobRing.exe with Mono's C# compiler (Linux/macOS). On Windows use build.bat.
set -e
cd "$(dirname "$0")"
mkdir -p out
mcs -target:winexe -optimize+ -platform:anycpu -sdk:4.8 \
  -out:out/KnobRing.exe -win32icon:src/app.ico \
  -resource:src/rings.default.json,rings.default.json \
  -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll \
  src/*.cs
echo "Built out/KnobRing.exe"
