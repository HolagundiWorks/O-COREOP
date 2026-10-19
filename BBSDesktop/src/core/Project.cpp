// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

// Project.cpp — project persistence + shared file I/O helpers.
#include "Project.h"

#include "Engine.h"
#include "Json.h"

#include <cstdio>
#include <filesystem>
#include <fstream>

namespace bbs {

bool write_text_file(const std::wstring& path, const std::string& utf8, std::string& err) {
    std::ofstream f(std::filesystem::path(path), std::ios::binary);  // path overload: wide paths work on MSVC and libc++
    if (!f) { err = "Could not open file for writing."; return false; }
    f.write(utf8.data(), static_cast<std::streamsize>(utf8.size()));
    if (!f) { err = "Write failed."; return false; }
    return true;
}

bool read_text_file(const std::wstring& path, std::string& out, std::string& err) {
    std::ifstream f(std::filesystem::path(path), std::ios::binary);
    if (!f) { err = "Could not open file for reading."; return false; }
    out.assign((std::istreambuf_iterator<char>(f)), std::istreambuf_iterator<char>());
    return true;
}

// ------------------------------ settings <-> json ------------------------------

static JsonValue settings_to_json(const Settings& s) {
    JsonValue o = JsonValue::Object();
    JsonValue dias = JsonValue::Array();
    for (int d : s.diameters) dias.arr.push_back(JsonValue::Num(d));
    o.set("diameters", dias);

    JsonValue hooks = JsonValue::Object();
    for (const auto& kv : s.hook_allowance) hooks.set(std::to_string(kv.first), JsonValue::Num(kv.second));
    o.set("hook_allowance", hooks);

    JsonValue bends = JsonValue::Object();
    for (const auto& kv : s.bend_deduction) bends.set(std::to_string(kv.first), JsonValue::Num(kv.second));
    o.set("bend_deduction", bends);

    JsonValue tau = JsonValue::Object();
    for (const auto& kv : s.tau_bd) tau.set(kv.first, JsonValue::Num(kv.second));
    o.set("tau_bd", tau);

    JsonValue fy = JsonValue::Object();
    for (const auto& kv : s.fy) fy.set(kv.first, JsonValue::Num(kv.second));
    o.set("fy", fy);

    o.set("hysd_bond", JsonValue::Num(s.hysd_bond ? 1 : 0));
    o.set("hysd_bond_factor", JsonValue::Num(s.hysd_bond_factor));
    o.set("min_hook_mm", JsonValue::Num(s.min_hook_mm));
    return o;
}

static void settings_from_json(const JsonValue* o, Settings& s) {
    if (!o || !o->isObject()) return;
    if (const JsonValue* d = o->find("diameters"); d && d->isArray()) {
        s.diameters.clear();
        for (const auto& v : d->arr) s.diameters.push_back(static_cast<int>(v.asNumber()));
    }
    if (const JsonValue* h = o->find("hook_allowance"); h && h->isObject()) {
        s.hook_allowance.clear();
        for (const auto& kv : h->obj) s.hook_allowance[std::atoi(kv.first.c_str())] = kv.second.asNumber();
    }
    if (const JsonValue* b = o->find("bend_deduction"); b && b->isObject()) {
        s.bend_deduction.clear();
        for (const auto& kv : b->obj) s.bend_deduction[std::atoi(kv.first.c_str())] = kv.second.asNumber();
    }
    if (const JsonValue* t = o->find("tau_bd"); t && t->isObject()) {
        s.tau_bd.clear();
        for (const auto& kv : t->obj) s.tau_bd[kv.first] = kv.second.asNumber();
    }
    if (const JsonValue* f = o->find("fy"); f && f->isObject()) {
        s.fy.clear();
        for (const auto& kv : f->obj) s.fy[kv.first] = kv.second.asNumber();
    }
    if (const JsonValue* hb = o->find("hysd_bond")) s.hysd_bond = hb->asNumber() != 0;
    if (const JsonValue* hf = o->find("hysd_bond_factor")) s.hysd_bond_factor = hf->asNumber();
    if (const JsonValue* mh = o->find("min_hook_mm")) s.min_hook_mm = mh->asNumber();
}

static JsonValue rows_to_json(const std::vector<RawRow>& rows) {
    JsonValue arr = JsonValue::Array();
    for (const auto& row : rows) {
        JsonValue o = JsonValue::Object();
        for (const auto& kv : row) o.set(kv.first, JsonValue::Str(kv.second));
        arr.arr.push_back(o);
    }
    return arr;
}

static std::vector<RawRow> rows_from_json(const JsonValue* arr) {
    std::vector<RawRow> rows;
    if (!arr || !arr->isArray()) return rows;
    for (const auto& item : arr->arr) {
        if (!item.isObject()) continue;
        RawRow row;
        for (const auto& kv : item.obj) {
            if (kv.second.type == JsonValue::Type::String) row[kv.first] = kv.second.str;
            else if (kv.second.type == JsonValue::Type::Number) row[kv.first] = format_num(kv.second.num, 6);
        }
        rows.push_back(row);
    }
    return rows;
}

// ------------------------------ save / load ------------------------------

bool save_project(const std::wstring& path, const ProjectData& data, std::string& err) {
    JsonValue root = JsonValue::Object();
    root.set("format", JsonValue::Str("bbsproj"));
    root.set("version", JsonValue::Num(3));
    root.set("name", JsonValue::Str(data.name));
    root.set("settings", settings_to_json(data.settings));
    JsonValue levels = JsonValue::Array();
    for (const auto& lv : data.levels) {
        JsonValue o = JsonValue::Object();
        o.set("id", JsonValue::Str(lv.id));
        o.set("name", JsonValue::Str(lv.name));
        o.set("height_mm", JsonValue::Num(lv.height_mm));
        o.set("slab_thickness_mm", JsonValue::Num(lv.slab_thickness_mm));
        o.set("beam_depth_mm", JsonValue::Num(lv.beam_depth_mm));
        levels.arr.push_back(o);
    }
    root.set("levels", levels);
    root.set("columns", rows_to_json(data.columns));
    root.set("beams", rows_to_json(data.beams));
    root.set("slabs", rows_to_json(data.slabs));
    root.set("footings", rows_to_json(data.footings));
    root.set("walls", rows_to_json(data.walls));
    root.set("stairs", rows_to_json(data.stairs));
    return write_text_file(path, json_dump(root), err);
}

bool load_project(const std::wstring& path, ProjectData& out, std::string& err) {
    std::string text;
    if (!read_text_file(path, text, err)) return false;
    JsonValue root;
    if (!json_parse(text, root, err)) return false;
    if (!root.isObject() || !root.find("settings")) {
        err = "This file doesn't look like a BBS project (.bbsproj).";
        return false;
    }
    if (const JsonValue* n = root.find("name"); n && n->type == JsonValue::Type::String)
        out.name = n->str;
    settings_from_json(root.find("settings"), out.settings);
    out.levels.clear();
    if (const JsonValue* la = root.find("levels"); la && la->isArray()) {
        for (const auto& item : la->arr) {
            if (!item.isObject()) continue;
            ProjectData::Level lv;
            if (const JsonValue* id = item.find("id")) lv.id = id->asString();
            if (const JsonValue* nm = item.find("name")) lv.name = nm->asString();
            if (const JsonValue* h = item.find("height_mm")) lv.height_mm = h->asNumber();
            if (const JsonValue* st = item.find("slab_thickness_mm")) lv.slab_thickness_mm = st->asNumber();
            if (const JsonValue* bd = item.find("beam_depth_mm")) lv.beam_depth_mm = bd->asNumber();
            out.levels.push_back(lv);
        }
    }
    out.columns  = rows_from_json(root.find("columns"));
    out.beams    = rows_from_json(root.find("beams"));
    out.slabs    = rows_from_json(root.find("slabs"));
    out.footings = rows_from_json(root.find("footings"));
    out.walls    = rows_from_json(root.find("walls"));
    out.stairs   = rows_from_json(root.find("stairs"));
    return true;
}

}  // namespace bbs
