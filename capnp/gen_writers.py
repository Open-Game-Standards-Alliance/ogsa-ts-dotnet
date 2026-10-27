#!/usr/bin/env python3
"""gen_writers.py — generate the canonical-encoding schema writers.

Reads ogs_telemetry.capnp through pycapnp (the official compiler computes
every field offset; nothing here does layout arithmetic) and emits one
typed writer class per struct, plus the schema's enums, for:

  --lang csharp  over Wire.cs (default; ogs-ts-dotnet's core)
  --lang cpp     over OgsTelemetryCanonical.h (ogs-ts-unreal's core)

Each writer upholds the field-presence rules of the standard's encoding
rules (implementation-capnproto.md) by construction: non-union Text and
struct fields are created in the constructor, and selecting a union variant
clears every other variant's storage.

Supports exactly what the schema uses — top-level structs and enums,
primitive/enum/Text/struct fields, List(struct), one unnamed union per
struct, named unions (emitted as a nested view type over the parent's
storage, e.g. `obj.LocalGravity.InitVector()`), no explicit defaults — and
fails loudly on anything else, so a schema change that needs more support
cannot slip through.

Usage (pip install pycapnp):
  python capnp/gen_writers.py                       # C#, this repo
  python capnp/gen_writers.py --lang cpp --out <OgsTelemetrySchema.h>
"""
import argparse
import pathlib
import sys
from typing import Iterable

import capnp  # pycapnp

HERE = pathlib.Path(__file__).parent
SCHEMA = HERE / "ogs_telemetry.capnp"
CS_OUT = HERE.parent / "src" / "Ogs.Telemetry" / "Schema" / "ogs_telemetry.writers.cs"

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


class Union:
    """One union's variants and discriminant. A struct's unnamed union and
    each named union are separate: switching variant clears only that
    union's storage."""
    def __init__(self, disc_bit, fields: Iterable[Field]):
        self.disc_bit = disc_bit
        self.fields = sorted(fields, key=lambda f: f.disc)

    def clears(self):
        """Union storage to clear on a variant switch: (kind, offset, width),
        each shared slot once."""
        out = []
        for f in self.fields:
            if f.kind in WIDTH:
                c = ("bits", f.bit, f.width)
            elif f.is_pointer:
                c = ("ptr", f.ptr, None)
            else:
                continue
            if c not in out:
                out.append(c)
        return out


class Group:
    """A named union: a group whose fields all sit in its union, laid out in
    the parent's data and pointer sections."""
    def __init__(self, owner, f, schema, names):
        g = schema.fields[f.name].schema.node
        s = g.struct
        self.name = f.name
        self.prop = pascal(f.name)
        if g.nestedNodes:
            fail(f"{owner}.{f.name}: nested declarations")
        fields = []
        for gf in sorted(s.fields, key=lambda x: x.codeOrder):
            if str(gf.which) != "slot":
                fail(f"{owner}.{f.name}.{gf.name}: nested group")
            if gf.discriminantValue == NO_DISCRIMINANT:
                fail(f"{owner}.{f.name}.{gf.name}: group field outside its union")
            if gf.slot.hadExplicitDefault:
                fail(f"{owner}.{f.name}.{gf.name}: explicit default")
            fld = Field(gf, names, f"{owner}.{f.name}")
            if fld.kind == "text":
                fail(f"{owner}.{f.name}.{gf.name}: Text in a named union")
            fields.append(fld)
        self.union = Union(s.discriminantOffset * 16, fields)


