#!/usr/bin/env python3
# Genera assets/bestiary.json, assets/items.json y assets/charms.json (página Perfiles) desde la API de
# TibiaWiki.
#
# bestiary.json: las criaturas de Category:Bestiary_Creatures (sin las de Category:Deprecated) con su
# clase, dificultad, si son muy raras (las muy raras se completan con 5 kills), HP, experiencia y los
# sitios enlazados en su "location" (para recomendar dónde terminar varias a la vez).
# items.json: el equipo de cada categoría (cascos, armaduras…) para elegir las piezas del inventario.
#
# Uso: python3 assets/make_profiles_data.py
import json
import os
import re
import time
import urllib.parse
import urllib.request

API = "https://tibia.fandom.com/api.php"
HERE = os.path.dirname(os.path.abspath(__file__))

# Categoría del wiki → hueco del inventario (en el orden en que se muestran).
ITEM_CATEGORIES = [
    ("Helmets", "helmet"), ("Armors", "armor"), ("Legs", "legs"), ("Boots", "boots"),
    ("Amulets_and_Necklaces", "amulet"), ("Rings", "ring"),
    ("Sword_Weapons", "weapon"), ("Axe_Weapons", "weapon"), ("Club_Weapons", "weapon"),
    ("Distance_Weapons", "weapon"), ("Wands", "weapon"), ("Rods", "weapon"), ("Fist_Fighting_Weapons", "weapon"),
    ("Shields", "shield"), ("Spellbooks", "shield"), ("Quivers", "shield"),
]
LEVELS = ["Harmless", "Trivial", "Easy", "Medium", "Hard", "Challenging"]
# Daño que recibe cada criatura de cada elemento (physicalDmgMod… en su infobox), para los charms.
ELEMENTS = ["physical", "earth", "fire", "death", "energy", "holy", "ice"]


def get(params):
    url = API + "?" + urllib.parse.urlencode({**params, "format": "json", "formatversion": "2"})
    request = urllib.request.Request(url, headers={"User-Agent": "AlfaHub (datos de perfiles)"})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                return json.load(response)
        except Exception:
            time.sleep(2 * (attempt + 1))
    raise RuntimeError(url)


def members(category):
    """Páginas (espacio principal) de una categoría, incluidas sus subcategorías."""
    titles, subcats, cont = [], [], {}
    while True:
        data = get({"action": "query", "list": "categorymembers", "cmtitle": "Category:" + category,
                    "cmlimit": "500", "cmtype": "page|subcat", **cont})
        for m in data["query"]["categorymembers"]:
            (titles if m["ns"] == 0 else subcats).append(m["title"])
        if "continue" not in data:
            break
        cont = data["continue"]
    for sub in subcats:
        titles += members(sub.split(":", 1)[1].replace(" ", "_"))
    return titles


def contents(titles):
    """Wikitexto de cada página, de 50 en 50."""
    out = {}
    for i in range(0, len(titles), 50):
        data = get({"action": "query", "prop": "revisions", "rvprop": "content", "rvslots": "main",
                    "titles": "|".join(titles[i:i + 50])})
        for page in data["query"]["pages"]:
            revisions = page.get("revisions")
            if revisions:
                out[page["title"]] = revisions[0]["slots"]["main"]["content"]
    return out


def field(text, name):
    match = re.search(r"^\|\s*" + name + r"\s*=\s*(.*?)\s*$", text, re.M)
    return match.group(1) if match else ""


def number(text):
    match = re.match(r"\s*([\d,.]+)", text)
    return int(re.sub(r"[,.]", "", match.group(1))) if match else 0


def percent(text):
    match = re.match(r"\s*\+?(-?\d+)\s*%", text)
    return int(match.group(1)) if match else 100


def places(text):
    """Los sitios enlazados en "location" ([[Destino|texto]]), en orden y sin repetir."""
    out = []
    for target in re.findall(r"\[\[([^\]|#]+)", text):
        target = target.strip()
        if target and target not in out:
            out.append(target)
    return out


def main():
    deprecated = set(members("Deprecated"))

    creatures = []
    titles = [t for t in members("Bestiary_Creatures") if t not in deprecated]
    for title, text in contents(sorted(set(titles))).items():
        if "{{Infobox Creature" not in text or field(text, "isboss").lower() == "yes":
            continue
        level = field(text, "bestiarylevel")
        klass = field(text, "bestiaryclass")
        if level not in LEVELS or not klass:
            continue
        creatures.append({"name": field(text, "name") or title, "class": klass, "level": level,
                          "rare": field(text, "occurrence") == "Very Rare",
                          "hp": number(field(text, "hp")), "exp": number(field(text, "exp")),
                          "places": places(field(text, "location")),
                          "mods": {element: percent(field(text, element + "DmgMod")) for element in ELEMENTS}})
    creatures.sort(key=lambda c: c["name"])
    # Los enlaces a otras criaturas ("[[Troll]] cave") no son sitios.
    names = {c["name"] for c in creatures}
    for c in creatures:
        c["places"] = [p for p in c["places"] if p not in names][:12]

    slots = {}
    for category, slot in ITEM_CATEGORIES:
        for title in sorted(set(members(category)) - deprecated):
            slots.setdefault(title, slot)
    # Solo las páginas de items (las categorías también tienen páginas de lista, como "Armors").
    pages = contents(sorted(slots))
    items = [{"name": t, "slot": slots[t]} for t in slots if "{{Infobox Object" in pages.get(t, "")]

    # Charms: nombre, mayor o menor, coste de cada nivel, efecto y su imagen en el wiki.
    charms = []
    titles = [t for t in members("Charms") if not t.startswith("User:")]
    for title, text in sorted(contents(titles).items()):
        if not re.search(r"\{\{Infobox[ _]Charm", text):
            continue
        images = get({"action": "parse", "page": title, "prop": "images"})["parse"]["images"]
        image = next((i for i in images if "Charm_Points" not in i), "")
        effect = re.sub(r"\[\[(?:[^|\]]*\|)?([^\]]*)\]\]", r"\1", field(text, "effect"))
        charms.append({"name": field(text, "actualname") or field(text, "name") or title, "type": field(text, "type"),
                       "costs": [number(c) for c in re.split(r"[/,]\s(?=\d)|\s/\s", field(text, "cost"))],
                       "effect": effect, "image": image.replace("_", " ")})
    items.sort(key=lambda i: i["name"])

    source = "https://tibia.fandom.com (API de TibiaWiki)"
    with open(os.path.join(HERE, "bestiary.json"), "w") as f:
        json.dump({"source": source, "creatures": creatures}, f, ensure_ascii=False, separators=(",", ":"))
    with open(os.path.join(HERE, "items.json"), "w") as f:
        json.dump({"source": source, "items": items}, f, ensure_ascii=False, separators=(",", ":"))
    with open(os.path.join(HERE, "charms.json"), "w") as f:
        json.dump({"source": source, "charms": charms}, f, ensure_ascii=False, separators=(",", ":"))
    print(f"{len(creatures)} criaturas, {len(items)} items, {len(charms)} charms")


if __name__ == "__main__":
    main()
