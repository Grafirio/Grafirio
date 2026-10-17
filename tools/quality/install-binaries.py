"""Install only named, checksum-pinned CLI executables into this checkout's .quality/bin."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import platform
import tarfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def install(name, spec, target):
    asset = spec["assets"][target]
    url = f"https://github.com/{spec['repository']}/releases/download/v{spec['version']}/{asset['archive']}"
    with urllib.request.urlopen(url, timeout=60) as response:
        archive = response.read()
    if hashlib.sha256(archive).hexdigest() != asset["sha256"]:
        raise RuntimeError(f"Checksum mismatch for {name}")
    executable = name + (".exe" if target.startswith("windows") else "")
    # Read exactly one member, never extract arbitrary paths or symlinks.
    if asset["archive"].endswith(".zip"):
        with zipfile.ZipFile(io.BytesIO(archive)) as package:
            data = package.read(executable)
    else:
        with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as package:
            member = package.getmember(executable)
            if not member.isfile():
                raise RuntimeError("Executable is not a regular file")
            data = package.extractfile(member).read()
    destination = ROOT / ".quality/bin" / executable
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(destination.suffix + ".tmp")
    temporary.write_bytes(data)
    temporary.chmod(0o755)
    temporary.replace(destination)
    print(f"Installed {name} {spec['version']} ({target}, SHA-256 verified)")


if __name__ == "__main__":
    manifest = json.loads((Path(__file__).with_name("binaries.json")).read_text(encoding="utf-8-sig"))["tools"]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tools", nargs="*", choices=list(manifest))
    arguments = parser.parse_args()
    system = platform.system().lower()
    if system not in ("windows", "linux") or platform.machine().lower() not in ("amd64", "x86_64"):
        raise SystemExit("Pinned binaries currently support Windows/Linux x64 only")
    for tool in arguments.tools or manifest:
        install(tool, manifest[tool], system + "-x64")