class Struct:
    def __init__(self, name, schema, names):
        node = schema.node
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
        self.groups = []
        for f in sorted(s.fields, key=lambda f: f.codeOrder):
            which = str(f.which)
            if which == "group":
                if f.discriminantValue != NO_DISCRIMINANT:
                    fail(f"{name}.{f.name}: group inside a union")
                self.groups.append(Group(name, f, schema, names))
                continue
            if which != "slot":
                fail(f"{name}.{f.name}: {which} field")
            if f.slot.hadExplicitDefault:
                fail(f"{name}.{f.name}: explicit default")
            self.fields.append(Field(f, names, name))
        variants = [f for f in self.fields if f.in_union]
        self.union = Union(self.disc_bit, variants) if variants else None

    def created(self):
        """Pointer fields the constructor fills: non-union Text/struct, and
        each union's discriminant-0 variant payload when it is one."""
        unions = [self.union] if self.union else []
        unions += [g.union for g in self.groups]
        return [f for f in self.fields if f.kind in ("text", "struct") and not f.in_union] +                [f for u in unions for f in u.fields if f.kind in ("text", "struct") and f.disc == 0]

    def referenced(self):
        """Every field, group variants included (dependency ordering)."""
        return self.fields + [f for g in self.groups for f in g.union.fields]


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
            schema = getattr(module, name).schema
            model.append(("struct", name, Struct(name, schema, names)))
        else:
            fail(f"{name}: {which} declaration")
    return model


# ---------------------------------------------------------------------------
# C# emitter
# ---------------------------------------------------------------------------

CS_PRIM = {
    # kind: (C# type, getter, setter value); {r} = receiver prefix
    "bool": ("bool", "{r}GetBool({b})", "{r}SetBool({b}, value)"),
    "int8": ("sbyte", "(sbyte){r}GetBits({b}, 8)", "{r}SetBits({b}, 8, (ulong)value)"),
    "uint8": ("byte", "(byte){r}GetBits({b}, 8)", "{r}SetBits({b}, 8, value)"),
    "int16": ("short", "(short){r}GetBits({b}, 16)", "{r}SetBits({b}, 16, (ulong)value)"),
    "uint16": ("ushort", "(ushort){r}GetBits({b}, 16)", "{r}SetBits({b}, 16, value)"),
    "int32": ("int", "(int){r}GetBits({b}, 32)", "{r}SetBits({b}, 32, (ulong)value)"),
    "uint32": ("uint", "(uint){r}GetBits({b}, 32)", "{r}SetBits({b}, 32, value)"),
    "int64": ("long", "(long){r}GetBits({b}, 64)", "{r}SetBits({b}, 64, (ulong)value)"),
    "uint64": ("ulong", "{r}GetBits({b}, 64)", "{r}SetBits({b}, 64, value)"),
    "float32": ("float", "{r}GetFloat32({b})", "{r}SetFloat32({b}, value)"),
    "float64": ("double", "{r}GetFloat64({b})", "{r}SetFloat64({b}, value)"),
}


def cs_variant(union):
    out = ["public enum Variant : ushort", "{"]
    out += [f"    {f.prop} = {f.disc}," for f in union.fields]
    return out + ["}"]


def cs_select(union, r=""):
    """Which and Select for one union, over receiver `r`."""
    out = [f"public Variant Which => (Variant){r}GetBits({union.disc_bit}, 16);",
           "", "void Select(Variant v)", "{", "    if (Which == v) return;"]
    for kind, off, width in union.clears():
        out.append(f"    {r}SetBits({off}, {width}, 0);" if kind == "bits"
                   else f"    {r}SetPointer({off}, null);")
    out += [f"    {r}SetBits({union.disc_bit}, 16, (ushort)v);", "}"]
    return out


def indent(lines):
    return [("    " + line) if line else "" for line in lines]


def cs_group(st, g):
    """A named union as a view over the parent's storage."""
    view = f"{g.prop}Group"
    out = [f"public {view} {g.prop} => new {view}(this);", "",
           f"/// <summary>Named union <c>{g.name}</c>, a view over the {st.name}'s storage.</summary>",
           f"public readonly struct {view}", "{"]
    out += indent(cs_variant(g.union))
    out += ["", f"    readonly {st.name} _s;", f"    internal {view}({st.name} s) {{ _s = s; }}", ""]
    out += indent(cs_select(g.union, "_s."))
    for f in g.union.fields:
        out.append("")
        out += indent(cs_field(f, "_s."))
    out.append("}")
    return out


