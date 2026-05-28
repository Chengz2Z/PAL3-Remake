"""
GDB 解析器 — Python 移植自 Pal3.Core/DataReader/Gdb/GdbFileReader.cs

将 *_Softstar.gdb 二进制数据库解析为 4 个 JSON 文件:
  - CombatActors.json
  - CombatSkills.json
  - GameItems.json
  - CombatComboSkills.json

用法:
    python gdb_parse.py <gdb_path> <out_dir> [--variant PAL3|PAL3A]
"""

import argparse
import json
import struct
from pathlib import Path

# ======================================================================
# 枚举映射 (与 Pal3.Core/Contract/Enums/*.cs 保持一致)
# ======================================================================

ELEMENT_TYPE = {0: "None", 1: "Water", 2: "Fire", 3: "Wind", 4: "Thunder", 5: "Earth"}
ELEMENT_POSITION_REQ = {0: "Any", 1: "Water", 2: "Fire", 3: "Wind", 4: "Thunder",
                        5: "Earth", 6: "Middle", 7: "WindFireThunder", 8: "WaterFireEarth"}
TARGET_RANGE = {0: "None", 1: "FirstPartySingle", 2: "FirstPartyAll",
                3: "EnemyPartySingle", 4: "EnemyPartyAll",
                5: "EnemyPartyOneRow", 6: "EnemyPartyOneColumn"}
PLACE_OF_USE = {0: "None", 1: "Combat", 2: "OutOfCombat", 3: "Anywhere"}

ACTOR_ATTR = ["Hp", "Sp", "Mp", "Attack", "Defense", "Speed", "Luck",
              "Water", "Fire", "Wind", "Thunder", "Earth"]

COMBAT_ACTOR_TYPE = {0: "MainActor", 1: "Human", 2: "Monster", 3: "Fairy",
                     4: "God", 5: "Ghost", 6: "Demon"}

COMBAT_STATE = ["PoisonWind", "PoisonThunder", "PoisonWater", "PoisonFire", "PoisonEarth",
                "AttackIncrease", "AttackDecrease", "DefenseIncrease", "DefenseDecrease",
                "LuckIncrease", "LuckDecrease", "SpeedIncrease", "SpeedDecrease",
                "Paralysis", "Seal", "Forbidden", "Sleep", "Chaos", "Madness",
                "Reflection", "Evade", "Barrier",
                "Invisible", "Dying", "Death", "PoisonResist", "PoisonAny",
                "EvilResist", "DemonResist", "GodsEye", "ExpIncrease"]

SKILL_TYPE = {0: "StandardMagic", 1: "AssistMagic", 2: "DamageMagic",
              3: "RecoverMagic", 4: "WeaponMagic"}

ATTRIBUTE_IMPACT_TYPE = {0: "Absolute", 1: "Percentage",
                         2: "RecoverToMax", 3: "IncreaseMax"}
COMBAT_STATE_IMPACT_TYPE = {0: "None", 1: "Increase", 2: "Remove"}

ITEM_TYPE = {0: "None", 1: "Healing", 2: "Throwable", 3: "Treasure", 4: "Antique",
             5: "Ore", 6: "Plot", 7: "Cloth", 8: "Hat", 9: "Shoes", 10: "Wearable",
             11: "Weapon", 12: "Corpse", 13: "Blueprint"}
WEAPON_TYPE = {0: "None", 1: "Spear", 2: "Sword", 3: "Staff", 4: "Machete",
               5: "Bow", 6: "Spine", 7: "Sickle"}
ITEM_SPECIAL_TYPE = {0: "None", 1: "Poison", 2: "Flee",
                     15: "LevelIncrease", 20: "ExIncrease5Percent", 21: "ExIncrease15Percent"}
PLAYER_ACTOR_PAL3 = {0: "JingTian", 1: "XueJian", 2: "LongKui",
                     3: "ZiXuan", 4: "ChangQing", 5: "HuaYing"}
PLAYER_ACTOR_PAL3A = {0: "NanGongHuang", 1: "WenHui", 2: "WangPengXu",
                      3: "XingXuan", 4: "LeiYuanGe", 5: "TaoZi"}


# ======================================================================
# 二进制读取器
# ======================================================================

