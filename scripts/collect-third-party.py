"""Collect pinned source archives and their unmodified license texts. Executes no sources.

The Deno Cargo.lock inventory is deliberately a superset (including build/test and
other-platform crates), not a claim that every crate is linked into the Windows exe.
Python 3.11+; standard library only. Review inventory/manual gaps before publication.
"""
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import tarfile
import time
import tomllib
import zipfile
from urllib.request import Request, urlopen
from urllib.error import HTTPError
from urllib.parse import urlparse

REPO = Path(__file__).resolve().parents[1]
CACHE = REPO / ".tools" / "release-sources"
DEST = REPO / ".tools" / "source-bundle"
ARCHIVES = DEST / "archives"
NOTICES = REPO / ".tools" / "licenses" / "collected"
for directory in (CACHE, ARCHIVES, NOTICES):
    directory.mkdir(parents=True, exist_ok=True)


def fetch(url, path, expected=None):
    if not path.exists():
        request = Request(url, headers={"User-Agent": "LumeFetch-source-inventory/1.0"})
        for attempt in range(3):
            try:
                with urlopen(request, timeout=45) as response:
                    data = response.read(200_000_001)
                if len(data) > 200_000_000:
                    raise ValueError("Source archive exceeds size limit")
                if expected and hashlib.sha256(data).hexdigest() != expected:
                    raise ValueError("Source checksum mismatch: " + path.name)
                path.write_bytes(data)
                break
            except (OSError, TimeoutError):
                if attempt == 2:
                    raise
                time.sleep(1 + attempt)
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    if expected and digest != expected:
        raise ValueError("Cached source checksum mismatch: " + path.name)
    return digest


def notices(archive, identity):
    # Read individual tar entries only, never extract untrusted paths or symlinks.
    texts = []
    license_expression = None
    if zipfile.is_zipfile(archive):
        with zipfile.ZipFile(archive) as zipped:
            for member in zipped.infolist():
                if not member.is_dir() and member.file_size <= 2_000_000 and notice_name(member.filename):
                    texts.append(member.filename + '\n' + '-' * 72 + '\n' + zipped.read(member).decode('utf-8', errors='replace'))
        if texts:
            (NOTICES / (identity + '.txt')).write_text('\n\n'.join(texts), encoding='utf-8')
        return None, len(texts)
    with tarfile.open(archive, "r:*") as tar:
        for member in tar:
            if not member.isfile() or member.size > 2_000_000:
                continue
            name = PurePosixPath(member.name).name
            if name == "Cargo.toml" and len(PurePosixPath(member.name).parts) == 2:
                metadata = tomllib.loads(tar.extractfile(member).read().decode("utf-8"))
                license_expression = metadata.get("package", {}).get("license")
            if notice_name(name):
                text = tar.extractfile(member).read().decode("utf-8", errors="replace")
                texts.append(member.name + "\n" + "-" * 72 + "\n" + text)
    if texts:
        (NOTICES / (identity + ".txt")).write_text("\n\n".join(texts), encoding="utf-8")
    return license_expression, len(texts)


def notice_name(path):
    return re.match(r"^(unlicense|licen[cs]e|copying|copyright|notice|third.party.notices|ofl)([._-].*)?$", PurePosixPath(path).name, re.I)