def cs_struct(st):
    out = [f"public sealed class {st.name} : WireStruct", "{"]
    if st.union:
        out += indent(cs_variant(st.union)) + [""]
    out.append(f"    public {st.name}() : base({st.data_words}, {st.pointers})")
    out.append("    {")
    for f in st.created():
        value = '""' if f.kind == "text" else f"new {f.type_name}()"
        out.append(f"        SetPointer({f.ptr}, {value});")
    out.append("    }")
    if st.union:
        out += [""] + indent(cs_select(st.union))
    for f in st.fields:
        out.append("")
        out += indent(cs_field(f))
    for g in st.groups:
        out.append("")
        out += indent(cs_group(st, g))
    out.append("}")
    return out


def cs_field(f, r=""):
    sel = f"Select(Variant.{f.prop}); " if f.in_union else ""
    guard = f"Which == Variant.{f.prop} ? " if f.in_union else ""
    if f.kind == "void":
        return [f"public void Set{f.prop}() => Select(Variant.{f.prop});"]
    if f.kind in CS_PRIM or f.kind == "enum":
        if f.kind == "enum":
            cs, getter = f.type_name, f"({f.type_name}){r}GetBits({f.bit}, 16)"
            setter = f"{r}SetBits({f.bit}, 16, (ushort)value)"
        else:
            cs, get, set_ = CS_PRIM[f.kind]
            getter, setter = get.format(b=f.bit, r=r), set_.format(b=f.bit, r=r)
        if f.in_union:
            getter = f"{guard}{getter} : default"
        return [f"public {cs} {f.prop}", "{", f"    get => {getter};",
                f"    set {{ {sel}{setter}; }}", "}"]
    if f.kind == "text":
        if f.in_union:
            return [f"public string? {f.prop}", "{",
                    f"    get => {guard}(string?){r}GetPointer({f.ptr}) : null;",
                    f"    set {{ {sel}{r}SetPointer({f.ptr}, value ?? \"\"); }}", "}"]
        return [f"public string {f.prop}", "{",
                f"    get => (string){r}GetPointer({f.ptr})!;",
                f"    set => {r}SetPointer({f.ptr}, value ?? \"\");", "}"]
    if f.kind == "struct":
        cs = f.type_name
        if f.in_union:
            return [f"public {cs}? {f.prop} => {guard}({cs}?){r}GetPointer({f.ptr}) : null;",
                    "",
                    f"/// <summary>Selects <c>{f.name}</c>; returns its (existing or new) payload.</summary>",
                    f"public {cs} Init{f.prop}()", "{",
                    f"    if (Which == Variant.{f.prop} && {r}GetPointer({f.ptr}) is {cs} existing) return existing;",
                    f"    Select(Variant.{f.prop});",
                    f"    var created = new {cs}();",
                    f"    {r}SetPointer({f.ptr}, created);",
                    "    return created;", "}"]
        return [f"public {cs} {f.prop} => ({cs}){r}GetPointer({f.ptr})!;"]
    # list
    cs = f.type_name
    return ["/// <summary>Written when non-empty; null and empty both encode as null.</summary>",
            f"public List<{cs}>? {f.prop}", "{",
            f"    get => (List<{cs}>?){r}GetPointer({f.ptr});",
            f"    set => {r}SetPointer({f.ptr}, value);", "}"]


