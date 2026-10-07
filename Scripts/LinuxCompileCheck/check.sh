#!/bin/sh
# Type-checks FoxyBrowser716's C# on Linux (no Windows, no XAML compiler). See README.md.
# Usage: Scripts/LinuxCompileCheck/check.sh     (prints compiler errors; exit code 1 if there are any)
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
APP="$REPO/FoxyBrowser716"
WORK="$HERE/.work"
PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

mkdir -p "$WORK/gen"
python3 "$HERE/make_csproj.py" "$APP/FoxyBrowser716.csproj" "$APP" "$WORK/CompileCheck.csproj"

# restore first: the WinUI map is read from the restored packages
if [ ! -f "$WORK/obj/project.assets.json" ] || [ "$APP/FoxyBrowser716.csproj" -nt "$WORK/obj/project.assets.json" ]; then
  dotnet restore "$WORK/CompileCheck.csproj" -v q
fi

if [ ! -f "$WORK/winui-map.json" ] || [ "$APP/FoxyBrowser716.csproj" -nt "$WORK/winui-map.json" ]; then
  dotnet build "$HERE/WinUiMap/WinUiMap.csproj" -c Release -v q -o "$WORK/winuimap-bin" >/dev/null
  dotnet "$WORK/winuimap-bin/WinUiMap.dll" "$PACKAGES" "$WORK/winui-map.json"
fi

rm -f "$WORK"/gen/*.cs
python3 "$HERE/genstubs.py" "$APP" "$WORK/gen" "$WORK/winui-map.json"

OUT="$(dotnet msbuild "$WORK/CompileCheck.csproj" -t:Compile -nologo -v:q -clp:NoSummary 2>&1 || true)"
ERRORS="$(printf '%s\n' "$OUT" | grep -E ': error ' | sed "s#$APP/##; s# \[$WORK.*##" | sort -u || true)"
if [ -n "$ERRORS" ]; then
  printf '%s\n' "$ERRORS"
  exit 1
fi
echo "No compile errors."