class BinaryReader:
    def __init__(self, data, codepage=936):
        self.data = data
        self.pos = 0
        self.codepage = codepage

    def seek(self, pos):
        self.pos = pos

    def read_bytes(self, n):
        v = self.data[self.pos:self.pos + n]
        self.pos += n
        return v

    def read_u8(self):
        v = self.data[self.pos]
        self.pos += 1
        return v

    def read_i16(self):
        v = struct.unpack_from("<h", self.data, self.pos)[0]
        self.pos += 2
        return v

    def read_u16(self):
        v = struct.unpack_from("<H", self.data, self.pos)[0]
        self.pos += 2
        return v

    def read_i32(self):
        v = struct.unpack_from("<i", self.data, self.pos)[0]
        self.pos += 4
        return v

    def read_u32(self):
        v = struct.unpack_from("<I", self.data, self.pos)[0]
        self.pos += 4
        return v

    def read_f32(self):
        v = struct.unpack_from("<f", self.data, self.pos)[0]
        self.pos += 4
        return v

    def read_string(self, n):
        raw = self.read_bytes(n)
        nul = raw.find(b"\x00")
        if nul >= 0:
            raw = raw[:nul]
        try:
            return raw.decode(f"cp{self.codepage}")
        except UnicodeDecodeError:
            return raw.decode(f"cp{self.codepage}", errors="replace")

    def read_u8s(self, n):
        v = list(self.data[self.pos:self.pos + n])
        self.pos += n
        return v

    def read_i16s(self, n):
        v = list(struct.unpack_from(f"<{n}h", self.data, self.pos))
        self.pos += 2 * n
        return v

    def read_u16s(self, n):
        v = list(struct.unpack_from(f"<{n}H", self.data, self.pos))
        self.pos += 2 * n
        return v

    def read_i32s(self, n):
        v = list(struct.unpack_from(f"<{n}i", self.data, self.pos))
        self.pos += 4 * n
        return v

    def read_u32s(self, n):
        v = list(struct.unpack_from(f"<{n}I", self.data, self.pos))
        self.pos += 4 * n
        return v

    def read_f32s(self, n):
        v = list(struct.unpack_from(f"<{n}f", self.data, self.pos))
        self.pos += 4 * n
        return v


# ======================================================================
# 辅助转换
# ======================================================================

def attr_value_map(values):
    return {ACTOR_ATTR[i]: values[i] for i in range(len(ACTOR_ATTR))}


def element_value_map(values):
    """ 5 element values (Water..Earth) starting at ElementType 1 """
    return {ELEMENT_TYPE[i + 1]: values[i] for i in range(5)}


def combat_state_impact_types(impact_bytes):
    out = {}
    for i, name in enumerate(COMBAT_STATE):
        v = impact_bytes[i]
        if v != 0:
            out[name] = COMBAT_STATE_IMPACT_TYPE.get(v, v)
    return out


def combat_state_impacts(impact_types_dict, values):
    out = {}
    for i, name in enumerate(COMBAT_STATE):
        v = values[i]
        if v != 0:
            out[name] = {
                "Type": impact_types_dict.get(name, "None"),
                "Value": v,
            }
    return out


def attribute_impacts(impact_type_bytes, impact_values):
    out = {}
    for i, name in enumerate(ACTOR_ATTR):
        v = impact_values[i]
        if v != 0:
            out[name] = {
                "Type": ATTRIBUTE_IMPACT_TYPE.get(impact_type_bytes[i], impact_type_bytes[i]),
                "Value": v,
            }
    return out


def player_actor_set(byte5, variant):
    table = PLAYER_ACTOR_PAL3 if variant == "PAL3" else PLAYER_ACTOR_PAL3A
    return [table[i] for i in range(5) if byte5[i] == 1]


def element_attr_set(byte5):
    """For items/skills: ObjectElementType — assume same indexing as ElementType minus None.
       Here just returning index mapping for present (=1) elements."""
    names = ["Water", "Fire", "Wind", "Thunder", "Earth"]
    return [names[i] for i in range(5) if byte5[i] == 1]


# ======================================================================
# 各结构解析
# ======================================================================

