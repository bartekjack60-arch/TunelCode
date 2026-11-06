"""Fetch pinned, unmodified USB installers for building; the app never downloads them."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--verify-only", action="store_true")
args = parser.parse_args()
folder = root / "vendor/usb"
packages = json.loads((folder / "packages.json").read_text(encoding="utf-8"))
for package in packages:
    name = package["fileName"]
    if Path(name).name != name:
        raise SystemExit("Invalid package name")
    path = folder / name
    if not path.exists() and not args.verify_only:
        temporary = path.with_suffix(path.suffix + ".partial")
        try:
            with urllib.request.urlopen(package["url"], timeout=90) as response, temporary.open("xb") as output:
                while chunk := response.read(1024 * 1024):
                    output.write(chunk)
            if hashlib.sha256(temporary.read_bytes()).hexdigest() != package["sha256"]:
                raise SystemExit("USB package SHA-256 mismatch: " + name)
            temporary.replace(path)
        finally:
            temporary.unlink(missing_ok=True)
    if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != package["sha256"]:
        raise SystemExit("Missing or modified USB package: " + name + "; run python3 tools/fetch-usb.py")
    print("Verified " + name)
