#!/usr/bin/env python3
"""Die Mods der Waystones aus den Spieldateien nach data/waystones/mods.json holen.

Der Waystone-Tab braucht für die Regex (Suchfeld im Spiel), für die Pulldowns und für die
Trade-Abfrage dieselbe Liste: jede Mod-Zeile, wie sie auf einem Waystone steht, mit ihrem
Wertebereich über alle Stufen, als Prefix oder Suffix, und mit der Kennung, unter der die
Trade-Seite danach filtert. Die Liste entsteht aus drei Quellen, alle von Grinding Gear Games:

  - Tabelle `Mods` (mit `Stats`, `Tags`): welche Mods auf einem Waystone würfeln — Gebietsmods
    (Domain 6), deren Spawn-Gewicht an einem `map_key_*`-Tag hängt (das Waystone-Item heißt
    intern MapKey) oder am Tag `default` bei einer Id `Map…` (die Flüche und „Monsters are
    Armoured/Evasive"), als Prefix (GenerationType 1) oder Suffix (2); je Stufe eine Zeile mit
    Min/Max des ersten Stat-Werts.
  - `data/statdescriptions/stat_descriptions.csd`: der englische Zeilentext je Stat, mit `{0}`
    für die Zahl (`{0:+d}` mit Vorzeichen) und `[Tag|Text]`-Markierungen für Links; ein Stat mit
    mehreren Zweigen (`1|#` / `#|-1`, „more"/„less") wird nach dem Vorzeichen des Werts gewählt,
    `negate` dreht die Anzeige ins Positive.
  - https://www.pathofexile.com/api/trade2/data/stats: die Kennung je Zeilentext, Gruppe
    `explicit`, wortgleich.

Dieses Skript läuft nur bei uns, wenn ein Patch die Mods ändert. Voraussetzungen wie in
tooltip_headers.py: `pathofexile-dat` aus der Werkzeugkiste, die Steam-Installation von PoE 2,
Netz für die Trade-Kennungen. Spieldaten selbst wandern nicht ins Repository, nur die Liste.

  tools/waystone_mods.py [--work <ordner>] [--stats <trade-stats.json>]

--work: ein Ordner mit fertigem Export (tables/English/*.json, files/…csd) aus einem früheren
Lauf, statt die Bundles neu zu lesen. --stats: die Antwort der Trade-Seite aus einer Datei.
"""
import json
import os
import re
import subprocess
import sys
import tempfile
import urllib.request
from collections import defaultdict

PACKAGE = os.path.expanduser("~/.claude/tools/poe-dat/node_modules/pathofexile-dat/dist/cli/run.js")
GAME = "/mnt/e/Steam/steamapps/common/Path of Exile 2"
CSD = "data/statdescriptions/stat_descriptions.csd"
TRADE_STATS = "https://www.pathofexile.com/api/trade2/data/stats"
TARGET = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "data", "waystones", "mods.json")
TABLES = {
    "Mods": ["Id", "Level", "Domain", "GenerationType", "Name", "Stat1", "Stat2", "Stat3", "Stat4", "Stat5", "Stat6",
             "Stat1Value", "Stat2Value", "Stat3Value", "Stat4Value", "Stat5Value", "Stat6Value",
             "SpawnWeight_Tags", "SpawnWeight_Values"],
    "Stats": ["Id"],
    "Tags": ["Id"],
}
AREA = 6
PREFIX, SUFFIX = 1, 2


def export(work, files=(CSD,), tables=TABLES):
    config = {"steam": GAME, "translations": ["English", "German"], "files": list(files),
              "tables": [{"name": n, "columns": c} for n, c in tables.items()]}
    with open(os.path.join(work, "config.json"), "w", encoding="utf-8") as f:
        json.dump(config, f)
    done = subprocess.run(["node", PACKAGE], cwd=work, capture_output=True, text=True)
    if done.returncode != 0:
        sys.exit("pathofexile-dat: " + (done.stderr or done.stdout).strip())


