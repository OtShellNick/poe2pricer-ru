#!/usr/bin/env python3
"""Baut die Veröffentlichung einer Version: Publish (self-contained und framework-dependent),
die beiden Zips und den Installer mit Velopack (vpk).

Aufruf (aus dem Projektordner, unter WSL):
  tools/build_release.py            Version aus Directory.Build.props
  tools/build_release.py 1.0.1      andere Version (nur für Proben; die Props bleiben)

Ergebnis in release/:
  poe2pricer-<v>/                        framework-dependent (braucht die .NET-10-Desktop-Runtime)
  poe2pricer-<v>-selfcontained/          mit Runtime
  poe2pricer-<v>-win-x64.zip             aus dem self-contained Ordner, Unterordner poe2pricer/
  poe2pricer-<v>-win-x64-framework-dependent.zip
  installer/<v>/                         vpk pack: poe2pricer-app-win-Setup.exe, poe2pricer-app-<v>-full.nupkg,
                                         releases.win.json
Vorher muss poe2pricer beendet sein (sonst sind die Dateien gesperrt).
"""
import os
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOTNET = "/mnt/c/Program Files/dotnet/dotnet.exe"
# vpk is a global dotnet tool on the Windows side (dotnet tool install -g vpk): asked where it is.
VPK = subprocess.run(["/mnt/c/Windows/System32/where.exe", "vpk.exe"], capture_output=True, text=True).stdout.strip().splitlines()[0]
VPK = subprocess.run(["wslpath", "-u", VPK], capture_output=True, text=True, check=True).stdout.strip()
APP = os.path.join("src", "Poe2Pricer.App")


def run(args, **kw):
    print("+", " ".join(args))
    subprocess.run(args, check=True, cwd=ROOT, **kw)


def windows_path(path):
    out = subprocess.run(["wslpath", "-w", path], capture_output=True, text=True, check=True).stdout.strip()
    return out


def zip_folder(source, target):
    tmp = target + ".tmp"
    count = 0
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as z:
        for folder, _, files in os.walk(source):
            for name in sorted(files):
                path = os.path.join(folder, name)
                z.write(path, "poe2pricer/" + os.path.relpath(path, source).replace(os.sep, "/"))
                count += 1
    os.replace(tmp, target)
    print(f"{target}: {count} Dateien, {os.path.getsize(target) // 1_000_000} MB")


def main():
    props = open(os.path.join(ROOT, "Directory.Build.props"), encoding="utf-8").read()
    version = sys.argv[1] if len(sys.argv) > 1 else re.search(r"<Version>([^<]+)</Version>", props).group(1)
    # A copy running from release/ would lock the files; the installed copy runs elsewhere.
    running = subprocess.run(["/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe", "-NoProfile", "-Command", "Get-Process poe2pricer -ErrorAction SilentlyContinue | ForEach-Object Path"], capture_output=True).stdout.decode("utf-8", "replace")
    if "\\release\\" in running:
        sys.exit("poe2pricer läuft aus release/ — erst beenden.")
    # The guide names the version: kept in step here (a test checks it against the program).
    guide = os.path.join(ROOT, "data", "guide", "README.html")
    html = open(guide, encoding="utf-8").read()
    stamped = re.sub(r'(<span id="version">)[^<]*(</span>)', lambda m: m.group(1) + version + m.group(2), html, count=1)
    if stamped != html:
        open(guide, "w", encoding="utf-8").write(stamped)
        print(f"Anleitung: Version {version} eingetragen")
    release = os.path.join(ROOT, "release")
    fd = os.path.join(release, f"poe2pricer-{version}")
    sc = os.path.join(release, f"poe2pricer-{version}-selfcontained")
    inst = os.path.join(release, "installer", version)
    for folder in (fd, sc, inst):
        shutil.rmtree(folder, ignore_errors=True)
    extra = [f"-p:Version={version}"] if len(sys.argv) > 1 else []
    run([DOTNET, "publish", APP, "-c", "Release", "-o", windows_path(fd), "--nologo", "-v", "q", *extra])
    run([DOTNET, "publish", APP, "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", windows_path(sc), "--nologo", "-v", "q", *extra])
    for folder in (fd, sc):
        leftover = [p for p, _, files in os.walk(folder) for f in files if "ffmpeg" in f.lower()]
        if leftover:
            sys.exit(f"FFmpeg-Hülle im Publish: {leftover}")
    zip_folder(sc, os.path.join(release, f"poe2pricer-{version}-win-x64.zip"))
    zip_folder(fd, os.path.join(release, f"poe2pricer-{version}-win-x64-framework-dependent.zip"))
    os.makedirs(inst, exist_ok=True)
    run([
        VPK, "pack",
        # Not "poe2pricer": the setup installs to %LOCALAPPDATA%\<packId>, and that folder is
        # where the program keeps its settings — an existing one counts as "already installed".
        "--packId", "poe2pricer-app",
        "--packVersion", version,
        "--packDir", windows_path(sc),
        "--runtime", "win-x64",
        "--channel", "win",
        "--mainExe", "poe2pricer.exe",
        "--packTitle", "poe2pricer",
        "--packAuthors", "saxe78",
        "--icon", windows_path(os.path.join(ROOT, "assets", "poe2pricer.ico")),
        "--shortcuts", "StartMenuRoot",
        # The zip of ours stands in for the portable package.
        "--noPortable",
        "--outputDir", windows_path(inst),
    ])
    print("Installer:", sorted(os.listdir(inst)))


if __name__ == "__main__":
    main()