def read_combat_actor(r):
    actor_id = r.read_u32()
    type_ = COMBAT_ACTOR_TYPE.get(r.read_u16(), "?")
    description = r.read_string(512)
    model_id = r.read_string(30)
    icon_id = r.read_string(32)
    element_attribute_values = r.read_i32s(5)
    name = r.read_string(32)
    level = r.read_i32()
    attribute_values = r.read_i32s(12)
    combat_state_impact = r.read_u8s(31)
    r.read_u8()  # padding
    max_round = r.read_i32()
    special_action_id = r.read_i32()
    escape_rate = r.read_f32()
    main_actor_favor = r.read_u16s(6)
    experience = r.read_i32()
    money = r.read_u16()
    r.read_u16()  # padding
    normal_attack_mode_id = r.read_u32()
    height_level = r.read_u8()
    move_range_level = r.read_u8()
    attack_range_level = r.read_u8()
    move_speed_level = r.read_u8()
    chase_speed = r.read_u8()
    r.read_bytes(3)  # padding
    skill_ids = r.read_u32s(4)
    skill_levels = r.read_u8s(4)
    sp_impact_value = r.read_i32()
    properties = r.read_f32s(10)
    normal_loot = r.read_u32()
    normal_loot_count = r.read_i16()
    r.read_i16()  # padding
    corpse_id = r.read_u32()
    corpse_skill_id = r.read_u32()
    corpse_success_rate = r.read_i16()
    stealable_money_amount = r.read_i16()
    stealable_item_id = r.read_u32()
    stealable_item_count = r.read_i16()
    money_when_killed = r.read_i16()

    return {
        "Id": actor_id,
        "Type": type_,
        "Name": name,
        "Description": description,
        "ModelId": model_id,
        "IconId": icon_id,
        "Level": level,
        "ElementAttributeValues": element_value_map(element_attribute_values),
        "AttributeValues": attr_value_map(attribute_values),
        "CombatStateImpactTypes": combat_state_impact_types(combat_state_impact),
        "MaxRound": max_round,
        "SpecialActionId": special_action_id,
        "EscapeRate": escape_rate,
        "MainActorFavor": main_actor_favor,
        "Experience": experience,
        "Money": money,
        "NormalAttackModeId": normal_attack_mode_id,
        "HeightLevel": height_level,
        "MoveRangeLevel": move_range_level,
        "AttackRangeLevel": attack_range_level,
        "MoveSpeedLevel": move_speed_level,
        "ChaseSpeedLevel": chase_speed,
        "SkillIds": skill_ids,
        "SkillLevels": skill_levels,
        "SpImpactValue": sp_impact_value,
        "Properties": properties,
        "NormalLoot": normal_loot,
        "NormalLootCount": normal_loot_count,
        "CorpseId": corpse_id,
        "CorpseSkillId": corpse_skill_id,
        "CorpseSuccessRate": corpse_success_rate,
        "StealableMoneyAmount": stealable_money_amount,
        "StealableItemId": stealable_item_id,
        "StealableItemCount": stealable_item_count,
        "MoneyWhenKilled": money_when_killed,
    }


