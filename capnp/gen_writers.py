#!/usr/bin/env python3
"""gen_writers.py — generate the canonical-encoding schema writers.

Reads ogsa_telemetry.capnp through pycapnp (the official compiler computes
every field offset; nothing here does layout arithmetic) and emits one
typed writer class per struct, plus the schema's enums, for:

  --lang csharp  over Wire.cs (default; ogsa-ts-dotnet's core)
  --lang cpp     over OgsaTelemetryCanonical.h (ogsa-ts-unreal's core)

Each writer upholds the field-presence rules of the standard's encoding
rules (implementation-capnproto.md) by construction: non-union Text and
struct fields are created in the constructor, and selecting a union variant
clears every other variant's storage.

Supports exactly what the schema uses — top-level structs and enums,
primitive/enum/Text/struct fields, List(struct), one unnamed union per
struct, no explicit defaults — and fails loudly on anything else, so a
schema change that needs more support cannot slip through.

Usage (pip install pycapnp):
  python capnp/gen_writers.py                       # C#, this repo
  python capnp/gen_writers.py --lang cpp --out <OgsaTelemetrySchema.h>
"""
import argparse
import pathlib
import sys

import capnp  # pycapnp

HERE = pathlib.Path(__file__).parent
SCHEMA = HERE / "ogsa_telemetry.capnp"
CS_OUT = HERE.parent / "src" / "Ogsa.Telemetry" / "Schema" / "ogsa_telemetry.writers.cs"

NO_DISCRIMINANT = 0xFFFF
WIDTH = {
    "bool": 1, "int8": 8, "uint8": 8, "int16": 16, "uint16": 16, "enum": 16,
    "int32": 32, "uint32": 32, "int64": 64, "uint64": 64,
    "float32": 32, "float64": 64,
}


def fail(msg):
    sys.exit(f"gen_writers: unsupported schema construct: {msg}")


def pascal(name):
    return name[0].upper() + name[1:]


# ---------------------------------------------------------------------------
# Layout model (language-neutral)
# ---------------------------------------------------------------------------

class Field:
    def __init__(self, f, names, owner):
        self.name = f.name
        self.prop = pascal(f.name)
        self.kind = str(f.slot.type.which)
        self.in_union = f.discriminantValue != NO_DISCRIMINANT
        self.disc = f.discriminantValue
        self.type_name = None
        if self.kind in ("struct", "enum"):
            type_id = getattr(f.slot.type, self.kind).typeId
            if type_id not in names:
                fail(f"{owner}.{f.name}: type {type_id:#x} is not top-level")
            self.type_name = names[type_id]
        elif self.kind == "list":
            elem = f.slot.type.list.elementType
            if str(elem.which) != "struct":
                fail(f"{owner}.{f.name}: List of {elem.which}")
            if self.in_union:
                fail(f"{owner}.{f.name}: list inside a union")
            self.type_name = names[elem.struct.typeId]
        elif self.kind == "void":
            if not self.in_union:
                fail(f"{owner}.{f.name}: non-union Void")
        elif self.kind not in WIDTH and self.kind != "text":
            fail(f"{owner}.{f.name}: field type {self.kind}")
        if self.kind in WIDTH:
            self.width = WIDTH[self.kind]
            self.bit = f.slot.offset * self.width
        else:
            self.ptr = f.slot.offset

    @property
    def is_pointer(self):
        return self.kind in ("text", "struct", "list")