def recover_workspace_notice(archive, identity):
    """Some Rust workspace crates omit root licenses. Use their recorded VCS commit."""
    with tarfile.open(archive, "r:*") as tar:
        root = tar.getnames()[0].split('/')[0]
        try:
            metadata = tomllib.loads(tar.extractfile(root + '/Cargo.toml').read().decode('utf-8'))['package']
        except (KeyError, TypeError):
            return 0
        try:
            vcs = json.load(tar.extractfile(root + '/.cargo_vcs_info.json'))
        except (KeyError, TypeError):
            vcs = {}
    repository = urlparse(metadata.get('repository') or metadata.get('homepage') or '')
    parts = repository.path.removesuffix('.git').strip('/').split('/')
    revision = vcs.get('git', {}).get('sha1', '')
    if repository.hostname != 'github.com' or len(parts) < 2:
        return 0
    cached = NOTICES / (identity + '.txt')
    if cached.exists():
        if cached.read_text(encoding='utf-8').startswith('Upstream archive declares '):
            return 0
        return 1
    texts = []
    revisions = ([revision] if re.fullmatch('[a-f0-9]{40}', revision) else []) + ['HEAD']
    for ref in revisions:
        for filename in ('LICENSE', 'LICENSE.txt', 'LICENSE.md', 'LICENSE-MIT', 'LICENSE-APACHE', 'COPYING', 'COPYRIGHT', 'NOTICE', 'LICENSE_MIT.txt', 'LICENSE_APACHE_2_0.txt'):
            url = f'https://raw.githubusercontent.com/{parts[0]}/{parts[1]}/{ref}/{filename}'
            try:
                with urlopen(Request(url, headers={'User-Agent': 'LumeFetch-source-inventory/1.0'}), timeout=30) as response:
                    text = response.read(2_000_001)
                if len(text) <= 2_000_000:
                    texts.append(url + '\n' + '-' * 72 + '\n' + text.decode('utf-8'))
            except HTTPError as error:
                if error.code != 404:
                    raise
        if texts:
            if ref == 'HEAD':
                texts.insert(0, 'Upstream root notice retrieved 2026-09-06. The recorded crate VCS revision was unavailable; source archive and SPDX declaration remain pinned.\n' + json.dumps(metadata, ensure_ascii=False, indent=2))
            break
    if texts:
        (NOTICES / (identity + '.txt')).write_text('\n\n'.join(texts), encoding='utf-8')
    return len(texts)


def crate(package):
    name, version = package["name"], package["version"]
    identity = f"{name}-{version}"
    filename = identity + ".crate"
    url = f"https://static.crates.io/crates/{name}/{filename}"
    path = ARCHIVES / filename
    digest = fetch(url, path, package["checksum"])
    expression, count = notices(path, identity)
    if not count:
        count = recover_workspace_notice(path, identity)
    return {"component": identity, "group": "Deno Cargo.lock superset", "url": url,
            "file": "archives/" + filename, "sha256": digest, "license": expression, "noticeFiles": count}


def pypi(pair):
    name, version = pair
    request = Request(f"https://pypi.org/pypi/{name}/{version}/json", headers={"User-Agent": "LumeFetch-source-inventory/1.0"})
    with urlopen(request, timeout=45) as response:
        metadata = json.load(response)
    source = next(entry for entry in metadata["urls"] if entry["packagetype"] == "sdist")
    filename = source["filename"]
    if PurePosixPath(filename).name != filename or "\\" in filename:
        raise ValueError("Invalid registry filename")
    path = ARCHIVES / filename
    digest = fetch(source["url"], path, source["digests"]["sha256"])
    _, count = notices(path, f"python-{name}-{version}")
    return {"component": f"{name}-{version}", "group": "yt-dlp Python dependencies",
            "url": source["url"], "file": "archives/" + filename, "sha256": digest, "noticeFiles": count}


