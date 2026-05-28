"""
CPK 解包工具 — Python 移植自 Pal3.Core/DataReader/Cpk/CpkArchive.cs

用法:
    python cpk_extract.py <cpk_path> <out_dir> [--filter PATTERN]
    python cpk_extract.py <cpk_path> --list
    python cpk_extract.py <cpk_path> --extract-one <virtual_path> <out_file>
"""

import argparse
import os
import struct
import sys
from pathlib import Path

import lzokay

CPK_HEADER_MAGIC = 0x1A_54_53_52
CPK_VERSION = 1
CPK_DEFAULT_MAX_NUM_OF_FILE = 32768

FLAG_VALID = 0x1
FLAG_DIR = 0x2
FLAG_LARGE_FILE = 0x4
FLAG_DELETED = 0x10
FLAG_NOT_COMPRESSED = 0x10000


class CpkTableEntity:
    __slots__ = ("crc", "flag", "father_crc", "start_pos",
                 "packed_size", "origin_size", "extra_info_size")

    def __init__(self, raw):
        (self.crc, self.flag, self.father_crc, self.start_pos,
         self.packed_size, self.origin_size, self.extra_info_size) = struct.unpack("<7I", raw)

    @property
    def is_empty(self):
        return self.flag == 0

    @property
    def is_deleted(self):
        return (self.flag & FLAG_DELETED) != 0

    @property
    def is_dir(self):
        return (self.flag & FLAG_DIR) != 0

    @property
    def is_compressed(self):
        return (self.flag & FLAG_NOT_COMPRESSED) == 0


class CpkArchive:
    def __init__(self, path, codepage=936):
        self.path = path
        self.codepage = codepage
        self._data = None
        self._entries = {}        # crc -> CpkTableEntity
        self._children = {}       # father_crc -> set(crc)
        self._names = {}          # crc -> filename (single segment, no path)

    def load(self):
        with open(self.path, "rb") as f:
            self._data = f.read()

        header = struct.unpack_from("<12I20I", self._data, 0)
        label, version, table_start, _data_start, max_file_count, num_files = header[:6]
        valid_table_num = header[8]
        max_table_num = header[9]

        if label != CPK_HEADER_MAGIC:
            raise ValueError(f"Not a valid CPK file: bad magic 0x{label:08X}")
        if version != CPK_VERSION:
            raise ValueError(f"Unsupported CPK version: {version}")
        if num_files > max_file_count or valid_table_num > max_table_num or num_files > valid_table_num:
            raise ValueError("CPK header validation failed")

        entry_size = 7 * 4  # 28 bytes
        offset = 128  # header size
        found = 0
        for _ in range(max_file_count):
            raw = self._data[offset:offset + entry_size]
            offset += entry_size
            if len(raw) < entry_size:
                break
            entity = CpkTableEntity(raw)
            if entity.is_empty or entity.is_deleted:
                continue
            self._entries[entity.crc] = entity
            self._children.setdefault(entity.father_crc, set()).add(entity.crc)
            found += 1
            if found >= num_files:
                break

        # Read filenames from extra-info area (located after each entry's data)
        for crc, entity in self._entries.items():
            extra_offset = entity.start_pos + entity.packed_size
            extra = self._data[extra_offset:extra_offset + entity.extra_info_size]
            null_idx = extra.find(b"\x00")
            if null_idx >= 0:
                extra = extra[:null_idx]
            try:
                self._names[crc] = extra.decode(f"cp{self.codepage}")
            except UnicodeDecodeError:
                self._names[crc] = extra.decode(f"cp{self.codepage}", errors="replace")

    def iter_paths(self):
        """Yield (virtual_path, entity) for every file (not directory)."""
        def walk(father_crc, parent_path):
            for child_crc in self._children.get(father_crc, ()):
                entity = self._entries[child_crc]
                name = self._names.get(child_crc, f"<crc_{child_crc:08X}>")
                vpath = f"{parent_path}\\{name}" if parent_path else name
                if entity.is_dir:
                    yield from walk(child_crc, vpath)
                else:
                    yield vpath, entity

        yield from walk(0, "")

    def read_file(self, entity):
        raw = self._data[entity.start_pos:entity.start_pos + entity.packed_size]
        if entity.is_compressed:
            return lzokay.decompress(raw, entity.origin_size)
        return raw

    def find(self, virtual_path):
        target = virtual_path.replace("/", "\\").lower()
        for vpath, entity in self.iter_paths():
            if vpath.lower() == target:
                return vpath, entity
        return None, None


def cmd_list(archive):
    for vpath, entity in archive.iter_paths():
        print(f"{entity.origin_size:>10}  {vpath}")


def cmd_extract(archive, out_dir, filter_pattern=None):
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    count = 0
    skipped = 0
    for vpath, entity in archive.iter_paths():
        if filter_pattern and filter_pattern.lower() not in vpath.lower():
            continue
        rel_path = vpath.replace("\\", os.sep)
        target = out_dir / rel_path
        target.parent.mkdir(parents=True, exist_ok=True)
        try:
            data = archive.read_file(entity)
            target.write_bytes(data)
            count += 1
            if count % 200 == 0:
                print(f"  ... {count} files extracted", file=sys.stderr)
        except Exception as e:
            skipped += 1
            print(f"  [skip] {vpath}: {e}", file=sys.stderr)
    print(f"Done. {count} files extracted, {skipped} skipped, output: {out_dir}")


def cmd_extract_one(archive, virtual_path, out_file):
    vpath, entity = archive.find(virtual_path)
    if entity is None:
        print(f"Not found: {virtual_path}", file=sys.stderr)
        sys.exit(2)
    data = archive.read_file(entity)
    Path(out_file).parent.mkdir(parents=True, exist_ok=True)
    Path(out_file).write_bytes(data)
    print(f"Extracted {vpath} ({len(data)} bytes) -> {out_file}")


def main():
    parser = argparse.ArgumentParser(description="Extract files from a Softstar CPK archive")
    parser.add_argument("cpk", help="Path to .cpk archive")
    parser.add_argument("out", nargs="?", help="Output directory (for extract mode)")
    parser.add_argument("--list", action="store_true", help="List archive contents")
    parser.add_argument("--filter", help="Substring filter on virtual paths")
    parser.add_argument("--extract-one", nargs=2, metavar=("VPATH", "OUTFILE"),
                        help="Extract a single file by virtual path")
    parser.add_argument("--codepage", type=int, default=936, help="Filename codepage (default 936)")
    args = parser.parse_args()

    archive = CpkArchive(args.cpk, codepage=args.codepage)
    archive.load()

    if args.list:
        cmd_list(archive)
    elif args.extract_one:
        cmd_extract_one(archive, args.extract_one[0], args.extract_one[1])
    else:
        if not args.out:
            parser.error("Output directory is required for extract mode")
        cmd_extract(archive, args.out, args.filter)


if __name__ == "__main__":
    main()