class Struct:
    def __init__(self, name, node, names):
        s = node.struct
        if node.nestedNodes:
            fail(f"{name}: nested declarations")
        if s.isGroup:
            fail(f"{name}: groups")
        self.name = name
        self.data_words = s.dataWordCount
        self.pointers = s.pointerCount
        self.disc_bit = s.discriminantOffset * 16
        self.fields = []
        for f in sorted(s.fields, key=lambda f: f.codeOrder):
            if str(f.which) != "slot":
                fail(f"{name}.{f.name}: groups / named unions")
            if f.slot.hadExplicitDefault:
                fail(f"{name}.{f.name}: explicit default")
            self.fields.append(Field(f, names, name))
        self.union = sorted((f for f in self.fields if f.in_union),
                            key=lambda f: f.disc)

    def created(self):
        """Pointer fields the constructor fills: non-union Text/struct, and
        the discriminant-0 variant's payload when it is one."""
        return [f for f in self.fields
                if f.kind in ("text", "struct")
                and (not f.in_union or f.disc == 0)]

    def clears(self):
        """Union storage to clear on a variant switch: (kind, offset, width),
        each shared slot once."""
        out = []
        for f in self.union:
            if f.kind in WIDTH:
                c = ("bits", f.bit, f.width)
            elif f.is_pointer:
                c = ("ptr", f.ptr, None)
            else:
                continue
            if c not in out:
                out.append(c)
        return out


def load(path):
    module = capnp.load(str(path))  # type: ignore # stub-less Cython module
    names, decls = {}, []
    for nested in module.schema.node.nestedNodes:
        node = getattr(module, nested.name).schema.node
        names[node.id] = nested.name
        decls.append((nested.name, node))
    model = []
    for name, node in decls:
        which = str(node.which)
        if which == "enum":
            model.append(("enum", name, [e.name for e in node.enum.enumerants]))
        elif which == "struct":
            model.append(("struct", name, Struct(name, node, names)))
        else:
            fail(f"{name}: {which} declaration")
    return model


# ---------------------------------------------------------------------------
# C# emitter
# ---------------------------------------------------------------------------

CS_PRIM = {
    # kind: (C# type, getter, setter value)
    "bool": ("bool", "GetBool({b})", "SetBool({b}, value)"),
    "int8": ("sbyte", "(sbyte)GetBits({b}, 8)", "SetBits({b}, 8, (ulong)value)"),
    "uint8": ("byte", "(byte)GetBits({b}, 8)", "SetBits({b}, 8, value)"),
    "int16": ("short", "(short)GetBits({b}, 16)", "SetBits({b}, 16, (ulong)value)"),
    "uint16": ("ushort", "(ushort)GetBits({b}, 16)", "SetBits({b}, 16, value)"),
    "int32": ("int", "(int)GetBits({b}, 32)", "SetBits({b}, 32, (ulong)value)"),
    "uint32": ("uint", "(uint)GetBits({b}, 32)", "SetBits({b}, 32, value)"),
    "int64": ("long", "(long)GetBits({b}, 64)", "SetBits({b}, 64, (ulong)value)"),
    "uint64": ("ulong", "GetBits({b}, 64)", "SetBits({b}, 64, value)"),
    "float32": ("float", "GetFloat32({b})", "SetFloat32({b}, value)"),
    "float64": ("double", "GetFloat64({b})", "SetFloat64({b}, value)"),
}


def cs_struct(st):
    out = [f"public sealed class {st.name} : WireStruct", "{"]
    if st.union:
        out += ["    public enum Variant : ushort", "    {"]
        out += [f"        {f.prop} = {f.disc}," for f in st.union]
        out += ["    }", ""]
    out.append(f"    public {st.name}() : base({st.data_words}, {st.pointers})")
    out.append("    {")
    for f in st.created():
        value = '""' if f.kind == "text" else f"new {f.type_name}()"
        out.append(f"        SetPointer({f.ptr}, {value});")
    out.append("    }")
    if st.union:
        out += ["", f"    public Variant Which => (Variant)GetBits({st.disc_bit}, 16);",
                "", "    void Select(Variant v)", "    {", "        if (Which == v) return;"]
        for kind, off, width in st.clears():
            out.append(f"        SetBits({off}, {width}, 0);" if kind == "bits"
                       else f"        SetPointer({off}, null);")
        out += [f"        SetBits({st.disc_bit}, 16, (ushort)v);", "    }"]
    for f in st.fields:
        out.append("")
        out.extend(("    " + line) if line else "" for line in cs_field(f))
    out.append("}")
    return out


