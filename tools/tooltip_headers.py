#!/usr/bin/env python3
"""Titelleisten der Tooltips und das Eckornament einzigartiger Gegenstände aus den Spieldateien
nach data/ui holen.

Der Erkenner (OccluderDetector) findet einen Tooltip an den Enden seiner Titelleiste. Die
Vorlagen dafür sind die Grafiken des Spiels selbst: je Art der Leiste ein linkes und ein
rechtes Stück (`Art/2DArt/UIImages/InGame/ItemsHeader<Art>Left|Right`, einzeilig 47×55,
zweizeilig 71×88 Bildpunkte). Das Spiel zeichnet die Stücke mit 0,90 × Popup Size; so passen
sie an allen Fenstergrößen und Reglerwerten mit .85–.99.

Dazu das Ornament, das das Spiel einem einzigartigen Gegenstand oben links und unten rechts an
seinen Umfang zeichnet (`VendorRarityDisplayUniqueTopLeft|BottomRight`, als Datei 65×66 Bildpunkte, mit
Durchsicht): daran erkennt das Programm ein Feld, das als Währung gelesen wurde und in Wahrheit
die Ecke eines großen Gegenstands ist (RarityCorners).

Und das linke Ende der Leiste eines Vaal-Uniques: die Unique-Leiste, auf die das Spiel ein
Medaillon legt (`VaalItemIcon`, 44×44, mit Durchsicht), 2,5 Bildpunkte von links und 22,5 von
oben im Endstück (das Stück allein passt .72–.76, mit Medaillon .92–.94; auf ganzen
Bildpunkten nur .84–.89). Die Vorlage wird hier aus beiden
Grafiken zusammengesetzt, in doppelter Größe, damit die halben Bildpunkte ganze sind
(`tooltip-header-unique-vaal-left.png`, 142×176); der Erkenner rechnet die doppelte Größe heraus.

Und die Titelleiste des Tooltips einer Fertigkeit (eine Gem-Zeile der Relikt-Liste unter Maus
oder Cursor): keine Gegenstandsleiste, sondern `SmartHover/GemHoverTitle`
(560×88) — links ein Ornament, nach rechts läuft die Leiste aus, ab Spalte 31 liegt das Bild der
Fertigkeit in einem Rahmen darüber. Die Vorlage sind die ersten 30 Spalten
(`tooltip-header-gem-left.png`, 30×88); ein rechtes Ende gibt es nicht. Sie passt an einem
Fertigkeiten-Tooltip mit mindestens .95, sonst höchstens .50.

Dieses Skript läuft nur bei uns und nur, wenn Grinding Gear Games die Grafik ändert oder eine
Art dazukommt. Das Programm selbst liest nur die fertigen PNG-Dateien; wer es benutzt, braucht
weder das Skript noch dessen Werkzeuge.

Voraussetzungen (auf dem Entwicklungsrechner):
  - das npm-Paket `pathofexile-dat` (liest die Bundles der Steam-Installation), hier aus der
    Werkzeugkiste: ~/.claude/tools/poe-dat/node_modules
  - ImageMagick 7 (`magick`), wandelt die DDS-Dateien des Spiels in PNG
    (unter Windows: winget install ImageMagick.ImageMagick)

Aufruf (aus WSL, im Projektordner):
  tools/tooltip_headers.py [--spiel <Spielordner>] [--ziel data/ui] [--alle]

Ohne --alle kommen nur die Arten, die der Erkenner benutzt (STYLES); mit --alle jede Art, die
das Spiel im Satz „InGame“ mitbringt, in einen Unterordner des Ziels namens `alle` — zum
Ausmessen, nicht fürs Repository.

Die Bilder sind Material von Grinding Gear Games.
"""
import argparse
import glob
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile

# Art der Leiste im Spiel → Name der Vorlage (data/ui/tooltip-header-<name>-left|right.png).
STYLES = {
    "Currency": "currency",
    "White": "white",
    "Magic": "magic",
    "Rare": "rare",
    "Unique": "unique",
}
# Weitere Grafik des Spiels → Name der Vorlage in data/ui.
EXTRA = {
    "VendorRarityDisplayUniqueTopLeft": "rarity-unique-topleft.png",
    "VendorRarityDisplayUniqueBottomRight": "rarity-unique-bottomright.png",
}
# Zusammengesetzte Vorlage → (Grundbild, aufgelegtes Bild, Vergrößerung, Lage des aufgelegten
# Bilds im vergrößerten Grundbild).
COMPOSED = {
    "tooltip-header-unique-vaal-left.png": ("ItemsHeaderUniqueLeft", "VaalItemIcon", 2, 5, 45),
}
# Ausschnitt → (Grafik unterhalb von InGame/, Zahl der Spalten von links).
CROPPED = {
    "tooltip-header-gem-left.png": ("SmartHover/GemHoverTitle", 30),
}
SPRITES = "Art/2DArt/UIImages/InGame/"
PACKAGE = os.path.expanduser("~/.claude/tools/poe-dat/node_modules/pathofexile-dat/dist/cli/run.js")
GAME = "/mnt/e/Steam/steamapps/common/Path of Exile 2"


def magick():
    found = shutil.which("magick") or next(iter(sorted(glob.glob("/mnt/c/Program Files/ImageMagick*/magick.exe"), reverse=True)), None)
    if not found:
        sys.exit("ImageMagick (magick) not found.")
    return found


