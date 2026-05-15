"""Parse an FFXIV ACT/IINACT network log and print pulls in a given territory.

WARNING: parser is 100% vibe-coded, code is bad but it works

Usage:
    python parser.py <log_path> <territory_id_decimal>

Example:
    python parser.py ..\\logs\\Network_30108_20260504_TOP.log 1122

Territory IDs are decimal (1122 = TOP = 0x462). The log stores them in hex
on `01|` (ChangeZone) lines.

A "pull" is a combat segment that occurred while the local player was in the
selected territory. Combat boundaries come from `260|` (InCombat) lines: the
first state flag going 0->1 marks combat start, 1->0 marks combat end. A pull
is treated as a clear iff director subcommand 0x40000003 appears inside the
combat window (it fires once per duty completion, just before combat-end);
every other pull is a wipe.
"""

from __future__ import annotations

import argparse
import math
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from typing import Iterator

from filter import (
    FilterContext,
    get_dropped_change_keys,
    record_dropped_change_key,
    should_drop,
)


# Opcodes whose payload contains (actor-id, actor-name) pairs at known positions.
# Restricting to a whitelist avoids false-positives from buff/effect hex values
# that happen to start with '4' and be 8 chars long (e.g. IEEE-754 encoded floats).
EVENT_ID_FIELDS: dict[str, list[tuple[int, int]]] = {
    "03": [(2, 3)],                   # AddCombatant
    "04": [(2, 3)],                   # RemoveCombatant
    "20": [(2, 3), (6, 7)],           # StartsCasting: caster, target
    "21": [(2, 3), (6, 7)],           # NetworkAbility: source, target
    "22": [(2, 3), (6, 7)],           # NetworkAOEAbility: source, target
    "23": [(2, 3)],                   # NetworkCancelAbility: caster
    "24": [(2, 3), (17, 18)],         # NetworkDoT/HoT tick: source, target
    "25": [(2, 3), (4, 5)],           # NetworkDeath: target, source
    "26": [(5, 6), (7, 8)],           # NetworkBuff (apply): source, target
    "27": [(2, 3)],                   # HeadMarker: target (id+name) — wears the marker; source at [4] has no name field
    "30": [(5, 6), (7, 8)],           # NetworkBuffRemove: source, target (duration always 0.00)
    "33": [(2, -1)],                  # ActorControl (mostly director — actor at field 2)
    "34": [(2, 3), (4, 5)],           # NameToggle: actor, target (often the same)
    "35": [(2, 3), (4, 5)],           # Tether: source, target — major mechanic marker
    "37": [(2, 3)],                   # NetworkActionSync hp-update (filtered)
    "38": [(2, 3)],                   # State sync (filtered)
    "39": [(2, 3)],                   # Periodic actor hp/mp/pos ping (filtered)
    "42": [(2, 3)],                   # Actor status snapshot
    "264": [(2, -1)],                 # AbilityExtra: actor (no adjacent name)
    "267": [(2, -1)],                 # BattleTalk2: source NPC for boss dialog cues
    "270": [(2, -1)],                 # ActorMove: id only, no name
    "271": [(2, -1)],                 # ActorSetPos: id only, no name
    "272": [(2, -1)],                 # NpcSpawnExtra: actor at field 2
    "273": [(2, -1)],                 # ActorControlExtra: actor at field 2
    "261": [(3, -1)],                 # ActorChange snapshot (Add/Change/Remove); subject at parts[3], variable kv payload
    "263": [(2, -1)],                 # ActorCastExtra: ground-target xyz + heading after a 20|StartsCasting

    # 31 omitted — in this log it's a job-change / init event, not cancel-cast,
    # so we don't want false NPC references from its fields.
}

NPC_ID_RE = re.compile(r"^4[0-9a-fA-F]{7}$")


@dataclass
class Pull:
    index: int
    zone_id: int
    zone_name: str
    start: datetime
    end: datetime | None = None
    director_codes: list[int] = field(default_factory=list)

    @property
    def duration_s(self) -> float:
        if self.end is None:
            return 0.0
        return (self.end - self.start).total_seconds()

    @property
    def outcome(self) -> str:
        if self.end is None:
            return "open"
        if 0x40000003 in self.director_codes:
            return "clear"
        return "wipe"


def parse_timestamp(s: str) -> datetime:
    # IINACT logs use ISO-8601 with 7-digit fractional seconds; python supports up to 6.
    # Trim to 6 fractional digits to make fromisoformat happy on older interpreters.
    if "." in s:
        head, tail = s.split(".", 1)
        if "+" in tail or "-" in tail[1:]:
            for i, c in enumerate(tail):
                if i > 0 and c in "+-":
                    frac, tz = tail[:i], tail[i:]
                    tail = frac[:6] + tz
                    break
        else:
            tail = tail[:6]
        s = head + "." + tail
    return datetime.fromisoformat(s)


def iter_records(path: str) -> Iterator[list[str]]:
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            line = line.rstrip("\r\n")
            if not line:
                continue
            yield line.split("|")


def find_pulls(path: str, target_zone: int) -> list[Pull]:
    pulls: list[Pull] = []
    current_zone: int | None = None
    current_zone_name: str = ""
    in_combat: bool = False
    open_pull: Pull | None = None

    for parts in iter_records(path):
        opcode = parts[0]

        if opcode == "01" and len(parts) >= 4:
            try:
                current_zone = int(parts[2], 16)
            except ValueError:
                continue
            current_zone_name = parts[3]
            # Zone change while combat was "on" closes the open pull defensively.
            if open_pull is not None:
                open_pull.end = parse_timestamp(parts[1])
                pulls.append(open_pull)
                open_pull = None
                in_combat = False
            continue

        if current_zone != target_zone:
            continue

        if opcode == "260" and len(parts) >= 3:
            try:
                flag = int(parts[2])
            except ValueError:
                continue
            ts = parse_timestamp(parts[1])
            if flag == 1 and not in_combat:
                in_combat = True
                open_pull = Pull(
                    index=len(pulls) + 1,
                    zone_id=current_zone,
                    zone_name=current_zone_name,
                    start=ts,
                )
            elif flag == 0 and in_combat:
                in_combat = False
                if open_pull is not None:
                    open_pull.end = ts
                    pulls.append(open_pull)
                    open_pull = None
            continue

        if opcode == "33" and open_pull is not None and len(parts) >= 5:
            try:
                code = int(parts[3], 16)
            except ValueError:
                continue
            open_pull.director_codes.append(code)

    if open_pull is not None:
        pulls.append(open_pull)
    return pulls


def format_duration(seconds: float) -> str:
    if seconds <= 0:
        return "    -   "
    m, s = divmod(int(round(seconds)), 60)
    return f"{m:>3d}m {s:02d}s"


