#!/usr/bin/env python3
"""gen_writers.py — generate the canonical-encoding schema writers.

Reads ogsa_telemetry.capnp through pycapnp (the official compiler computes
every field offset; nothing here does layout arithmetic) and emits one
typed writer class per struct over the Wire runtime (Wire.cs), plus the
schema's enums.

Each writer upholds the field-presence rules of the standard's encoding
rules (implementation-capnproto.md) by construction: non-union Text and
struct fields are created in the constructor, and selecting a union variant
clears every other variant's storage.

Supports exactly what the schema uses — top-level structs and enums,
primitive/enum/Text/struct fields, List(struct), one unnamed union per
struct, no explicit defaults — and fails loudly on anything else, so a
schema change that needs more support cannot slip through.

Usage: python capnp/gen_writers.py   (from the repo root; pip install pycapnp)
"""
import pathlib
import sys

import capnp  # pycapnp

HERE = pathlib.Path(__file__).parent
SCHEMA = HERE / "ogsa_telemetry.capnp"
OUT = HERE.parent / "src" / "Ogsa.Telemetry" / "Schema" / "ogsa_telemetry.writers.cs"

NO_DISCRIMINANT = 0xFFFF
PRIMITIVES = {
    # capnp type: (bit width, C# type, getter expression, setter value)
    "bool": (1, "bool", "GetBool({b})", "SetBool({b}, value)"),
    "int8": (8, "sbyte", "(sbyte)GetBits({b}, 8)", "SetBits({b}, 8, (ulong)value)"),
    "uint8": (8, "byte", "(byte)GetBits({b}, 8)", "SetBits({b}, 8, value)"),
    "int16": (16, "short", "(short)GetBits({b}, 16)", "SetBits({b}, 16, (ulong)value)"),
    "uint16": (16, "ushort", "(ushort)GetBits({b}, 16)", "SetBits({b}, 16, value)"),
    "int32": (32, "int", "(int)GetBits({b}, 32)", "SetBits({b}, 32, (ulong)value)"),
    "uint32": (32, "uint", "(uint)GetBits({b}, 32)", "SetBits({b}, 32, value)"),
    "int64": (64, "long", "(long)GetBits({b}, 64)", "SetBits({b}, 64, (ulong)value)"),
    "uint64": (64, "ulong", "GetBits({b}, 64)", "SetBits({b}, 64, value)"),
    "float32": (32, "float", "GetFloat32({b})", "SetFloat32({b}, value)"),
    "float64": (64, "double", "GetFloat64({b})", "SetFloat64({b}, value)"),
}


def fail(msg):
    sys.exit(f"gen_writers: unsupported schema construct: {msg}")


def pascal(name):
    return name[0].upper() + name[1:]