def export(game, names, work):
    """pathofexile-dat ruft `magick dds:- -crop … <Ziel>` auf; unter WSL ist das die Windows-
    Fassung, die den Zielpfad in Windows-Schreibweise braucht."""
    binary = magick()
    wrapper = os.path.join(work, "bin", "magick")
    os.makedirs(os.path.dirname(wrapper))
    with open(wrapper, "w", encoding="utf-8") as f:
        if binary.endswith(".exe"):
            f.write(
                "#!/bin/bash\n"
                'args=("$@"); last=$((${#args[@]}-1)); out="${args[$last]}"\n'
                'mkdir -p "$(dirname "$out")"\n'
                'args[$last]="$(wslpath -w "$(realpath "$(dirname "$out")")")\\\\$(basename "$out")"\n'
                f'exec "{binary}" "${{args[@]}}"\n'
            )
        else:
            f.write(f'#!/bin/bash\nexec "{binary}" "$@"\n')
    os.chmod(wrapper, os.stat(wrapper).st_mode | stat.S_IEXEC)
    with open(os.path.join(work, "config.json"), "w", encoding="utf-8") as f:
        json.dump({"steam": game, "translations": ["English"], "tables": [], "files": names}, f)
    env = dict(os.environ, PATH=os.path.dirname(wrapper) + os.pathsep + os.environ["PATH"])
    done = subprocess.run(["node", PACKAGE], cwd=work, env=env, capture_output=True, text=True)
    if done.returncode != 0:
        sys.exit("pathofexile-dat: " + (done.stderr or done.stdout).strip())
    return os.path.join(work, "files")


def native(path):
    """Pfad, wie ihn das gefundene ImageMagick braucht (unter WSL die Windows-Fassung)."""
    path = os.path.realpath(path)
    return subprocess.run(["wslpath", "-w", path], capture_output=True, text=True, check=True).stdout.strip() if magick().endswith(".exe") else path


def compose(files, target):
    for name, (base, over, factor, x, y) in COMPOSED.items():
        source = lambda sprite: native(os.path.join(files, (SPRITES + sprite).replace("/", "@") + ".png"))
        size = f"{factor * 100}%"
        done = subprocess.run(
            [magick(), source(base), "-filter", "Catrom", "-resize", size, "(", source(over), "-filter", "Catrom", "-resize", size, ")", "-geometry", f"+{x}+{y}", "-composite", native(target) + (chr(92) if magick().endswith(".exe") else "/") + name],
            capture_output=True,
            text=True,
        )
        if done.returncode != 0:
            sys.exit("magick: " + done.stderr.strip())


def crop(files, target):
    for name, (sprite, columns) in CROPPED.items():
        source = native(os.path.join(files, (SPRITES + sprite).replace("/", "@") + ".png"))
        done = subprocess.run(
            [magick(), source, "-crop", f"{columns}x+0+0", "+repage", native(target) + (chr(92) if magick().endswith(".exe") else "/") + name],
            capture_output=True,
            text=True,
        )
        if done.returncode != 0:
            sys.exit("magick: " + done.stderr.strip())


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--spiel", default=os.environ.get("POE2_DIR", GAME))
    parser.add_argument("--ziel", default="data/ui")
    parser.add_argument("--alle", action="store_true")
    args = parser.parse_args()
    if not os.path.exists(PACKAGE):
        sys.exit(f"pathofexile-dat is missing: {PACKAGE}")
    with tempfile.TemporaryDirectory() as work:
        if args.alle:
            index = export(args.spiel, ["Art/UIImages1.txt"], os.path.join(work, "index"))
            with open(os.path.join(index, "Art@UIImages1.txt"), "rb") as f:
                listed = [line.split('"')[1] for line in f.read().decode("utf-16-le").splitlines() if line.startswith('"')]
            names = [n for n in listed if n.startswith(SPRITES + "ItemsHeader") or n.startswith(SPRITES + "RunicItemsHeader") or n.startswith(SPRITES + "HardmodeItemsHeader")]
            names = [n for n in names if n.endswith("Left") or n.endswith("Right")]
            target = os.path.join(args.ziel, "alle")
            rename = lambda sprite: sprite[len(SPRITES):] + ".png"
        else:
            names = [f"{SPRITES}ItemsHeader{style}{side}" for style in STYLES for side in ("Left", "Right")] + [SPRITES + extra for extra in EXTRA]
            only = [SPRITES + over for _, over, *_ in COMPOSED.values()] + [SPRITES + sprite for sprite, _ in CROPPED.values()]
            target = args.ziel
            rename = lambda sprite: EXTRA.get(sprite[len(SPRITES):]) or next(
                f"tooltip-header-{name}-{side.lower()}.png" for style, name in STYLES.items() for side in ("Left", "Right") if sprite == f"{SPRITES}ItemsHeader{style}{side}"
            )
        only = [] if args.alle else only
        files = export(args.spiel, names + only, os.path.join(work, "bilder"))
        os.makedirs(target, exist_ok=True)
        if not args.alle:
            compose(files, target)
            crop(files, target)
        for sprite in names:
            source = os.path.join(files, sprite.replace("/", "@") + ".png")
            if not os.path.exists(source):
                sys.exit(f"not delivered: {sprite}")
            shutil.copyfile(source, os.path.join(target, rename(sprite)))
        print(f"{len(names) + (0 if args.alle else len(COMPOSED) + len(CROPPED))} pictures written to {target}")


if __name__ == "__main__":
    main()
