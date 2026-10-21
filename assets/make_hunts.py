#!/usr/bin/env python3
"""Genera hunts.json (los presets de hunt del optimizador de charms) a partir de huntingPlaces.json del
Charm Optimizer de hakaimarket.com, guardado desde el navegador (su servidor no deja descargarlo con
curl). Solo se quedan las criaturas que están en bestiary.json, con su nombre exacto y sus kills por hora.

    python3 make_hunts.py huntingPlaces.json
"""
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).parent
# Erratas de la fuente.
ALIASES = {"affliected strider": "afflicted strider"}

raw = json.loads(Path(sys.argv[1]).read_text())
bestiary = {c["name"].lower(): c["name"] for c in json.loads((HERE / "bestiary.json").read_text())["creatures"]}

hunts, missing = [], set()
for place in raw:
    text = place.get("creatures") or place.get("monstersOfResp") or ""
    text = text.split(":", 1)[-1]
    creatures = []
    for part in text.split(","):
        match = re.match(r"\s*(?:(\d+)\s*x\s*)?(.+?)[\s\]]*$", part)
        if not match or not match.group(2):
            continue
        key = ALIASES.get(match.group(2).lower(), match.group(2).lower())
        name = bestiary.get(key)
        if not name:
            missing.add(match.group(2))
            continue
        if all(c["name"] != name for c in creatures):
            creatures.append({"name": name, "perHour": int(match.group(1) or 1)})
    if not creatures:
        continue
    level = int(place.get("minLevel") or 0)
    hunts.append({
        "name": place["name"].strip(),
        "city": " ".join(w if w == "of" else w[:1].upper() + w[1:] for w in (place.get("city") or place.get("surroundings") or "").split()),  # "Port hope"
        "minLevel": level if level <= 3000 else int(str(level)[:3]),  # "250050" → 250
        "premium": place.get("premium") not in (False, "no"),
        "creatures": creatures,
    })

hunts.sort(key=lambda h: (h["city"].lower(), h["minLevel"], h["name"].lower()))
out = {"source": "https://hakaimarket.com/calculators?tool=charmOptimizer (huntingPlaces.json)", "hunts": hunts}
(HERE / "hunts.json").write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n")
print(f"{len(hunts)} hunts; sin el Bestiary: {', '.join(sorted(missing))}")
