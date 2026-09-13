# AstrumLoom.Rhythm

音楽ゲーム用の時刻付き入力、曲時計、譜面、判定、リプレイをまとめた独立モジュール。
Core・Audio・描画バックエンドへのプロジェクト参照や外部NuGet依存はありません。

利用側から `Rhythm/AstrumLoom.Rhythm.csproj` を参照し、`using AstrumLoom.Rhythm;` を追加してください。
`WasapiTransport` も同じ名前空間です（旧 `AstrumLoom.Audio.Rhythm` から変更）。

判定・時刻変換・リプレイは .NET 10 上で利用できます。
`WindowsRawInputSource` と `WasapiTransport` の生成はWindows専用です。
Raw Inputの登録と音声再生は明示的に生成した場合のみ開始されます。

```powershell
dotnet build Rhythm/AstrumLoom.Rhythm.csproj -c Release
dotnet run --project tests/RhythmChecks -c Release
dotnet run --project tests/RhythmChecks -c Release -- --native
```

Sandboxのシーン12が利用例です。設計・検証範囲・残件は
[RHYTHM-TIMING-DESIGN](../docs/RHYTHM-TIMING-DESIGN.md) を参照してください。
