// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

// bbs_c_api.cpp — C ABI implementation.
#include "bbs_c_api.h"

#include "Bridge.h"
#include "../core/Export.h"
#include "../core/Json.h"
#include "../core/Project.h"

#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

static char* dup_utf8(const std::string& s) {
    char* p = static_cast<char*>(std::malloc(s.size() + 1));
    if (!p) return nullptr;
    std::memcpy(p, s.data(), s.size());
    p[s.size()] = '\0';
    return p;
}

static std::string err_json(const std::string& msg) {
    return std::string("{\"ok\":false,\"error\":\"") + msg + "\"}";
}

extern "C" {

BBS_API void bbs_free(char* p) { std::free(p); }

BBS_API int bbs_generate(const char* kind, const char* settings_json, const char* rows_json,
                         char** out_json) {
    if (!out_json) return 0;
    *out_json = nullptr;
    std::string err;
    bbs::Settings s = bbs::settings_from_json_text(settings_json ? settings_json : "", err);
    if (!err.empty()) {
        *out_json = dup_utf8(err_json(err));
        return 0;
    }
    err.clear();
    auto rows = bbs::rows_from_json_text(rows_json ? rows_json : "[]", err);
    if (!err.empty()) {
        *out_json = dup_utf8(err_json(err));
        return 0;
    }
    auto res = bbs::generate_kind(kind ? kind : "", s, rows);
    std::string json = bbs::bridge_result_to_json(res);
    *out_json = dup_utf8(json);
    return res.error.empty() ? 1 : 0;
}

BBS_API int bbs_load_project(const wchar_t* path, char** out_json) {
    if (!out_json || !path) return 0;
    *out_json = nullptr;
    bbs::ProjectData data;
    std::string err;
    if (!bbs::load_project(path, data, err)) {
        *out_json = dup_utf8(err_json(err));
        return 0;
    }
    *out_json = dup_utf8(bbs::project_to_json_text(data));
    return 1;
}

BBS_API int bbs_save_project(const wchar_t* path, const char* project_json, char** out_error) {
    if (out_error) *out_error = nullptr;
    if (!path || !project_json) {
        if (out_error) *out_error = dup_utf8("Missing path or JSON.");
        return 0;
    }
    bbs::ProjectData data;
    std::string err;
    if (!bbs::project_from_json_text(project_json, data, err)) {
        if (out_error) *out_error = dup_utf8(err);
        return 0;
    }
    if (!bbs::save_project(path, data, err)) {
        if (out_error) *out_error = dup_utf8(err);
        return 0;
    }
    return 1;
}

BBS_API int bbs_export_csv(const wchar_t* path, const char* headers_json, const char* rows_json,
                           char** out_error) {
    if (out_error) *out_error = nullptr;
    if (!path) {
        if (out_error) *out_error = dup_utf8("Missing path.");
        return 0;
    }
    std::string err;
    bbs::JsonValue hdrRoot, rowRoot;
    if (!bbs::json_parse(headers_json ? headers_json : "[]", hdrRoot, err) || !hdrRoot.isArray()) {
        if (out_error) *out_error = dup_utf8(err.empty() ? "Bad headers JSON" : err);
        return 0;
    }
    if (!bbs::json_parse(rows_json ? rows_json : "[]", rowRoot, err) || !rowRoot.isArray()) {
        if (out_error) *out_error = dup_utf8(err.empty() ? "Bad rows JSON" : err);
        return 0;
    }
    std::vector<std::string> headers;
    for (const auto& h : hdrRoot.arr) headers.push_back(h.asString());
    std::vector<std::vector<std::string>> rows;
    for (const auto& r : rowRoot.arr) {
        if (!r.isArray()) continue;
        std::vector<std::string> cells;
        for (const auto& c : r.arr) cells.push_back(c.asString());
        rows.push_back(cells);
    }
    if (!bbs::export_table_csv(headers, rows, path, err)) {
        if (out_error) *out_error = dup_utf8(err);
        return 0;
    }
    return 1;
}

BBS_API int bbs_export_html(const wchar_t* path, const char* project_json, char** out_error) {
    if (out_error) *out_error = nullptr;
    if (!path || !project_json) {
        if (out_error) *out_error = dup_utf8("Missing path or JSON.");
        return 0;
    }
    bbs::ProjectData data;
    std::string err;
    if (!bbs::project_from_json_text(project_json, data, err)) {
        if (out_error) *out_error = dup_utf8(err);
        return 0;
    }
    auto sections = bbs::build_report_sections(data);
    if (!bbs::export_html_report(data.name, sections, path, err)) {
        if (out_error) *out_error = dup_utf8(err);
        return 0;
    }
    return 1;
}

}  // extern "C"