def cs_field(f):
    sel = f"Select(Variant.{f.prop}); " if f.in_union else ""
    guard = f"Which == Variant.{f.prop} ? " if f.in_union else ""
    if f.kind == "void":
        return [f"public void Set{f.prop}() => Select(Variant.{f.prop});"]
    if f.kind in CS_PRIM or f.kind == "enum":
        if f.kind == "enum":
            cs, getter = f.type_name, f"({f.type_name})GetBits({f.bit}, 16)"
            setter = f"SetBits({f.bit}, 16, (ushort)value)"
        else:
            cs, get, set_ = CS_PRIM[f.kind]
            getter, setter = get.format(b=f.bit), set_.format(b=f.bit)
        if f.in_union:
            getter = f"{guard}{getter} : default"
        return [f"public {cs} {f.prop}", "{", f"    get => {getter};",
                f"    set {{ {sel}{setter}; }}", "}"]
    if f.kind == "text":
        if f.in_union:
            return [f"public string? {f.prop}", "{",
                    f"    get => {guard}(string?)GetPointer({f.ptr}) : null;",
                    f"    set {{ {sel}SetPointer({f.ptr}, value ?? \"\"); }}", "}"]
        return [f"public string {f.prop}", "{",
                f"    get => (string)GetPointer({f.ptr})!;",
                f"    set => SetPointer({f.ptr}, value ?? \"\");", "}"]
    if f.kind == "struct":
        cs = f.type_name
        if f.in_union:
            return [f"public {cs}? {f.prop} => {guard}({cs}?)GetPointer({f.ptr}) : null;",
                    "",
                    f"/// <summary>Selects <c>{f.name}</c>; returns its (existing or new) payload.</summary>",
                    f"public {cs} Init{f.prop}()", "{",
                    f"    if (Which == Variant.{f.prop} && GetPointer({f.ptr}) is {cs} existing) return existing;",
                    f"    Select(Variant.{f.prop});",
                    f"    var created = new {cs}();",
                    f"    SetPointer({f.ptr}, created);",
                    "    return created;", "}"]
        return [f"public {cs} {f.prop} => ({cs})GetPointer({f.ptr})!;"]
    # list
    cs = f.type_name
    return ["/// <summary>Written when non-empty; null and empty both encode as null.</summary>",
            f"public List<{cs}>? {f.prop}", "{",
            f"    get => (List<{cs}>?)GetPointer({f.ptr});",
            f"    set => SetPointer({f.ptr}, value);", "}"]


def emit_csharp(model):
    out = [
        "// <auto-generated>",
        "// Generated by capnp/gen_writers.py from ogsa_telemetry.capnp — do not edit.",
        "// Regenerate with: python capnp/gen_writers.py",
        "// </auto-generated>",
        "#nullable enable",
        "using System.Collections.Generic;",
        "using Ogsa.Telemetry.Wire;",
        "",
        "namespace Ogsa.Telemetry.Schema",
        "{",
    ]
    for kind, name, item in model:
        out.append("")
        if kind == "enum":
            out += [f"public enum {name} : ushort", "{"]
            out += [f"    {pascal(e)} = {v}," for v, e in enumerate(item)]
            out.append("}")
        else:
            out += cs_struct(item)
    out.append("}")
    return "\n".join(out) + "\n"


# ---------------------------------------------------------------------------
# C++ emitter (C++17, std-only; types fully qualified so a member function
# named like a type — Environment(), Range() — never shadows it)
# ---------------------------------------------------------------------------

