import argparse
import hashlib
import json
from pathlib import Path

LISTING_NAME = "TAO VPM Repository"
LISTING_ID = "com.takuho.tao.vpm"
LISTING_AUTHOR = "Takuho"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--listing", type=Path, required=True)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--zip", type=Path, required=True)
    parser.add_argument("--zip-url", required=True)
    parser.add_argument("--listing-url", required=True)
    args = parser.parse_args()

    manifest = json.loads(args.package.read_text(encoding="utf-8"))
    zip_sha256 = hashlib.sha256(args.zip.read_bytes()).hexdigest()

    if args.listing.exists():
        listing = json.loads(args.listing.read_text(encoding="utf-8"))
        listing["url"] = args.listing_url
    else:
        listing = {
            "name": LISTING_NAME,
            "id": LISTING_ID,
            "url": args.listing_url,
            "author": LISTING_AUTHOR,
            "packages": {},
        }

    package = listing["packages"].setdefault(manifest["name"], {"versions": {}})
    package["versions"][manifest["version"]] = {
        **manifest,
        "url": args.zip_url,
        "zipSHA256": zip_sha256,
    }

    args.listing.write_text(json.dumps(listing, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
