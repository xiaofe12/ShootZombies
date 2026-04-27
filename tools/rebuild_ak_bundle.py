import argparse
import os
import sys

import UnityPy


PREFERRED_EXPORTS = (
    ("assets/_ak/ak.prefab", "GameObject", "AK"),
    ("assets/_ak/materials/m_ak.mat", "Material", "M_AK"),
    ("assets/_ak/textures/ak_texture.asset", "Texture2D", "AK-47_type_II"),
    ("assets/_ak/textures/ak_icon.asset", "Texture2D", "AK-47_type_II_icon"),
)


def find_object(env, type_name, object_name):
    for obj in env.objects:
        if obj.type.name != type_name:
            continue
        data = obj.read()
        if getattr(data, "m_Name", None) == object_name:
            return obj
    raise RuntimeError(f"Missing {type_name} named {object_name!r} in bundle")


def patch_bundle(src_path, out_path):
    env = UnityPy.load(src_path)
    assetbundle_obj = next(obj for obj in env.objects if obj.type.name == "AssetBundle")
    tree = assetbundle_obj.read_typetree()

    entry_map = {name: value for name, value in tree["m_Container"]}
    ic_entry = entry_map.get("assets/_ak/ic_ak.asset")
    if ic_entry is None:
        raise RuntimeError("Bundle does not contain assets/_ak/ic_ak.asset")

    preload_index = ic_entry["preloadIndex"]
    preload_size = ic_entry["preloadSize"]
    updated_container = [
        (name, value)
        for name, value in tree["m_Container"]
        if name != "assets/_ak/ic_ak.asset"
    ]

    for asset_name, type_name, object_name in PREFERRED_EXPORTS:
        target = find_object(env, type_name, object_name)
        entry = {
            "preloadIndex": preload_index,
            "preloadSize": preload_size,
            "asset": {"m_FileID": 0, "m_PathID": target.path_id},
        }
        if asset_name in entry_map:
            entry_map[asset_name] = entry
            for index, (existing_name, _) in enumerate(updated_container):
                if existing_name == asset_name:
                    updated_container[index] = (asset_name, entry)
                    break
        else:
            updated_container.append((asset_name, entry))

    main_asset_obj = find_object(env, "GameObject", "AK")
    tree["m_Container"] = updated_container
    tree["m_MainAsset"] = {
        "preloadIndex": preload_index,
        "preloadSize": preload_size,
        "asset": {"m_FileID": 0, "m_PathID": main_asset_obj.path_id},
    }

    assetbundle_obj.save_typetree(tree)

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "wb") as handle:
        handle.write(env.file.save())

    print(f"patched {src_path} -> {out_path}")
    for asset_name, _, _ in PREFERRED_EXPORTS:
        print(f"exported {asset_name}")


def main():
    parser = argparse.ArgumentParser(description="Promote AK assets to top-level exports in Weapons_shootzombies.bundle")
    parser.add_argument("src", nargs="?", default=os.path.join("bin", "Release", "Weapons_shootzombies.bundle"))
    parser.add_argument("out", nargs="?", default=None)
    args = parser.parse_args()

    src_path = os.path.abspath(args.src)
    out_path = os.path.abspath(args.out or args.src)

    if not os.path.exists(src_path):
        raise SystemExit(f"Input bundle not found: {src_path}")

    patch_bundle(src_path, out_path)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"error: {exc}", file=sys.stderr)
        raise