NS = "::ogsa::telemetry::schema::"
CPP_PRIM = {
    "bool": ("bool", "WireReadBool({b})", "WireWriteBool({b}, v)"),
    "int8": ("int8_t", "(int8_t)WireReadBits({b}, 8)", "WireWriteBits({b}, 8, (uint64_t)(uint8_t)v)"),
    "uint8": ("uint8_t", "(uint8_t)WireReadBits({b}, 8)", "WireWriteBits({b}, 8, v)"),
    "int16": ("int16_t", "(int16_t)WireReadBits({b}, 16)", "WireWriteBits({b}, 16, (uint64_t)(uint16_t)v)"),
    "uint16": ("uint16_t", "(uint16_t)WireReadBits({b}, 16)", "WireWriteBits({b}, 16, v)"),
    "int32": ("int32_t", "(int32_t)WireReadBits({b}, 32)", "WireWriteBits({b}, 32, (uint64_t)(uint32_t)v)"),
    "uint32": ("uint32_t", "(uint32_t)WireReadBits({b}, 32)", "WireWriteBits({b}, 32, v)"),
    "int64": ("int64_t", "(int64_t)WireReadBits({b}, 64)", "WireWriteBits({b}, 64, (uint64_t)v)"),
    "uint64": ("uint64_t", "WireReadBits({b}, 64)", "WireWriteBits({b}, 64, v)"),
    "float32": ("float", "WireReadFloat32({b})", "WireWriteFloat32({b}, v)"),
    "float64": ("double", "WireReadFloat64({b})", "WireWriteFloat64({b}, v)"),
}


def cpp_struct(st):
    out = [f"class {st.name} final : public ::ogsa::telemetry::wire::Struct", "{", "public:"]
    if st.union:
        out.append("    enum class Variant : uint16_t")
        out.append("    {")
        out += [f"        {f.prop} = {f.disc}," for f in st.union]
        out += ["    };", ""]
    out.append(f"    {st.name}() : Struct({st.data_words}, {st.pointers})")
    out.append("    {")
    for f in st.created():
        if f.kind == "text":
            out.append(f"        WireSetText({f.ptr}, std::string());")
        else:
            out.append(f"        WireEmplace<{NS}{f.type_name}>({f.ptr});")
    out.append("    }")
    if st.union:
        out += ["", f"    Variant Which() const {{ return (Variant)WireReadBits({st.disc_bit}, 16); }}"]
    for f in st.fields:
        out.append("")
        out.extend(("    " + line) if line else "" for line in cpp_field(f))
    if st.union:
        out += ["", "private:", "    void Select(Variant v)", "    {",
                "        if (Which() == v) return;"]
        for kind, off, width in st.clears():
            out.append(f"        WireWriteBits({off}, {width}, 0);" if kind == "bits"
                       else f"        WirePtrs[{off}].Reset();")
        out += [f"        WireWriteBits({st.disc_bit}, 16, (uint16_t)v);", "    }"]
    out.append("};")
    return out