def table(work, name, language="English"):
    with open(os.path.join(work, "tables", language, name + ".json"), encoding="utf-8") as f:
        return json.load(f)


def descriptions(work, csd=CSD, language=None):
    """Stat-Id → Liste von (Bedingungen, Vorlage, Flags) der englischen Zeilen.

    language: die Zeilen dieser Sprache (`lang "German"`-Block nach den englischen); ein Stat
    ohne solchen Block fehlt dann. Der deutsche Client zeigt die deutschen Zeilen, und der
    Tooltip wird an ihnen gelesen (21.10.2026)."""
    with open(os.path.join(work, "files", csd.replace("/", "@")), "rb") as f:
        text = f.read().decode("utf-16").replace("\r", "")
    lines = text.split("\n")
    found = {}
    i = 0
    while i < len(lines):
        if lines[i].strip() != "description":
            i += 1
            continue
        head = lines[i + 1].split()
        ids = head[1:1 + int(head[0])]
        count = int(lines[i + 2].strip())
        english = parse_entries(lines[i + 3:i + 3 + count])
        i += 3 + count
        localized = None
        while i < len(lines) and lines[i].strip().startswith("lang "):
            name = lines[i].strip()[len("lang "):].strip('"')
            n = int(lines[i + 1].strip())
            if name == language:
                localized = parse_entries(lines[i + 2:i + 2 + n])
            i += 2 + n
        entries = english if language is None else localized
        if entries:
            for stat in ids:
                found.setdefault(stat, entries)
    return found


def parse_entries(block):
    entries = []
    for line in block:
        m = re.match(r'\s*(.*?)\s*"(.*)"(.*)$', line)
        if m:
            entries.append((m.group(1).split(), m.group(2), m.group(3).split()))
    return entries


def branch(entries, value):
    """Die Zeile, deren Bedingung für den ersten Wert gilt (`1|#`: ab 1, `#|-1`: bis -1)."""
    for conditions, template, flags in entries:
        ok = True
        for condition in conditions[:1]:
            if condition == "#":
                continue
            low, high = (condition.split("|") + [condition])[:2] if "|" in condition else (condition, condition)
            if not ((low == "#" or int(low) <= value) and (high == "#" or value <= int(high))):
                ok = False
        if ok:
            return template, flags
    return entries[0][1], entries[0][2]


def render(template, flags, low, high):
    """Vorlage → Zeilentext mit `#` und der Bereich, wie das Spiel ihn zeigt."""
    text = re.sub(r"\[([^\]|]*)\|([^\]]*)\]", r"\2", template)
    text = re.sub(r"\[([^\]]*)\]", r"\1", text)
    if "negate" in flags:
        low, high = -high, -low
    # Ein Stat in Hundertsteln oder Zehnteln (tablet_mods.py: „permyriad" → divide_by_one_hundred).
    for flag, divisor in (("divide_by_one_hundred", 100), ("divide_by_ten", 10)):
        if flag in flags:
            low, high = low // divisor, high // divisor
    if "{0:+d}" in text:
        text = text.replace("{0:+d}", "+#")
    elif low < 0 and "{0}" in text:
        text = text.replace("{0}", "-#")
        low, high = -high, -low
    text = re.sub(r"\{\d+(:[^}]*)?\}", "#", text)
    return text, low, high


def trade_ids(path):
    if path:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    else:
        with urllib.request.urlopen(urllib.request.Request(TRADE_STATS, headers={"User-Agent": "poe2pricer-tools (local)"})) as r:
            data = json.load(r)
    return {e["text"]: e["id"] for g in data["result"] if g["id"] == "explicit" for e in g["entries"]}


