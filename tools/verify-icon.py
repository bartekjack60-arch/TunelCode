"""Verify a Windows GUI EXE contains every frame of its source application icon.

Usage: python3 tools/verify-icon.py path/to/AutoTunel.exe source/AutoTunel.ico
Uses only the Python standard library and handles PNG and DIB icon payloads.
"""

import argparse
from pathlib import Path
import struct


class InvalidImage(ValueError):
    """The PE, ICO, or their relationship does not match the release contract."""


def require(condition, message):
    if not condition:
        raise InvalidImage(message)


class Bytes:
    def __init__(self, content, label):
        self.content = content
        self.label = label

    def read(self, offset, size):
        require(offset >= 0 and size >= 0 and offset + size <= len(self.content),
                f"{self.label}: truncated data at offset {offset}")
        return self.content[offset:offset + size]

    def unpack(self, fmt, offset):
        return struct.unpack(fmt, self.read(offset, struct.calcsize(fmt)))


def ico_frames(icon):
    reserved, kind, count = icon.unpack("<HHH", 0)
    require(reserved == 0 and kind == 1 and 1 <= count <= 256,
            "ICO: invalid icon directory")
    end_of_directory = 6 + 16 * count
    icon.read(0, end_of_directory)
    frames = []
    ranges = []
    for index in range(count):
        width, height, colors, reserved, planes, bits, size, offset = icon.unpack(
            "<BBBBHHII", 6 + 16 * index)
        require(reserved == 0 and size > 0 and offset >= end_of_directory,
                f"ICO: invalid frame {index}")
        payload = icon.read(offset, size)
        require(all(offset + size <= start or offset >= end for start, end in ranges),
                "ICO: overlapping frame payloads")
        ranges.append((offset, offset + size))
        frames.append(((width or 256, height or 256, colors, planes, bits), payload))
    sizes = {(metadata[0], metadata[1]) for metadata, _ in frames}
    required = {(size, size) for size in (16, 24, 32, 48, 64, 128, 256)}
    require(required.issubset(sizes),
            "ICO: missing required dimensions " + str(sorted(required - sizes)))
    return frames


def pe_resources(executable):
    require(executable.read(0, 2) == b"MZ", "EXE: missing DOS signature")
    pe_offset, = executable.unpack("<I", 0x3C)
    require(executable.read(pe_offset, 4) == b"PE\0\0", "EXE: missing PE signature")
    machine, count, _, _, _, optional_size, characteristics = executable.unpack(
        "<HHIIIHH", pe_offset + 4)
    require(machine == 0x8664, "EXE: expected native AMD64 (0x8664)")
    require(1 <= count <= 96, "EXE: invalid number of PE sections")
    require(not characteristics & 0x2000, "EXE: image is marked as a DLL")
    optional = pe_offset + 24
    executable.read(optional, optional_size)
    magic, = executable.unpack("<H", optional)
    require(magic == 0x20B, "EXE: expected PE32+ optional header")
    require(optional_size >= 112 + 3 * 8, "EXE: optional header has no resource directory")
    subsystem, = executable.unpack("<H", optional + 68)
    require(subsystem == 2, "EXE: expected Windows GUI subsystem (2)")
    directories, = executable.unpack("<I", optional + 108)
    require(directories >= 3, "EXE: resource data-directory entry is absent")
    resource_rva, resource_size = executable.unpack("<II", optional + 112 + 2 * 8)
    require(resource_rva != 0 and resource_size >= 16, "EXE: resource directory is empty")

    sections = []
    for index in range(count):
        offset = optional + optional_size + index * 40
        executable.read(offset, 40)
        virtual_size, rva, raw_size, raw_offset = executable.unpack("<IIII", offset + 8)
        if raw_size:
            executable.read(raw_offset, raw_size)
        sections.append((rva, virtual_size, raw_offset, raw_size))

    def file_offset(rva, size):
        matches = [(base, raw, raw_size) for base, virtual_size, raw, raw_size in sections
                   if base <= rva < base + max(virtual_size, raw_size)]
        require(len(matches) == 1, "EXE: resource RVA has no unambiguous section")
        base, raw, raw_size = matches[0]
        relative = rva - base
        require(relative + size <= raw_size, "EXE: resource data exceeds section file data")
        executable.read(raw + relative, size)
        return raw + relative

    resource_offset = file_offset(resource_rva, resource_size)
    resource = Bytes(executable.read(resource_offset, resource_size), "PE resources")
    resources = {}
    visited = set()

    def walk(relative, path):
        require(len(path) < 3 and relative not in visited,
                "EXE: unexpected resource depth or cyclic/shared directory")
        visited.add(relative)
        _, _, _, _, named, numbered = resource.unpack("<IIHHHH", relative)
        entries = named + numbered
        require(entries <= 65535, "EXE: too many resource entries")
        resource.read(relative + 16, entries * 8)
        for index in range(entries):
            name, target = resource.unpack("<II", relative + 16 + 8 * index)
            if name & 0x80000000:
                text_offset = name & 0x7FFFFFFF
                length, = resource.unpack("<H", text_offset)
                try:
                    name = resource.read(text_offset + 2, length * 2).decode("utf-16le")
                except UnicodeDecodeError as error:
                    raise InvalidImage("EXE: malformed resource name") from error
            key = path + (name,)
            if target & 0x80000000:
                require(len(key) < 3, "EXE: directory in place of a resource language")
                walk(target & 0x7FFFFFFF, key)
            else:
                require(len(key) == 3, "EXE: resource data has incomplete type/name/language")
                require(key not in resources, "EXE: duplicate resource identity")
                data_rva, size, _, reserved = resource.unpack("<IIII", target)
                require(reserved == 0, "EXE: invalid reserved resource field")
                resources[key] = executable.read(file_offset(data_rva, size), size)

    walk(0, ())
    return resources