def cpp_field(f):
    sel = f"Select(Variant::{f.prop}); " if f.in_union else ""
    guard = f"Which() == Variant::{f.prop} ? " if f.in_union else ""
    if f.kind == "void":
        return [f"void Set{f.prop}() {{ Select(Variant::{f.prop}); }}"]
    if f.kind in CPP_PRIM or f.kind == "enum":
        if f.kind == "enum":
            ty = f"{NS}{f.type_name}"
            getter = f"({ty})WireReadBits({f.bit}, 16)"
            setter = f"WireWriteBits({f.bit}, 16, (uint16_t)v)"
        else:
            ty, get, set_ = CPP_PRIM[f.kind]
            getter, setter = get.format(b=f.bit), set_.format(b=f.bit)
        if f.in_union:
            getter = f"{guard}{getter} : {ty}()"
        return [f"{ty} {f.prop}() const {{ return {getter}; }}",
                f"void Set{f.prop}({ty} v) {{ {sel}{setter}; }}"]
    if f.kind == "text":
        if f.in_union:
            return [f"const std::string* {f.prop}() const {{ return {guard}&WirePtrs[{f.ptr}].Text : nullptr; }}",
                    f"void Set{f.prop}(std::string v) {{ {sel}WireSetText({f.ptr}, std::move(v)); }}"]
        return [f"const std::string& {f.prop}() const {{ return WirePtrs[{f.ptr}].Text; }}",
                f"void Set{f.prop}(std::string v) {{ WireSetText({f.ptr}, std::move(v)); }}"]
    if f.kind == "struct":
        ty = f"{NS}{f.type_name}"
        if f.in_union:
            return [f"{ty}* Get{f.prop}() {{ return {guard}static_cast<{ty}*>(WirePtrs[{f.ptr}].Child.get()) : nullptr; }}",
                    f"/// Selects `{f.name}`; returns its (existing or new) payload.",
                    f"{ty}& Init{f.prop}()",
                    "{",
                    f"    if (Which() == Variant::{f.prop} && WirePtrs[{f.ptr}].Child)",
                    f"        return static_cast<{ty}&>(*WirePtrs[{f.ptr}].Child);",
                    f"    Select(Variant::{f.prop});",
                    f"    return WireEmplace<{ty}>({f.ptr});",
                    "}"]
        return [f"{ty}& {f.prop}() {{ return static_cast<{ty}&>(*WirePtrs[{f.ptr}].Child); }}",
                f"const {ty}& {f.prop}() const {{ return static_cast<const {ty}&>(*WirePtrs[{f.ptr}].Child); }}"]
    ty = f"{NS}{f.type_name}"
    return [f"/// `{f.name}`: written when non-empty, null when empty.",
            f"{ty}& Add{f.prop}() {{ return WireAppend<{ty}>({f.ptr}); }}",
            f"size_t {f.prop}Count() const {{ return WirePtrs[{f.ptr}].List.size(); }}",
            f"{ty}& {f.prop}(size_t i) {{ return static_cast<{ty}&>(*WirePtrs[{f.ptr}].List[i]); }}",
            f"void Clear{f.prop}() {{ WirePtrs[{f.ptr}].List.clear(); }}"]


def emit_cpp(model):
    out = [
        "// Generated by ogsa-ts-dotnet/capnp/gen_writers.py from ogsa_telemetry.capnp",
        "// — do not edit. Regenerate with:",
        "//   python ../ogsa-ts-dotnet/capnp/gen_writers.py --lang cpp \\",
        "//     --schema ../OGSA-Telemetry-Standard/ogsa_telemetry.capnp --out <this file>",
        "#pragma once",
        "#include \"OgsaTelemetryCanonical.h\"",
        "#include <cstddef>",
        "#include <cstdint>",
        "#include <string>",
        "",
        "namespace ogsa::telemetry::schema",
        "{",
    ]
    for kind, name, item in model:
        if kind == "struct":
            out.append(f"class {name};")
    for kind, name, item in model:
        out.append("")
        if kind == "enum":
            out += [f"enum class {name} : uint16_t", "{"]
            out += [f"    {pascal(e)} = {v}," for v, e in enumerate(item)]
            out.append("};")
    # definitions in dependency order: inline members create and cast to
    # every struct type a struct references, so each must be complete first
    done, order = set(), []
    structs = {name: item for kind, name, item in model if kind == "struct"}

    def visit(name, stack=()):
        if name in done:
            return
        if name in stack:
            fail(f"{name}: recursive struct reference")
        for f in structs[name].fields:
            if f.kind in ("struct", "list"):
                visit(f.type_name, stack + (name,))
        done.add(name)
        order.append(name)
    for name in structs:
        visit(name)
    for name in order:
        out.append("")
        out += cpp_struct(structs[name])
    out.append("")
    out.append("} // namespace ogsa::telemetry::schema")
    return "\n".join(out) + "\n"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lang", choices=("csharp", "cpp"), default="csharp")
    ap.add_argument("--schema", type=pathlib.Path, default=SCHEMA)
    ap.add_argument("--out", type=pathlib.Path)
    args = ap.parse_args()
    if args.lang == "cpp" and args.out is None:
        ap.error("--lang cpp needs --out")
    model = load(args.schema)
    out = args.out or CS_OUT
    text = emit_csharp(model) if args.lang == "csharp" else emit_cpp(model)
    out.write_text(text, encoding="utf-8", newline="\n")
    print(f"generated: {out}")


if __name__ == "__main__":
    main()
