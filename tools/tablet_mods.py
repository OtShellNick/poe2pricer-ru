#!/usr/bin/env python3
"""Die Tablet-Arten und ihre Mods aus den Spieldateien nach data/tablets/mods.json holen.

Der Tablets-Tab braucht je Tablet-Art dieselbe Liste wie der Waystone-Tab für Waystones: jede
Mod-Zeile, wie sie auf dem Tablet steht, mit ihrem Wertebereich, als Prefix oder Suffix, und
mit der Kennung, unter der die Trade-Seite danach filtert. Quellen wie bei waystone_mods.py,
dazu:

  - `BaseItemTypes` (mit `ItemClasses`): die Tablet-Arten sind die Items der Klasse
    TowerAugmentation (Breach, Delirium, …, Irradiated = das generische); jedes trägt ein
    eigenes Tag `tower_augment_<art>` und einen Implicit („Adds Irradiated to a Map").
  - `Mods`: Tablet-Mods sind Domain 34. Generische (auf jeder Art, Irradiated hat nur diese)
    haben ihr Spawn-Gewicht am Tag `default`, artspezifische am Tag der Art. Jede Mod hat nur
    eine Stufe.
  - `data/statdescriptions/tablet_stat_descriptions.csd`: der Wortlaut auf dem Tablet („… in
    Map"); die Implicits und ein paar Stats stehen nur in stat_descriptions.csd.
  - Trade-Kennungen: eine Kennung führt auf der Trade-Seite mehrere Wortlaute (Waystone „Area
    …" und Tablet „Map …"), und derselbe Tablet-Wortlaut („Map contains an additional
    Essence") gehört zu zwei Kennungen (generischer Prefix, Overseer-Suffix). Entschieden wird
    über alle Wortlaute eines Stats aus allen drei Beschreibungsdateien: die Kennung, die die
    meisten davon führt. Eine Zeile mit „reduced", die die Seite nur als „increased" kennt,
    bekommt deren Kennung mit tradeNegated.

  tools/tablet_mods.py [--work <ordner>] [--stats <trade-stats.json>]

Wie waystone_mods.py: --work ein Ordner mit fertigem Export (tables/English/*.json,
files/*.csd), --stats die Antwort der Trade-Seite aus einer Datei.
"""
import json
import os
import sys
import tempfile
import urllib.request
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import waystone_mods as wm  # noqa: E402

TARGET = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "data", "tablets", "mods.json")
TABLES = dict(wm.TABLES, **{
    "BaseItemTypes": ["Id", "Name", "ItemClass", "Tags", "Implicit_Mods"],
    "ItemClasses": ["Id", "Name"],
})
CSDS = ["data/statdescriptions/tablet_stat_descriptions.csd", "data/statdescriptions/map_stat_descriptions.csd", wm.CSD]
TABLET_DOMAIN = 34
# Zwei Kennungen mit wortgleichen Texten, die kein Wortlaut trennt: die, unter der die Seite
# heutige Tablets führt (Probe 20.10.2026, Liga Forbidden Rites: Delirium Tablet mit
# stat_3226351972 10.000 Angebote, mit stat_1174954559 keines; der alte Mod
# TowerDeliriumFogDissipationDelay ist ohne Gewicht).
PREFERRED = {"map_affliction_secondary_wave_delay_seconds_+": "explicit.stat_3226351972"}
IMPLICIT = 3
ITEM_CLASS = "TowerAugmentation"
TAG_PREFIX = "tower_augment_"


def trade_index(path):
    """Wortlaut → Kennungen und Kennung → Wortlaute der Gruppe explicit."""
    if path:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    else:
        with urllib.request.urlopen(urllib.request.Request(wm.TRADE_STATS, headers={"User-Agent": "poe2pricer-tools (local)"})) as r:
            data = json.load(r)
    by_text, by_id = defaultdict(set), defaultdict(set)
    for group in data["result"]:
        if group["id"] != "explicit":
            continue
        for entry in group["entries"]:
            by_text[entry["text"]].add(entry["id"])
            by_id[entry["id"]].add(entry["text"])
    return by_text, by_id


def variants(text):
    return {text, text.replace("+#", "#"), text.replace("-#", "#")}