def verify(exe_path, ico_path):
    frames = ico_frames(Bytes(ico_path.read_bytes(), "ICO"))
    resources = pe_resources(Bytes(exe_path.read_bytes(), "EXE"))
    groups = [(key, value) for key, value in resources.items() if key[0] == 14]
    require(groups, "EXE: RT_GROUP_ICON resource is missing")
    # This application has exactly one application icon; additional independent icons
    # would need an explicit selection policy rather than silently choosing a group.
    require(len(groups) == 1, "EXE: expected exactly one application icon group")
    group_key, group_payload = groups[0]
    group = Bytes(group_payload, "RT_GROUP_ICON")
    reserved, kind, count = group.unpack("<HHH", 0)
    require(reserved == 0 and kind == 1 and count == len(frames),
            "EXE: icon group header/count differs from source ICO")
    require(len(group_payload) == 6 + count * 14,
            "EXE: icon group has unexpected byte length")
    remaining = list(frames)
    matched_ids = set()
    dimensions = []
    for index in range(count):
        width, height, colors, reserved, planes, bits, size, icon_id = group.unpack(
            "<BBBBHHIH", 6 + index * 14)
        require(reserved == 0 and icon_id not in matched_ids,
                "EXE: invalid or repeated icon resource ID")
        matched_ids.add(icon_id)
        key = (3, icon_id, group_key[2])
        require(key in resources, "EXE: referenced RT_ICON resource is missing: " + str(key))
        payload = resources[key]
        require(len(payload) == size, "EXE: RT_ICON byte length differs from group entry")
        metadata = (width or 256, height or 256, colors, planes, bits)
        match = next((frame for frame in remaining if frame == (metadata, payload)), None)
        require(match is not None,
                f"EXE: {metadata[0]}x{metadata[1]} icon frame differs from source ICO")
        remaining.remove(match)
        dimensions.append(f"{metadata[0]}x{metadata[1]}")
    require(not remaining, "EXE: some source ICO frames are not embedded")
    native_icons = {key[1] for key in resources if key[0] == 3}
    require(native_icons == matched_ids, "EXE: unexpected unreferenced native icon resources")
    print("Verified AMD64 PE32+ Windows GUI executable.")
    print("Verified native RT_GROUP_ICON/RT_ICON frames match source ICO exactly: "
          + ", ".join(dimensions))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe", type=Path)
    parser.add_argument("ico", type=Path)
    args = parser.parse_args()
    try:
        verify(args.exe, args.ico)
    except (OSError, InvalidImage) as error:
        parser.exit(1, f"Icon verification failed: {error}\n")


if __name__ == "__main__":
    main()
