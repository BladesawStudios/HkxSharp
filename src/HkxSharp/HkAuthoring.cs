namespace HkxSharp;

/// <summary>Makes tagfile types and objects out of their definitions, for data the file does not have yet.</summary>
/// <remarks>
/// <para>
/// A tagfile lists the types of the objects it holds and nothing else, so a file with no sphere in it has no sphere type, and an object of that type cannot be written until the type is. A type made here
/// stands alone: it is not in any file's type table until an object of it is written, and the writer then puts it there (with every type it refers to that the file lacks), the same way it would a type
/// read from another file. What a type has to say - its flags, size, field offsets and hash - is the format's own and is given by the caller from the SDK's definition.
/// </para>
/// <para>
/// An object made here has every field at zero (false, 0, null, an empty array, an empty string) in the form the reader gives that field, so it reads back, and writes, like one that was read.
/// </para>
/// </remarks>
public static class HkAuthoring
{
    /// <summary>One field of a record type: its name, the flags the file gives it, its byte offset, and its type.</summary>
    public sealed record FieldSpec(string Name, int Flags, int Offset, HkTagType Type);

    /// <summary>A record type (a class or a struct) of the given layout.</summary>
    public static HkTagType Record(string name, HkTagType? parent, int presence, int version, int size, int alignAndFlags, int abstractValue, uint hash, params FieldSpec[] fields)
    {
        HkTagType type = new()
        {
            Index = 0, Name = name, Parent = parent, Presence = presence, Format = 7, Version = version, Size = size,
            AlignAndFlags = alignAndFlags, AbstractValue = abstractValue, Hash = hash,
        };
        foreach (FieldSpec f in fields) type.Fields.Add(new HkTagField(f.Name, f.Flags, f.Offset, f.Type));
        return type;
    }

    /// <summary>A new object of a record type, its fields zero, then those named set.</summary>
    public static HkObject Object(HkTagType type, params (string Name, object? Value)[] values)
    {
        HkClass cls = ClassOf(type, []);
        HkStruct fields = Struct(cls);
        foreach ((string name, object? value) in values) Set(fields, name, value);
        return new HkObject { ClassName = type.FullName, Signature = type.Hash, Offset = 0, Class = cls, Fields = fields };
    }

    /// <summary>A new struct of a record type (an element of an array of structs), its fields zero, then those named set.</summary>
    public static HkStruct NewStruct(HkTagType type, params (string Name, object? Value)[] values)
    {
        HkStruct fields = Struct(ClassOf(type, []));
        foreach ((string name, object? value) in values) Set(fields, name, value);
        return fields;
    }

    /// <summary>The type of the elements of an array field of a record, or null where the field is not an array.</summary>
    public static HkTagType? ElementType(HkTagType record, string field)
    {
        for (HkTagType? t = record; t is not null; t = t.Parent)
            foreach (HkTagField f in t.Fields)
                if (f.Name == field) return f.Type.ResolvedSubType;
        return null;
    }

    static void Set(HkStruct fields, string name, object? value)
    {
        int at = fields.Class.IndexOf(name);
        if (at < 0) throw new KeyNotFoundException($"{fields.Class.Name}.{name}");
        fields.Values[at] = value;
    }

    static HkStruct Struct(HkClass cls)
    {
        List<HkTagField> flat = Flat(cls);
        object?[] values = new object?[flat.Count];
        for (int i = 0; i < values.Length; i++) values[i] = Zero(flat[i].Type);
        return new HkStruct(cls, values);
    }

    static List<HkTagField> Flat(HkClass cls) => [.. cls.Parent is null ? [] : Flat(cls.Parent), .. cls.TagType!.Fields];

    // The class of a record type, parents first, as the reader makes it: its members are the type's fields.
    static HkClass ClassOf(HkTagType t, Dictionary<HkTagType, HkClass> made)
    {
        t = t.Canonical;
        if (made.TryGetValue(t, out HkClass? known)) return known;

        HkTagType? parent = t.Parent is { } p && p.Canonical.Kind == HkTagKind.Record ? p.Canonical : null;
        HkClass cls = new() { Name = t.FullName, ParentName = parent?.FullName, Signature = t.Hash, TagType = t };
        made[t] = cls;
        if (parent is not null) cls.Parent = ClassOf(parent, made);
        foreach (HkTagField f in t.Fields) cls.Members.Add(new HkMember(f.Name, MemberType(f.Type, made)));
        int size = t.ResolvedSize;
        cls.Explicit = new HkLayout { Size = size, DataSize = size, Align = Math.Max(1, t.ResolvedAlign), HasVptr = false, VptrOffset = -1, Offsets = [.. t.Fields.Select(f => f.Offset)] };
        return cls;
    }

