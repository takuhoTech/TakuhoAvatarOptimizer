# TAO: Takuho's Avatar Optimizer

VRChat アバター向けの非破壊最適化コンポーネント集です。処理はすべて NDMF のビルド時に行われるため、編集中のシーンやアセットは変更されません。

## コンポーネント

| コンポーネント | 内容 | 詳細 |
|---|---|---|
| TAO Merge PhysBone By Constraint | 髪の毛一本ごとに設定された多数の PhysBone を、VRC Rotation Constraint を使って 1 つの PhysBone にまとめて軽量化します。 | [docs/merge-physbone-by-constraint.md](docs/merge-physbone-by-constraint.md) |
| TAO Optimize VRC Shader Fallback | VRChat の Safety でカスタムシェーダーが無効化されたときの見た目が、元の見た目に近くなるように、マテリアルごとに VRCFallback を設定します。 | [docs/shader-fallback/README.md](docs/shader-fallback/README.md) |
| TAO Reorder GameObject | 同じ親を持つ子 GameObject 全体を、Order（並び順）の昇順に並べ替えます。 | [docs/reorder-gameobject.md](docs/reorder-gameobject.md) |

## 必要環境

- Unity 2022.3
- VRChat Avatars SDK（`com.vrchat.avatars` 3.10.0 以降）
- NDMF（`nadena.dev.ndmf` 1.8.0 以降）

## インストール

VRChat Creator Companion（VCC）2.1.0 以降で、次のリポジトリを追加してからパッケージを追加します。

1. VCC の **Settings** を開き、**Packages** タブの **Add Repository** を押します。
2. 次の URL を入力して **Add** を押し、表示された内容を確認して **I Understand, Add Repository** を押します。

   ```
   https://takuhotech.github.io/TakuhoAvatarOptimizer/vpm.json
   ```

3. プロジェクトのパッケージ一覧で **TAO: Takuho's Avatar Optimizer** を追加します。

## ビルド時の順序

| フェーズ | コンポーネント | 処理 |
|---|---|---|
| FirstChance（Modular Avatar より前） | TAO Reorder GameObject | 子 GameObject を並べ替えます。処理後、コンポーネントは削除されます。 |
| Transforming（Modular Avatar の後、AAO より前） | TAO Merge PhysBone By Constraint | PhysBone をまとめます。 |
| Resolving | TAO Optimize VRC Shader Fallback | 設定を読み取り、コンポーネントを削除します。AAO に検出されないようにするためです。 |
| Optimizing（AAO の後） | TAO Optimize VRC Shader Fallback | マテリアルを複製し、フォールバックの設定を書き込みます。 |

## ライセンス

MIT License（[LICENSE](LICENSE)）

## 言語

インスペクター上部の **Editor Language** で、日本語と英語を切り替えられます。

## その他

- 変更履歴: [CHANGELOG.md](CHANGELOG.md)
- 開発・リリース手順: [CONTRIBUTING.md](CONTRIBUTING.md)