def read_skill(r, variant):
    skill_id = r.read_u32()
    type_ = SKILL_TYPE.get(r.read_u8(), "?")
    r.read_bytes(3)
    # element attributes are read as 5*int32 then cast to byte (per .cs source)
    element_attrs_i32 = r.read_i32s(5)
    name = r.read_string(32)
    description = r.read_string(512)
    main_actor_can_use = r.read_u8s(5)
    target_range = TARGET_RANGE.get(r.read_u8(), "?")
    special_skill_id = r.read_u8()
    attribute_impact_type = r.read_u8s(12)
    r.read_u8()
    attribute_impact_value = r.read_i16s(12)
    success_rate_level = r.read_u8()
    r.read_u8()
    # combat state impact types: 31 * int16 then cast to byte
    combat_state_impact_i16 = r.read_i16s(31)
    sp_consume_impact_type = ATTRIBUTE_IMPACT_TYPE.get(r.read_i32(), "?")
    mp_consume_impact_type = ATTRIBUTE_IMPACT_TYPE.get(r.read_i32(), "?")
    sp_consume_value = r.read_i32()
    mp_consume_value = r.read_i32()
    special_consume_type = r.read_i32()
    special_consume_impact_type = ATTRIBUTE_IMPACT_TYPE.get(r.read_u8(), "?")
    r.read_bytes(3)
    special_consume_value = r.read_i32()
    level = r.read_u8()
    times_before_level_up = r.read_u8s(4)
    required_actor_level = r.read_u8()
    magic_level = r.read_u8()
    r.read_u8()
    next_level_skill_id = r.read_u32()
    is_usable_outside_combat = r.read_u8()
    r.read_bytes(3)
    composite_skill_ids = r.read_u32s(3)
    composite_required_skill_ids = r.read_u32s(3)
    composite_required_skill_levels = r.read_u8s(3)
    composite_required_current_skill_levels = r.read_u8s(3)
    composite_required_actor_levels = r.read_u8s(3)
    can_trigger_combo_skill = r.read_u8()
    r.read_bytes(2)

    element_attrs = [b & 0xFF for b in element_attrs_i32]
    combat_state_impact = [b & 0xFF for b in combat_state_impact_i16]

    return {
        "Id": skill_id,
        "Type": type_,
        "Name": name,
        "Description": description,
        "ElementAttributes": element_attr_set(element_attrs),
        "ApplicableActors": player_actor_set(main_actor_can_use, variant),
        "TargetRangeType": target_range,
        "SpecialSkillId": special_skill_id,
        "AttributeImpacts": attribute_impacts(attribute_impact_type, attribute_impact_value),
        "SuccessRateLevel": success_rate_level,
        "CombatStateImpactTypes": combat_state_impact_types(combat_state_impact),
        "SpConsumeImpactType": sp_consume_impact_type,
        "MpConsumeImpactType": mp_consume_impact_type,
        "SpConsumeValue": sp_consume_value,
        "MpConsumeValue": mp_consume_value,
        "SpecialConsumeType": special_consume_type,
        "SpecialConsumeImpactType": special_consume_impact_type,
        "SpecialConsumeValue": special_consume_value,
        "Level": level,
        "TimesBeforeLevelUp": times_before_level_up,
        "RequiredActorLevel": required_actor_level,
        "MagicLevel": magic_level,
        "NextLevelSkillId": next_level_skill_id,
        "IsUsableOutsideCombat": bool(is_usable_outside_combat),
        "CompositeSkillIds": composite_skill_ids,
        "CompositeRequiredSkillIds": composite_required_skill_ids,
        "CompositeRequiredSkillLevels": composite_required_skill_levels,
        "CompositeRequiredCurrentSkillLevels": composite_required_current_skill_levels,
        "CompositeRequiredActorLevels": composite_required_actor_levels,
        "CanTriggerComboSkill": bool(can_trigger_combo_skill),
    }


def read_item(r, variant):
    item_id = r.read_u32()
    name = r.read_string(32)
    model_name = r.read_string(30)
    icon_name = r.read_string(30)
    description = r.read_string(512)
    price = r.read_i32()
    type_ = ITEM_TYPE.get(r.read_u8(), "?")
    weapon_type = WEAPON_TYPE.get(r.read_u8(), "?")
    main_actor_can_use = r.read_u8s(5)
    element_attrs = r.read_u8s(5)
    ancient_value = r.read_i32()
    item_special_type = ITEM_SPECIAL_TYPE.get(r.read_u8(), "?")
    target_range = TARGET_RANGE.get(r.read_u8(), "?")
    place_of_use = PLACE_OF_USE.get(r.read_u8(), "?")
    attribute_impact_type = r.read_u8s(12)
    r.read_u8()
    attribute_impact_value = r.read_i16s(12)
    combat_state_impact_types_bytes = r.read_u8s(31)
    r.read_u8()
    combat_state_impact_values = r.read_i16s(31)
    r.read_bytes(2)

    info = {
        "Id": item_id,
        "Name": name,
        "ModelName": model_name,
        "IconName": icon_name,
        "Description": description,
        "Price": price,
        "Type": type_,
        "WeaponType": weapon_type,
        "ApplicableActors": player_actor_set(main_actor_can_use, variant),
        "ElementAttributes": element_attr_set(element_attrs),
        "AncientValue": ancient_value,
        "ItemSpecialType": item_special_type,
        "TargetRangeType": target_range,
        "PlaceOfUseType": place_of_use,
        "AttributeImpacts": attribute_impacts(attribute_impact_type, attribute_impact_value),
        "CombatStateImpacts": combat_state_impacts(
            combat_state_impact_types(combat_state_impact_types_bytes),
            combat_state_impact_values),
    }

    if variant == "PAL3":
        info.update({
            "ComboCount": r.read_i32(),
            "SpSavingPercentage": r.read_i16(),
            "MpSavingPercentage": r.read_i16(),
            "CriticalAttackAmplifyPercentage": r.read_i16(),
            "SpecialSkillSuccessRate": r.read_i16(),
            "OreId": r.read_u32(),
            "ProductId": r.read_u32(),
            "ProductPrice": r.read_i32(),
            "SynthesisMaterialIds": r.read_u32s(2),
            "SynthesisProductId": r.read_u32(),
        })
    else:  # PAL3A
        info.update({
            "Unknown1": r.read_i32(),
            "Unknown2": r.read_u32(),
            "SpSavingPercentage": r.read_i16(),
            "MpSavingPercentage": r.read_i16(),
            "CriticalAttackAmplifyPercentage": r.read_i16(),
            "SpecialSkillSuccessRate": r.read_i16(),
            "CreatorActorId": PLAYER_ACTOR_PAL3A.get(r.read_i32(), "?"),
            "MaterialId": r.read_u32(),
            "ProductType": r.read_i32(),
            "ProductId": r.read_u32(),
            "RequiredFavorValue": r.read_u32(),
        })
    return info