def emit_csharp(model):
    out = [
        "// <auto-generated>",
        "// Generated by capnp/gen_writers.py from ogs_telemetry.capnp — do not edit.",
        "// Regenerate with: python capnp/gen_writers.py",
        "// </auto-generated>",
        "#nullable enable",
        "using System.Collections.Generic;",
        "using Ogs.Telemetry.Wire;",
        "",
        "namespace Ogs.Telemetry.Schema",
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

NS = "::ogs::telemetry::schema::"
CPP_PRIM = {
    # kind: (C++ type, getter, setter); {r} = receiver prefix
    "bool": ("bool", "{r}WireReadBool({b})", "{r}WireWriteBool({b}, v)"),
    "int8": ("int8_t", "(int8_t){r}WireReadBits({b}, 8)", "{r}WireWriteBits({b}, 8, (uint64_t)(uint8_t)v)"),
    "uint8": ("uint8_t", "(uint8_t){r}WireReadBits({b}, 8)", "{r}WireWriteBits({b}, 8, v)"),
    "int16": ("int16_t", "(int16_t){r}WireReadBits({b}, 16)", "{r}WireWriteBits({b}, 16, (uint64_t)(uint16_t)v)"),
    "uint16": ("uint16_t", "(uint16_t){r}WireReadBits({b}, 16)", "{r}WireWriteBits({b}, 16, v)"),
    "int32": ("int32_t", "(int32_t){r}WireReadBits({b}, 32)", "{r}WireWriteBits({b}, 32, (uint64_t)(uint32_t)v)"),
    "uint32": ("uint32_t", "(uint32_t){r}WireReadBits({b}, 32)", "{r}WireWriteBits({b}, 32, v)"),
    "int64": ("int64_t", "(int64_t){r}WireReadBits({b}, 64)", "{r}WireWriteBits({b}, 64, (uint64_t)v)"),
    "uint64": ("uint64_t", "{r}WireReadBits({b}, 64)", "{r}WireWriteBits({b}, 64, v)"),
    "float32": ("float", "{r}WireReadFloat32({b})", "{r}WireWriteFloat32({b}, v)"),
    "float64": ("double", "{r}WireReadFloat64({b})", "{r}WireWriteFloat64({b}, v)"),
}


def cpp_variant(union):
    out = ["enum class Variant : uint16_t", "{"]
    out += [f"    {f.prop} = {f.disc}," for f in union.fields]
    return out + ["};"]


def cpp_select(union, r=""):
    out = ["void Select(Variant v)", "{", "    if (Which() == v) return;"]
    for kind, off, width in union.clears():
        out.append(f"    {r}WireWriteBits({off}, {width}, 0);" if kind == "bits"
                   else f"    {r}WirePtrs[{off}].Reset();")
    out += [f"    {r}WireWriteBits({union.disc_bit}, 16, (uint16_t)v);", "}"]
    return out


def cpp_group(st, g):
    """A named union as a view over the parent's storage (members of a
    nested class have the parent's access to its protected wire helpers)."""
    view = f"{g.prop}Group"
    out = [f"/// Named union `{g.name}`, a view over the {st.name}'s storage.",
           f"class {view}", "{", "public:"]
    out += indent(cpp_variant(g.union))
    out += ["", f"    explicit {view}({st.name}& s) : Self(&s) {{}}",
            f"    Variant Which() const {{ return (Variant)Self->WireReadBits({g.union.disc_bit}, 16); }}"]
    for f in g.union.fields:
        out.append("")
        out += indent(cpp_field(f, "Self->"))
    out += ["", "private:", f"    {st.name}* Self;", ""]
    out += indent(cpp_select(g.union, "Self->"))
    out.append("};")
    out.append(f"{view} {g.prop}() {{ return {view}(*this); }}")
    return out


def cpp_struct(st):
    out = [f"class {st.name} final : public ::ogs::telemetry::wire::Struct", "{", "public:"]
    if st.union:
        out += indent(cpp_variant(st.union)) + [""]
    out.append(f"    {st.name}() : Struct({st.data_words}, {st.pointers})")
    out.append("    {")
    for f in st.created():
        if f.kind == "text":
            out.append(f"        WireSetText({f.ptr}, std::string());")
        else:
            out.append(f"        WireEmplace<{NS}{f.type_name}>({f.ptr});")
    out.append("    }")
    if st.union:
        out += ["", f"    Variant Which() const {{ return (Variant)WireReadBits({st.union.disc_bit}, 16); }}"]
    for f in st.fields:
        out.append("")
        out += indent(cpp_field(f))
    for g in st.groups:
        out.append("")
        out += indent(cpp_group(st, g))
    if st.union:
        out += ["", "private:"] + indent(cpp_select(st.union))
    out.append("};")
    return out


def cpp_field(f, r=""):
    sel = f"Select(Variant::{f.prop}); " if f.in_union else ""
    guard = f"Which() == Variant::{f.prop} ? " if f.in_union else ""
    if f.kind == "void":
        return [f"void Set{f.prop}() {{ Select(Variant::{f.prop}); }}"]
    if f.kind in CPP_PRIM or f.kind == "enum":
        if f.kind == "enum":
            ty = f"{NS}{f.type_name}"
            getter = f"({ty}){r}WireReadBits({f.bit}, 16)"
            setter = f"{r}WireWriteBits({f.bit}, 16, (uint16_t)v)"
        else:
            ty, get, set_ = CPP_PRIM[f.kind]
            getter, setter = get.format(b=f.bit, r=r), set_.format(b=f.bit, r=r)
        if f.in_union:
            getter = f"{guard}{getter} : {ty}()"
        return [f"{ty} {f.prop}() const {{ return {getter}; }}",
                f"void Set{f.prop}({ty} v) {{ {sel}{setter}; }}"]
    if f.kind == "text":
        if f.in_union:
            return [f"const std::string* {f.prop}() const {{ return {guard}&{r}WirePtrs[{f.ptr}].Text : nullptr; }}",
                    f"void Set{f.prop}(std::string v) {{ {sel}{r}WireSetText({f.ptr}, std::move(v)); }}"]
        return [f"const std::string& {f.prop}() const {{ return {r}WirePtrs[{f.ptr}].Text; }}",
                f"void Set{f.prop}(std::string v) {{ {r}WireSetText({f.ptr}, std::move(v)); }}"]
    if f.kind == "struct":
        ty = f"{NS}{f.type_name}"
        if f.in_union:
            return [f"{ty}* Get{f.prop}() {{ return {guard}static_cast<{ty}*>({r}WirePtrs[{f.ptr}].Child.get()) : nullptr; }}",
                    f"/// Selects `{f.name}`; returns its (existing or new) payload.",
                    f"{ty}& Init{f.prop}()",
                    "{",
                    f"    if (Which() == Variant::{f.prop} && {r}WirePtrs[{f.ptr}].Child)",
                    f"        return static_cast<{ty}&>(*{r}WirePtrs[{f.ptr}].Child);",
                    f"    Select(Variant::{f.prop});",
                    f"    return {r}WireEmplace<{ty}>({f.ptr});",
                    "}"]
        return [f"{ty}& {f.prop}() {{ return static_cast<{ty}&>(*{r}WirePtrs[{f.ptr}].Child); }}",
                f"const {ty}& {f.prop}() const {{ return static_cast<const {ty}&>(*{r}WirePtrs[{f.ptr}].Child); }}"]
    ty = f"{NS}{f.type_name}"
    return [f"/// `{f.name}`: written when non-empty, null when empty.",
            f"{ty}& Add{f.prop}() {{ return {r}WireAppend<{ty}>({f.ptr}); }}",
            f"size_t {f.prop}Count() const {{ return {r}WirePtrs[{f.ptr}].List.size(); }}",
            f"{ty}& {f.prop}(size_t i) {{ return static_cast<{ty}&>(*{r}WirePtrs[{f.ptr}].List[i]); }}",
            f"void Clear{f.prop}() {{ {r}WirePtrs[{f.ptr}].List.clear(); }}"]


def emit_cpp(model):
    out = [
        "// Generated by ogs-ts-dotnet/capnp/gen_writers.py from ogs_telemetry.capnp",
        "// — do not edit. Regenerate with:",
        "//   python ../ogs-ts-dotnet/capnp/gen_writers.py --lang cpp \\",
        "//     --schema ../OGS-Telemetry-Standard/ogs_telemetry.capnp --out <this file>",
        "#pragma once",
        "#include \"OgsTelemetryCanonical.h\"",
        "#include <cstddef>",
        "#include <cstdint>",
        "#include <string>",
        "",
        "namespace ogs::telemetry::schema",
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
        for f in structs[name].referenced():
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
    out.append("} // namespace ogs::telemetry::schema")
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
