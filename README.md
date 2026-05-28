# Secure Token 付与ツール (Avalonia / .NET 9)

新規アカウントに Secure Token が付与されず FileVault 有効化が失敗する端末を、
ヘルプデスクが手動でリカバリするための macOS GUI ツール。

## 機能

- 起動時に端末上のローカルユーザ(UID 500 以上)を自動列挙し、
  「Secure Token 保有者(=親候補)」「未保有者(=子候補)」の2つのコンボへ振り分ける
- 各ユーザに管理者バッジを表示
- 「付与」押下時、実処理の前にパスワードを事前検証(`dscl -authonly`、状態は変更しない)
- 親が標準ユーザの場合、共通管理者アカウント(macadmin)の権限で一時的に管理者へ昇格 →
  付与 → 完了後(失敗時も)標準ユーザへ自動で降格
- `sysadminctl -secureTokenOn` による Token 付与(パスワードは stdin 経由)
- 付与後、`sysadminctl -secureTokenStatus` で実状態を再確認して成否を判定

## 処理フロー

```
[ユーザ一覧を自動取得]
        │
[親(Token保有者) / 子(未保有者) / 各パスワードを選択・入力]
        │
[付与ボタン]
        │
   パスワード事前チェック ── dscl -authonly(変更なし)
        │  macadmin(親が標準ユーザのときのみ) / 親 / 子
        ├─ NG が1つでも → 中止(昇格には入らない。パスワードは保持)
        │
   親が標準ユーザ? ──Yes──→ macadmin 権限で管理者へ昇格
        │                         │
   Secure Token 付与(sysadminctl)←┘
        │
   付与後の状態を再確認
        │
   昇格していた場合 → 必ず標準ユーザへ降格(成否に関わらず)
```

## プロジェクト構成

```
SecureTokenTool/
├── SecureTokenTool.csproj
├── Program.cs                       エントリポイント
├── App.axaml / App.axaml.cs         アプリ初期化・MainWindow 生成
├── Configuration/
│   └── AdminAccount.cs              ★ macadmin の認証情報(配布前に要編集)
├── Models/
│   └── LocalUser.cs                 ローカルユーザ1件の情報(record)
├── Services/
│   ├── ProcessRunner.cs             外部コマンド実行ヘルパー(stdin 対応)
│   └── SecureTokenService.cs        sysadminctl / dscl / dseditgroup ラッパー
├── ViewModels/
│   └── MainWindowViewModel.cs       画面ロジック(MVVM)
├── Views/
│   ├── MainWindow.axaml             画面レイアウト
│   └── MainWindow.axaml.cs          ログ自動スクロール
├── ui-mock.html                     UI プレビュー(ブラウザで開く)
└── README.md
```

## 配布前に必須の設定

`Configuration/AdminAccount.cs` の `Password` を、実際の macadmin のパスワードへ
置き換えること。プレースホルダのままだと事前チェックで「設定エラー」となり中止する。

```csharp
public const string Password = "REPLACE_WITH_MACADMIN_PASSWORD"; // ← 要置換
```

## 設計上のポイント

- **パスワードを引数に出さない(付与時)**: `sysadminctl` には `-password -` /
  `-adminPassword -` を使い、パスワードを stdin 経由で渡す。`ps` に残らない。
  stdin の順序はコマンドラインの `-` 出現順、すなわち 子 → 親。
- **昇格の認証ダイアログを出さない**: 昇格/降格(`dseditgroup`)は osascript の
  `do shell script ... user name ... password ... with administrator privileges` で実行。
  AppleScript は osascript の stdin 経由で渡すため、macadmin のパスワードも `ps` に出ない。
- **事前チェックは引数渡し**: `dscl -authonly` のパスワード stdin 形式は制御端末を
  持たないプロセスで不安定(ハングの恐れ)なため、検証は引数渡しを採用。露出は
  数百ミリ秒のみ。実際の付与は上記の通り stdin 渡しを維持。
- **終了コードを信用しない**: `sysadminctl` の終了コードは不安定なため、付与後に
  必ず `-secureTokenStatus` で実状態を再確認する。
- **昇格は確実に戻す**: 降格は `finally` で実行し、付与の成否に関わらず標準ユーザへ
  戻す。降格に失敗した場合はログに警告と手動確認コマンドを出す。
- **パスワードの保持/破棄**: 事前チェックで止まった場合はパスワードを保持し
  (NG項目だけ直して再実行できる)、実処理に入った場合のみ完了後にクリアする。
- **コマンドインジェクション対策**: シェルへ渡すユーザ名は `[A-Za-z0-9._-]` のみ許可。

## ⚠ セキュリティ上の注意

`AdminAccount.cs` に埋め込んだ macadmin のパスワードは、.NET アセンブリを
逆コンパイル(ILSpy / dotPeek 等)すれば平文同然で抽出できる。

- 本アプリの配布先は信頼できる範囲(社内ヘルプデスク端末など)に限定すること。
- より安全にするには、Jamf Pro の LAPS でパスワードをローテーションし、
  API 経由で取得する方式への切り替えを推奨する。

## 必要環境

- .NET 9 SDK
- macOS(実行対象)。Windows でも開発・ビルドは可能だが、
  `sysadminctl` 等は macOS 上でのみ動作する。

## 開発・実行

```bash
cd SecureTokenTool
dotnet restore
dotnet run
```

## macOS 向け発行(配布用)

```bash
# Apple Silicon
dotnet publish -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true -o ./publish/arm64

# Intel
dotnet publish -c Release -r osx-x64 --self-contained true \
  -p:PublishSingleFile=true -o ./publish/x64
```

`.app` バンドル化・署名・公証は別途必要。.NET self-contained アプリは
Hardened Runtime 対応のため、署名時に以下の entitlements が必要:

```xml
<key>com.apple.security.cs.allow-jit</key><true/>
<key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
<key>com.apple.security.cs.disable-library-validation</key><true/>
```

## 権限について

- `sysadminctl -secureTokenOn` は `-adminUser` / `-adminPassword` を渡すため、
  アプリ自体を sudo で起動する必要はない。
- 昇格/降格は macadmin の認証情報を osascript 経由で渡して実行するため、
  これも sudo 起動や認証ダイアログは不要。
- `dscl` / `diskutil` / `fdesetup` の参照系コマンドは通常ユーザ権限で動作する。

## 注意

- 付与成功後、Jamf Pro のインベントリへ反映するには対象 Mac で
  `sudo jamf recon` を別途実行すること(本ツールには含めていない)。
- 親アカウントは「Secure Token 保有者」であること。標準ユーザでも、本ツールが
  付与時のみ一時的に管理者へ昇格して処理する(完了後に標準へ戻す)。