def format_rel(seconds: float) -> str:
    sign = "-" if seconds < 0 else "+"
    seconds = abs(seconds)
    m = int(seconds // 60)
    s = seconds - m * 60
    return f"{sign}{m:02d}:{s:05.2f}"


def format_win(seconds: float | None, begin_rel: float) -> str:
    """Format a pull-relative time as window-relative (subtract begin_rel)."""
    if seconds is None:
        return "    -    "
    return format_rel(seconds - begin_rel)


@dataclass
class NpcLife:
    obj_id: str
    base_id: int | None  # BNpcBase row id (stable per NPC type)
    name: str
    added: float | None = None  # seconds relative to pull start
    removed: float | None = None


@dataclass
class CastEvent:
    t_rel: float           # seconds relative to pull start
    src_name: str
    src_base_id: int | None
    spell: str
    cast_seconds: float
    count: int = 1         # number of simultaneous actors casting (e.g. TOP decoy bursts)


def _parse_int(s: str) -> int | None:
    try:
        return int(s)
    except (ValueError, TypeError):
        return None


def _parse_float(s: str) -> float | None:
    try:
        return float(s)
    except (ValueError, TypeError):
        return None


def parse_relative_time(s: str) -> float:
    """Accept "90", "90.5", "1:30", "1:30.5" — all return seconds (float)."""
    if ":" in s:
        m, rest = s.split(":", 1)
        return int(m) * 60 + float(rest)
    return float(s)


@dataclass
class ActiveNpc:
    obj_id: str
    base_id: int | None
    name: str
    first_seen: float                  # first in-window reference, rel to pull start
    last_seen: float                   # last in-window reference, rel to pull start
    spawned_at: float | None = None    # 03 that started this instance (or None)
    despawned_at: float | None = None  # next 03 or 04 for the actor (clean despawn or implicit re-spawn)
    # True when this row was synthesized from 261|Add/Remove because the actor
    # never produced a 03|AddCombatant — i.e. it's a Type|7 EventObject used
    # for tower/AOE visuals. Drives spawn-from-261|Add emission downstream.
    is_event_object: bool = False


@dataclass
class _Instance:
    """One spawn-despawn lifetime of a single actor id."""
    spawn: float | None
    despawn: float | None
    name: str
    base_id: int | None
    is_event_object: bool = False


def _build_instances(events: list[tuple[float, str, str, int | None]]) -> list[_Instance]:
    """events = chronological [(t_rel, opcode, name, base_id)]. Each `03` starts
    a new instance; that instance ends at the next `03` (implicit re-spawn) or
    the next `04` (clean despawn). Orphan `04`s (no preceding `03`) become an
    instance with spawn=None.

    Per-instance is the intentional unit of identity here: FFXIV pools actor
    ids, so a single id can cycle through several BNpc identities in one pull
    (e.g. an Omega decoy slot later becomes an Alpha Omega decoy). Those are
    distinct entities — do not merge them by id."""
    out: list[_Instance] = []
    open_inst: _Instance | None = None
    for t, op, name, base_id in events:
        if op == "03":
            if open_inst is not None:
                open_inst.despawn = t
                out.append(open_inst)
            open_inst = _Instance(spawn=t, despawn=None, name=name, base_id=base_id)
        elif op == "04":
            if open_inst is not None:
                open_inst.despawn = t
                out.append(open_inst)
                open_inst = None
            else:
                out.append(_Instance(spawn=None, despawn=t, name=name, base_id=base_id))
    if open_inst is not None:
        out.append(open_inst)
    return out


def _pick_instance(instances: list[_Instance], t_rel: float) -> _Instance | None:
    """Return the instance whose lifetime contains t_rel — i.e. spawn <= t_rel
    AND (despawn is None OR t_rel <= despawn). When an actor id is re-spawned
    at time t (implicit despawn = explicit spawn), the new instance wins thanks
    to the last-match policy: both old and new match at t_rel == t, we keep
    the latest. For a clean `04` at t, the only instance ending at t matches."""
    chosen: _Instance | None = None
    for inst in instances:
        if inst.spawn is not None and inst.spawn > t_rel:
            continue
        if inst.despawn is not None and t_rel > inst.despawn:
            continue
        chosen = inst
    return chosen


def collect_active_npcs(
    path: str, pull: Pull, window_start: datetime, window_end: datetime
) -> list[ActiveNpc]:
    """Return every enemy NPC actor referenced by any whitelisted event in
    [window_start, window_end]. Each row corresponds to one (actor id, spawn
    instance) — so if an actor id is reused mid-pull with a different BNpc
    identity and both identities are referenced in the window, they appear
    as two separate rows. Player pets are filtered out.

    On id reuse: actor ids in the 4xxxxxxx range are pooled by the game and
    handed back out to fresh entities after a despawn. Per-instance rows are
    the correct unit of identity — merging by id would conflate distinct
    entities that just happen to share a slot."""
    pet_ids: set[str] = set()
    # actor_id -> chronological [(t_rel, opcode, name_from_03_04, base_id)]
    events_for: dict[str, list[tuple[float, str, str, int | None]]] = {}
    # Same shape, but populated from 261|Add/Remove for EventObject discovery.
    # Merged into instances_by_actor below only for ids that have NO 03/04 —
    # real BattleNpcs use their 03 row as the authoritative spawn.
    eventobj_events_for: dict[str, list[tuple[float, str, str, int | None]]] = {}
    # (actor_id, t_rel, name_from_event) for every in-window reference.
    in_window_refs: list[tuple[str, float, str]] = []

    for parts in iter_records(path):
        if len(parts) < 2:
            continue
        opcode = parts[0]
        try:
            ts = parse_timestamp(parts[1])
        except ValueError:
            continue
        t_rel = (ts - pull.start).total_seconds()

        if opcode in ("03", "04") and len(parts) > 10:
            obj_id = parts[2]
            if NPC_ID_RE.match(obj_id):
                owner = parts[6] if len(parts) > 6 else ""
                if owner and owner.startswith("1"):
                    pet_ids.add(obj_id)
                else:
                    events_for.setdefault(obj_id, []).append(
                        (t_rel, opcode, parts[3], _parse_int(parts[10]))
                    )

        # 261|Add/Remove for Type|7 EventObjects (tower / AOE visuals). They
        # never appear in 03|, so this is the only spawn signal we have.
        # Tracked globally (no window check) so a spawn just outside the
        # window still resolves when in-window refs land inside the lifetime.
        if opcode == "261" and len(parts) > 4:
            mode_261 = parts[2]
            subject_id = parts[3]
            if NPC_ID_RE.match(subject_id):
                if mode_261 == "Add":
                    bnpc_id_int: int | None = None
                    owner = ""
                    pairs = parts[4:-1]
                    for i in range(0, len(pairs) - 1, 2):
                        k = pairs[i]
                        if k == "BNpcID":
                            try:
                                bnpc_id_int = int(pairs[i + 1], 16)
                            except ValueError:
                                bnpc_id_int = None
                        elif k == "OwnerID":
                            owner = pairs[i + 1]
                    # Player-owned (pet) → mirror the 03|AddCombatant pet rule.
                    if owner and owner.startswith("1"):
                        pet_ids.add(subject_id)
                    else:
                        name = f"EventObj_{bnpc_id_int:X}" if bnpc_id_int is not None else "EventObj"
                        eventobj_events_for.setdefault(subject_id, []).append(
                            (t_rel, "Add", name, bnpc_id_int)
                        )
                elif mode_261 == "Remove":
                    eventobj_events_for.setdefault(subject_id, []).append(
                        (t_rel, "Remove", "", None)
                    )

        if ts < window_start or ts > window_end:
            continue
        for id_idx, name_idx in EVENT_ID_FIELDS.get(opcode, ()):
            if id_idx >= len(parts):
                continue
            obj_id = parts[id_idx]
            if not NPC_ID_RE.match(obj_id):
                continue
            in_window_refs.append(
                (obj_id, t_rel, parts[name_idx] if 0 <= name_idx < len(parts) else "")
            )

    instances_by_actor: dict[str, list[_Instance]] = {
        k: _build_instances(v) for k, v in events_for.items() if k not in pet_ids
    }

    # Merge EventObject lifetimes for actors that produced 261|Add but no
    # 03|. _build_instances expects 03/04 opcodes — translate Add→03 and
    # Remove→04 so the same lifetime-pairing logic applies, then flag each
    # synth instance so downstream emit knows to spawn from 261|Add.
    for actor_id, evs in eventobj_events_for.items():
        if actor_id in pet_ids or actor_id in events_for:
            continue
        synth = [
            (t, "03" if m == "Add" else "04", name, base_id)
            for t, m, name, base_id in evs
        ]
        eo_instances = _build_instances(synth)
        for inst in eo_instances:
            inst.is_event_object = True
        if eo_instances:
            instances_by_actor[actor_id] = eo_instances

    # Map (actor_id, instance_index) -> ActiveNpc. Use "*" as the instance key
    # for actors referenced in-window with no 03/04 anywhere in the log.
    rows: dict[tuple[str, object], ActiveNpc] = {}
    for actor_id, t_rel, ref_name in in_window_refs:
        if actor_id in pet_ids:
            continue
        instances = instances_by_actor.get(actor_id, [])
        inst = _pick_instance(instances, t_rel) if instances else None
        if inst is None:
            key = (actor_id, "*")
            row = rows.get(key)
            if row is None:
                rows[key] = ActiveNpc(
                    obj_id=actor_id, base_id=None, name=ref_name,
                    first_seen=t_rel, last_seen=t_rel,
                )
            else:
                row.last_seen = t_rel
                if not row.name and ref_name:
                    row.name = ref_name
        else:
            key = (actor_id, instances.index(inst))
            row = rows.get(key)
            if row is None:
                rows[key] = ActiveNpc(
                    obj_id=actor_id, base_id=inst.base_id, name=inst.name,
                    first_seen=t_rel, last_seen=t_rel,
                    spawned_at=inst.spawn, despawned_at=inst.despawn,
                    is_event_object=inst.is_event_object,
                )
            else:
                row.last_seen = t_rel

    return sorted(rows.values(), key=lambda n: (n.first_seen, n.obj_id))


def _safe(parts: list[str], idx: int) -> str:
    return parts[idx] if 0 <= idx < len(parts) else ""


def _named(name: str, ident: str) -> str:
    """Render an actor / spell / status as `name[id]`, falling back gracefully
    when either side is missing."""
    if name and ident:
        return f"{name}[{ident}]"
    if name:
        return name
    if ident:
        return f"[{ident}]"
    return "?"


def _fmt_heading(h: str) -> str:
    """Render `h=rad (deg)`. FFXIV stores heading in radians; degrees are
    easier to scan for "is the boss facing N/E/S/W?". Falls back to the raw
    string if the value isn't a parseable float."""
    try:
        rad = float(h)
        return f"{h} ({math.degrees(rad):.1f} deg)"
    except (ValueError, TypeError):
        return h


PARTY_ROLES = (
    "MainTank", "OffTank", "RegenHealer", "ShieldHealer",
    "MeleeDpsA", "MeleeDpsB", "PhysRangedDps", "CasterDps",
)


def _csharp_const_name(name: str, id_: int) -> str:
    """Convert a free-form display name into a PascalCase C# identifier.
    Empty names fall back to `X_<idHex>`. Names whose first character isn't
    a letter get an `X_` prefix so the identifier is syntactically valid."""
    s = "".join(c if c.isalnum() else "_" for c in (name or ""))
    parts = [p for p in s.split("_") if p]
    if not parts:
        return f"X_{id_:X}"
    pieces = []
    for p in parts:
        if p[0].isalpha():
            pieces.append(p[0].upper() + p[1:])
        else:
            pieces.append(p)
    pascal = "".join(pieces)
    if not (pascal[0].isalpha() or pascal[0] == "_"):
        pascal = "X_" + pascal
    return pascal


# Per-constant-category C# type and literal-format. Categories are emitted as
# nested static classes inside the generated `Constants` block.
_CONST_TYPES: dict[str, tuple[str, str]] = {
    "BNpcBaseId":  ("uint", "{}"),
    "BNpcNameId":  ("uint", "{}"),
    "EObjId":      ("uint", "{}"),
    "ActionId":    ("uint",   "0x{:X}U"),
    "StatusId":    ("ushort", "(ushort)0x{:X}"),
    "TetherId":    ("ushort", "(ushort)0x{:X}"),
    "TimelineId":  ("ushort", "(ushort)0x{:X}"),
    "LockonId":    ("uint",   "{}"),
    # Knockback sheet row id, named after the action that produced it. Used by
    # the `world.Party.Knockback(source, knockbackId)` overload.
    "KnockbackId": ("uint",   "{}"),
}


class ConstantsBuilder:
    """Collects (category, id, name) tuples seen while emitting actions and
    renders them as a nested static `Constants` class. `register` returns the
    C# identifier (with the `Constants.<cat>.` prefix) so callers can splice
    it into the generated call sites directly."""

    def __init__(self) -> None:
        # category -> id (int) -> sanitized C# identifier
        self.cats: dict[str, dict[int, str]] = {}

    def register(self, cat: str, id_: int, name: str) -> str:
        d = self.cats.setdefault(cat, {})
        existing = d.get(id_)
        if existing is not None:
            return f"Constants.{cat}.{existing}"
        sanitized = _csharp_const_name(name, id_)
        taken = set(d.values())
        if sanitized in taken:
            sanitized = f"{sanitized}_{id_:X}"
        d[id_] = sanitized
        return f"Constants.{cat}.{sanitized}"

    def emit_lines(self) -> list[str]:
        out: list[str] = []
        any_emitted = False
        for cat, (ctype, fmt) in _CONST_TYPES.items():
            entries = self.cats.get(cat)
            if not entries:
                continue
            if any_emitted:
                out.append("")
            any_emitted = True
            out.append(f"        public static class {cat}")
            out.append("        {")
            for id_, name in sorted(entries.items(), key=lambda kv: kv[1]):
                out.append(f"            public const {ctype} {name} = {fmt.format(id_)};")
            out.append("        }")
        if not any_emitted:
            return []
        return ["    public static class Constants", "    {", *out, "    }"]


def _csharp_id(name: str, obj_id: str) -> str:
    """Sanitise a BNpc display name + actor id into a unique C# identifier
    suitable for both a field and a method-name suffix. Empty/unknown names
    fall back to `npc`."""
    base = "".join(c if (c.isalnum() or c == "_") else "_" for c in (name or "npc"))
    base = base.lstrip("_") or "npc"
    # Lowercase the first letter so fields read naturally (omega_4000A3E8).
    base = base[0].lower() + base[1:]
    return f"{base}_{obj_id}"


def _csharp_vec3_local(x: str, y: str, z: str, cx: float, cy: float) -> str:
    """Log (PosX, PosY, PosZ) → scenario-local C# Vector3(X, Y, Z) where C#'s
    Y axis is the log's PosZ (height) and C#'s Z axis is the log's PosY
    (north/south). Origin (`--x`, `--y`) is subtracted from X and Z. Used for
    SpawnEnemy/SpawnEventObject Placement, SetPosition (both overloads), and
    Cast targetLocation — every SimXxx position-bearing API speaks local."""
    try:
        return (f"new Vector3({float(x) - cx:.3f}f, {float(z):.3f}f, "
                f"{float(y) - cy:.3f}f)")
    except (ValueError, TypeError):
        return "new Vector3(0f, 0f, 0f)"


def _csharp_float(s: str, default: str = "0f") -> str:
    try:
        return f"{float(s):.3f}f"
    except (ValueError, TypeError):
        return default


def _fmt_actor_control(parts: list[str]) -> str:
    """Render the `[code] params=...` body shared by `33|` (ActorControl) and
    director-sourced ActorControl in the instance timeline."""
    code = _safe(parts, 3)
    params = [p for p in (_safe(parts, i) for i in range(4, 8)) if p and p != "00"]
    params_str = (" params=" + ",".join(params)) if params else ""
    return f"[{code}]{params_str}"


def _fmt_pos(x: str, y: str, z: str, cx: float, cy: float) -> str:
    """Format a world-space position with X and Y offset by the arena center.
    Z is left as-is (height in FFXIV coords). Falls back to the raw strings
    if either x or y can't be parsed as float."""
    try:
        xv = float(x) - cx
        yv = float(y) - cy
        return f"({xv:.4f}, {yv:.4f}, {z})"
    except (ValueError, TypeError):
        return f"({x}, {y}, {z})"


def format_event_for_npc(
    opcode: str,
    parts: list[str],
    npc_id: str,
    center_x: float = 0.0,
    center_y: float = 0.0,
) -> str | None:
    """Render one log row from the NPC's point of view, or None if the row
    is malformed / has no representation. Filtering is upstream — callers
    must check `filter.should_drop` first."""
    if opcode == "03":
        level_hex = _safe(parts, 5)
        bnpc_name = _safe(parts, 9)
        bnpc_base = _safe(parts, 10)
        hp = _safe(parts, 11)
        max_hp = _safe(parts, 12)
        pos_x = _safe(parts, 17)
        pos_y = _safe(parts, 18)
        pos_z = _safe(parts, 19)
        heading = _safe(parts, 20)
        meta = []
        if max_hp:
            meta.append(f"hp {hp}/{max_hp}")
        if level_hex:
            try:
                meta.append(f"lvl={int(level_hex, 16)}")
            except ValueError:
                meta.append(f"lvl={level_hex}")
        if bnpc_name:
            meta.append(f"bnpc={bnpc_name}")
        if bnpc_base:
            meta.append(f"base={bnpc_base}")
        pos_str = ""
        if pos_x and pos_y:
            pos_str = f" at {_fmt_pos(pos_x, pos_y, pos_z, center_x, center_y)}"
            if heading:
                pos_str += f" h={_fmt_heading(heading)}"
        return "spawned" + (f" ({', '.join(meta)})" if meta else "") + pos_str
    if opcode == "04":
        bnpc_name = _safe(parts, 9)
        bnpc_base = _safe(parts, 10)
        hp = _safe(parts, 11)
        max_hp = _safe(parts, 12)
        meta = []
        if max_hp:
            meta.append(f"hp {hp}/{max_hp}")
        if bnpc_name:
            meta.append(f"bnpc={bnpc_name}")
        if bnpc_base:
            meta.append(f"base={bnpc_base}")
        return "despawned" + (f" ({', '.join(meta)})" if meta else "")
    if opcode in ("20", "21", "22"):
        src_id, src_name = _safe(parts, 2), _safe(parts, 3)
        spell_id, spell_name = _safe(parts, 4), _safe(parts, 5)
        tgt_id, tgt_name = _safe(parts, 6), _safe(parts, 7)
        verb = "starts casting" if opcode == "20" else "uses"
        spell_str = _named(spell_name, spell_id)
        cast_suffix = ""
        if opcode == "20":
            cast_time = _parse_float(_safe(parts, 8))
            if cast_time and cast_time > 0:
                cast_suffix = f" ({cast_time:.1f}s)"
        # 21/22 resolution carries the target's hp at the moment the hit landed.
        hp_suffix = ""
        if opcode in ("21", "22"):
            tgt_hp = _safe(parts, 24)
            tgt_max = _safe(parts, 25)
            if tgt_hp and tgt_max:
                hp_suffix = f" [tgt hp {tgt_hp}/{tgt_max}]"
        if src_id == npc_id:
            target = "self" if tgt_id == npc_id else _named(tgt_name, tgt_id)
            return f"{verb} {spell_str}{cast_suffix} -> {target}{hp_suffix}"
        return f"{verb} {spell_str}{cast_suffix} <- {_named(src_name, src_id)}{hp_suffix}"
    if opcode == "23":
        actor_id, actor_name = _safe(parts, 2), _safe(parts, 3)
        spell_id, spell_name = _safe(parts, 4), _safe(parts, 5)
        reason = _safe(parts, 6)
        spell_str = _named(spell_name, spell_id)
        reason_str = f" ({reason})" if reason else ""
        if actor_id == npc_id:
            return f"cancelled cast of {spell_str}{reason_str}"
        return f"cancelled cast of {spell_str}{reason_str} by {_named(actor_name, actor_id)}"
    if opcode == "34":
        actor_id, actor_name = _safe(parts, 2), _safe(parts, 3)
        tgt_id, tgt_name = _safe(parts, 4), _safe(parts, 5)
        state = _safe(parts, 6)
        if tgt_id and tgt_id != actor_id:
            return f"nameplate toggle state={state} on {_named(tgt_name, tgt_id)}"
        return f"nameplate toggle state={state}"
    if opcode == "35":
        src_id, src_name = _safe(parts, 2), _safe(parts, 3)
        tgt_id, tgt_name = _safe(parts, 4), _safe(parts, 5)
        tether_id = _safe(parts, 8)
        # Tether ids vary; surface raw hex so the user can recognise mechanics.
        tag = f"[{tether_id}]" if tether_id else ""
        if src_id == npc_id:
            return f"tether{tag} -> {_named(tgt_name, tgt_id)}"
        return f"tether{tag} <- {_named(src_name, src_id)}"
    if opcode == "27":
        # HeadMarker / Lockon. Layout: [2]=tgtId, [3]=tgtName, [4]=srcId,
        # [5]=0000, [6]=iconHex (Lockon sheet row). Target wears the marker;
        # we only extract target via EVENT_ID_FIELDS so npc_id == tgt_id here,
        # but render symmetrically in case that ever changes.
        tgt_id, tgt_name = _safe(parts, 2), _safe(parts, 3)
        src_id = _safe(parts, 4)
        icon = _safe(parts, 6)
        if tgt_id == npc_id:
            return f"head marker [{icon}] from {src_id}"
        return f"head marker [{icon}] on {_named(tgt_name, tgt_id)}"
    if opcode == "267":
        src_id = _safe(parts, 2)
        director_id = _safe(parts, 3)
        codes = "|".join(_safe(parts, i) for i in range(4, 7) if _safe(parts, i))
        return f"battle dialog [{codes}] director={director_id}"
    if opcode == "33":
        # ActorControl: director-level commands (engage, defeat, recommence,
        # timer broadcasts, etc.). Codes are 4-byte hex.
        return "actor control " + _fmt_actor_control(parts)
    if opcode == "42":
        # Actor status snapshot — trailing hex tuples (statusId, valueFloat,
        # sourceId). Field count varies; surface raw trailing data trimmed.
        actor_id, actor_name = _safe(parts, 2), _safe(parts, 3)
        tail = [_safe(parts, i) for i in range(4, len(parts) - 1)]
        tail = [t for t in tail if t]
        snapshot = " ".join(tail[:9]) + ("..." if len(tail) > 9 else "")
        return f"status snapshot {snapshot}" if snapshot else "status snapshot"
    if opcode == "263":
        # ActorCastExtra: ground-target xyz + heading that follows a 20|StartsCasting.
        # Layout: parts[2]=actorId, [3]=spellId hex, [4..6]=x/y/z, [7]=heading rad.
        spell_id = _safe(parts, 3)
        pos_x, pos_y, pos_z = _safe(parts, 4), _safe(parts, 5), _safe(parts, 6)
        heading = _safe(parts, 7)
        pos_str = _fmt_pos(pos_x, pos_y, pos_z, center_x, center_y) if pos_x else "?"
        head_str = f" h={_fmt_heading(heading)}" if heading else ""
        return f"cast target spell=[{spell_id}] pos={pos_str}{head_str}"
    if opcode == "264":
        # AbilityExtra: diagnostic info tied to a recent ability — code,
        # sequence, a float (heading or remaining cast), and a repeated
        # actor id at the end.
        code = _safe(parts, 3)
        seq = _safe(parts, 4)
        value = _safe(parts, 9)
        ref = _safe(parts, 10)
        bits = []
        if seq:
            bits.append(f"seq={seq}")
        if value:
            bits.append(f"v={value}")
        if ref:
            bits.append(f"ref={ref}")
        return f"ability extra [{code}]" + (" " + " ".join(bits) if bits else "")
    if opcode == "272":
        # NpcSpawnExtra: associates an NPC with a parent/owner and some
        # spawn-state flags. Most fields are zero in TOP.
        parent = _safe(parts, 3)
        state1 = _safe(parts, 4)
        state2 = _safe(parts, 5)
        bits = []
        if parent and parent != "E0000000":
            bits.append(f"parent={parent}")
        if state1 and state1 != "0000":
            bits.append(f"s1={state1}")
        if state2 and state2 != "00":
            bits.append(f"s2={state2}")
        return "npc spawn extra" + (" " + " ".join(bits) if bits else "")
    if opcode == "273":
        # ActorControlExtra: per-actor control with a 4-byte hex code and up
        # to 4 small param fields. Common in boss state changes.
        code = _safe(parts, 3)
        params = [p for p in (_safe(parts, i) for i in range(4, 8)) if p and p != "0"]
        return f"actor control extra [{code}]" + (" params=" + ",".join(params) if params else "")
    if opcode == "261":
        # ActorChange: IINACT's per-actor state-sync delta. Mode in parts[2]
        # (Add/Change/Remove); subject id in parts[3]; the body is variable
        # `key|value` pairs from parts[4] up to parts[-2] (last is a content
        # hash). Whole-packet drops live in filter.should_drop; here we only
        # render kept packets and silently suppress the per-key drop list,
        # recording suppressed keys via filter.record_dropped_change_key so
        # callers can surface a single distinct-keys summary at the end.
        mode = _safe(parts, 2)
        if mode == "Remove":
            return "scope leave"
        pairs = parts[4:-1]
        if len(pairs) % 2 != 0:
            # Truncated / malformed — surface raw tail rather than guess.
            return f"{mode.lower()} {' '.join(pairs)}"
        items: list[str] = []
        pos_x = pos_y = pos_z = None
        for i in range(0, len(pairs), 2):
            key, value = pairs[i], pairs[i + 1]
            if key in ("MaxHP", "CurrentHP", "Heading", "CastBuff",
                       "NPCTargetID", "PCTargetID", "CurrentMP"):
                record_dropped_change_key(key)
                continue
            if key == "PosX":
                pos_x = value; continue
            if key == "PosY":
                pos_y = value; continue
            if key == "PosZ":
                pos_z = value; continue
            if key == "ModelStatus":
                # ModelStatus is a flag bitfield — always show it as hex.
                # IINACT logs it in decimal; convert here. Known labels:
                #   0x0    = fully visible
                #   0x4000 = goes invisible (fade-out flag)
                try:
                    raw = f"0x{int(value):X}"
                except ValueError:
                    raw = value
                label = {"0x0": "visible", "0x4000": "invisible"}.get(raw, raw)
                items.append(f"ModelStatus={label}")
                continue
            items.append(f"{key}={value}")
        if pos_x is not None or pos_y is not None:
            items.append(
                "pos=" + _fmt_pos(pos_x or "0", pos_y or "0", pos_z or "0", center_x, center_y)
            )
        if not items:
            return None
        verb = "scope enter" if mode == "Add" else "change"
        return f"{verb} " + ", ".join(items)
    if opcode == "25":
        tgt_id, tgt_name = _safe(parts, 2), _safe(parts, 3)
        src_id, src_name = _safe(parts, 4), _safe(parts, 5)
        if tgt_id == npc_id:
            return f"killed by {_named(src_name, src_id)}"
        return f"killed {_named(tgt_name, tgt_id)}"
    if opcode == "26":
        status_id, status_name = _safe(parts, 2), _safe(parts, 3)
        duration = _safe(parts, 4)
        src_id, src_name = _safe(parts, 5), _safe(parts, 6)
        tgt_id, tgt_name = _safe(parts, 7), _safe(parts, 8)
        stacks_hex = _safe(parts, 9)
        suffix = f" ({duration}s)" if duration else ""
        # Stack count is hex; surface only when non-trivial so single-stack debuffs stay quiet.
        if stacks_hex:
            try:
                stacks = int(stacks_hex, 16)
            except ValueError:
                stacks = 0
            if stacks > 1:
                suffix += f" x{stacks}"
        status_str = _named(status_name, status_id)
        if tgt_id == npc_id:
            return f"gained status {status_str}{suffix} from {_named(src_name, src_id)}"
        return f"applied status {status_str}{suffix} to {_named(tgt_name, tgt_id)}"
    if opcode == "30":
        status_id, status_name = _safe(parts, 2), _safe(parts, 3)
        src_id, src_name = _safe(parts, 5), _safe(parts, 6)
        tgt_id, tgt_name = _safe(parts, 7), _safe(parts, 8)
        status_str = _named(status_name, status_id)
        if tgt_id == npc_id:
            return f"lost status {status_str} (source {_named(src_name, src_id)})"
        return f"removed status {status_str} from {_named(tgt_name, tgt_id)}"
    if opcode in ("270", "271"):
        actor_id = _safe(parts, 2)
        if actor_id != npc_id:
            return None
        heading = _safe(parts, 3)
        pos_x = _safe(parts, 6)
        pos_y = _safe(parts, 7)
        pos_z = _safe(parts, 8)
        verb = "moved to" if opcode == "270" else "set position to"
        pos_str = _fmt_pos(pos_x, pos_y, pos_z, center_x, center_y) if pos_x else "?"
        head_str = f" h={_fmt_heading(heading)}" if heading else ""
        return f"{verb} {pos_str}{head_str}"
    return None


def collect_extended_logs(
    path: str,
    pull: Pull,
    npcs: list[ActiveNpc],
    end_rel: float,
    center_x: float = 0.0,
    center_y: float = 0.0,
) -> dict[int, list[tuple[float, str, int, str]]]:
    """For every ActiveNpc instance in `npcs`, gather events from the log that
    reference the actor id AND fall within that instance's lifetime AND have
    t_rel <= end_rel. Returns a map keyed by `id(npc)` so each instance gets
    its own bucket as a list of `(t_rel, description, count)`. 270/271 events
    are coalesced when their position (and, for 271, heading) match the most
    recent 270/271 of the same kind for the same NPC — the first timestamp
    wins, `count` records how many were folded in. The key shape matches what
    the --code emitter renders so --raw and --code stay in sync. Player → NPC
    damage events are filtered by the formatter."""
    # actor id -> list of ActiveNpc rows for that id (one per instance)
    by_actor_id: dict[str, list[ActiveNpc]] = {}
    for npc in npcs:
        by_actor_id.setdefault(npc.obj_id, []).append(npc)
    logs: dict[int, list[list]] = {id(n): [] for n in npcs}
    # Last emitted 270/271 (pos+heading) per NPC instance and the index of
    # that entry in the bucket — used to coalesce repeats even when other
    # events arrive between them.
    last_pos: dict[int, tuple[tuple[str, ...], int]] = {}
    # Pets are detected on the fly from `03|` lines with a player owner.
    # Once an id is in here, the formatter filters its outgoing damage /
    # debuffs / tethers on the boss.
    pet_ids: set[str] = set()

    def pick(rows: list[ActiveNpc], t_rel: float) -> ActiveNpc | None:
        chosen: ActiveNpc | None = None
        for r in rows:
            lo = r.spawned_at if r.spawned_at is not None else float("-inf")
            hi = r.despawned_at if r.despawned_at is not None else float("inf")
            if lo <= t_rel <= hi:
                chosen = r
        return chosen

    def pick_for_add(rows: list[ActiveNpc], t_rel: float) -> ActiveNpc | None:
        """IINACT's 261|Add fires a few ms BEFORE the matching 03|AddCombatant,
        so strict-lifetime `pick` always drops it. Route every Add to its
        instance instead: try the strict lifetime first (covers out-of-order
        logs), then the next instance whose spawn is at or after t_rel, then
        as a last resort the instance with the nearest spawn time. Adds must
        never be silently lost."""
        chosen = pick(rows, t_rel)
        if chosen is not None:
            return chosen
        upcoming = [r for r in rows if r.spawned_at is not None and r.spawned_at >= t_rel]
        if upcoming:
            return min(upcoming, key=lambda r: r.spawned_at)
        spawned = [r for r in rows if r.spawned_at is not None]
        if spawned:
            return min(spawned, key=lambda r: abs(r.spawned_at - t_rel))
        return rows[0] if rows else None

    COALESCE_OPS = {"270", "271"}

    for parts in iter_records(path):
        if len(parts) < 2:
            continue
        opcode = parts[0]
        try:
            ts = parse_timestamp(parts[1])
        except ValueError:
            continue
        t_rel = (ts - pull.start).total_seconds()
        # Always track pet detection, regardless of the time window — that
        # way a 03 that came in earlier in the log still tags the actor as a
        # pet for later filtering. (Pet ids are detected from any 03 with a
        # player owner.)
        if opcode == "03" and len(parts) > 6:
            obj_id_03 = parts[2]
            owner = parts[6]
            if NPC_ID_RE.match(obj_id_03) and owner and owner.startswith("1"):
                pet_ids.add(obj_id_03)
        if t_rel > end_rel:
            continue
        matched: set[int] = set()
        pet_ids_snapshot = frozenset(pet_ids)
        for id_idx, _ in EVENT_ID_FIELDS.get(opcode, ()):
            if id_idx >= len(parts):
                continue
            obj_id = parts[id_idx]
            rows = by_actor_id.get(obj_id)
            if not rows:
                continue
            if should_drop(opcode, parts, FilterContext(npc_id=obj_id, pet_ids=pet_ids_snapshot)):
                continue
            if opcode == "261" and _safe(parts, 2) == "Add":
                row = pick_for_add(rows, t_rel)
            else:
                row = pick(rows, t_rel)
            if row is None:
                continue
            key = id(row)
            if key in matched:
                continue
            desc = format_event_for_npc(opcode, parts, obj_id, center_x, center_y)
            if desc is None:
                continue
            # Drop the timestamp (field 1) from raw — we already show a
            # relative time on the parsed line, so the absolute timestamp is noise.
            raw = "|".join(parts[:1] + parts[2:])
            bucket = logs[key]
            if opcode in COALESCE_OPS:
                # The coalesce key mirrors what each emitter renders so --raw
                # and --code stay in sync: 270 emits SetPosition(Vector3) (position
                # only), 271 emits SetPosition(Placement(Vector3, heading)) (position
                # + heading). If heading were in the 270 key, consecutive 270
                # packets at the same position would survive as redundant
                # SetPosition calls in --code.
                if opcode == "270":
                    pos_key = (
                        _safe(parts, 6),
                        _safe(parts, 7),
                        _safe(parts, 8),
                    )
                else:
                    pos_key = (
                        _safe(parts, 6),
                        _safe(parts, 7),
                        _safe(parts, 8),
                        _safe(parts, 3),
                    )
                prev = last_pos.get(key)
                if prev is not None and prev[0] == pos_key:
                    bucket[prev[1]][2] += 1
                    matched.add(key)
                    continue
                bucket.append([t_rel, desc, 1, raw])
                last_pos[key] = (pos_key, len(bucket) - 1)
            else:
                bucket.append([t_rel, desc, 1, raw])
            matched.add(key)
    return {k: [(t, d, c, r) for t, d, c, r in v] for k, v in logs.items()}


INSTANCE_OPCODES = ("257", "258", "259", "268", "269", "33", "35")


def format_instance_event(opcode: str, parts: list[str]) -> str | None:
    """Render an instance-level packet (map effect, director updates, countdown).
    Returns None when the line should be skipped — currently used to drop 33
    lines whose source is an NPC/player (those are already in the per-NPC dump)."""
    if opcode == "257":
        # MapEffect: parts[2]=sourceId, [3]=flags (8-hex), [4]=index (2-hex).
        # See reference_map_effect_arena.md for the TOP-specific decoding.
        source = _safe(parts, 2)
        flags = _safe(parts, 3)
        index = _safe(parts, 4)
        return f"mapeffect [{flags}] index={index} source={source}"
    if opcode == "258" or opcode == "259":
        # FateDirector / CEDirector: parts[2]=mode (Update), [3..]=hex fields.
        # Drop trailing all-zero fields to keep the line readable.
        label = "fate director" if opcode == "258" else "ce director"
        mode = _safe(parts, 2)
        tail = [_safe(parts, i) for i in range(3, len(parts) - 1)]
        while tail and tail[-1] in ("", "00000000", "0000", "00", "0"):
            tail.pop()
        body = " ".join(tail) if tail else ""
        return f"{label} {mode}" + (f": {body}" if body else "")
    if opcode == "268":
        # Countdown: parts[2]=sourceId, [3]=type-flags, [4]=seconds (decimal),
        # [5]=00, [6]=name.
        source = _safe(parts, 2)
        seconds = _safe(parts, 4)
        name = _safe(parts, 6)
        return f"countdown by {_named(name, source)} seconds={seconds}"
    if opcode == "269":
        # CountdownCancel: parts[2]=sourceId, [3]=type, [4]=name.
        source = _safe(parts, 2)
        name = _safe(parts, 4)
        return f"countdown cancelled by {_named(name, source)}"
    if opcode == "33":
        # ActorControl from the duty director — non-director-sourced rows
        # are filtered upstream via should_drop's instance view.
        source = _safe(parts, 2)
        return f"director {_fmt_actor_control(parts)} source={source}"
    if opcode == "35":
        # Player-to-player tether. NPC-involving tethers already render
        # inside the owning NPC's extended dump (via _canonical_owner_for),
        # so suppress them here to avoid duplicates.
        src_id, src_name = _safe(parts, 2), _safe(parts, 3)
        tgt_id, tgt_name = _safe(parts, 4), _safe(parts, 5)
        if NPC_ID_RE.match(src_id) or NPC_ID_RE.match(tgt_id):
            return None
        tether_id = _safe(parts, 8)
        tag = f"[{tether_id}]" if tether_id else ""
        return f"tether{tag} {_named(src_name, src_id)} <-> {_named(tgt_name, tgt_id)}"
    return None


PLAYER_ID_RE = re.compile(r"^1[0-9a-fA-F]{7}$")


def collect_player_role_map(
    path: str, pull: Pull, begin_rel: float, end_rel: float
) -> dict[str, tuple[str, str]]:
    """Walk the log once and assign every distinct player id seen inside
    [begin_rel, end_rel] to a PartyRole in first-seen order. Returns
    `id -> (role_enum_name, display_name)` for at most 8 ids; subsequent
    players fall through to bare ids in the emitted code."""
    out: dict[str, tuple[str, str]] = {}
    for parts in iter_records(path):
        if len(parts) < 2:
            continue
        opcode = parts[0]
        try:
            ts = parse_timestamp(parts[1])
        except ValueError:
            continue
        t_rel = (ts - pull.start).total_seconds()
        if t_rel < begin_rel or t_rel > end_rel:
            continue
        # Pull (id, name) pairs from the same whitelist the per-NPC dump uses
        # so we catch players as source/target of casts, buffs, tethers, etc.
        for id_idx, name_idx in EVENT_ID_FIELDS.get(opcode, ()):
            if id_idx >= len(parts):
                continue
            obj_id = parts[id_idx]
            if not PLAYER_ID_RE.match(obj_id):
                continue
            if obj_id in out or len(out) >= len(PARTY_ROLES):
                continue
            name = parts[name_idx] if 0 <= name_idx < len(parts) else ""
            out[obj_id] = (PARTY_ROLES[len(out)], name)
    return out


def _player_role_expr(obj_id: str, roles: dict[str, tuple[str, str]]) -> str | None:
    """Return the `party.Get(PartyRole.X)` expression for `obj_id`, or None
    when the id isn't a known player slot."""
    role = roles.get(obj_id)
    if role is None:
        return None
    return f"party.Get(PartyRole.{role[0]})"


def collect_player_status_applies(
    path: str, pull: Pull, begin_rel: float, end_rel: float
) -> list[tuple[float, list[str], str]]:
    """Return (t_rel, parts, raw) for every 26|NetworkBuff in [begin_rel, end_rel]
    whose target is a player. Used to find statuses bundled with player tethers —
    these `26|` rows otherwise go nowhere (no NPC owner, not an instance opcode)."""
    out: list[tuple[float, list[str], str]] = []
    for parts in iter_records(path):
        if len(parts) < 2 or parts[0] != "26":
            continue
        try:
            ts = parse_timestamp(parts[1])
        except ValueError:
            continue
        t_rel = (ts - pull.start).total_seconds()
        if t_rel < begin_rel or t_rel > end_rel:
            continue
        tgt_id = _safe(parts, 7)
        if not PLAYER_ID_RE.match(tgt_id):
            continue
        raw = "|".join(parts[:1] + parts[2:])
        out.append((t_rel, parts, raw))
    return out


def collect_player_status_removes(
    path: str, pull: Pull, begin_rel: float, end_rel: float
) -> list[tuple[float, list[str], str]]:
    """Return (t_rel, parts, raw) for every 30|NetworkBuffRemove in
    [begin_rel, end_rel] whose target is a player and whose duration field is
    "0.00" (the canonical removal marker). Used by Run_OtherDebuffs to emit
    `RemoveStatus` calls for duty-director-driven removals (source `E0000000`)
    — the symmetric counterpart of `collect_player_status_applies`."""
    out: list[tuple[float, list[str], str]] = []
    for parts in iter_records(path):
        if len(parts) < 2 or parts[0] != "30":
            continue
        try:
            ts = parse_timestamp(parts[1])
        except ValueError:
            continue
        t_rel = (ts - pull.start).total_seconds()
        if t_rel < begin_rel or t_rel > end_rel:
            continue
        if _safe(parts, 4) != "0.00":
            continue
        tgt_id = _safe(parts, 7)
        if not PLAYER_ID_RE.match(tgt_id):
            continue
        raw = "|".join(parts[:1] + parts[2:])
        out.append((t_rel, parts, raw))
    return out


def collect_player_lockons(
    path: str, pull: Pull, begin_rel: float, end_rel: float
) -> list[tuple[float, list[str], str]]:
    """Return (t_rel, parts, raw) for every 27|HeadMarker in [begin_rel, end_rel]
    whose target is a player. NPC-targeted head markers are routed into the
    owning NPC's Run_<...>() method by the standard per-NPC pipeline; player
    targets have no NPC owner and so live in their own Run_PlayerLockons()
    method — the lockon analogue of Run_PlayerTethers / Run_OtherDebuffs."""
    out: list[tuple[float, list[str], str]] = []
    for parts in iter_records(path):
        if len(parts) < 2 or parts[0] != "27":
            continue
        try:
            ts = parse_timestamp(parts[1])
        except ValueError:
            continue
        t_rel = (ts - pull.start).total_seconds()
        if t_rel < begin_rel or t_rel > end_rel:
            continue
        tgt_id = _safe(parts, 2)
        if not PLAYER_ID_RE.match(tgt_id):
            continue
        raw = "|".join(parts[:1] + parts[2:])
        out.append((t_rel, parts, raw))
    return out


def _bundled_status_applies(
    t: float, src_id: str, tgt_id: str,
    applies: list[tuple[float, list[str], str]],
    window: float = 0.1,
) -> list[tuple[list[str], str]]:
    """Find `26|` status applies bundled with a player tether: target is one of
    the tether ends, timestamp within `window` seconds. Confirmed via TOP P5
    (Hello World tether 0xDE) where both ends receive Mid Glitch at the same
    packet timestamp as the tether."""
    out: list[tuple[list[str], str]] = []
    for sa_t, sa_parts, sa_raw in applies:
        if abs(sa_t - t) > window:
            continue
        sa_tgt = _safe(sa_parts, 7)
        if sa_tgt not in (src_id, tgt_id):
            continue
        out.append((sa_parts, sa_raw))
    return out


def collect_instance_events(
    path: str, pull: Pull, begin_rel: float, end_rel: float
) -> list[tuple[float, str, str]]:
    """Walk the log once and return (t_rel, description, raw) for every
    instance/director packet in [begin_rel, end_rel] of `pull`."""
    out: list[tuple[float, str, str]] = []
    for parts in iter_records(path):
        if len(parts) < 2:
            continue
        opcode = parts[0]
        if opcode not in INSTANCE_OPCODES:
            continue
        try:
            ts = parse_timestamp(parts[1])
        except ValueError:
            continue
        t_rel = (ts - pull.start).total_seconds()
        if t_rel < begin_rel or t_rel > end_rel:
            continue
        if should_drop(opcode, parts, FilterContext()):
            continue
        desc = format_instance_event(opcode, parts)
        if desc is None:
            continue
        # Drop the absolute timestamp from raw — window-relative is already shown.
        raw = "|".join(parts[:1] + parts[2:])
        out.append((t_rel, desc, raw))
    return out


def print_instance_events(
    events: list[tuple[float, str, str]], begin_rel: float, show_raw: bool = False
) -> None:
    print()
    print(f"=== Instance events ({len(events)}) ===")
    if not events:
        return
    for t, desc, raw in events:
        prefix = "//" if t - begin_rel < 0 else "  "
        print(f"{prefix}{format_win(t, begin_rel):>9}  {desc}")
        if show_raw:
            print(f"{prefix}           | {raw}")


# ── C# scenario code emission (`--code` mode) ──────────────────────────────────
#
# Each opcode's "primary subject" — the actor id whose Run_<...>() method owns
# the event. Multi-actor events (26 source/target, 35 source/target) are emitted
# once, from the primary side; the secondary side is suppressed to avoid dupes.
_PER_NPC_PRIMARY = {
    "03": 2, "04": 2, "20": 2, "21": 2, "22": 2, "23": 2, "26": 5, "27": 2,
    "34": 4, "35": 2, "261": 3, "263": 2, "270": 2, "271": 2, "273": 2,
}


def _reparse_raw(raw: str) -> list[str]:
    """Reverse the timestamp-dropping trick used when raw lines were stored
    in `collect_extended_logs`. Re-inserts an empty placeholder at index 1 so
    field offsets line up with the original parts list."""
    chunks = raw.split("|")
    return [chunks[0], ""] + chunks[1:]


def _build_field_names(npcs: list[ActiveNpc]) -> dict[int, str]:
    """`id(npc)` → C# field name. Disambiguate by suffixing `_<i>` when an
    actor id hosts multiple identities in the window (pooled id reuse, e.g.
    Omega → Alpha Omega on the same id)."""
    by_id: dict[str, list[ActiveNpc]] = {}
    for npc in npcs:
        by_id.setdefault(npc.obj_id, []).append(npc)
    out: dict[int, str] = {}
    for obj_id, instances in by_id.items():
        for i, npc in enumerate(instances):
            base = _csharp_id(npc.name, npc.obj_id)
            out[id(npc)] = f"{base}_{i}" if len(instances) > 1 else base
    return out


def _method_name(field: str) -> str:
    return "Run_" + field[0].upper() + field[1:]


def _format_csharp_target(
    obj_id: str,
    roles: dict[str, tuple[str, str]],
    owner_id: str,
    owner_field: str,
) -> str | None:
    """Resolve an actor id to a C# expression usable inside `owner`'s method.
    Players → `party.Get(PartyRole.X)`; the owner itself → its local variable;
    other NPCs → None (out of scope under method-local NPC variables)."""
    if obj_id == owner_id:
        return owner_field
    return _player_role_expr(obj_id, roles)


def _resolve_cast_target_id(
    tgt_id: str,
    roles: dict[str, tuple[str, str]],
    owner_id: str,
    owner_field: str,
) -> str | None:
    """Build the `targetId:` argument for a `Cast(...)` emission. Skips the
    ground-target sentinel (`E0000000`) and unknown targets — falling through
    to None lets `Cast` use its default (self when no targetLocation, ground
    target when there is one). Players and self both resolve to a non-null
    GameObjectId? via `?.GameObjectId`."""
    if not tgt_id or tgt_id.upper() == "E0000000":
        return None
    tgt_expr = _format_csharp_target(tgt_id, roles, owner_id, owner_field)
    if tgt_expr is None:
        return None
    return f"{tgt_expr}?.GameObjectId"


def _canonical_owner_for(
    opcode: str, parts: list[str], npc_fields_by_id: dict[str, str]
) -> str | None:
    """Which NPC's `Run_<...>()` method should render this event? For events
    that enter multiple per-NPC buckets (26 source/target, 35 source/target),
    we pick one canonical side so the rendering — code line or fallback bare
    comment — happens exactly once.

    Fallback: when no opcode-specific rule resolves to a known NPC, scan
    EVENT_ID_FIELDS for the first id that does. This guarantees every event
    `should_drop` lets through and that references at least one tracked NPC
    surfaces at least as a `// raw` comment — opcodes wired up in
    EVENT_ID_FIELDS but not yet in `_PER_NPC_PRIMARY` / explicit handlers
    don't silently disappear from the generated scenario."""
    if opcode == "35":
        a_id = _safe(parts, 2)
        b_id = _safe(parts, 4)
        if a_id in npc_fields_by_id:
            return a_id
        if b_id in npc_fields_by_id:
            return b_id
    elif opcode == "26":
        src_id = _safe(parts, 5)
        tgt_id = _safe(parts, 7)
        if src_id in npc_fields_by_id:
            return src_id
        if tgt_id in npc_fields_by_id:
            return tgt_id
    elif opcode == "30":
        # 30|NetworkBuffRemove: only an enemy-sourced removal belongs in that
        # enemy's method. E0000000-sourced removals route to Run_OtherDebuffs
        # via collect_player_status_removes — never fall through to the target
        # NPC, and never fall through to the generic id-scan below.
        src_id = _safe(parts, 5)
        if src_id in npc_fields_by_id:
            return src_id
        return None
    else:
        primary = _PER_NPC_PRIMARY.get(opcode)
        if primary is not None:
            actor = _safe(parts, primary)
            if actor in npc_fields_by_id:
                return actor
    for id_idx, _ in EVENT_ID_FIELDS.get(opcode, ()):
        actor = _safe(parts, id_idx)
        if actor in npc_fields_by_id:
            return actor
    return None


def emit_csharp_per_npc_action(
    opcode: str, parts: list[str],
    owner_id: str, owner_field: str,
    roles: dict[str, tuple[str, str]],
    npc_fields_by_id: dict[str, str],
    consts: ConstantsBuilder,
    center_x: float, center_y: float,
    cast_target: str | None = None,
    is_event_object: bool = False,
) -> str | None:
    """Return a single C# expression to splice inside `world.Events.Add(t, () => <here>)`,
    or None to signal the caller to emit a bare comment with the raw packet only.

    Constant references (BNpc base/name ids, spell ids, status ids, tether ids,
    timeline ids) are registered with `consts` so they appear in the generated
    `Constants` block; the emitted expressions use `Constants.<Cat>.<Name>`."""
    if opcode == "03":
        bnpc_base = _safe(parts, 10)
        name_id = _safe(parts, 9)
        display_name = _safe(parts, 3)
        bnpc_base_expr = "0"
        if bnpc_base:
            try:
                bnpc_base_expr = consts.register("BNpcBaseId", int(bnpc_base), display_name)
            except ValueError:
                bnpc_base_expr = bnpc_base
        name_id_expr = "0"
        if name_id:
            try:
                name_id_expr = consts.register("BNpcNameId", int(name_id), display_name)
            except ValueError:
                name_id_expr = name_id
        level_hex = _safe(parts, 5)
        try:
            level = int(level_hex, 16) if level_hex else 0
        except ValueError:
            level = 0
        # Targetability is always false at spawn — every transition is driven
        # explicitly by the emitted 34|NameToggle handlers, so we don't try to
        # guess the initial state.
        x, y, z = _safe(parts, 17), _safe(parts, 18), _safe(parts, 19)
        heading = _safe(parts, 20)
        return (
            f"{owner_field} = world.SpawnEnemy(new EnemySpawnConfig("
            f"BNpcBaseId: {bnpc_base_expr}, NameId: {name_id_expr}, Level: {level}, "
            f"Targetable: false, EnemyList: EnemyListMode.Never, IsVisible: false, "
            f"Placement: new Placement({_csharp_vec3_local(x, y, z, center_x, center_y)}, "
            f"{_csharp_float(heading)})))"
        )
    if opcode == "04":
        return f"{owner_field}?.Despawn()"
    if opcode == "20":
        spell_id = _safe(parts, 4)
        spell_name = _safe(parts, 5)
        tgt_id = _safe(parts, 6)
        cast_s = _safe(parts, 8)
        try:
            spell_expr = consts.register("ActionId", int(spell_id, 16), spell_name)
        except ValueError:
            spell_expr = f"0x{spell_id}U"
        bits = [spell_expr]
        if cast_target is not None:
            bits.append(f"targetLocation: {cast_target}")
        try:
            if cast_s and float(cast_s) > 0:
                bits.append(f"castSeconds: {_csharp_float(cast_s)}")
        except ValueError:
            pass
        target_id_expr = _resolve_cast_target_id(tgt_id, roles, owner_id, owner_field)
        if target_id_expr is not None:
            bits.append(f"targetId: {target_id_expr}")
        return f"{owner_field}?.Cast({', '.join(bits)})"
    if opcode in ("21", "22"):
        # NetworkAbility / NetworkAOEAbility — the resolution side of a cast.
        # For instant abilities (no preceding `20|StartsCasting`) this is the
        # only event, so emit Cast(..., castSeconds: 0f). When this 21/22
        # resolves a preceding 20 the emitter that walks the timeline filters
        # us out by index (see `resolved_idx` in emit_scenario_class), so we
        # don't double-emit; we only land here for genuine instants.
        spell_id = _safe(parts, 4)
        spell_name = _safe(parts, 5)
        tgt_id = _safe(parts, 6)
        try:
            spell_expr = consts.register("ActionId", int(spell_id, 16), spell_name)
        except ValueError:
            spell_expr = f"0x{spell_id}U"
        bits = [spell_expr, "castSeconds: 0f"]
        target_id_expr = _resolve_cast_target_id(tgt_id, roles, owner_id, owner_field)
        if target_id_expr is not None:
            bits.append(f"targetId: {target_id_expr}")
        return f"{owner_field}?.Cast({', '.join(bits)})"
    if opcode == "263":
        return None  # folded into the preceding 20 via the merge pass
    if opcode == "26":
        status_id = _safe(parts, 2)
        status_name = _safe(parts, 3)
        duration = _safe(parts, 4)
        tgt_id = _safe(parts, 7)
        tgt_expr = _format_csharp_target(tgt_id, roles, owner_id, owner_field)
        if tgt_expr is None:
            return None
        stacks_hex = _safe(parts, 9)
        try:
            stacks = int(stacks_hex, 16) if stacks_hex else 0
        except ValueError:
            stacks = 0
        try:
            status_expr = consts.register("StatusId", int(status_id, 16), status_name)
        except ValueError:
            status_expr = f"(ushort)0x{status_id}"
        bits = [status_expr]
        # IINACT uses 9999.00 as the "permanent" sentinel; AddStatus's default
        # duration (0f) already means "no expiry", so drop the parameter.
        if duration:
            try:
                duration_f = float(duration)
            except ValueError:
                duration_f = 0.0
            if 0 < duration_f < 9999:
                bits.append(_csharp_float(duration))
        if stacks > 1:
            bits.append(f"stacks: (ushort){stacks}")
            bits.append("overrideStacks: true")
        return f"{tgt_expr}?.AddStatus({', '.join(bits)})"
    if opcode == "30":
        status_id = _safe(parts, 2)
        status_name = _safe(parts, 3)
        tgt_id = _safe(parts, 7)
        tgt_expr = _format_csharp_target(tgt_id, roles, owner_id, owner_field)
        if tgt_expr is None:
            return None
        try:
            status_expr = consts.register("StatusId", int(status_id, 16), status_name)
        except ValueError:
            status_expr = f"(ushort)0x{status_id}"
        return f"{tgt_expr}?.RemoveStatus({status_expr})"
    if opcode == "27":
        # HeadMarker / Lockon on this NPC. Render as AttachLockonVfx so the
        # canonical Lockon-sheet IconName resolves to vfx/lockon/eff/{name}.avfx
        # at run time (SimCharacter.AttachLockonVfx).
        icon_hex = _safe(parts, 6)
        try:
            lockon_expr = consts.register("LockonId", int(icon_hex, 16), "")
        except ValueError:
            lockon_expr = f"0x{icon_hex}U"
        return f"{owner_field}?.AttachLockonVfx({lockon_expr}, persistent: false)"
    if opcode == "35":
        a_id = _safe(parts, 2)
        b_id = _safe(parts, 4)
        a_expr = _format_csharp_target(a_id, roles, owner_id, owner_field)
        b_expr = _format_csharp_target(b_id, roles, owner_id, owner_field)
        if a_expr is None or b_expr is None:
            return None
        # Tether takes non-null SimCharacter; assert via `!` so the SimEnemy?
        # and SimPartySlot? both narrow.
        a_arg = a_expr + ("!" if not a_expr.endswith("!") else "")
        b_arg = b_expr + ("!" if not b_expr.endswith("!") else "")
        tether_id = _safe(parts, 8)
        try:
            tether_expr = consts.register("TetherId", int(tether_id, 16), f"Tether_{tether_id}")
        except ValueError:
            tether_expr = f"(ushort)0x{tether_id}"
        return f"world.Tether({a_arg}, {b_arg}, {tether_expr})"
    if opcode == "270":
        x, y, z = _safe(parts, 6), _safe(parts, 7), _safe(parts, 8)
        return f"{owner_field}?.SetPosition({_csharp_vec3_local(x, y, z, center_x, center_y)})"
    if opcode == "271":
        heading = _safe(parts, 3)
        x, y, z = _safe(parts, 6), _safe(parts, 7), _safe(parts, 8)
        return (
            f"{owner_field}?.SetPosition(new Placement({_csharp_vec3_local(x, y, z, center_x, center_y)}, "
            f"{_csharp_float(heading)}))"
        )
    if opcode == "34":
        # NameToggle doubles as the per-actor targetability flag for raid
        # bosses (01 = targetable, 00 = untargetable). Boss flips itself in
        # the common case (src==tgt); duty director can also flip adds.
        state = _safe(parts, 6)
        if state == "01":
            return f"{owner_field}?.SetTargetable(true)"
        if state == "00":
            return f"{owner_field}?.SetTargetable(false)"
        return None
    if opcode == "261":
        # Mode `Add` = enter scope, `Change` = field delta, `Remove` = leave scope.
        # For BattleNpcs the authoritative spawn/despawn pair is 03/04 — their
        # 261|Add/Remove are scope-changes only and fall through to bare
        # comments. For EventObjects (Type|7) there is no 03/04, so 261|Add
        # IS the spawn and 261|Remove IS the despawn — gated on the
        # `is_event_object` flag the caller sets from the ActiveNpc row.
        mode = _safe(parts, 2)
        if mode == "Add" and is_event_object:
            pairs = parts[4:-1]
            if len(pairs) % 2 != 0:
                return None
            bnpc_id_str = pos_x = pos_y = pos_z = heading = ""
            for i in range(0, len(pairs), 2):
                k, v = pairs[i], pairs[i + 1]
                if   k == "BNpcID":  bnpc_id_str = v
                elif k == "PosX":    pos_x = v
                elif k == "PosY":    pos_y = v
                elif k == "PosZ":    pos_z = v
                elif k == "Heading": heading = v
            # The "BNpcID" field on Type|7 actors is actually an EObj sheet row id,
            # not a BNpcBase row. Register it as an EObjId constant and route to
            # SimWorld.SpawnEventObject, which allocates a slot from the
            # EventObjectManager 40-slot pool and binds it to the row's SharedGroup
            # via the engine's internal attach (FUN_14174dac0). See SimEventObject.cs
            # and EventObjectSpawn.cs for the spawn pipeline.
            eobj_expr = "0"
            if bnpc_id_str:
                try:
                    eobj_id_int = int(bnpc_id_str, 16)
                    eobj_expr = consts.register(
                        "EObjId", eobj_id_int, f"EventObj_{eobj_id_int:X}",
                    )
                except ValueError:
                    eobj_expr = bnpc_id_str
            # IsVisible: false by default — the 261|Change ModelStatus|0 that the
            # log emits one tick later is what flips visibility on, and we
            # already translate that into SetVisible(true) below.
            return (
                f"{owner_field} = world.SpawnEventObject(new EventObjectSpawnConfig("
                f"EObjRowId: {eobj_expr}, "
                f"Placement: new Placement({_csharp_vec3_local(pos_x, pos_y, pos_z, center_x, center_y)}, "
                f"{_csharp_float(heading)}), "
                f"IsVisible: false))"
            )
        if mode == "Remove" and is_event_object:
            return f"{owner_field}?.Despawn()"
        # ModelStatus 0x0 = visible, 0x4000 = invisible (render-state flips,
        # not targetability — SimEnemy.SetVisible toggles DisableDraw + the
        # Model/Nameplate VisibilityFlags). TransformationId is a boss form
        # swap (SimNpc.SetTransformationId toggles draw to rebuild the model).
        # Other 261 deltas (NPCTargetID, Heading, pos, CurrentMP, ...) still
        # fall through to a bare comment.
        if mode != "Change":
            return None
        pairs = parts[4:-1]
        if len(pairs) % 2 != 0:
            return None
        ms = None
        tid = None
        for i in range(0, len(pairs), 2):
            k = pairs[i]
            if k == "ModelStatus":
                ms = pairs[i + 1]
            elif k == "TransformationId":
                tid = pairs[i + 1]
        if ms == "0":
            return f"{owner_field}?.SetVisible(true)"
        if ms == "16384":
            return f"{owner_field}?.SetVisible(false)"
        if tid is not None:
            try:
                tid_i = int(tid)
            except ValueError:
                return None
            return f"{owner_field}?.SetTransformationId((short){tid_i})"
        return None
    if opcode == "273":
        code = _safe(parts, 3).upper()
        param = _safe(parts, 4).upper()
        if code == "0197":
            if param == "1E39":
                timeline_expr = consts.register("TimelineId", 0x1E39, "WarpOut")
                return f"{owner_field}?.PlayActionTimeline({timeline_expr})"
            if param == "1E43":
                timeline_expr = consts.register("TimelineId", 0x1E43, "Spawn")
                return f"{owner_field}?.PlayActionTimeline({timeline_expr})"
        if code == "0031":
            # ActorControl SetModeAttributeFlags — param1 is written to
            # ModelContainer.ModeAttributeFlags (offset 0x22). Empirically the
            # field that drives Omega-M's body sub-mesh variants (e.g. shield
            # present/absent). Params 2..4 are always 0 in observed P5 logs.
            try:
                p1 = int(param, 16)
            except ValueError:
                return None
            return f"{owner_field}?.SetModeAttributeFlags((byte)0x{p1:02X})"
        if code == "003F":
            # ActorControl SetModelState — param1 is written to
            # Timeline.ModelState. Plays a supporting role to the 0031 packet
            # for boss pose/variant commits. Params 2..4 are always 0 in
            # observed P5 logs.
            try:
                p1 = int(param, 16)
            except ValueError:
                return None
            return f"{owner_field}?.SetModelState((byte)0x{p1:02X})"
        return None
    return None


def emit_csharp_instance_action(
    opcode: str, parts: list[str],
    roles: dict[str, tuple[str, str]] | None = None,
    consts: "ConstantsBuilder | None" = None,
    bundled_status: tuple[str, str, str] | None = None,
) -> str | None:
    if opcode == "257":
        flags = _safe(parts, 3)
        index = _safe(parts, 4)
        return f"world.Map.AddEffect(packetFlags: 0x{flags}U, index: (byte)0x{index})"
    if opcode == "35":
        # Player-to-player tether. Both ends must resolve to a PartyRole;
        # NPC-involving tethers are emitted inside the owning NPC's method
        # (see emit_csharp_per_npc_action) and never reach this path because
        # format_instance_event filters them out.
        #
        # `bundled_status` is `(status_id_hex, status_name, duration_str)` for
        # a 26|NetworkBuff applied to either tether end at the same packet
        # timestamp — see `_bundled_status_applies`. When present, we forward
        # the status id + duration to `world.Tether(..., duration:, debuffStatusId:)`
        # so the simulated tether expires together with the real-world status.
        if roles is None or consts is None:
            return None
        a_id = _safe(parts, 2)
        b_id = _safe(parts, 4)
        a_expr = _player_role_expr(a_id, roles)
        b_expr = _player_role_expr(b_id, roles)
        if a_expr is None or b_expr is None:
            return None
        tether_id = _safe(parts, 8)
        try:
            tether_expr = consts.register("TetherId", int(tether_id, 16), f"Tether_{tether_id}")
        except ValueError:
            tether_expr = f"(ushort)0x{tether_id}"
        bits = [f"{a_expr}!", f"{b_expr}!", tether_expr]
        if bundled_status is not None:
            sid_hex, sname, dur_str = bundled_status
            try:
                duration_f = float(dur_str) if dur_str else 0.0
            except ValueError:
                duration_f = 0.0
            # 9999.00 is IINACT's "permanent" sentinel; drop it so the tether
            # falls back to the status's own lifetime.
            if 0 < duration_f < 9999:
                bits.append(f"duration: {_csharp_float(dur_str)}")
            try:
                status_expr = consts.register("StatusId", int(sid_hex, 16), sname)
            except ValueError:
                status_expr = f"(ushort)0x{sid_hex}"
            bits.append(f"debuffStatusId: {status_expr}")
        return f"world.Tether({', '.join(bits)})"
    if opcode == "33":
        # Director-sourced ActorControl (filter.should_drop already removed
        # NPC/player-sourced 33 rows). Fields after the directorId: 3=category,
        # 4=arg1, 5=arg2 — all uppercase hex without 0x.
        cat = _safe(parts, 3)
        arg1 = _safe(parts, 4)
        arg2 = _safe(parts, 5)
        if not cat:
            return None
        try:
            cat_i = int(cat, 16)
            arg1_i = int(arg1, 16) if arg1 else 0
            arg2_i = int(arg2, 16) if arg2 else 0
        except ValueError:
            return None
        bits = [f"0x{cat_i:X}U", f"0x{arg1_i:X}U"]
        if arg2_i != 0:
            bits.append(f"0x{arg2_i:X}U")
        return f"world.Map.DirectorUpdate({', '.join(bits)})"
    # 258 / 259 / 268 / 269 → no API mapping; comment-only.
    return None


def _merge_cast_extras(events: list[tuple[float, str, int, str]]) -> dict[int, str]:
    """For each 20|StartsCasting event, find a 263|ActorCastExtra within 0.25s
    from the same actor and return a map `event_index -> csharp_target_vec3`.
    Also returns which 263 indices should be suppressed (caller decides via the
    returned set). The 263 row contains x,y,z,heading at fields 4..7."""
    # Returns (cast_index -> target_vec3, set of 263 indices to skip).
    return {}  # placeholder — real impl in caller for clarity


def emit_scenario_class(
    path: str, pull: Pull, npcs: list[ActiveNpc],
    begin_rel: float, end_rel: float,
    center_x: float, center_y: float,
    class_name: str = "ScenarioTemplate",
    spawn_x: float | None = None,
    spawn_y: float | None = None,
    printed_raws: set[str] | None = None,
) -> str:
    """Build a complete C# scenario class from the given window.

    Layout:
      - One `Run_<Name>()` method per stable NPC instance; the method declares
        a local `SimEnemy? <field> = null;` (or `SimEventObject? <field>` for
        Type|7 visual-only EventObjects) and registers all of that NPC's events
        through `world.Events.Add(...)`.
      - Run_InstanceEvents() for map / director events (257, 33-director, etc).
      - Run_PlayerTethers() for player-to-player tethers — events that have no
        owning NPC and so cannot live inside any per-NPC method.
      - Run_OtherDebuffs() for `26|NetworkBuff` applies and `30|NetworkBuffRemove`
        removals whose source is the duty director (`E0000000`) and that aren't
        already consumed as the debuff backing a player tether. Applies emit
        `AddStatus`; removals emit `RemoveStatus`. Sorted by timestamp.
      - A nested static `Constants` class collects every BNpc base/name id,
        spell id, status id, tether id, and timeline id encountered, with
        sanitised PascalCase names sourced from the log itself.

    Every emitted `world.Events.Add(...)` is preceded by a `// <raw packet>`
    comment line; opcodes without an API mapping fall through to a bare
    comment with no code line.

    Events before `begin_rel` keep their negative scenario timestamp so the
    reader can see when they actually fired in the recording; the
    `EventScheduler` internally clamps negatives to 0, so they all fire at
    scenario start, which matches the recorded ordering."""
    # Drop snapshot-only references (no 03 found anywhere in the log). These
    # can't be materialised in a sim and would only produce noisy comment-only
    # methods.
    npcs = [n for n in npcs if n.spawned_at is not None]
    fields_by_npc = _build_field_names(npcs)
    npc_fields_by_id: dict[str, str] = {}
    for npc in npcs:
        # When a single actor id hosts multiple identities the last instance
        # wins for cross-references — usually fine since by then the earlier
        # instance is gone.
        npc_fields_by_id[npc.obj_id] = fields_by_npc[id(npc)]
    roles = collect_player_role_map(path, pull, begin_rel, end_rel)
    per_npc = collect_extended_logs(path, pull, npcs, end_rel, center_x, center_y)
    instance = collect_instance_events(path, pull, begin_rel, end_rel)
    consts = ConstantsBuilder()

    # Build the per-method bodies first so the Constants block accumulates
    # every id seen across all methods before we render it.
    method_bodies: list[list[str]] = []
    for npc in npcs:
        field = fields_by_npc[id(npc)]
        events = per_npc.get(id(npc), [])
        body: list[str] = []
        body.append(f"    private void {_method_name(field)}()")
        body.append("    {")
        field_type = "SimEventObject?" if npc.is_event_object else "SimEnemy?"
        body.append(f"        {field_type} {field} = null;")
        if not events:
            body.append("        // (no events in window)")
        else:
            parsed: list[tuple[float, str, list[str], str]] = []
            for t, _desc, _count, raw in events:
                parts = _reparse_raw(raw)
                parsed.append((t, parts[0], parts, raw))
            # Fold a 263|ActorCastExtra into the preceding 20|StartsCasting from
            # the same actor (within 0.25s) as `targetLocation`.
            cast_targets: dict[int, str] = {}
            suppress_idx: set[int] = set()
            for i, (t, opcode, parts, _raw) in enumerate(parsed):
                if opcode != "20":
                    continue
                src_id = _safe(parts, 2)
                for j in range(i + 1, len(parsed)):
                    tj, opj, pj, _ = parsed[j]
                    if tj - t > 0.25:
                        break
                    if opj != "263" or _safe(pj, 2) != src_id:
                        continue
                    x, y, z = _safe(pj, 4), _safe(pj, 5), _safe(pj, 6)
                    cast_targets[i] = _csharp_vec3_local(x, y, z, center_x, center_y)
                    suppress_idx.add(j)
                    break

            # Mark every 21|NetworkAbility / 22|NetworkAOEAbility that resolves
            # a preceding 20 (same source, same spell, within castTime + 0.5s
            # slack) so it falls through as a bare comment instead of a
            # duplicate Cast() — genuine instants (21/22 with no matching 20)
            # still emit Cast(...).
            resolved_idx: set[int] = set()
            for i, (t, opcode, parts, _raw) in enumerate(parsed):
                if opcode != "20":
                    continue
                src_id = _safe(parts, 2)
                spell_id = _safe(parts, 4)
                try:
                    cast_s = float(_safe(parts, 8))
                except ValueError:
                    cast_s = 0.0
                deadline = t + cast_s + 0.5
                for j in range(i + 1, len(parsed)):
                    tj, opj, pj, _ = parsed[j]
                    if tj > deadline:
                        break
                    if opj not in ("21", "22") or _safe(pj, 2) != src_id:
                        continue
                    if _safe(pj, 4) != spell_id:
                        continue
                    resolved_idx.add(j)
                    break

            # Dedup repeated party-wide knockback emissions when one 22|AOE
            # action lands on every target (each target produces its own 22
            # line at the same timestamp). The cast itself is still per-target
            # for legacy compatibility, but Knockback applies to all slots and
            # only needs one event.
            knockback_emitted: set[tuple[float, str]] = set()
            for i, (t, opcode, parts, raw) in enumerate(parsed):
                if i in suppress_idx:
                    continue
                if _canonical_owner_for(opcode, parts, npc_fields_by_id) != npc.obj_id:
                    continue
                t_scenario = t - begin_rel
                body.append(f"        // [{t_scenario:.2f}s] {raw}")
                if printed_raws is not None:
                    printed_raws.add(raw)
                if i not in resolved_idx:
                    call = emit_csharp_per_npc_action(
                        opcode, parts, npc.obj_id, field,
                        roles, npc_fields_by_id, consts,
                        center_x, center_y,
                        cast_target=cast_targets.get(i),
                        is_event_object=npc.is_event_object,
                    )
                    if call is not None:
                        line = f"world.Events.Add({t_scenario:.2f}f, () => {call});"
                        if t_scenario < 0:
                            line = "// " + line  # negative offset — pre-window event, not schedulable
                        body.append(f"        {line}")
                # Independent of whether the 21/22 line resolved a preceding
                # `20|StartsCasting` (and is thus skipped above as a duplicate
                # Cast), emit a `world.Party.Knockback(source, knockbackRowId)`
                # when the resolution carries a knockback effect (type 0x1F).
                # The Knockback-sheet row id is taken from the effect's Value
                # field and registered as `Constants.KnockbackId.<ActionName>`
                # so scenarios read it symbolically; KnockbackLookup reads the
                # Knockback sheet by row at runtime.
                if opcode in ("21", "22"):
                    kb_row = _extract_knockback_row(parts)
                    if kb_row is not None:
                        spell_id = _safe(parts, 4)
                        kb_key = (round(t_scenario, 3), spell_id)
                        if kb_key not in knockback_emitted:
                            knockback_emitted.add(kb_key)
                            spell_name = _safe(parts, 5) or f"Kb_{spell_id}"
                            kb_const_expr = consts.register("KnockbackId", kb_row, spell_name)
                            kb_line = (
                                f"world.Events.Add({t_scenario:.2f}f, () => {{ "
                                f"if ({field} != null) world.Party.Knockback({field}.Position, {kb_const_expr}); }});"
                            )
                            if t_scenario < 0:
                                kb_line = "// " + kb_line
                            body.append(f"        {kb_line}")
        body.append("    }")
        method_bodies.append(body)

    # Partition instance events into map/director (Run_InstanceEvents) and
    # player-player tethers (Run_PlayerTethers). Player tethers have no
    # owning NPC; bucketing them into their own method keeps Run_InstanceEvents
    # focused on arena / director packets.
    instance_main: list[tuple[float, str, str]] = []
    player_tethers: list[tuple[float, str, str]] = []
    for t, desc, raw in instance:
        if raw.startswith("35|"):
            player_tethers.append((t, desc, raw))
        else:
            instance_main.append((t, desc, raw))

    def _build_method_body(method_name: str, events: list[tuple[float, str, str]]) -> list[str]:
        body: list[str] = []
        body.append(f"    private void {method_name}()")
        body.append("    {")
        for t, _desc, raw in events:
            parts = _reparse_raw(raw)
            t_scenario = t - begin_rel
            call = emit_csharp_instance_action(parts[0], parts, roles, consts)
            body.append(f"        // [{t_scenario:.2f}s] {raw}")
            if printed_raws is not None:
                printed_raws.add(raw)
            if call is not None:
                line = f"world.Events.Add({t_scenario:.2f}f, () => {call});"
                if t_scenario < 0:
                    line = "// " + line  # negative offset — pre-window event, not schedulable
                body.append(f"        {line}")
        body.append("    }")
        return body

    instance_body: list[str] = (
        _build_method_body("Run_InstanceEvents", instance_main) if instance_main else []
    )

    # Pull 26|NetworkBuff applies once so each tether can find its bundled
    # status without re-walking the log, and so Run_OtherDebuffs can sweep up
    # every unowned status-apply afterwards. Status applies (e.g. Mid Glitch on
    # TOP P5 Hello World) fire at the same packet timestamp as the tether, so
    # `_bundled_status_applies` defaults to a 0.1s window.
    status_applies = collect_player_status_applies(path, pull, begin_rel, end_rel)
    # Raws consumed by Run_PlayerTethers as the tether's debuffStatusId; these
    # are excluded from Run_OtherDebuffs to avoid double-application in the sim.
    consumed_apply_raws: set[str] = set()

    player_tether_body: list[str] = []
    if player_tethers:
        player_tether_body.append("    private void Run_PlayerTethers()")
        player_tether_body.append("    {")
        for t, _desc, raw in player_tethers:
            parts = _reparse_raw(raw)
            t_scenario = t - begin_rel
            src_id = _safe(parts, 2)
            tgt_id = _safe(parts, 4)
            bundled = _bundled_status_applies(t, src_id, tgt_id, status_applies)
            # Prefer a status_id applied to BOTH tether ends — that's the
            # canonical pair-debuff (e.g. Mid Glitch on TOP P5 Hello World).
            # Unilateral applies that happen to land at the same packet
            # timestamp (e.g. Hello, Distant World on only one player) would
            # otherwise win the older "first target-side match" tiebreaker.
            # Fall back to the target-side apply, then the first apply.
            tgts_by_status: dict[str, set[str]] = {}
            for p, _ in bundled:
                tgts_by_status.setdefault(_safe(p, 2), set()).add(_safe(p, 7))
            bilateral = {sid for sid, tgts in tgts_by_status.items()
                         if {src_id, tgt_id} <= tgts}
            chosen_pair: tuple[list[str], str] | None = next(
                ((p, r) for p, r in bundled if _safe(p, 2) in bilateral), None)
            if chosen_pair is None:
                chosen_pair = next(
                    ((p, r) for p, r in bundled if _safe(p, 7) == tgt_id), None)
            if chosen_pair is None and bundled:
                chosen_pair = bundled[0]
            chosen = chosen_pair[0] if chosen_pair is not None else None
            chosen_raw = chosen_pair[1] if chosen_pair is not None else None

            player_tether_body.append(f"        // [{t_scenario:.2f}s] {raw}")
            if printed_raws is not None:
                printed_raws.add(raw)
            # Echo every bundled apply that shares the chosen status_id — a
            # player-tether debuff lands on BOTH ends, so the chosen status
            # has two 26| rows (one per player). Other concurrently-applied
            # debuffs (unilateral applies on a single player, unrelated
            # debuffs that happen to land at the same packet timestamp) are
            # not part of *this* mechanic and live in Run_OtherDebuffs.
            if chosen is not None:
                chosen_sid = _safe(chosen, 2)
                for sa_parts, sa_raw in bundled:
                    if _safe(sa_parts, 2) != chosen_sid:
                        continue
                    player_tether_body.append(
                        f"        // [{t_scenario:.2f}s] {sa_raw}")
                    if printed_raws is not None:
                        printed_raws.add(sa_raw)
                    consumed_apply_raws.add(sa_raw)
            bundled_info: tuple[str, str, str] | None = None
            if chosen is not None:
                bundled_info = (_safe(chosen, 2), _safe(chosen, 3), _safe(chosen, 4))
            call = emit_csharp_instance_action(
                parts[0], parts, roles, consts, bundled_status=bundled_info,
            )
            if call is not None:
                line = f"world.Events.Add({t_scenario:.2f}f, () => {call});"
                if t_scenario < 0:
                    line = "// " + line  # negative offset — pre-window event, not schedulable
                player_tether_body.append(f"        {line}")
        player_tether_body.append("    }")

    # Run_OtherDebuffs: every 26|NetworkBuff apply and 30|NetworkBuffRemove
    # removal in-window with no actor source (src id `E0000000` — duty-director
    # / system-driven), minus 26 rows already consumed as tether debuffs above.
    # Source-bearing 26/30 rows are emitted inside the source NPC's Run_<Npc>()
    # method, so they don't land here. 30 rows are gated to duration "0.00"
    # upstream in collect_player_status_removes.
    status_removes = collect_player_status_removes(path, pull, begin_rel, end_rel)
    other_debuffs_body: list[str] = []
    other_debuffs: list[tuple[float, str, list[str], str]] = []
    for t, p, r in status_applies:
        if _safe(p, 5) == "E0000000" and r not in consumed_apply_raws:
            other_debuffs.append((t, "26", p, r))
    for t, p, r in status_removes:
        if _safe(p, 5) == "E0000000":
            other_debuffs.append((t, "30", p, r))
    other_debuffs.sort(key=lambda x: x[0])
    if other_debuffs:
        other_debuffs_body.append("    private void Run_OtherDebuffs()")
        other_debuffs_body.append("    {")
        for t, opcode, parts, raw in other_debuffs:
            t_scenario = t - begin_rel
            other_debuffs_body.append(f"        // [{t_scenario:.2f}s] {raw}")
            if printed_raws is not None:
                printed_raws.add(raw)
            status_id = _safe(parts, 2)
            status_name = _safe(parts, 3)
            tgt_id = _safe(parts, 7)
            tgt_expr = _player_role_expr(tgt_id, roles)
            if tgt_expr is None:
                continue  # target not a known party member — comment-only
            try:
                status_expr = consts.register(
                    "StatusId", int(status_id, 16), status_name)
            except ValueError:
                status_expr = f"(ushort)0x{status_id}"
            if opcode == "30":
                call = f"{tgt_expr}?.RemoveStatus({status_expr})"
            else:
                bits = [status_expr]
                duration = _safe(parts, 4)
                if duration:
                    try:
                        duration_f = float(duration)
                    except ValueError:
                        duration_f = 0.0
                    # 9999.00 is IINACT's "permanent" sentinel; AddStatus's default
                    # duration (0f) already means "no expiry", so drop the parameter.
                    if 0 < duration_f < 9999:
                        bits.append(_csharp_float(duration))
                stacks_hex = _safe(parts, 9)
                try:
                    stacks = int(stacks_hex, 16) if stacks_hex else 0
                except ValueError:
                    stacks = 0
                if stacks > 1:
                    bits.append(f"stacks: (ushort){stacks}")
                    bits.append("overrideStacks: true")
                call = f"{tgt_expr}?.AddStatus({', '.join(bits)})"
            line = f"world.Events.Add({t_scenario:.2f}f, () => {call});"
            if t_scenario < 0:
                line = "// " + line  # negative offset — pre-window event, not schedulable
            other_debuffs_body.append(f"        {line}")
        other_debuffs_body.append("    }")

    # Run_PlayerLockons: every 27|HeadMarker in-window whose target is a known
    # player slot. NPC-targeted head markers live in the owning NPC's
    # Run_<Npc>() method via the standard per-NPC pipeline.
    player_lockons = collect_player_lockons(path, pull, begin_rel, end_rel)
    player_lockons_body: list[str] = []
    if player_lockons:
        player_lockons_body.append("    private void Run_PlayerLockons()")
        player_lockons_body.append("    {")
        for t, parts, raw in player_lockons:
            t_scenario = t - begin_rel
            player_lockons_body.append(f"        // [{t_scenario:.2f}s] {raw}")
            if printed_raws is not None:
                printed_raws.add(raw)
            tgt_id = _safe(parts, 2)
            tgt_expr = _player_role_expr(tgt_id, roles)
            if tgt_expr is None:
                continue  # target not a known party member — comment-only
            icon_hex = _safe(parts, 6)
            try:
                lockon_expr = consts.register("LockonId", int(icon_hex, 16), "")
            except ValueError:
                lockon_expr = f"0x{icon_hex}U"
            call = f"{tgt_expr}?.AttachLockonVfx({lockon_expr}, persistent: false)"
            line = f"world.Events.Add({t_scenario:.2f}f, () => {call});"
            if t_scenario < 0:
                line = "// " + line  # negative offset — pre-window event, not schedulable
            player_lockons_body.append(f"        {line}")
        player_lockons_body.append("    }")

    # Now assemble the file.
    out: list[str] = []
    out.append("// Generated by tools/parser.py --code. Edit freely.")
    out.append("// Player -> role mapping (first-seen order inside the window):")
    if roles:
        for pid, (role, name) in roles.items():
            out.append(f"//   {pid}  {name or '?':<24}  -> PartyRole.{role}")
    else:
        out.append("//   (no players observed in the window)")
    dropped = get_dropped_change_keys()
    if dropped:
        out.append("// Suppressed 261|Change keys (state-sync churn — no C# emission):")
        out.append(f"//   {', '.join(dropped)}")
    out.append("")
    out.append("using System.Collections.Generic;")
    out.append("using System.Numerics;")
    out.append("using UltiSim.Core;")
    out.append("using UltiSim.Core.Map;")
    out.append("using UltiSim.Core.SimObjects;")
    out.append("")
    out.append("namespace UltiSim.Scenarios;")
    out.append("")
    out.append(f"public sealed class {class_name} : IScenario")
    out.append("{")
    out.append(f"    public string Name => \"{class_name}\";")
    out.append("    public TargetInstance? TargetInstance { get; } = new(")
    out.append(f"        TerritoryId: {pull.zone_id},")
    sx = spawn_x if spawn_x is not None else center_x
    sy = spawn_y if spawn_y is not None else center_y
    out.append(f"        Origin: new Vector3({center_x:.3f}f, 0f, {center_y:.3f}f),")
    out.append(f"        PlayerPosition: new Vector3({sx:.3f}f, 0f, {sy:.3f}f),")
    out.append("        WeatherId: 0);")
    # Inn (TerritoryId 801) override so loading the scenario from the inn
    # places the world origin at the arena center instead of the player's
    # inn position. Edit this list to add other testing-from territories.
    out.append("    public IReadOnlyList<ScenarioOriginOverride> OriginOverrides { get; } = [")
    out.append(f"        new(TerritoryId: 801, X: {center_x:.3f}f, Z: {center_y:.3f}f),")
    out.append("    ];")
    out.append("    public IReadOnlyList<uint> HiddenBaseIds { get; } = [];")
    out.append("    public IReadOnlyList<Waymark> Waymarks { get; } = [];")
    out.append("    public ushort Bgm => 0;")
    out.append("")
    out.append("    private SimWorld world = null!;")
    out.append("    private SimParty party = null!;")
    out.append("")
    out.append("    public void Run(SimWorld worldParam, PartyRole playerRole)")
    out.append("    {")
    out.append("        world = worldParam;")
    out.append("        party = worldParam.Party;")
    out.append("        _ = playerRole;")
    out.append("")
    for npc in npcs:
        out.append(f"        {_method_name(fields_by_npc[id(npc)])}();")
    if instance_body:
        out.append("        Run_InstanceEvents();")
    if player_tether_body:
        out.append("        Run_PlayerTethers();")
    if other_debuffs_body:
        out.append("        Run_OtherDebuffs();")
    if player_lockons_body:
        out.append("        Run_PlayerLockons();")
    out.append("    }")
    out.append("")
    if instance_body:
        out.extend(instance_body)
        out.append("")
    if player_tether_body:
        out.extend(player_tether_body)
        out.append("")
    if other_debuffs_body:
        out.extend(other_debuffs_body)
        out.append("")
    if player_lockons_body:
        out.extend(player_lockons_body)
        out.append("")
    out.append("    public void Tick(float delta, float elapsed) { }")
    out.append("")

    for body in method_bodies:
        out.extend(body)
        out.append("")

    consts_lines = consts.emit_lines()
    if consts_lines:
        out.extend(consts_lines)

    out.append("}")
    return "\n".join(out)


def _extract_knockback_row(parts: list[str]) -> int | None:
    """Decode the 8 (flags|data) effect pairs of a type-21/22 NetworkAbility line
    and return the Knockback row id from the first type-0x1F effect, or None.

    Effect layout (matches FFXIVClientStructs.ActionEffectHandler.Effect):
      flags uint32 LE = type | param0<<8 | param1<<16 | param2<<24
      data  uint32 LE = param3 | param4<<8 | value<<16   (value = 2-byte
                                                          Knockback row id)
    """
    if len(parts) < 24:
        return None
    for i in range(8):
        flags_hex = parts[8 + i * 2]
        data_hex = parts[9 + i * 2]
        if not flags_hex:
            continue
        try:
            flags = int(flags_hex, 16)
        except ValueError:
            continue
        if (flags & 0xFF) != 0x1F:
            continue
        try:
            data = int(data_hex, 16) if data_hex else 0
        except ValueError:
            continue
        row = (data >> 16) & 0xFFFF
        if row != 0:
            return row
    return None


def _human_readable_event(
    opcode: str, parts: list[str], center_x: float, center_y: float
) -> str:
    """Best-effort prose description for the dropped-events dump. Tries the
    instance-level renderer first (works without an NPC perspective), then
    falls back to `format_event_for_npc` using the first NPC-shaped id in the
    row, then the first id of any kind. Returns a placeholder when nothing
    renders so every line has a second line in the output."""
    if opcode in INSTANCE_OPCODES:
        desc = format_instance_event(opcode, parts)
        if desc is not None:
            return desc
    if opcode in EVENT_ID_FIELDS:
        candidate = ""
        for id_idx, _ in EVENT_ID_FIELDS[opcode]:
            actor_id = _safe(parts, id_idx)
            if NPC_ID_RE.match(actor_id):
                candidate = actor_id
                break
        if not candidate:
            for id_idx, _ in EVENT_ID_FIELDS[opcode]:
                actor_id = _safe(parts, id_idx)
                if actor_id:
                    candidate = actor_id
                    break
        desc = format_event_for_npc(opcode, parts, candidate, center_x, center_y)
        if desc is not None:
            return desc
    return f"opcode {opcode} (no interpretation)"


def write_dropped_events(
    path: str,
    pull: Pull,
    begin_rel: float,
    end_rel: float,
    printed_raws: set[str],
    out_path: str,
    center_x: float = 0.0,
    center_y: float = 0.0,
) -> int:
    """Walk the log across [begin_rel, end_rel] and write every packet whose
    canonical raw form (`opcode|<parts[2:]>`, matching what --code emits as
    `// {raw}`) is NOT in `printed_raws`. Each record is two lines: the
    window-relative timestamp + raw, then the human-readable interpretation.
    Returns the number of records written."""
    written = 0
    with open(out_path, "w", encoding="utf-8") as fh:
        for parts in iter_records(path):
            if len(parts) < 2:
                continue
            opcode = parts[0]
            try:
                ts = parse_timestamp(parts[1])
            except ValueError:
                continue
            t_rel = (ts - pull.start).total_seconds()
            if t_rel < begin_rel or t_rel > end_rel:
                continue
            raw = "|".join(parts[:1] + parts[2:])
            if raw in printed_raws:
                continue
            interp = _human_readable_event(opcode, parts, center_x, center_y)
            fh.write(f"{format_win(t_rel, begin_rel):>9}  {raw}\n")
            fh.write(f"           {interp}\n")
            written += 1
    return written


def print_extended_logs(
    path: str,
    pull: Pull,
    npcs: list[ActiveNpc],
    begin_rel: float,
    end_rel: float,
    center_x: float = 0.0,
    center_y: float = 0.0,
    show_raw: bool = False,
) -> None:
    logs = collect_extended_logs(path, pull, npcs, end_rel, center_x, center_y)
    for npc in npcs:
        events = logs.get(id(npc), [])
        if not events:
            continue
        base = f"base {npc.base_id}" if npc.base_id is not None else "base -"
        spawn = format_win(npc.spawned_at, begin_rel) if npc.spawned_at is not None else "-"
        despawn = format_win(npc.despawned_at, begin_rel) if npc.despawned_at is not None else "-"
        print()
        print(
            f"=== {npc.name or '?'} ({npc.obj_id}, {base}, "
            f"spawn {spawn} -> despawn {despawn}) - {len(events)} events ==="
        )
        for t, desc, count, raw in events:
            suffix = f"  x{count}" if count > 1 else ""
            prefix = "//" if t - begin_rel < 0 else "  "
            print(f"{prefix}{format_win(t, begin_rel):>9}  {desc}{suffix}")
            if show_raw:
                print(f"{prefix}{format_win(t, begin_rel):>9}  | {raw}")
    dropped = get_dropped_change_keys()
    if dropped:
        print()
        print(f"=== Suppressed 261|Change keys ({len(dropped)}) ===")
        print("  " + ", ".join(dropped))


def print_active_npcs(
    path: str, pull: Pull, begin_rel: float, end_rel: float
) -> list[ActiveNpc]:
    window_start = pull.start + timedelta(seconds=begin_rel)
    window_end = pull.start + timedelta(seconds=end_rel)
    npcs = collect_active_npcs(path, pull, window_start, window_end)
    print()
    print(
        f"Pull #{pull.index} window: pull {format_rel(begin_rel)} -> {format_rel(end_rel)} "
        f"({len(npcs)} NPCs). Times below are window-relative."
    )
    if not npcs:
        return npcs
    print(
        f"  {'id':<10}  {'base':>6}  {'name':<24}  "
        f"{'first':>9}  {'last':>9}  {'spawned':>9}  {'despawned':>9}"
    )
    for n in npcs:
        base = str(n.base_id) if n.base_id is not None else "-"
        name = (n.name or "?")[:24]
        print(
            f"  {n.obj_id:<10}  {base:>6}  {name:<24}  "
            f"{format_win(n.first_seen, begin_rel):>9}  {format_win(n.last_seen, begin_rel):>9}  "
            f"{format_win(n.spawned_at, begin_rel):>9}  {format_win(n.despawned_at, begin_rel):>9}"
        )
    return npcs


def collect_npc_casts(path: str, pull: Pull, window_end: datetime) -> list[CastEvent]:
    """Return every `20|` (StartsCasting) line in [pull.start, window_end] whose
    source is an enemy NPC (actor id starts with 4). Player casts are excluded.
    BNpcBase id is filled in from the source's most recent `03|` add event.

    Simultaneous casts of the same spell by multiple actors (e.g. TOP decoy
    "Flame Thrower" burst) are collapsed into a single CastEvent with `count`
    set to the number of actors. Casts are considered simultaneous if their
    timestamps round to the same 100ms bucket."""
    base_id_for: dict[str, int] = {}
    # key = (rounded_t, spell, src_name) -> CastEvent
    grouped: dict[tuple[float, str, str], CastEvent] = {}
    order: list[tuple[float, str, str]] = []
    for parts in iter_records(path):
        if len(parts) < 2:
            continue
        opcode = parts[0]
        if opcode == "03" and len(parts) > 10:
            obj_id = parts[2]
            base_id = _parse_int(parts[10])
            if base_id is not None:
                base_id_for[obj_id] = base_id
            continue
        if opcode != "20" or len(parts) < 9:
            continue
        try:
            ts = parse_timestamp(parts[1])
        except ValueError:
            continue
        if ts < pull.start or ts > window_end:
            continue
        src_id = parts[2]
        if not NPC_ID_RE.match(src_id):
            continue
        t_rel = (ts - pull.start).total_seconds()
        cast_seconds = _parse_float(parts[8]) or 0.0
        spell = parts[5]
        src_name = parts[3]
        key = (round(t_rel, 1), spell, src_name)
        existing = grouped.get(key)
        if existing is None:
            grouped[key] = CastEvent(
                t_rel=t_rel,
                src_name=src_name,
                src_base_id=base_id_for.get(src_id),
                spell=spell,
                cast_seconds=cast_seconds,
            )
            order.append(key)
        else:
            existing.count += 1
    return [grouped[k] for k in order]


def collect_pull_data(
    path: str, pull: Pull, window_end: datetime
) -> tuple[list[NpcLife], dict[str, str]]:
    """Walk the log once and return:
      - enemy-NPC lifecycle events (add/remove) in [pull.start, window_end]
      - id -> name mapping for every NPC id referenced by any other event in
        that same window but not present in the lifecycle list (and not a pet).
    Player pets are filtered out via the owner field on 03/04 lines."""
    lives: dict[str, NpcLife] = {}
    pet_ids: set[str] = set()
    referenced: dict[str, str] = {}

    for parts in iter_records(path):
        if len(parts) < 2:
            continue
        try:
            ts = parse_timestamp(parts[1])
        except ValueError:
            continue
        if ts < pull.start or ts > window_end:
            continue
        opcode = parts[0]
        rel = (ts - pull.start).total_seconds()

        # NPC lifecycle (03 = add, 04 = remove). Pets are flagged here and
        # excluded from both lives and referenced.
        if opcode in ("03", "04") and len(parts) >= 4:
            obj_id = parts[2]
            if NPC_ID_RE.match(obj_id):
                owner = parts[6] if len(parts) > 6 else ""
                if owner and owner.startswith("1"):
                    pet_ids.add(obj_id)
                else:
                    name = parts[3]
                    base_id = _parse_int(parts[10]) if len(parts) > 10 else None
                    life = lives.get(obj_id)
                    if life is None:
                        life = NpcLife(obj_id=obj_id, base_id=base_id, name=name)
                        lives[obj_id] = life
                    elif life.base_id is None and base_id is not None:
                        life.base_id = base_id
                    if opcode == "03" and life.added is None:
                        life.added = rel
                    elif opcode == "04":
                        life.removed = rel

        # Collect every NPC id referenced anywhere in the window so we can
        # report ones that never had an add/remove event.
        for id_idx, name_idx in EVENT_ID_FIELDS.get(opcode, ()):
            if id_idx >= len(parts):
                continue
            obj_id = parts[id_idx]
            if not NPC_ID_RE.match(obj_id):
                continue
            name = parts[name_idx] if 0 <= name_idx < len(parts) else ""
            prev = referenced.get(obj_id, "")
            if not prev and name:
                referenced[obj_id] = name
            elif obj_id not in referenced:
                referenced[obj_id] = name

    # Drop anything that is already accounted for as a spawned enemy or a pet.
    leftover = {k: v for k, v in referenced.items() if k not in lives and k not in pet_ids}
    sorted_lives = sorted(
        lives.values(), key=lambda l: (l.added if l.added is not None else -1e9, l.name)
    )
    return sorted_lives, leftover


def print_pull_table(pulls: list[Pull], category: int) -> None:
    zone_name = pulls[0].zone_name
    print(f"Pulls in territory {category} (0x{category:X}) - {zone_name}")
    print(f"{'#':>3}  {'start':<19}  {'duration':>9}  outcome")
    for p in pulls:
        start = p.start.strftime("%Y-%m-%d %H:%M:%S")
        print(f"{p.index:>3}  {start}  {format_duration(p.duration_s)}  {p.outcome}")


def print_pull_details(path: str, pulls: list[Pull], idx: int) -> None:
    pull = pulls[idx]
    if idx + 1 < len(pulls):
        window_end = pulls[idx + 1].start
    elif pull.end is not None:
        window_end = pull.end + timedelta(seconds=60)
    else:
        window_end = pull.start + timedelta(hours=1)

    lives, referenced_only = collect_pull_data(path, pull, window_end)
    casts = collect_npc_casts(path, pull, window_end)

    start = pull.start.strftime("%Y-%m-%d %H:%M:%S")
    print()
    print(f"Pull #{pull.index} - {start} ({format_duration(pull.duration_s).strip()}, {pull.outcome})")

    if not lives:
        print()
        print("  (no enemy NPC add/remove events in this pull's window)")
    else:
        print()
        print(f"  {'id':<10}  {'base':>6}  {'name':<24}  {'added':>9}  {'removed':>9}")
        for life in lives:
            added = format_rel(life.added) if life.added is not None else "    -    "
            removed = format_rel(life.removed) if life.removed is not None else "    -    "
            base = str(life.base_id) if life.base_id is not None else "-"
            print(f"  {life.obj_id:<10}  {base:>6}  {life.name:<24}  {added:>9}  {removed:>9}")

    if referenced_only:
        print()
        print(f"  Referenced but not in spawned/removed list ({len(referenced_only)}):")
        print(f"    {'id':<10}  name")
        for obj_id in sorted(referenced_only.keys()):
            name = referenced_only[obj_id] or "?"
            print(f"    {obj_id:<10}  {name}")

    if casts:
        print()
        print(f"  NPC cast bars ({len(casts)}):")
        print(f"    {'time':>9}  {'cast':>5}  {'npc':<24}  spell")
        for c in casts:
            base = f" ({c.src_base_id})" if c.src_base_id is not None else ""
            npc_col = (c.src_name + base)[:24]
            count_suffix = f"  x{c.count}" if c.count > 1 else ""
            print(
                f"    {format_rel(c.t_rel):>9}  {c.cast_seconds:>4.1f}s  "
                f"{npc_col:<24}  {c.spell}{count_suffix}"
            )


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description="List pulls in a given territory from an ACT/IINACT network log.")
    ap.add_argument("log", help="Path to the network log file")
    ap.add_argument("category", type=lambda v: int(v, 0), help="Territory id in decimal (or 0x... for hex)")
    ap.add_argument(
        "pull",
        type=int,
        nargs="?",
        default=None,
        help="Optional pull number — when present, prints that pull's summary instead of the pull list.",
    )
    ap.add_argument(
        "begin",
        type=parse_relative_time,
        nargs="?",
        default=None,
        help='Optional window start, relative to pull start. Accepts "90", "1:30", "1:30.5".',
    )
    ap.add_argument(
        "end",
        type=parse_relative_time,
        nargs="?",
        default=None,
        help="Optional window end, relative to pull start (same format as begin).",
    )
    ap.add_argument(
        "-x",
        "--extended",
        action="store_true",
        help="When used with begin/end, also dump every related log event per active NPC "
             "(from pull start through end, inclusive). Player damage on the NPC is filtered.",
    )
    ap.add_argument(
        "--x",
        type=float,
        default=0.0,
        dest="center_x",
        help="Arena center X — subtracted from displayed X coords (default 0).",
    )
    ap.add_argument(
        "--y",
        type=float,
        default=0.0,
        dest="center_y",
        help="Arena center Y — subtracted from displayed Y coords (default 0).",
    )
    ap.add_argument(
        "--spawnx",
        type=float,
        default=None,
        dest="spawn_x",
        help="C# scenario PlayerPosition X (in --code mode). Defaults to --x when omitted.",
    )
    ap.add_argument(
        "--spawny",
        type=float,
        default=None,
        dest="spawn_y",
        help="C# scenario PlayerPosition Y (in --code mode). Defaults to --y when omitted.",
    )
    ap.add_argument(
        "--raw",
        action="store_true",
        help="In the extended view, print the original log line under each parsed event.",
    )
    ap.add_argument(
        "--code",
        action="store_true",
        help="Emit a complete C# IScenario class to stdout instead of the extended dump. "
             "Requires begin/end. Each event carries its raw ACT line as a trailing comment; "
             "opcodes without a C# mapping fall through as bare comments.",
    )
    ap.add_argument(
        "--class-name",
        default="ScenarioTemplate",
        help="C# class name to emit in --code mode (default: ScenarioTemplate).",
    )
    ap.add_argument(
        "--dropped",
        default=None,
        help="In --code mode, path to write every event in the [begin, end] "
             "window that did NOT appear as a `// {raw}` comment in the "
             "generated C# (one event per pair of lines: raw + human-readable).",
    )
    args = ap.parse_args(argv)

    pulls = find_pulls(args.log, args.category)
    if not pulls:
        print(f"No pulls found in territory {args.category} (0x{args.category:X}).")
        return 0

    if args.pull is None:
        print_pull_table(pulls, args.category)
        return 0

    if not (1 <= args.pull <= len(pulls)):
        print(f"Pull {args.pull} out of range (1-{len(pulls)}).")
        return 1

    pull = pulls[args.pull - 1]

    if (args.begin is None) != (args.end is None):
        print("begin and end must both be provided (or neither).")
        return 1

    if args.begin is not None:
        if args.end <= args.begin:
            print(f"end ({args.end}) must be greater than begin ({args.begin}).")
            return 1
        if args.code:
            window_start = pull.start + timedelta(seconds=args.begin)
            window_end = pull.start + timedelta(seconds=args.end)
            npcs = collect_active_npcs(args.log, pull, window_start, window_end)
            printed_raws: set[str] = set()
            print(emit_scenario_class(
                args.log, pull, npcs, args.begin, args.end,
                center_x=args.center_x, center_y=args.center_y,
                class_name=args.class_name,
                spawn_x=args.spawn_x, spawn_y=args.spawn_y,
                printed_raws=printed_raws,
            ))
            if args.dropped:
                n = write_dropped_events(
                    args.log, pull, args.begin, args.end,
                    printed_raws, args.dropped,
                    center_x=args.center_x, center_y=args.center_y,
                )
                print(f"Wrote {n} dropped events to {args.dropped}", file=sys.stderr)
            return 0
        npcs = print_active_npcs(args.log, pull, args.begin, args.end)
        if args.extended:
            instance_events = collect_instance_events(args.log, pull, args.begin, args.end)
            print_instance_events(instance_events, args.begin, show_raw=args.raw)
            if npcs:
                print_extended_logs(
                    args.log, pull, npcs, args.begin, args.end,
                    center_x=args.center_x, center_y=args.center_y,
                    show_raw=args.raw,
                )
        return 0

    if args.extended:
        print("--extended requires begin and end window arguments.")
        return 1

    if args.code:
        print("--code requires begin and end window arguments.")
        return 1

    print_pull_details(args.log, pulls, args.pull - 1)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