def main():
    args = sys.argv[1:]
    work = args[args.index("--work") + 1] if "--work" in args else None
    stats_file = args[args.index("--stats") + 1] if "--stats" in args else None
    if work is None:
        work = tempfile.mkdtemp(prefix="waystone-mods-")
        export(work)
    mods, stats, tags = table(work, "Mods"), table(work, "Stats"), table(work, "Tags")
    stat_id = {s["_index"]: s["Id"] for s in stats}
    tag_id = {t["_index"]: t["Id"] for t in tags}
    described = descriptions(work)
    german = descriptions(work, language="German")
    trade = trade_ids(stats_file)

    def spawns_on_waystone(mod):
        for tag, weight in zip(mod["SpawnWeight_Tags"], mod["SpawnWeight_Values"]):
            name = tag_id.get(tag, "")
            if weight > 0 and (name.startswith("map_key") or (name == "default" and mod["Id"].startswith("Map"))):
                return True
        return False

    families = defaultdict(list)
    for mod in mods:
        if mod["Domain"] == AREA and mod["GenerationType"] in (PREFIX, SUFFIX) and spawns_on_waystone(mod):
            stat_ids = tuple(stat_id[mod[f"Stat{i}"]] for i in range(1, 7) if mod.get(f"Stat{i}") is not None)
            families[(mod["GenerationType"], stat_ids)].append(mod)

    entries = []
    for (generation, stat_ids), members in families.items():
        members.sort(key=lambda m: m["Stat1Value"][0])
        lines = []
        for position, stat in enumerate(stat_ids, start=1):
            if stat not in described:
                continue  # dummy_stat_display_nothing und reine Rechenstats
            values = [m[f"Stat{position}Value"] for m in members]
            low, high = min(v[0] for v in values), max(v[1] for v in values)
            template, flags = branch(described[stat], low)
            # Dieselbe Zeile im deutschen Client: derselbe Zweig, dieselbe Zahl.
            if stat not in german:
                print(f"{stat}: no German line, English kept", file=sys.stderr)
            de_template, de_flags = branch(german.get(stat, described[stat]), low)
            de_text = render(de_template, de_flags, low, high)[0]
            text, low, high = render(template, flags, low, high)
            tiers = [render(template, flags, v[0], v[1])[1:] for v in values]
            line = {"text": text, "de": de_text, "stat": stat}
            if "#" in text:
                line.update({"min": low, "max": high, "tiers": tiers})
            trade_id = trade.get(text) or trade.get(text.replace("+#", "#")) or trade.get(text.replace("-#", "#"))
            if trade_id is None and len(described[stat]) > 1:
                # Die Trade-Seite kennt nur einen Zweig („more Cooldown Recovery Rate"); der
                # andere („less") ist derselbe Stat mit umgekehrtem Vorzeichen.
                for conditions, other, other_flags in described[stat]:
                    other_text = render(other, other_flags, high, high)[0]
                    if other_text != text and trade.get(other_text):
                        trade_id, line["tradeNegated"] = trade[other_text], True
                        break
            line["trade"] = trade_id
            lines.append(line)
        if not lines:
            continue
        entries.append({"kind": "prefix" if generation == PREFIX else "suffix",
                        "names": sorted(set(m["Name"] for m in members)), "lines": lines})
    entries.sort(key=lambda e: (e["kind"], e["lines"][0]["text"].lower()))
    os.makedirs(os.path.dirname(TARGET), exist_ok=True)
    with open(TARGET, "w", encoding="utf-8") as f:
        json.dump({"source": "Spieldaten (Mods, Stats, Tags, stat_descriptions.csd) und trade2/data/stats, tools/waystone_mods.py",
                   "mods": entries}, f, indent=1, ensure_ascii=False)
        f.write("\n")
    missing = [l["text"] for e in entries for l in e["lines"] if l["trade"] is None]
    print(f"{len(entries)} mods ({sum(1 for e in entries if e['kind'] == 'prefix')} prefix, "
          f"{sum(1 for e in entries if e['kind'] == 'suffix')} suffix), {sum(len(e['lines']) for e in entries)} lines → {TARGET}")
    if missing:
        print("without trade id: " + "; ".join(missing))


if __name__ == "__main__":
    main()
