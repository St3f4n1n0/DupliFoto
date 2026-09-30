#!/bin/sh
# Sostituto di mt.exe per compilare da Linux/macOS (vedi Directory.Build.props).
# Windows App SDK lo invoca come:  mt.exe -nologo -manifest <ingresso> -out:<uscita>
# Con un solo manifest in ingresso la "fusione" equivale a una copia.
in=""; out=""
while [ $# -gt 0 ]; do
  case "$1" in
    -manifest) shift; in="$1" ;;
    -out:*) out="${1#-out:}" ;;
  esac
  shift
done
if [ -z "$in" ] || [ -z "$out" ]; then
  echo "mt-linux.sh: servono -manifest <file> e -out:<file>" >&2
  exit 1
fi
cp "$in" "$out"
