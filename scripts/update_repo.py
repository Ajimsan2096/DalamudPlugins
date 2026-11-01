#!/usr/bin/env python3
"""Collects every built plugin under src/*/bin/Release/<Name>/ and refreshes plugins/ + repo.json.

Run after `dotnet build -c Release` for each plugin. Each entry in repo.json is the manifest that
DalamudPackager generated, plus the download links Dalamud needs.
"""
import json
import shutil
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RAW = "https://raw.githubusercontent.com/AdrianJimenezSantiago/DalamudPlugins/main"


def main() -> None:
    repo_path = ROOT / "repo.json"
    existing = {p["InternalName"]: p for p in json.loads(repo_path.read_text(encoding="utf-8") or "[]")}

    for project in sorted((ROOT / "src").glob("*/*.csproj")):
        name = project.stem
        out = project.parent / "bin" / "Release" / name
        manifest_file, zip_file = out / f"{name}.json", out / "latest.zip"
        if not manifest_file.exists() or not zip_file.exists():
            print(f"skip {name}: not built")
            continue

        manifest = json.loads(manifest_file.read_text(encoding="utf-8-sig"))
        dest = ROOT / "plugins" / name
        dest.mkdir(parents=True, exist_ok=True)
        old = existing.get(name, {})
        changed = not (dest / "latest.zip").exists() or old.get("AssemblyVersion") != manifest["AssemblyVersion"]
        shutil.copyfile(zip_file, dest / "latest.zip")

        link = f"{RAW}/plugins/{name}/latest.zip"
        manifest.update({
            "DownloadLinkInstall": link,
            "DownloadLinkTesting": link,
            "DownloadLinkUpdate": link,
            "IsTestingExclusive": False,
            "LastUpdate": int(time.time()) if changed else old.get("LastUpdate", int(time.time())),
            "DownloadCount": 0,
        })
        icon = project.parent / "images" / "icon.png"
        if icon.exists():
            manifest["IconUrl"] = f"{RAW}/src/{name}/images/icon.png"
        existing[name] = manifest
        print(f"{name} {manifest['AssemblyVersion']} (API {manifest['DalamudApiLevel']})")

    repo_path.write_text(json.dumps(sorted(existing.values(), key=lambda p: p["InternalName"]), indent=2) + "\n",
                         encoding="utf-8")


if __name__ == "__main__":
    main()