def lookup(csds, stat, low, high, by_text):
    """(Kennung, negiert) für einen Stat mit Werten low..high; (None, False), wenn die Seite ihn nicht führt.

    Wortlaute des Stats aus allen Beschreibungsdateien, in drei Rängen: die Zweige, die einen
    Wert des Bereichs oder sein Doppeltes annehmen (Einzahl „an additional Essence", Mehrzahl
    „# additional Essences"); die übrigen Zweige desselben Vorzeichens („for 1 second", wenn
    das Tablet 3–5 würfelt); die Zweige des anderen Vorzeichens („reduced" zu „increased"),
    deren Kennung negiert gilt. Gewertet wird die Kennung, die die meisten Wortlaute eines
    Rangs führt; bei Gleichstand die mit dem Wortlaut des Tablets selbst."""
    primary = None
    own, same, opposite = set(), set(), set()
    for described in csds:
        entries = described.get(stat, [])
        if not entries:
            continue
        accepted = {wm.branch(entries, value)[0] for value in (low, high, 2 * high)}
        negative = {wm.branch(entries, value)[0] for value in (-low, -high)} - accepted
        primary = primary or wm.render(wm.branch(entries, low)[0], wm.branch(entries, low)[1], low, high)[0]
        for conditions, template, flags in entries:
            texts = variants(wm.render(template, flags, low, high)[0]) | variants(wm.render(template, flags, high, high)[0])
            (own if template in accepted else opposite if template in negative else same).update(texts)
    for texts, negated in ((own, False), (same, False), (opposite, True)):
        score = defaultdict(int)
        for text in texts:
            for trade_id in by_text.get(text, ()):
                score[trade_id] += 1
        if not score:
            continue
        best = max(score.values())
        winners = [i for i, n in score.items() if n == best]
        if len(winners) > 1 and primary in by_text:
            winners = [i for i in winners if i in by_text[primary]] or winners
        if len(winners) > 1 and PREFERRED.get(stat) in winners:
            winners = [PREFERRED[stat]]
        if len(winners) > 1:
            print(f"ambiguous trade id for {stat}: {sorted(winners)} (texts {sorted(texts)})", file=sys.stderr)
        return winners[0], negated
    return None, False


def german_of(german, stat, low, high):
    """Die deutsche Zeile eines Stats mit denselben Werten wie die englische (Zweig nach low)."""
    described = next((d for d in german if stat in d), None)
    if described is None:
        return None
    template, flags = wm.branch(described[stat], low)
    if "{0" not in template and high != low:
        template, flags = wm.branch(described[stat], high)
    return wm.render(template, flags, low, high)[0]


def line_of(csds, stat, values, by_text, german=()):
    """Die Zeile eines Stats: Text mit „#" (der Zweig, der die Zahl zeigt, wenn der Bereich einen hat), Bereich, Kennung."""
    described = next((d for d in csds if stat in d), None)
    if described is None:
        return None
    low, high = min(v[0] for v in values), max(v[1] for v in values)
    if low == 0 and high == 0:
        return None
    template, flags = wm.branch(described[stat], low)
    if "{0" not in template and high != low:
        template, flags = wm.branch(described[stat], high)
    # Die Kennung wird mit den rohen Werten gesucht (ein „reduced"-Mod ist negativ gespeichert
    # und steht erst nach dem Rendern positiv da); die Zeile trägt die gerenderten.
    trade_id, negated = lookup(csds, stat, low, high, by_text)
    de_text = german_of(german, stat, low, high)
    text, low, high = wm.render(template, flags, low, high)
    tiers = [wm.render(template, flags, v[0], v[1])[1:] for v in values]
    if de_text is None:
        print(f"{stat}: no German line, English kept", file=sys.stderr)
    line = {"text": text, "de": de_text or text, "stat": stat}
    if "#" in text:
        line.update({"min": low, "max": high, "tiers": tiers})
    line["trade"] = trade_id
    if negated:
        line["tradeNegated"] = True
    return line


