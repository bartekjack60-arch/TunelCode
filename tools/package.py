"""Package the unified AutoTunel executable, source, and documentation."""
import argparse
import hashlib
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
version = ET.parse(root / "source/AutoTunel.csproj").findtext(".//Version")
if not version:
    raise SystemExit("Missing application version")
source = root / "source/dist/AutoTunel/AutoTunel.exe"
if not source.is_file():
    raise SystemExit(f"Build first: missing {source}")
if source.with_suffix(".exe.config").exists():
    raise SystemExit("Unexpected AutoTunel.exe.config: clean the build output and rebuild")
shutil.copy2(source, root / "AutoTunel.exe")

files = [root / p for p in (
    "AutoTunel.exe", "README.md", "DEVELOPMENT.md", "INSTRUKCJA.html", "TESTY.txt",
    "THIRD-PARTY.txt", "WINDOWS-TEST.md", ".gitignore",
)]
for folder in (root / "source", root / "tools", root / "vendor"):
    files.extend(p for p in folder.rglob("*") if p.is_file()
                 and not {"bin", "obj", "dist", "__pycache__"}.intersection(p.relative_to(folder).parts))
files = sorted(set(files))
manifest = "".join(f"{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.relative_to(root).as_posix()}\n" for p in files)
(root / "SHA256SUMS.txt").write_text(manifest, encoding="utf-8")
files.append(root / "SHA256SUMS.txt")
args.output.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(args.output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for path in files:
        archive.write(path, f"AutoTunel-MB-{version}/" + path.relative_to(root).as_posix())
with zipfile.ZipFile(args.output) as archive:
    if archive.testzip() is not None:
        raise SystemExit("ZIP integrity check failed")
print(f"Created {args.output}: {len(files)} files; version {version}")
print("SHA256 " + hashlib.sha256(args.output.read_bytes()).hexdigest())
