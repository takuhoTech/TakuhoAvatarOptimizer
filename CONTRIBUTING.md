# Contributing

## リリース手順

1. `CHANGELOG.md` に新しいバージョンの節（`## [x.y.z]`）を追加して `main` に反映します。
2. GitHub の **Actions** タブで **Release** ワークフローを `main` ブランチから実行します。
   - `version`: リリースするバージョン（例: `0.1.1`、プレリリースは `0.2.0-beta.1`）
   - `publish`: 最初はオフのまま実行し、artifact の zip の内容を確認します。問題がなければ、オンにして再実行します。
3. `publish` をオンで実行すると、次の処理を行います。
   - `package.json` の `version` を更新して `main` にコミットし、タグ `v<version>` を作成します。
   - zip と `CHANGELOG.md` の節を添えて GitHub Release を作成します。
   - `gh-pages` ブランチの `vpm.json` に新しいバージョンを追加します。

## 初回の設定

- リポジトリを **Public** にします。VCC は認証なしで zip をダウンロードするため、Private のままでは利用できません。
- `main` にブランチ保護を設定している場合は、`github-actions[bot]` が `main` と `gh-pages` に push できるように許可します。
- 最初の `publish` 実行で `gh-pages` ブランチが作成されます。その後、**Settings → Pages** の Source を **Deploy from a branch**、Branch を **gh-pages** / **(root)** に設定します。
