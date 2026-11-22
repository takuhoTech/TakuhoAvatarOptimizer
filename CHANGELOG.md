# Changelog

## [0.1.0]

初回リリースです。

- TAO Merge PhysBone By Constraint を追加。髪の毛一本ごとに設定された多数の PhysBone を、VRC Rotation Constraint を使って 1 つの PhysBone にまとめて軽量化します
- TAO Reorder GameObject を追加。同じ親を持つ子 GameObject 全体を、Order（並び順）の昇順（小さいほど上）に並べ替えます。コンポーネントが付いていない GameObject は Order = 50 として扱い、Order が同じ場合は元の並び順を維持します
- TAO Optimize VRC Shader Fallback を追加。VRChat の Safety でカスタムシェーダーが無効化されたときの見た目が元に近くなるように、マテリアルごとに VRCFallback を設定します。元のマテリアルとテクスチャのアセットは変更しません
  - 基本のフォールバック先（Toon Standard・Toon・Unlit）、影、輪郭線、明るさの下限と上限を設定できます
  - 詳細設定で、メインカラーの焼き込み、透明度の焼き込み、設定済みの VRCFallback の上書きを切り替えられます
  - 半透明マテリアルは、透け方に合わせて半透明のフォールバック先、不透明、または Hidden に分類します。判断の理由は一覧で確認できます
  - マテリアルごとにフォールバック先を指定できます
  - フォールバックの見た目をシーン上でプレビュー（Preview Fallback）し、検出結果を一覧で確認できます（Preview Result）
- ビルド時の問題を NDMF のエラーレポートに表示します
- インスペクター上部の Editor Language で、日本語と英語を切り替えられます

### 制限事項

- Particle・Toon・Unlit・Unlit Transparent・Standard 系の見え方は、実機で確認していません
- MatCap、金属の反射、リムライト、FakeShadow、各種マスク、頂点の変形などは再現しません
- 焼き込みは、Windows 向けのビルドだけで行います
- フォールバックのプレビュー（Preview Fallback）は、VRChat の表示を近似したものです
