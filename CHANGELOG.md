# Changelog

## [0.1.1]

- TAO Reorder GameObject に Order（並び順）を追加。同じ親を持つ子 GameObject 全体を Order の昇順（小さいほど上）に並べ替えます。コンポーネントが付いていない GameObject は Order = 50 として扱い、Order が同じ場合は元の並び順を維持します
- TAO Reorder GameObject の「親の最後の子に移動する」動作は廃止しました。既存のコンポーネントは Order = 50 になるため、最後に配置したい場合は、他の兄弟より大きな Order（例: 100）を設定してください

## [0.1.0]

- TAO Merge PhysBone By Constraint を追加（旧 TAO Convert PhysBone。内部のクラス名・シリアライズ項目を整理したため、旧バージョンとの互換性はありません）
- TAO Reorder GameObject を追加
- ビルド時の問題を NDMF のエラーレポートに表示
