"""Package a WindowsNoEditor cook of Azure Blockhead as an RMM .rhammer."""

import argparse
import json
from pathlib import Path
import shutil
import tempfile
import zipfile


SAMPLE_ROOT = Path(__file__).resolve().parents[2]
TEMPLATE_PATH = SAMPLE_ROOT / "hammer.json.template"
CONTENT_PACKAGE_ROOT = Path("RMM/Hammers/azure-blockhead-verified")
COOKED_EXTENSIONS = {".uasset", ".uexp", ".ubulk", ".uptnl"}


def package_path_for(content_file, content_root):
    relative = content_file.relative_to(content_root)
    return "/Game/" + relative.with_suffix("").as_posix()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "content_dir",
        type=Path,
        help="Cooked project Content directory for WindowsNoEditor",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=SAMPLE_ROOT / "azure-blockhead-verified.rhammer",
        help="Output .rhammer path",
    )
    args = parser.parse_args()

    content_root = args.content_dir.resolve()
    package_root = (content_root / CONTENT_PACKAGE_ROOT).resolve()
    try:
        package_root.relative_to(content_root)
    except ValueError:
        parser.error("The hammer content folder must be inside content_dir")
    if not package_root.is_dir():
        parser.error("Cooked hammer folder not found: " + str(package_root))

    files = sorted(
        path
        for path in package_root.rglob("*")
        if path.is_file() and path.suffix.lower() in COOKED_EXTENSIONS
    )
    package_assets = []
    package_files = set()
    for source in files:
        relative = source.relative_to(content_root)
        package_path = package_path_for(source, content_root)
        archive_source = (Path("Assets") / relative).as_posix()
        package_assets.append({"source": archive_source, "packagePath": package_path})
        if source.suffix.lower() == ".uasset":
            package_files.add(package_path)

    if not package_files:
        parser.error("No cooked .uasset files found under " + str(package_root))

    with TEMPLATE_PATH.open(encoding="utf-8") as template_file:
        manifest = json.load(template_file)
    required_paths = {
        manifest["meshAssetPath"],
        manifest["iconAssetPath"],
        manifest["silhouetteAssetPath"],
        manifest["symbolAssetPath"],
    }
    missing = sorted(required_paths - package_files)
    if missing:
        parser.error("Cook is missing required hammer assets: " + ", ".join(missing))
    manifest["assets"] = package_assets

    output_path = args.output.resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="rmm-hammer-") as temporary:
        staging_root = Path(temporary)
        (staging_root / "hammer.json").write_text(
            json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
        )
        for source in files:
            relative = source.relative_to(content_root)
            destination = staging_root / "Assets" / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)

        preview_source = SAMPLE_ROOT / "Source/T_RMM_AzureBlockhead_Icon.png"
        preview_destination = staging_root / manifest["thumbnail"]
        preview_destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(preview_source, preview_destination)

        with zipfile.ZipFile(output_path, "w", zipfile.ZIP_DEFLATED) as archive:
            for path in sorted(staging_root.rglob("*")):
                if path.is_file():
                    archive.write(path, path.relative_to(staging_root).as_posix())

    print("Created", output_path)
    print("Included", len(package_assets), "cooked package files")


if __name__ == "__main__":
    main()
