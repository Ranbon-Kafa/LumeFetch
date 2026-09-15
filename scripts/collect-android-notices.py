"""Collect Android redistribution notices from pinned sources and the restored graph.

Reads archives without extracting/executing code. The graph includes build-time
and non-Android NuGet entries; this deliberately labelled superset is not an SBOM.
Fails closed on missing notices. No credentials or local paths are put in inventory.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import tarfile
import urllib.request
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parent.parent
PATTERN = re.compile(r"^(unlicense|licen[cs]e|copying|copyright|notice|third.party.notices|ofl)([._-].*)?$", re.I)


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def put(root, relative, data):
    parts = PurePosixPath(relative)
    if parts.is_absolute() or ".." in parts.parts or "\\" in relative or ":" in relative:
        raise ValueError(f"Unsafe notice path: {relative}")
    target = root.joinpath(*parts.parts)
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and target.read_bytes() != data:
        raise ValueError(f"Notice collision: {relative}")
    target.write_bytes(data)


def collect_archive(path, output, component):
    count = 0
    with tarfile.open(path, "r:*") as archive:
        for member in archive:
            if not member.isfile():
                continue
            name = PurePosixPath(member.name).name
            sqlite = name == "sqlite3.c" and path.name.startswith("sqlite-autoconf")
            zlib = name == "README" and path.name.startswith("zlib-")
            if not (PATTERN.match(name) or sqlite or zlib):
                continue
            if member.size > 4_000_000 and not sqlite:
                raise ValueError(f"Oversized notice: {path.name}/{member.name}")
            stream = archive.extractfile(member)
            if sqlite:
                data = b"".join(stream.readline() for _ in range(40))
                relative = member.name + "-license-header.txt"
            else:
                data = stream.read()
                relative = member.name
            put(output, "native/" + component + "/" + relative, data)
            count += 1
    if not count:
        raise ValueError(f"No notices: {path.name}")
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, required=True, help="Android project.assets.json")
    parser.add_argument("--ndk", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="Fresh notices directory")
    parser.add_argument("--offline", action="store_true")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    rows = []
    locks = read_json(REPO / "packaging/android/native-sources.lock.json")
    for package in read_json(REPO / "packaging/android/python-packages.lock.json"):
        locks.append({"file": package["sourceFile"], "cache": ".tools/android-native/downloads",
                      "sha256": package["sourceSha256"], "url": package["sourceUrl"]})
    for item in locks:
        path = REPO / item["cache"] / item["file"]
        if sha(path) != item["sha256"]:
            raise ValueError(f"Source checksum mismatch: {path.name}")
        count = collect_archive(path, args.output, path.name)
        rows.append({"kind": "source-archive", "file": path.name, "url": item["url"],
                     "sha256": item["sha256"], "noticeFiles": count})

    for item in read_json(REPO / "packaging/android/notices.lock.json"):
        path = REPO / ".tools/android-native/notice-inputs" / item["file"]
        if not path.exists() and not args.offline:
            path.parent.mkdir(parents=True, exist_ok=True)
            with urllib.request.urlopen(item["url"], timeout=60) as response:
                data = response.read(4_000_001)
            if hashlib.sha256(data).hexdigest() != item["sha256"]:
                raise ValueError(f"Downloaded notice checksum mismatch: {path.name}")
            path.write_bytes(data)
        if sha(path) != item["sha256"]:
            raise ValueError(f"Notice checksum mismatch: {path.name}")
        put(args.output, "upstream/" + item["file"], path.read_bytes())
        rows.append({"kind": "upstream-notice", **item})

    graph = read_json(args.assets)
    ns = {"n": "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"}
    for identity, library in sorted(graph["libraries"].items()):
        if library["type"] != "package":
            continue
        directory = REPO / ".tools/nuget/packages" / library["path"]
        spec = next(directory.glob("*.nuspec"))
        metadata = ET.parse(spec).getroot()
        namespace = metadata.tag.partition("}")[0].lstrip("{")
        ns["n"] = namespace
        license_node = metadata.find("n:metadata/n:license", ns)
        repository = metadata.find("n:metadata/n:repository", ns)
        count = 0
        for path in sorted(directory.rglob("*")):
            if path.is_file() and PATTERN.match(path.name):
                put(args.output, "nuget/" + identity + "/" + path.relative_to(directory).as_posix(), path.read_bytes())
                count += 1
        put(args.output, "nuget/" + identity + "/" + spec.name, spec.read_bytes())
        # These packages omit the text but refer to the separately pinned upstream MIT license.
        fallback = None
        if not count:
            if identity.startswith("Avalonia.BuildServices/"):
                fallback = "upstream/Avalonia.BuildServices-LICENSE.txt"
            elif identity.startswith("Avalonia.") or identity.startswith("Avalonia/"):
                fallback = "upstream/Avalonia-LICENSE.txt"
            elif identity.startswith("MicroCom.Runtime/"):
                fallback = "upstream/MicroCom-LICENSE.txt"
            else:
                raise ValueError(f"No notice or explicit upstream mapping: {identity}")
        archives = list(directory.glob("*.nupkg"))
        if len(archives) != 1:
            raise ValueError(f"Expected one NuGet archive: {identity}")
        rows.append({"kind": "nuget-restore-graph", "id": identity,
                     "license": license_node.text if license_node is not None else None,
                     "repository": repository.attrib if repository is not None else None,
                     "sha256": sha(archives[0]), "restoreSha512": library.get("sha512"),
                     "noticeFiles": count, "upstreamNotice": fallback})

    for runtime in ("Microsoft.NETCore.App.Runtime.Mono.android-arm64/10.0.11",
                    "Microsoft.NETCore.App.Runtime.Mono.android-x64/10.0.11",
                    "Microsoft.Android.Runtime.Mono.36.android-arm64/36.1.2",
                    "Microsoft.Android.Runtime.Mono.36.android-x64/36.1.2"):
        directory = REPO / ".tools/dotnet/packs" / runtime
        count = 0
        for path in directory.iterdir():
            if path.is_file() and (PATTERN.match(path.name) or path.suffix == ".nuspec" or path.name.endswith(".versions.txt")):
                put(args.output, "dotnet/" + runtime + "/" + path.name, path.read_bytes())
                count += 1
        if count < 2:
            raise ValueError(f"Missing runtime notices: {runtime}")
        rows.append({"kind": "runtime-pack", "id": runtime, "noticeFiles": count})
    for name in ("NOTICE", "NOTICE.toolchain", "source.properties"):
        path = args.ndk / name
        put(args.output, "ndk/" + name, path.read_bytes())
        rows.append({"kind": "ndk-notice", "file": name, "sha256": sha(path)})

    put(args.output, "LumeFetch-LICENSE.txt", (REPO / "LICENSE").read_bytes())
    put(args.output, "INDEX.md", (REPO / "packaging/android/REDISTRIBUTION.md").read_bytes())
    put(args.output, "inventory.json", (json.dumps({
        "scope": "Source-notice and NuGet restore-graph superset, NOT a binary-only SBOM. Includes build/test/non-Android upstream notices; no claim that every mentioned component ships.",
        "components": rows}, ensure_ascii=False, indent=2) + "\n").encode())
    print(f"Collected {sum(1 for _ in args.output.rglob('*') if _.is_file())} notice/metadata files for {len(rows)} inputs.")


if __name__ == "__main__":
    main()
