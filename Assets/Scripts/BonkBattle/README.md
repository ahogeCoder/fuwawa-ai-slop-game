# Baubau Bonk Battle — 30秒フワワ殴りカウンター的

私的利用前提。フワワが30秒でどれだけ敵を殴れるかカウントする、ド派手演出のミニゲームです。
殴るたびにフワモコの完成短尺ボイスがランダム再生され、浮遊文字は **「BAU!」** のみ表示されます。

## 遊び方
- **移動**: WASD / 矢印キー
- **殴る**: Space / J / K / 左クリック / 右クリック（フワワが攻撃モーション `Throw`/`Bash` で殴ります）
- **制限時間**: 30秒（デフォルト）。残り10秒で警告表示
- **スコア**: 殴った体数。ベストは自動保存（PlayerPrefs）
- **被ダメ**: 敵に触れると15ダメージ（HP 100）。死ぬと `GAME OVER`
- **演出**: コンボ「BAU! xN」、パーティクル＋膨張球＋斬撃スラッシュ、画面シェイク、ヒットストップ、画面フラッシュ、浮遊文字「BAU!」
- **カメラ**: フワワ追従（`enableCameraFollow`、オフセット `cameraFollowOffset`、滑らかさ `cameraFollowSmooth`）
- **敵**: Kenney MiniCharacters（ランダム10種）＋ `CharacterAnimator` 歩行動画（Idle/Walk, `isWalking`）＋プロシージャル歩行ボブ

## セットアップ
シーン `Assets/Scenes/BonkBattle.unity` に `BonkBattleGame` を配置済み。Unityで開いて **Play** するだけです。
またはメニュー `BonkBattle > Create Scene & Play`。

## 構成
- `Assets/Scripts/BonkBattle/BonkBattleGameController.cs` — ゲーム進行・スポーン・HUD・FX・音声・カメラ追従
- `Assets/Scripts/BonkBattle/BonkPlayer.cs` — フワワ移動＋殴り入力、アニメtor（Jogging/Bash）
- `Assets/Scripts/BonkBattle/BonkEnemy.cs` — 敵AI（追跡＋ゆらぎ）、Animator連携、殴られたら吹っ飛ぶ
- `Assets/Resources/BaubauVoice/Short/BaubauBonk_*.wav` — 殴り用完成短尺（1.0s×4、PCM 44.1kHz mono）
- `Assets/Resources/BaubauVoice/*.mp3` — 元版（8種、3.3〜29.5s）
- `Assets/Audio/BaubauVoice/*.mp3` — 元版のコピー

## BaubauVoiceについて（ネットから取得→短尺化）
ユーザーの指定どおり、フワモコの「バウバウ」音声をネットから取得し、殴り用に完成短尺化しました：
- 出典: FuwaMoco Noises（https://fuwa-moco-trivia.netlify.app/audio）
- 元版（8種）:
  - FWMC_Baubaubaubau.mp3 (4.61s)
  - FWMC_Bau_Bau_Cannon.mp3 (11.67s)
  - FWMC_Hola_Hola_Bau_Bau.mp3 (5.05s)
  - Mococo_BauBauBauBauBauBau_DKC_EP6.mp3 (11.11s)
  - Mococo_BauBauBauBauBau_Part_Only_YTSHORT.mp3 (13.71s)
  - Mococo_Bau_Bau_Eh_RE4_EP4.mp3 (3.30s)
  - Fuwawa_Unraveling_The_Bau_RnR.mp3 (4.62s)
  - FWMC_Worlds_Longest_Bau.mp3 (29.47s)
- 短尺化（完成品）: 4本の短尺元版から無音トリミング＋先頭1.0s切出＋ loudnorm (−12 LUFS) ＋フェードアウト
  - `BaubauBonk_01_MococoEh.wav` / `BaubauBonk_02_Baubaubau.wav` / `BaubauBonk_03_Fuwawa.wav` / `BaubauBonk_04_Hola.wav`（すべて 1.00s）
- 殴るたびに短尺4本からランダム1本、ピッチ ±10% で再生します（長尺は重なりすぎるため使用しない）。

## フワワモデル＆敵モデル
- フワワ: `Assets/ThirdParty/FuwawaAbyssgard/fuwawa.fbx` を自動ロードし、Animator（Idle/Jogging/Bash/Throw/Jumping）で動かします
- 敵: `Assets/ThirdParty/Kenney/MiniCharacters/character-*.glb`（10種ランダム）＋ `Assets/Animators/CharacterAnimator.controller`（Idle/Walk、`isWalking`）。骨階層 `root/torso/...` が一致することを確認済み。Animatorが不一致でもプロシージャルボブで歩行感を担保。

## ド派手演出の実装メモ
- ヒットストップ: `Time.timeScale=0.08` を45ms（unscaledで復帰）
- 画面シェイク: 成功時のみ ±0.28m ランダムオフセット
- パーティクル＋衝撃波＋浮遊文字
- フラッシュ: 白オーバーレイ alpha 0.18→0
- ポップアップ: TextMesh「BAU! / バウバウ！」等が浮上＋拡大＋フェード
- 敵: 殴られたら吹き飛び＋高速回転＋上向きベクトル

## 制限時間の変更
- Inspectorで `BonkBattleGameController.matchDurationSeconds` を変更（デフォルト60）

## 注意
- 完全に私的利用前提で作成。ホロライブ二次創作ガイドラインおよび原音声の権利に注意し、再配布・公開はしないでください。