    static HkType MemberType(HkTagType type, Dictionary<HkTagType, HkClass> made)
    {
        HkTagType c = type.Canonical;
        int size = c.ResolvedSize;
        switch (c.Kind)
        {
            case HkTagKind.Bool: return HkType.Prim(HkKind.Bool);
            case HkTagKind.String: return HkType.Str;
            case HkTagKind.Int:
                return (size, c.Signed) switch
                {
                    (1, false) => HkType.Prim(HkKind.U8), (1, true) => HkType.Prim(HkKind.S8),
                    (2, false) => HkType.Prim(HkKind.U16), (2, true) => HkType.Prim(HkKind.S16),
                    (4, false) => HkType.Prim(HkKind.U32), (4, true) => HkType.Prim(HkKind.S32),
                    (_, false) => HkType.Prim(HkKind.U64), _ => HkType.Prim(HkKind.S64),
                };
            case HkTagKind.Float: return HkType.Prim(HkKind.F32);
            case HkTagKind.Pointer: return HkType.Pointer(c.ResolvedSubType is { } s && s.Canonical.Kind != HkTagKind.Void ? s.Canonical.FullName : null);
            case HkTagKind.Record:
                HkType st = HkType.StructOf(c.FullName);
                st.Class = ClassOf(c, made);
                return st;
            case HkTagKind.Array when c.IsTuple && c.ResolvedSubType is { } e && e.Canonical.Kind == HkTagKind.Float:
                return c.IsA("hkTransformf") ? HkType.Prim(HkKind.Transform) : c.TupleCount == 4 ? HkType.Prim(HkKind.Vec4) : HkType.FixedOf(HkType.Prim(HkKind.F32), c.TupleCount);
            default: return HkType.Prim(HkKind.U8);
        }
    }

    // A field of this type at zero, in the form the reader gives it.
    static object? Zero(HkTagType type)
    {
        HkTagType c = type.Canonical;
        int size = c.ResolvedSize;
        switch (c.Kind)
        {
            case HkTagKind.Bool: return false;
            case HkTagKind.Int:
                return (size, c.Signed) switch
                {
                    (1, false) => (byte)0, (1, true) => (sbyte)0, (2, false) => (ushort)0, (2, true) => (short)0,
                    (4, false) => 0u, (4, true) => 0, (_, false) => 0ul, _ => (object)0L,
                };
            case HkTagKind.Float: return size == 8 ? 0d : 0f;
            case HkTagKind.String: return null;
            case HkTagKind.Pointer: return null;
            case HkTagKind.Record:
                return c.IsA("hkQsTransformf") ? new float[12] : Struct(ClassOf(c, []));
            case HkTagKind.Array:
                if (c.IsTuple && c.ResolvedSubType is { } e && e.Canonical is { Kind: HkTagKind.Float, ResolvedSize: 4 }) return new float[c.TupleCount];
                return Array.CreateInstance(ElementClr(c.ResolvedSubType), 0);
            default: return new byte[size];
        }
    }

    // The CLR element type the reader gives the elements of an array of this type.
    static Type ElementClr(HkTagType? element)
    {
        if (element is null) return typeof(byte);
        HkTagType c = element.Canonical;
        return c.Kind switch
        {
            HkTagKind.Bool => typeof(bool),
            HkTagKind.Float => typeof(float),
            HkTagKind.Int => (c.ResolvedSize, c.Signed) switch
            {
                (1, false) => typeof(byte), (1, true) => typeof(sbyte), (2, false) => typeof(ushort), (2, true) => typeof(short),
                (4, false) => typeof(uint), (4, true) => typeof(int), (_, false) => typeof(ulong), _ => typeof(long),
            },
            _ => typeof(object),
        };
    }
}