def read_combo_skill(r, variant):
    name = r.read_string(32)
    combo_id = r.read_u32()
    main_actor_requirements = r.read_u32s(4)
    element_pos_req = [ELEMENT_POSITION_REQ.get(b, b) for b in r.read_u8s(4)]
    skill_id = r.read_u32()
    weapon_type_req = [WEAPON_TYPE.get(b, b) for b in r.read_u8s(4)]
    r.read_bytes(4)
    combat_state_req = [COMBAT_STATE[v] if 0 <= v < len(COMBAT_STATE) else v
                        for v in r.read_i32s(3)]
    description = r.read_string(512)
    target_range = TARGET_RANGE.get(r.read_u8(), "?")
    attribute_impact_type = r.read_u8s(12)
    r.read_bytes(3)
    attribute_impact_value = r.read_i16s(12)

    info = {
        "Id": combo_id,
        "Name": name,
        "Description": description,
        "MainActorRequirements": main_actor_requirements,
        "ElementPositionRequirements": element_pos_req,
        "SkillId": skill_id,
        "WeaponTypeRequirements": weapon_type_req,
        "CombatStateRequirements": combat_state_req,
        "TargetRangeType": target_range,
        "AttributeImpacts": attribute_impacts(attribute_impact_type, attribute_impact_value),
    }
    if variant == "PAL3A":
        info["Unknown"] = r.read_i32()
    return info


# ======================================================================
# 主流程
# ======================================================================

def parse_gdb(path, variant, codepage=936):
    data = Path(path).read_bytes()
    r = BinaryReader(data, codepage=codepage)

    _header = r.read_string(128)
    actor_offset, num_actors = r.read_u32(), r.read_i32()
    skill_offset, num_skills = r.read_u32(), r.read_i32()
    item_offset, num_items = r.read_u32(), r.read_i32()
    combo_offset, num_combos = r.read_u32(), r.read_i32()

    r.seek(actor_offset)
    actors = [read_combat_actor(r) for _ in range(num_actors)]

    r.seek(skill_offset)
    skills = [read_skill(r, variant) for _ in range(num_skills)]

    r.seek(item_offset)
    items = [read_item(r, variant) for _ in range(num_items)]

    r.seek(combo_offset)
    combos = [read_combo_skill(r, variant) for _ in range(num_combos)]

    return {
        "CombatActors": actors,
        "CombatSkills": skills,
        "GameItems": items,
        "CombatComboSkills": combos,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("gdb", help="*_Softstar.gdb path")
    parser.add_argument("out", help="Output dir")
    parser.add_argument("--variant", choices=["PAL3", "PAL3A"], required=True)
    parser.add_argument("--codepage", type=int, default=936)
    args = parser.parse_args()

    parsed = parse_gdb(args.gdb, args.variant, codepage=args.codepage)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    for key, items in parsed.items():
        target = out / f"{key}.json"
        target.write_text(json.dumps(items, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"  wrote {len(items):>4} entries -> {target}")


if __name__ == "__main__":
    main()
