# Dome Projection Player

Unityで2:1の正距円筒図法（equirectangular）360°動画を、単眼180°ドーム用の円形ドームマスターへリアルタイム変換する試作アプリです。Nikon FC-E8などのフロントコンバージョンレンズをプロジェクター前面に置く構成を想定しています。

## 必要環境

- Unity 6.3 LTS（プロジェクト作成時の指定: 6000.3.26f1）
- macOSまたはWindows
- 入力動画: 2:1 equirectangular、MP4/H.264を推奨

Unity Hubでこのフォルダーをプロジェクトとして追加し、`Assets/Scenes/Main.unity`を開いて再生してください。シーンは空ですが、起動時にプレイヤー、出力カメラ、操作画面が自動生成されます。

ビルド済みアプリは通常のファイル選択に加え、検証や展示の自動起動用に動画をコマンドライン指定できます。

```sh
open Builds/DomeProjectionPlayer.app --args --video /absolute/path/to/video.mp4
```

## 現在の機能

- Unity標準 `VideoPlayer` によるローカル動画再生
- 2:1 equirectangularから180°円形ドームマスターへのGPU変換
- Yaw / Pitch / Roll
- 出力円の中心、半径、縦横倍率、回転
- FC-E8実測用の放射歪み係数 K1 / K2
- 明るさ、ガンマ、円周フェード
- 15°同心円・15°放射線の校正パターン
- 複数ディスプレイ出力
- 設定のJSON保存
- EditorおよびmacOSビルドでのファイル選択

設定は `Application.persistentDataPath/dome-projection-settings.json` に保存されます。

## キー操作

| キー | 動作 |
| --- | --- |
| Space | 再生 / 一時停止 |
| G | グリッド表示切替 |
| H | 操作パネル表示切替 |
| F | 全画面切替 |
| R | 幾何補正を初期化 |

## 動画の準備

Insta360 StudioやGoPro Playerでスティッチした後、次を目安に書き出します。

- 5760×2880または3840×1920
- H.264 MP4
- 30 fpsまたは60 fps
- 2D（モノラル）360°

独自形式の `.insv` や `.360` は直接読みません。

容量の大きい検証用動画は、プロジェクト直下の `LocalVideos/` に置いてください。このフォルダー内の動画は `.gitignore` でGit管理対象外になっています。

例:

```text
LocalVideos/GoProMAX-test-5.6K.mp4
```

アプリを起動して「選択」から動画を指定するか、次のように起動します。

```sh
open Builds/DomeProjectionPlayer.app --args --video "$PWD/LocalVideos/GoProMAX-test-5.6K.mp4"
```

## AVPro Videoについて

現状はAVPro Videoに依存しません。`IVideoSource` と標準実装 `UnityVideoSource` を分離しているため、5.7K/8K動画の性能やシーク精度に問題が出た場合、購入済みのAVPro Video 2.9.3を別実装として追加できます。

AVPro Video 2.xはDeprecatedのため、最初からプロジェクトへインポートしないでください。まず標準VideoPlayerで実機性能を測定します。

## FC-E8到着後の校正

1. 動画を読み込まず、校正パターンを表示する
2. 円の中心と半径を調整して、投影円をレンズ光軸へ合わせる
3. ドーム上の15°リング位置を確認する
4. `放射補正 K1` と `K2` でリング間隔を合わせる
5. 必要なら横倍率・縦倍率・出力回転を調整する
6. 設定を保存する

K1/K2だけで補正できない非対称歪みが確認された場合は、次段階でメッシュワープを追加します。