def main():
    manifest = json.loads((REPO / 'packaging/windows/sources.json').read_text(encoding='utf-8'))
    standalone = []
    for entry in manifest:
        path = CACHE / entry['file']
        digest = fetch(entry['url'], path, entry['sha256'])
        shutil.copy2(path, ARCHIVES / path.name)
        count = 0
        if not entry.get('input'):
            _, count = notices(path, 'source-' + path.name)
        standalone.append({'component': entry['component'], 'group': 'Standalone source/build input',
            'url': entry['url'], 'file': 'archives/' + path.name, 'sha256': digest, 'noticeFiles': count, 'input': entry.get('input', False)})
        print('Verified source:', entry['component'], flush=True)
    lock = tomllib.loads((CACHE / "deno-Cargo.lock").read_text(encoding="utf-8"))
    packages = [p for p in lock["package"] if p.get("source", "").startswith("registry+")]
    unsupported = [p["name"] for p in lock["package"] if p.get("source", "").startswith("git+")]
    if unsupported:
        raise ValueError("Uncollected git dependencies: " + ", ".join(unsupported))
    python_packages = set()
    for match in re.finditer(r"^([\w.-]+)==([\w.]+)(.*)$", (CACHE / "yt-dlp-curl-cffi-requirements.txt").read_text(), re.M):
        name, version, marker = match.groups()
        if name == "brotlicffi" or (name == "websockets" and version != "16.1.1"):
            continue
        python_packages.add((name, version))
    python_packages.add(("yt-dlp", "2026.8.19"))
    inventory, failures = standalone, []
    with ThreadPoolExecutor(max_workers=6) as pool:
        futures = [pool.submit(crate, p) for p in packages]
        futures += [pool.submit(pypi, p) for p in sorted(python_packages)]
        for index, future in enumerate(as_completed(futures), 1):
            try:
                inventory.append(future.result())
            except Exception as error:
                failures.append(str(error))
                print("FAILED:", error, flush=True)
            if index % 50 == 0:
                print(f"Collected {index}/{len(futures)} source archives", flush=True)
    (DEST / "source-inventory.json").write_text(json.dumps(sorted(inventory, key=lambda x: x["component"]), indent=2), encoding="utf-8")
    # These workspace crates use the same upstream root notices as their siblings.
    siblings = {'cranelift-assembler-x64-0.117.2': 'cranelift-0.117.2',
        'cranelift-assembler-x64-meta-0.117.2': 'cranelift-0.117.2',
        'dlopen2_derive-0.4.0': 'dlopen2-0.6.1', 'lazy-regex-proc_macros-3.1.0': 'lazy-regex-3.1.0',
        'rsqlite-vfs-0.1.1': 'sqlite-wasm-rs-0.5.5'}
    declaration_only = []
    for entry in inventory:
        if entry['noticeFiles'] or entry.get('input'):
            continue
        identity = entry['component']
        if identity in siblings:
            sibling = NOTICES / (siblings[identity] + '.txt')
            if not sibling.exists():
                raise ValueError('Missing workspace notice: ' + str(sibling))
            (NOTICES / (identity + '.txt')).write_text('Workspace root notice shared with ' + siblings[identity] + '\n\n' + sibling.read_text(encoding='utf-8'), encoding='utf-8')
            entry['noticeFiles'] = 1
            entry['noticeOrigin'] = 'workspace sibling: ' + siblings[identity]
        elif entry.get('license') and 'MIT' in entry['license'].split():
            # Preserve the upstream declaration and authors; do not invent copyright years/owners.
            # The full unmodified source is distributed alongside these standard license terms.
            standard = CACHE / 'SPDX-MIT-3.27.0.txt'
            fetch('https://raw.githubusercontent.com/spdx/license-list-data/v3.27.0/text/MIT.txt', standard,
                  'b05785f9f18e6716bab63424b11454513b9943a222595b70411009202fc592b5')
            with tarfile.open(DEST / entry['file'], 'r:*') as tar:
                root = tar.getnames()[0].split('/')[0]
                original = tar.extractfile(root + '/Cargo.toml').read().decode('utf-8')
                statements = []
                for member in tar:
                    if member.isfile() and member.size < 2_000_000 and member.name.endswith(('.rs', '.md', '.toml')):
                        lines = tar.extractfile(member).read().decode('utf-8', errors='replace').splitlines()
                        hits = [line for line in lines if 'copyright' in line.lower() or 'SPDX-License-Identifier' in line]
                        if hits:
                            statements.append(member.name + '\n' + '\n'.join(hits))
            (NOTICES / (identity + '.txt')).write_text(
                'Upstream archive declares ' + entry['license'] + ' but provides no separate license file.\n'
                'MIT is selected where alternatives are offered. Standard SPDX MIT text follows unchanged;\n'
                'its placeholders are not an asserted copyright notice. Original declarations and any\n'
                'copyright statements found in the source follow it. Full original source is supplied.\n\n'
                + standard.read_text(encoding='utf-8') + '\n\n' + original + '\n\n' + '\n\n'.join(statements), encoding='utf-8')
            entry['noticeOrigin'] = 'upstream declaration + standard MIT terms; no upstream license file'
            declaration_only.append(identity)
    gaps = [entry['component'] for entry in inventory if not entry['noticeFiles'] and not entry.get('input') and entry['component'] not in declaration_only]
    (DEST / 'source-inventory.json').write_text(json.dumps(sorted(inventory, key=lambda x: x['component']), indent=2), encoding='utf-8')
    (DEST / "review-gaps.json").write_text(json.dumps({"failed": failures, "noLicenseFileInArchive": gaps,
        'upstreamDeclarationOnly': declaration_only}, indent=2), encoding="utf-8")
    print(f"Source collection: {len(inventory)} archives; {len(failures)} failures; {len(gaps)} archives need notice review", flush=True)
    if failures or gaps:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
