# HkxSharp

Reader for Havok 2014 binary packfiles (hkx), in both 32-bit big-endian and 64-bit little-endian layouts, and for Havok tagfiles (TAG0): the physics in *Tears of the Kingdom*, and Havok 2022 tagfiles such as the collision meshes (`.trcol`) in *Pokemon Legends: Z-A*.

Havok 2022 tagfiles differ from the older ones in two ways that matter for reading:

- A type body's field count carries flags above bit 16 (`HkTagType.FieldFlags`), for example 0x20000 on `hkReferencedObject`.
- Pointers, strings and arrays, `hkRelArray`/`hkRelArrayView` included, are 4-byte item indices with no patch table, and an array's length is its item's count.

Writing Havok 2022 tagfiles is not supported yet: `HkTagfileWriter` throws `NotSupportedException` for them. All 26,331 TotK tagfiles read and write exactly as they did before this support was added, and all 1,316 Z-A collision files read with no decode issues.