def main():
    args = sys.argv[1:]
    work = args[args.index("--work") + 1] if "--work" in args else None
    stats_file = args[args.index("--stats") + 1] if "--stats" in args else None
    if work is None:
        work = tempfile.mkdtemp(prefix="tablet-mods-")
        wm.export(work, CSDS, TABLES)
    mods, stats, tags = wm.table(work, "Mods"), wm.table(work, "Stats"), wm.table(work, "Tags")
    bases, classes = wm.table(work, "BaseItemTypes"), wm.table(work, "ItemClasses")
    stat_id = {s["_index"]: s["Id"] for s in stats}
    tag_id = {t["_index"]: t["Id"] for t in tags}
    class_id = {c["_index"]: c["Id"] for c in classes}
    mod_by_index = {m["_index"]: m for m in mods}
    csds = [wm.descriptions(work, csd) for csd in CSDS]
    german = [wm.descriptions(work, csd, language="German") for csd in CSDS]
    german_names = {b["Id"]: b["Name"] for b in wm.table(work, "BaseItemTypes", "German")}
    by_text, by_id = trade_index(stats_file)

    def stats_of(mod):
        return [(stat_id[mod[f"Stat{i}"]], mod[f"Stat{i}Value"]) for i in range(1, 7) if mod.get(f"Stat{i}") is not None]

    kinds = []
    for base in sorted(bases, key=lambda b: b["Name"]):
        if class_id.get(base["ItemClass"]) != ITEM_CLASS:
            continue
        tag = next((tag_id[t] for t in base["Tags"] if tag_id[t].startswith(TAG_PREFIX) and tag_id[t] != TAG_PREFIX.rstrip("_")), None)
        if tag is None:
            sys.exit(f"{base['Name']}: no {TAG_PREFIX}* tag")
        implicit, implicit_de = [], []
        for index in base["Implicit_Mods"]:
            for stat, values in stats_of(mod_by_index[index]):
                lines = {}
                for name, sources in (("en", csds), ("de", german)):
                    described = next((d for d in sources if stat in d), None)
                    if described is not None:
                        template, flags = wm.branch(described[stat], values[0])
                        lines[name] = [part.strip() for part in wm.render(template, flags, values[0], values[1])[0].split("\\n")]
                if "en" not in lines:
                    continue
                # Line by line: a stat without German keeps its English lines, said aloud.
                if "de" not in lines or len(lines["de"]) != len(lines["en"]):
                    print(f"{base['Name']}: implicit {stat} without German, English kept", file=sys.stderr)
                    lines["de"] = lines["en"]
                implicit.extend(lines["en"])
                implicit_de.extend(lines["de"])
        if base["Id"] not in german_names:
            print(f"{base['Name']}: no German name, English kept", file=sys.stderr)
        kinds.append({"name": base["Name"], "de": german_names.get(base["Id"], base["Name"]), "id": base["Id"], "tag": tag,
                      "implicit": implicit, "implicitDe": implicit_de})
    tags_of_kind = {k["tag"]: k["name"] for k in kinds}

    families = defaultdict(list)
    for mod in mods:
        if mod["Domain"] != TABLET_DOMAIN or mod["GenerationType"] not in (wm.PREFIX, wm.SUFFIX):
            continue
        for tag, weight in zip(mod["SpawnWeight_Tags"], mod["SpawnWeight_Values"]):
            name = tag_id.get(tag, "")
            if weight <= 0:
                continue
            tablet = None if name == "default" else tags_of_kind.get(name)
            if name != "default" and tablet is None:
                continue
            families[(tablet, mod["GenerationType"], tuple(s for s, _ in stats_of(mod)))].append(mod)
    entries = []
    for (tablet, generation, stat_ids), members in families.items():
        members.sort(key=lambda m: m["Stat1Value"][0])
        lines = []
        for position, stat in enumerate(stat_ids):
            line = line_of(csds, stat, [stats_of(m)[position][1] for m in members], by_text, german)
            if line is not None:
                lines.append(line)
        if lines:
            entries.append({"kind": "prefix" if generation == wm.PREFIX else "suffix", "tablet": tablet,
                            "names": sorted(set(m["Name"] for m in members if m["Name"])), "lines": lines})
    entries.sort(key=lambda e: (e["tablet"] or "", e["kind"], e["lines"][0]["text"].lower()))
    os.makedirs(os.path.dirname(TARGET), exist_ok=True)
    with open(TARGET, "w", encoding="utf-8") as f:
        json.dump({"source": "Spieldaten (BaseItemTypes, ItemClasses, Mods, Stats, Tags, tablet_/map_/stat_descriptions.csd) und trade2/data/stats, tools/tablet_mods.py",
                   "kinds": kinds, "mods": entries}, f, indent=1, ensure_ascii=False)
        f.write("\n")
    generic = [e for e in entries if e["tablet"] is None]
    print(f"{len(kinds)} kinds: " + ", ".join(k["name"] for k in kinds))
    print(f"{len(entries)} mods ({len(generic)} generic: {sum(1 for e in generic if e['kind'] == 'prefix')} prefix, "
          f"{sum(1 for e in generic if e['kind'] == 'suffix')} suffix; " +
          ", ".join(f"{k['name']} {sum(1 for e in entries if e['tablet'] == k['name'])}" for k in kinds) + "), "
          f"{sum(len(e['lines']) for e in entries)} lines → {TARGET}")
    missing = [l["text"] for e in entries for l in e["lines"] if l["trade"] is None]
    if missing:
        print("without trade id: " + "; ".join(missing))


if __name__ == "__main__":
    main()