class Gen:
    def __init__(self, module):
        self.names = {}
        self.decls = []  # (name, node) in declaration order
        for nested in module.schema.node.nestedNodes:
            node = getattr(module, nested.name).schema.node
            self.names[node.id] = nested.name
            self.decls.append((nested.name, node))

    def type_name(self, type_id):
        if type_id not in self.names:
            fail(f"type {type_id:#x} is not a top-level declaration")
        return self.names[type_id]

    def enum(self, name, node):
        out = [f"public enum {name} : ushort", "{"]
        for value, e in enumerate(node.enum.enumerants):
            out.append(f"    {pascal(e.name)} = {value},")
        out.append("}")
        return out

    def struct(self, name, node):
        s = node.struct
        if node.nestedNodes:
            fail(f"{name}: nested declarations")
        if s.isGroup:
            fail(f"{name}: groups")
        fields = sorted(s.fields, key=lambda f: f.codeOrder)
        union = [f for f in fields if f.discriminantValue != NO_DISCRIMINANT]
        for f in fields:
            if str(f.which) != "slot":
                fail(f"{name}.{f.name}: groups / named unions")
            if f.slot.hadExplicitDefault:
                fail(f"{name}.{f.name}: explicit default")
        disc_bit = s.discriminantOffset * 16

        out = [f"public sealed class {name} : WireStruct", "{"]
        if union:
            out.append("    public enum Variant : ushort")
            out.append("    {")
            for f in sorted(union, key=lambda f: f.discriminantValue):
                out.append(f"        {pascal(f.name)} = {f.discriminantValue},")
            out.append("    }")
            out.append("")

        ctor = []
        for f in fields:
            t = str(f.slot.type.which)
            in_union = f.discriminantValue != NO_DISCRIMINANT
            if in_union and f.discriminantValue != 0:
                continue
            if t == "text":
                ctor.append(f"SetPointer({f.slot.offset}, \"\");")
            elif t == "struct":
                ctor.append(f"SetPointer({f.slot.offset}, new "
                            f"{self.type_name(f.slot.type.struct.typeId)}());")
        out.append(f"    public {name}() : base({s.dataWordCount}, {s.pointerCount})")
        if ctor:
            out.append("    {")
            out.extend("        " + c for c in ctor)
            out.append("    }")
        else:
            out.append("    {")
            out.append("    }")

        if union:
            out.append("")
            out.append(f"    public Variant Which => (Variant)GetBits({disc_bit}, 16);")
            out.append("")
            out.append("    void Select(Variant v)")
            out.append("    {")
            out.append("        if (Which == v) return;")
            clears = []  # variants may share storage: clear each slot once
            for f in union:
                t = str(f.slot.type.which)
                if t in PRIMITIVES or t == "enum":
                    width = 16 if t == "enum" else PRIMITIVES[t][0]
                    line = f"        SetBits({f.slot.offset * width}, {width}, 0);"
                elif t in ("text", "struct", "list"):
                    line = f"        SetPointer({f.slot.offset}, null);"
                else:
                    continue
                if line not in clears:
                    clears.append(line)
            out.extend(clears)
            out.append(f"        SetBits({disc_bit}, 16, (ushort)v);")
            out.append("    }")

        for f in fields:
            out.append("")
            out.extend(("    " + line) if line else "" for line in self.field(name, f))
        out.append("}")
        return out

    def field(self, owner, f):
        t = str(f.slot.type.which)
        prop = pascal(f.name)
        in_union = f.discriminantValue != NO_DISCRIMINANT
        sel = f"Select(Variant.{prop}); " if in_union else ""
        guard = f"Which == Variant.{prop} ? " if in_union else ""
        idx = f.slot.offset
        if t == "void":
            if not in_union:
                fail(f"{owner}.{f.name}: non-union Void")
            return [f"public void Set{prop}() => Select(Variant.{prop});"]
        if t in PRIMITIVES:
            width, cs, get, set_ = PRIMITIVES[t]
            b = idx * width
            getter = get.format(b=b)
            if in_union:
                getter = f"{guard}{getter} : default"
            return [f"public {cs} {prop}",
                    "{",
                    f"    get => {getter};",
                    f"    set {{ {sel}{set_.format(b=b)}; }}",
                    "}"]
        if t == "enum":
            cs = self.type_name(f.slot.type.enum.typeId)
            b = idx * 16
            getter = f"({cs})GetBits({b}, 16)"
            if in_union:
                getter = f"{guard}{getter} : default"
            return [f"public {cs} {prop}",
                    "{",
                    f"    get => {getter};",
                    f"    set {{ {sel}SetBits({b}, 16, (ushort)value); }}",
                    "}"]
        if t == "text":
            if in_union:
                return [f"public string? {prop}",
                        "{",
                        f"    get => {guard}(string?)GetPointer({idx}) : null;",
                        f"    set {{ {sel}SetPointer({idx}, value ?? \"\"); }}",
                        "}"]
            return [f"public string {prop}",
                    "{",
                    f"    get => (string)GetPointer({idx})!;",
                    f"    set => SetPointer({idx}, value ?? \"\");",
                    "}"]
        if t == "struct":
            cs = self.type_name(f.slot.type.struct.typeId)
            if in_union:
                return [f"public {cs}? {prop} => {guard}({cs}?)GetPointer({idx}) : null;",
                        "",
                        f"/// <summary>Selects <c>{f.name}</c>; returns its (existing or new) payload.</summary>",
                        f"public {cs} Init{prop}()",
                        "{",
                        f"    if (Which == Variant.{prop} && GetPointer({idx}) is {cs} existing) return existing;",
                        f"    Select(Variant.{prop});",
                        f"    var created = new {cs}();",
                        f"    SetPointer({idx}, created);",
                        "    return created;",
                        "}"]
            return [f"public {cs} {prop} => ({cs})GetPointer({idx})!;"]
        if t == "list":
            elem = f.slot.type.list.elementType
            if str(elem.which) != "struct":
                fail(f"{owner}.{f.name}: List of {elem.which}")
            if in_union:
                fail(f"{owner}.{f.name}: list inside a union")
            cs = self.type_name(elem.struct.typeId)
            return ["/// <summary>Written when non-empty; null and empty both encode as null.</summary>",
                    f"public List<{cs}>? {prop}",
                    "{",
                    f"    get => (List<{cs}>?)GetPointer({idx});",
                    f"    set => SetPointer({idx}, value);",
                    "}"]
        fail(f"{owner}.{f.name}: field type {t}")

    def emit(self):
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
        for name, node in self.decls:
            which = str(node.which)
            out.append("")
            if which == "enum":
                out.extend(self.enum(name, node))
            elif which == "struct":
                out.extend(self.struct(name, node))
            else:
                fail(f"{name}: {which} declaration")
        out.append("}")
        return "\n".join(out) + "\n"


def main():
    module = capnp.load(str(SCHEMA))  # type: ignore # stub-less Cython module
    text = Gen(module).emit()
    OUT.write_text(text, encoding="utf-8", newline="\n")
    print(f"generated: {OUT}")


if __name__ == "__main__":
    main()
