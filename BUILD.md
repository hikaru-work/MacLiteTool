# Secure Token 付与ツール — 段階構築手順 (VS 2022 開発前提)

開発は Windows + Visual Studio 2022、配布物の生成(署名・公証・PKG化)は macOS で行うクロス開発フロー。最小構成から段階的に拡張する。

```
[Windows] Phase 1: VS 2022 で開発・UI確認
   │                ↓ ソース共有(Git 推奨)
[macOS]   Phase 2: 実機で機能テスト
[macOS]   Phase 3: .app バンドルにする
[macOS]   Phase 4: コード署名する
[macOS]   Phase 5: 公証する
[macOS]   Phase 6: PKG インストーラ化
[macOS]   Phase 7: Intel Mac 対応(2系統)
[Web]     Phase 8: Jamf Pro 配布
```

各フェーズ冒頭の **🪟 Windows / 🍎 macOS / 🌐 Web** で作業ホストを示す。

-----

## Phase 0: 環境準備

### 0-1. 🪟 Windows 側(開発機)

|ツール                              |用途                  |備考                   |
|---------------------------------|--------------------|---------------------|
|Visual Studio 2022 (Community 以上)|開発・デバッグ             |「.NET デスクトップ開発」ワークロード|
|.NET 9 SDK                       |コンパイル・発行            |VS 2022 のインストーラから追加可能|
|Git for Windows                  |ソース管理・Mac との共有      |推奨                   |
|Avalonia for Visual Studio 2022  |`.axaml` の編集補助・プレビュー|VS Marketplace から入手  |

VS 2022 インストール後の確認:

```powershell
dotnet --version    # 9.0.x 以上
git --version       # ある
```

**Avalonia 拡張のインストール:**

1. VS 2022 → 拡張機能 → 拡張機能の管理
1. 「Avalonia」で検索 → **Avalonia for Visual Studio 2022** をインストール
1. VS 再起動

### 0-2. 🍎 macOS 側(ビルド & 配布パッケージ作成機)

ビルド環境としての Mac が1台必要(Phase 2 以降のすべてで使う)。Apple Silicon 推奨。

|ツール                     |用途                                    |入手                             |
|------------------------|--------------------------------------|-------------------------------|
|macOS 12 以上             |各種コマンド実行                              |—                              |
|.NET 9 SDK              |(オプション)Mac 側で直接ビルドする場合                |<https://dotnet.microsoft.com/>|
|Xcode Command Line Tools|`codesign` / `pkgbuild` / `notarytool`|`xcode-select --install`       |
|Git                     |Windows 側からのソース取得                     |macOS 標準                       |

```bash
xcode-select --install
git --version
codesign --version
xcrun notarytool --help    # ある
```

### 0-3. Windows ↔ Mac のソース共有方法

開発と配布物生成が別ホストになるため、コード同期手段が必要。**Git リポジトリ経由を推奨**(社内 GitLab / GitHub Enterprise / Azure DevOps など)。

```
[Windows VS 2022]  ──push──>  [Git サーバ]  <──pull──  [Mac]
                                                        ↓
                                              ./build-macapp.sh
                                                        ↓
                                                署名済み PKG
```

代替手段(Git が使えない場合):

- **SMB 共有フォルダ**経由でソースをコピー
- **scp / rsync** で Mac へ転送(`scp -r SecureTokenTool/ user@mac:/Users/user/work/`)
- **VS 2022 のリモート開発機能** — SSH 経由で Mac の `~/work/SecureTokenTool` を開く

このドキュメントは Git 前提で書く。他の手段でも、要は「Windows のソースを Mac に渡せれば良い」。

### 0-4. Apple Developer Program(Phase 4 以降に必要)

Phase 4(コード署名)を始める時点で必要:

- **Apple Developer Program 加入**(年額 USD 99 / JPY 14,800)
- **Developer ID Application 証明書**(`.app` 署名用)
- **Developer ID Installer 証明書**(Phase 6 の `.pkg` 署名用)

→ Phase 4 で詳述。

-----

## Phase 1: 🪟 VS 2022 で開発・UI確認

### 概要

Windows + VS 2022 でソースを開き、F5 で起動して UI を確認する。リファクタリング・コード修正・UI 開発はすべてこのフェーズで行う。

⚠ **`sysadminctl` 等の macOS コマンドは存在しないため、機能テスト(実際の Secure Token 付与)はできない**。Phase 1 で確認できるのは UI 動作・ユーザ列挙・コンボの振り分け・ログ出力までで、付与ボタンを押しても認証段階で失敗する。機能テストは Phase 2 で行う。

### 手順

**1-1. プロジェクトディレクトリを作って、ソースを配置**

```powershell
cd $HOME
mkdir work\SecureTokenTool
cd work\SecureTokenTool
```

提示済みのソース一式を以下の構成で配置:

```
SecureTokenTool/
├── SecureTokenTool.csproj
├── Program.cs
├── App.axaml
├── App.axaml.cs
├── Configuration\AdminAccount.cs
├── Models\LocalUser.cs
├── Services\ProcessRunner.cs
├── Services\SecureTokenService.cs
├── ViewModels\MainWindowViewModel.cs
├── Views\MainWindow.axaml
└── Views\MainWindow.axaml.cs
```

**1-2. Git リポジトリ化**

```powershell
git init
```

`.gitignore` を以下の内容で作成:

```
bin/
obj/
publish/
*.user
.vs/
*.suo
.vscode/

# 認証情報を含むファイル(必要に応じて)
# Configuration/AdminAccount.cs
```

`Configuration/AdminAccount.cs` を `.gitignore` に入れるか入れないかは運用判断。プレースホルダ版だけコミットして、実値はローカル管理という方式が安全。

**1-3. macadmin パスワードを埋め込む**

`Configuration/AdminAccount.cs` を編集:

```csharp
public const string Password = "(実際の macadmin パスワード)";
```

**1-4. VS 2022 で開く**

- ファイル → 開く → プロジェクト/ソリューション → `SecureTokenTool.csproj` を選択
- 初回は自動で NuGet 復元が走る(完了まで待つ)
- ソリューションエクスプローラに各ファイルが表示されること

**1-5. F5 で起動**

- 上部の「▶ SecureTokenTool」ボタンを押す、または `F5`
- ビルド → ウィンドウが起動
- ユーザ一覧が自動列挙される
- (Windows なので `dscl` 等がエラーになり「ユーザ取得失敗」ログが出るが、UI 自体は正常に表示される)

### 検証

✓ ウィンドウタイトル「Secure Token 付与ツール」が表示される
✓ コンボボックス2つ、パスワード欄2つ、付与ボタンが表示される
✓ ログ欄にエラーが出ても、UI 自体は操作可能

ここで UI のレイアウト・ボタン配置・ログ表示の体裁を確認する。Avalonia 拡張のプレビュー機能を使えば、起動せず XAML 編集中に表示確認もできる。

### ここで止めて良い条件

✅ **UI 開発・リファクタリング作業の段階**ならここで足りる。コードの構造を `ISecureTokenService` で抽象化する作業、ログ表示の改善などはここで完結。

### 次フェーズへ進む動機

- 実際に macOS 実機で動かして機能テストしたい
- Secure Token 付与が動くことを確認したい

-----

## Phase 2: 🍎 Mac 実機で機能テスト

### 概要

Mac 側にソースを取り込んで実際に動かす。`dotnet run` で良いし、Windows で発行したバイナリを Mac で実行する方式でも良い。

### 手順

**2-1. Mac へソースを取り込む**

Git 経由:

```bash
cd ~/work
git clone <リポジトリ URL> SecureTokenTool
cd SecureTokenTool
```

(`Configuration/AdminAccount.cs` を `.gitignore` で除外している場合は、Mac 側でも手動で同じ内容を作成する)

scp 経由(Git が使えない場合):

```bash
# Windows 側で zip 化して送る、または
# Mac 側で取りに行く
scp -r windows-user@windows-host:/c/Users/.../SecureTokenTool ~/work/
```

**2-2. (方法A)Mac で直接ビルド・実行**

```bash
cd ~/work/SecureTokenTool
dotnet restore
dotnet run
```

これが手軽。Mac に .NET 9 SDK が必要(Phase 0-2 で導入済み)。

**2-3. (方法B)Windows で発行したバイナリを使う**

Windows 側で:

```powershell
cd $HOME\work\SecureTokenTool
dotnet publish -c Release -r osx-arm64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish\arm64
```

⚠ **`PublishTrimmed=true` は付けない**(Avalonia/Mvvm のリフレクションで動作不良の可能性)。

成果物を Mac へ転送して実行:

```bash
# Mac 側
xattr -dr com.apple.quarantine ./SecureTokenTool   # 転送経路による属性を除去
chmod +x ./SecureTokenTool
./SecureTokenTool
```

### 検証

実機 Mac 上で:

✓ ユーザ一覧が正しく列挙される(`dscl` の出力ベース)
✓ 「親」コンボに Secure Token 保有者、「子」に未保有者が振り分けられる
✓ 各ユーザの管理者バッジが正しく表示される
✓ (テスト用ダミーアカウントで)実際に Secure Token を付与し、ログとステータスが想定通り

**テスト用アカウント作成(参考):**

```bash
# 標準ユーザのダミーを作成(macadmin 権限で)
sudo sysadminctl -addUser testuser01 -fullName "Test User 01" -password "TestPass!"
# このユーザは Secure Token 未保有なので、付与ツールの「子」候補に出る
```

テスト後は削除:

```bash
sudo sysadminctl -deleteUser testuser01
```

### ここで止めて良い条件

✅ **自分の Mac でだけ使う**なら完了。`dotnet run` で動かす運用で十分なら以降は不要。

### 次フェーズへ進む動機

- 他の Mac でも `dotnet` を入れずに使いたい
- Finder からダブルクリックで起動したい
- ヘルプデスクに配布したい

-----

## Phase 3: 🍎 `.app` バンドルにする

### 概要

Mac の「アプリケーション」フォーマットに包む。Finder で `.app` として認識され、ダブルクリックで起動できる。**この時点ではまだ未署名なので Gatekeeper の警告は出る**。

### 手順

**3-1. Release 発行(まだなら)**

Mac 側:

```bash
cd ~/work/SecureTokenTool
dotnet publish -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish/arm64
```

**3-2. .app バンドル構築**

```bash
APP_DIR="./publish/SecureTokenTool.app"
rm -rf "${APP_DIR}"
mkdir -p "${APP_DIR}/Contents/MacOS" "${APP_DIR}/Contents/Resources"
cp -r ./publish/arm64/* "${APP_DIR}/Contents/MacOS/"
chmod +x "${APP_DIR}/Contents/MacOS/SecureTokenTool"

cat > "${APP_DIR}/Contents/Info.plist" << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>SecureTokenTool</string>
    <key>CFBundleIdentifier</key>
    <string>jp.your-company.securetokentool</string>
    <key>CFBundleName</key>
    <string>SecureTokenTool</string>
    <key>CFBundleDisplayName</key>
    <string>Secure Token 付与ツール</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF
```

`CFBundleIdentifier` は自社ドメインに合わせて変更。

### 検証

```bash
open ./publish/SecureTokenTool.app
```

Finder からダブルクリックでも起動する。

### ここで止めて良い条件

✅ **自分の Mac + 信頼関係のあるテスト Mac** で完結する場合。
✅ 配布先で `xattr -dr com.apple.quarantine` を打ってもらう運用が可能なら可。

### 制約

❌ Gatekeeper の警告「開発元が未確認のため開けません」が出る。一般配布には不向き。

-----

## Phase 4: 🍎 コード署名する

### 概要

Apple Developer ID 証明書で `.app` に署名する。改ざん検知と「誰が作ったか」の証明。Gatekeeper の警告は緩和されるがゼロにはならない(初回起動時の確認は残る)。

### 追加要件

- **Apple Developer Program 加入**
- **Developer ID Application 証明書** — Developer アカウントの Certificates ページから作成、Mac の Keychain にインストール

確認:

```bash
security find-identity -v -p codesigning
# "Developer ID Application: 会社名 (XXXXXXXXXX)" が出ること
```

### 手順

**4-1. entitlements.plist 作成**

.NET の JIT 実行に必要な権限を宣言:

```bash
cat > entitlements.plist << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key>
    <true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
    <true/>
    <key>com.apple.security.cs.disable-library-validation</key>
    <true/>
</dict>
</plist>
EOF
```

**4-2. 署名(奥側から外側へ)**

```bash
CERT="Developer ID Application: 会社名 (XXXXXXXXXX)"
APP_DIR="./publish/SecureTokenTool.app"

# 1. 内部の .dylib を全部署名
find "${APP_DIR}" -name "*.dylib" -exec \
  codesign --force --timestamp --options runtime \
    --entitlements entitlements.plist \
    --sign "$CERT" {} \;

# 2. 本体実行ファイル
codesign --force --timestamp --options runtime \
  --entitlements entitlements.plist \
  --sign "$CERT" \
  "${APP_DIR}/Contents/MacOS/SecureTokenTool"

# 3. .app 全体
codesign --force --timestamp --options runtime \
  --entitlements entitlements.plist \
  --sign "$CERT" \
  "${APP_DIR}"
```

### 検証

```bash
codesign --verify --deep --strict --verbose=2 "${APP_DIR}"
# "valid on disk" / "satisfies its Designated Requirement" が出れば OK
```

### ここで止めて良い条件

✅ 限定的な社内配布で、初回起動時の「開く」操作を案内可能な場合。

### 制約

❌ 初回起動時に Gatekeeper の確認ダイアログが出る(警告は出るが「開く」で起動可能)。

-----

## Phase 5: 🍎 公証する

### 概要

Apple のサーバへ `.app` を提出し、マルウェアチェック後に「お墨付き」をもらう。Gatekeeper の警告が完全に消える。

### 追加要件

- **App-Specific Password** — <https://appleid.apple.com/> で発行
- notarytool 用 Keychain プロファイル登録(1回だけ):

```bash
xcrun notarytool store-credentials "AC_PROFILE" \
  --apple-id "you@example.com" \
  --team-id "XXXXXXXXXX" \
  --password "xxxx-xxxx-xxxx-xxxx"
```

### 手順

```bash
APP_DIR="./publish/SecureTokenTool.app"
ZIP="./publish/SecureTokenTool.zip"

# 公証用に zip
ditto -c -k --sequesterRsrc --keepParent "${APP_DIR}" "${ZIP}"

# 提出 & 待つ(--wait で完了まで)
xcrun notarytool submit "${ZIP}" \
  --keychain-profile "AC_PROFILE" \
  --wait
# Status: Accepted で完了

# staple(公証情報を .app に埋め込む)
xcrun stapler staple "${APP_DIR}"
```

### 検証

```bash
spctl --assess --type execute --verbose "${APP_DIR}"
# "accepted" / "source=Notarized Developer ID" が出れば成功

xcrun stapler validate "${APP_DIR}"
```

### 公証失敗時

```bash
xcrun notarytool log <submission-id> --keychain-profile AC_PROFILE
```

よくある原因: entitlements 不足、未署名の dylib、Hardened Runtime 無効。Phase 4 を見直す。

### ここで止めて良い条件

✅ **`.app` を zip で配布、または共有ドライブ経由で各自に取得してもらう**運用なら完了。Gatekeeper 警告なしで起動できる。

-----

## Phase 6: 🍎 PKG インストーラ化

### 概要

`.app` を `.pkg` インストーラに包む。`/Applications` への配置が確実で、MDM 配布の標準形式。

### 追加要件

- **Developer ID Installer 証明書** — Application 用とは別物。Developer アカウントから作成。

確認:

```bash
security find-identity -v -p codesigning
# "Developer ID Installer: 会社名 (XXXXXXXXXX)" も出るはず
```

### 手順

```bash
APP_DIR="./publish/SecureTokenTool.app"
PKG="./publish/SecureTokenTool-1.0.0.pkg"
PKG_SIGNED="./publish/SecureTokenTool-1.0.0-signed.pkg"
CERT_INST="Developer ID Installer: 会社名 (XXXXXXXXXX)"

# PKG 生成
pkgbuild --root "${APP_DIR}" \
  --identifier jp.your-company.securetokentool \
  --version 1.0.0 \
  --install-location /Applications/SecureTokenTool.app \
  "${PKG}"

# PKG に署名
productsign --sign "${CERT_INST}" "${PKG}" "${PKG_SIGNED}"

# PKG も公証(推奨)
xcrun notarytool submit "${PKG_SIGNED}" \
  --keychain-profile "AC_PROFILE" --wait
xcrun stapler staple "${PKG_SIGNED}"
```

### 検証

```bash
pkgutil --check-signature "${PKG_SIGNED}"
# "Status: signed by a developer certificate issued by Apple for distribution"

# テストインストール
sudo installer -pkg "${PKG_SIGNED}" -target /
ls /Applications/SecureTokenTool.app
open /Applications/SecureTokenTool.app
```

### ここで止めて良い条件

✅ **手動 or リモート操作で PKG を実行できる**運用なら完了。MDM がなくても問題なし。

-----

## Phase 7: 🍎 Intel Mac 対応

### 概要

ここまでは arm64 のみ。Intel Mac もサポートする場合、x64 用に同じ流れを繰り返す。

### 手順

**7-1. x64 用 Release 発行**

```bash
dotnet publish -c Release -r osx-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish/x64
```

**7-2. Phase 3〜6 を x64 でも実行**

Phase 3 のスクリプトで `arm64` を `x64` に置換するだけ。後述の付録 A の `build-macapp.sh` は両方を自動で処理する。

成果物:

- `SecureTokenTool-1.0.0-arm64-signed.pkg`
- `SecureTokenTool-1.0.0-x64-signed.pkg`

### ここで止めて良い条件

✅ 手動配布で Intel / Apple Silicon 両対応できれば完了。

-----

## Phase 8: 🌐 Jamf Pro 配布

### 概要

Jamf Pro 管理画面(ブラウザベース)で PKG をアップロードし、ヘルプデスク用 Mac へ自動配信。**ブラウザ操作なので Windows からも Mac からも可能**。

### 手順

**8-1. PKG をアップロード**

Jamf Pro 管理画面 → **Settings → Computer Management → Packages → New**

- arm64 用と x64 用、両方を別パッケージとして登録
- Category は「Helpdesk Tools」など適当に

**8-2. アーキテクチャ別のスマートグループ**

**Computers → Smart Computer Groups → New**

- 「Helpdesk Macs (Apple Silicon)」: メンバー所属 + `Architecture Type is arm64`
- 「Helpdesk Macs (Intel)」: メンバー所属 + `Architecture Type is i386`

**8-3. ポリシー作成(各アーキ用に1つずつ)**

**Computers → Policies → New**

- **General**:
  - Display Name: `Secure Token 付与ツール (arm64)`
  - Trigger: `Recurring Check-in` または `Self Service`
- **Packages**: arm64 用 PKG、Action: `Install`
- **Scope**: 「Helpdesk Macs (Apple Silicon)」
- (推奨)**Self Service** 有効化、アイコンと説明文を設定

x64 用も同様。

### 検証

配信対象の Mac で:

```bash
ls -la /Applications/SecureTokenTool.app
codesign --verify --deep --strict /Applications/SecureTokenTool.app
spctl --assess --type execute /Applications/SecureTokenTool.app
```

3つとも成功すれば Gatekeeper も通る。Self Service から起動 → 実際の付与までを最終テスト。

### 完成

ここまでで配布フローが完成。これ以降は運用フェーズ(バージョンアップ、退役)。

-----

## 付録 A: 全自動スクリプト(🍎 macOS 側)

Phase 3〜7 を一括で実行する `build-macapp.sh`。Mac 側のリポジトリルートに置く。

```bash
#!/bin/bash
set -e

# ==== 自社設定(初回のみ書き換え) ====
VERSION="1.0.0"
APP_NAME="SecureTokenTool"
BUNDLE_ID="jp.your-company.securetokentool"
CERT_APP="Developer ID Application: 会社名 (XXXXXXXXXX)"
CERT_INST="Developer ID Installer: 会社名 (XXXXXXXXXX)"
NOTARY_PROFILE="AC_PROFILE"
# =====================================

if [ ! -f entitlements.plist ]; then
  cat > entitlements.plist << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key><true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
    <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict>
</plist>
EOF
fi

build_arch() {
  local ARCH=$1
  echo "================= Building ${ARCH} ================="

  # --- Phase 2/7: publish ---
  dotnet publish -c Release -r osx-${ARCH} --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o ./publish/${ARCH}

  # --- Phase 3: .app バンドル ---
  local APP_DIR="./publish/${APP_NAME}-${ARCH}.app"
  rm -rf "${APP_DIR}"
  mkdir -p "${APP_DIR}/Contents/MacOS" "${APP_DIR}/Contents/Resources"
  cp -r ./publish/${ARCH}/* "${APP_DIR}/Contents/MacOS/"
  chmod +x "${APP_DIR}/Contents/MacOS/${APP_NAME}"

  cat > "${APP_DIR}/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key><string>${APP_NAME}</string>
    <key>CFBundleIdentifier</key><string>${BUNDLE_ID}</string>
    <key>CFBundleName</key><string>${APP_NAME}</string>
    <key>CFBundleDisplayName</key><string>Secure Token 付与ツール</string>
    <key>CFBundleVersion</key><string>${VERSION}</string>
    <key>CFBundleShortVersionString</key><string>${VERSION}</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>LSMinimumSystemVersion</key><string>12.0</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

  # --- Phase 4: 署名 ---
  find "${APP_DIR}" -name "*.dylib" -exec \
    codesign --force --timestamp --options runtime \
      --entitlements entitlements.plist \
      --sign "${CERT_APP}" {} \;
  codesign --force --timestamp --options runtime \
    --entitlements entitlements.plist \
    --sign "${CERT_APP}" \
    "${APP_DIR}/Contents/MacOS/${APP_NAME}"
  codesign --force --timestamp --options runtime \
    --entitlements entitlements.plist \
    --sign "${CERT_APP}" "${APP_DIR}"
  codesign --verify --deep --strict --verbose=2 "${APP_DIR}"

  # --- Phase 5: 公証 ---
  local ZIP="./publish/${APP_NAME}-${ARCH}.zip"
  ditto -c -k --sequesterRsrc --keepParent "${APP_DIR}" "${ZIP}"
  xcrun notarytool submit "${ZIP}" --keychain-profile "${NOTARY_PROFILE}" --wait
  xcrun stapler staple "${APP_DIR}"

  # --- Phase 6: PKG ---
  local PKG="./publish/${APP_NAME}-${VERSION}-${ARCH}.pkg"
  local PKG_SIGNED="./publish/${APP_NAME}-${VERSION}-${ARCH}-signed.pkg"
  pkgbuild --root "${APP_DIR}" \
    --identifier "${BUNDLE_ID}" \
    --version "${VERSION}" \
    --install-location "/Applications/${APP_NAME}.app" \
    "${PKG}"
  productsign --sign "${CERT_INST}" "${PKG}" "${PKG_SIGNED}"
  xcrun notarytool submit "${PKG_SIGNED}" \
    --keychain-profile "${NOTARY_PROFILE}" --wait
  xcrun stapler staple "${PKG_SIGNED}"

  echo "============= Done: ${PKG_SIGNED} ============="
}

# --- Phase 7: 両アーキビルド ---
build_arch arm64
build_arch x64
echo ""
echo "All builds finished. PKGs are under ./publish/"
```

使い方:

```bash
chmod +x build-macapp.sh
./build-macapp.sh
```

-----

## 付録 B: VS 2022 から発行する方法(任意)

VS 2022 から `dotnet publish` 相当を GUI で実行することも可能。

**B-1. 発行プロファイル作成**

1. ソリューションエクスプローラで `SecureTokenTool` プロジェクトを右クリック → 発行
1. 「フォルダー」を選択 → 次へ
1. 場所: `./publish/arm64`、次へ
1. 完了
1. プロファイル設定で:
- 構成: Release
- ターゲットランタイム: `osx-arm64`
- 配置モード: 自己完結
- ファイルの公開: 単一ファイルを生成する

「発行」ボタンを押すと `./publish/arm64` に出力される。

**B-2. プロファイルを Git にコミット**

`Properties/PublishProfiles/FolderProfile-osx-arm64.pubxml` がプロジェクトに保存される。コミットすれば次回からワンクリック発行可能。

ただし、Phase 7 のように arm64 と x64 を続けて作るならコマンドラインの方が早い。

-----

## 付録 C: Windows ↔ Mac の往復ワークフロー例

実際の開発ループ(典型例):

```
🪟 Windows VS 2022
  ├─ コード修正
  ├─ F5 で UI 動作確認
  ├─ git commit & push
  │
  ↓ (Git)
  │
🍎 Mac(ssh または直接操作)
  ├─ git pull
  ├─ dotnet run で機能テスト
  │  └─ 問題なし → 続行 / 問題あり → Windows へ戻る
  │
  ├─ ./build-macapp.sh
  │  └─ 署名済み PKG が生成
  │
  ↓ (PKG ファイル)
  │
🌐 ブラウザ(Win/Mac どちらでも)
  └─ Jamf Pro 管理画面で PKG をアップロード
     └─ ポリシー作成 → 配信
```

VS 2022 のターミナルウィンドウ(表示 → ターミナル)から直接 ssh で Mac に入れば、Windows から離れずに作業可能。

-----

## 付録 D: トラブルシューティング

|症状                                                                         |原因                                              |対処                                                                                                    |
|---------------------------------------------------------------------------|------------------------------------------------|------------------------------------------------------------------------------------------------------|
|VS 2022 でビルド失敗(NuGet 復元エラー)                                                |プロキシ・社内環境                                       |`nuget.config` でフィードを社内ミラーに設定                                                                         |
|`dotnet run` で「設定エラー」                                                      |`AdminAccount.cs` パスワード未設定                      |プレースホルダを実値に置換                                                                                         |
|Mac で .app 起動時「開発元が未確認」                                                    |未署名状態(Phase 4 前)                                |`xattr -dr com.apple.quarantine` か Phase 4 へ                                                          |
|Phase 4 後も Gatekeeper 警告                                                   |未公証(Phase 5 前)                                  |Phase 5 で公証                                                                                           |
|`codesign` で `Resource fork...` エラー                                        |拡張属性が残っている                                      |`xattr -cr ./publish/*.app` で除去後に再署名                                                                  |
|`notarytool submit` が `Invalid`                                            |entitlements / dylib 署名漏れ                       |`notarytool log <ID>` でログ確認 → Phase 4 見直し                                                             |
|`pkgutil --check-signature` で Untrusted                                    |Installer 証明書未使用 or 期限切れ                        |Developer ID Installer を使用                                                                            |
|アプリ内「認証エラー(macadmin: NG)」                                                  |配布先 Mac の macadmin パスワード相違                      |配布前のパスワード合わせ                                                                                          |
|「要確認: 親が管理者のまま」                                                            |降格処理失敗                                          |手動で `sudo dseditgroup -o edit -d <親> -t user admin`                                                   |
|VS 2022 で .axaml デザイナが出ない                                                  |Avalonia 拡張未インストール                              |Marketplace から「Avalonia for VS 2022」を入れる                                                              |
|ビルドエラー `CS0111: type already defines a member called 'InitializeComponent'`|手書き `InitializeComponent` が source generator と衝突|手書きの `private void InitializeComponent() => AvaloniaXamlLoader.Load(this);` を削除(Avalonia 11 では自動生成される)|

-----

## 付録 E: リリース前チェックリスト

🪟 **Windows 側:**

- [ ] VS 2022 で F5 起動が成功する
- [ ] `Configuration/AdminAccount.cs` のパスワードを実値に変更した(配布版のみ)
- [ ] Git に push 済み(Mac 側で取得可能)

🍎 **macOS 側:**

- [ ] `dotnet run` で機能テスト(実際の Secure Token 付与)が成功する
- [ ] `build-macapp.sh` の `BUNDLE_ID` を自社ドメインに変更した
- [ ] `CERT_APP` / `CERT_INST` を `security find-identity` の出力に合わせた
- [ ] `NOTARY_PROFILE` を `store-credentials` で登録した名前に合わせた
- [ ] arm64 / x64 両方のビルド・署名・公証・PKG 化が成功する
- [ ] `pkgutil --check-signature` が両方 OK
- [ ] テスト用 Mac でインストール → Gatekeeper 警告なしで起動できる

🌐 **Jamf 配布:**

- [ ] Jamf Pro に両アーキの PKG をアップロード済み
- [ ] スマートグループでアーキ別スコープを作成済み
- [ ] テストグループへ配信して動作確認完了
- [ ] Self Service に公開(任意)

-----

## 付録 F: 開発フロー(運用後)

バージョン更新の流れ:

1. 🪟 VS 2022 でコード修正 → F5 で動作確認
1. 🪟 `build-macapp.sh` の `VERSION` を上げる(VS 2022 から直接編集可)
1. 🪟 git commit & push
1. 🍎 Mac で `git pull && ./build-macapp.sh`
1. 🌐 Jamf Pro に新 PKG をアップロード、ポリシーで配信

-----

## 付録 G: Avalonia 11 コーディング規約メモ

WPF からの移植や記事を参考にすると、Avalonia では使えないパターンを書いてしまうことがある。本プロジェクトで踏襲している Avalonia 11 公式パターンを以下にまとめる(リファクタリングや機能追加の際の参考)。

### G-1. コードビハインド: 手書き `InitializeComponent` を書かない

❌ WPF / Avalonia 0.10 流(NG):

```csharp
public class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```

✅ Avalonia 11 流:

```csharp
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();   // source generator が自動生成
    }
    // 手書きの InitializeComponent は書かない
}
```

- クラスを `partial` にする
- コンストラクタ内の `InitializeComponent()` 呼び出しは残す
- メソッド本体は Avalonia の source generator が自動生成する
- `x:Name="..."` のフィールドも自動生成される(`using Avalonia.Markup.Xaml;` 不要)

### G-2. XAML 名前空間

|用途  |Avalonia                                                |(WPF 参考)                                                   |
|----|--------------------------------------------------------|-----------------------------------------------------------|
|ルート |`xmlns="https://github.com/avaloniaui"`                 |`http://schemas.microsoft.com/winfx/2006/xaml/presentation`|
|`x:`|`xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"`|同じ                                                         |
|自前NS|`xmlns:vm="using:MyApp.ViewModels"`                     |`clr-namespace:MyApp.ViewModels`                           |

### G-3. コンパイル済みバインディング

csproj に `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>` を入れることで、すべての `{Binding}` がコンパイル済みになる。代わりに `x:DataType` を必ず指定する:

```xml
<Window x:DataType="vm:MainWindowViewModel">      <!-- 親側で指定 -->
  ...
  <ComboBox.ItemTemplate>
    <DataTemplate x:DataType="models:LocalUser"> <!-- テンプレート内は別途 -->
      <TextBlock Text="{Binding Username}" />
    </DataTemplate>
  </ComboBox.ItemTemplate>
</Window>
```

### G-4. プロパティ・コントロール対応表

|やりたいこと         |Avalonia                                    |WPF からの典型的なミス                                       |
|---------------|--------------------------------------------|----------------------------------------------------|
|可視性を bool で切り替え|`IsVisible="{Binding ParentNeedsPromotion}"`|`Visibility=` を使うと型不一致でエラー                          |
|逆条件バインド        |`{Binding !IsBusy}`                         |コンバータを書きがちだが不要                                      |
|アイテムリストのバインド   |`ItemsSource="{Binding Items}"`             |`Items=` は read-only(Avalonia 11 で破壊的変更)            |
|パスワード入力        |`<TextBox PasswordChar="●">`                |`PasswordBox` を探しがちだが Avalonia にはない                 |
|プレースホルダ        |`PlaceholderText` / `Watermark`             |WPF にないので自作する流儀が残っている                               |
|スタックの間隔        |`<StackPanel Spacing="10">`                 |WPF にないので個別 `Margin` で頑張りがち                         |
|UI スレッド        |`Dispatcher.UIThread.Post(...)`             |`Application.Current.Dispatcher.Invoke(...)` は WPF 流|

### G-5. スタイル定義は CSS 風セレクタ

```xml
<Window.Styles>
    <Style Selector="Border.card">     <!-- クラス "card" の Border を狙う -->
        <Setter Property="Background" Value="#F7F7F9" />
    </Style>
</Window.Styles>

<Border Classes="card">...</Border>
```

WPF の `<Style TargetType="Border" x:Key="card">` は Avalonia では動かない。スコープ付きスタイルは `<要素.Styles>` で書く(`Window.Resources` ではない)。

### G-6. ライフサイクル・エントリポイント

- `Program.cs`: `AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)`
- `App.axaml.cs`: `OnFrameworkInitializationCompleted()` で `MainWindow` を生成

WPF の `App.OnStartup` や `StartupUri` は Avalonia にはない。

### G-7. MVVM(CommunityToolkit.Mvvm)

- ViewModel クラスは `partial class` にする(source generator のため)
- `[ObservableProperty] private string _foo;` で自動生成 `Foo` プロパティ
- `[RelayCommand] private async Task BarAsync()` で自動生成 `BarCommand`
- `[NotifyCanExecuteChangedFor(nameof(BarCommand))]` で `CanBarExecute` の再評価を連動

Avalonia 11 + CommunityToolkit.Mvvm の組み合わせが現状の事実上の標準。

### 参照リンク

- 公式移行ガイド: <https://docs.avaloniaui.net/docs/stay-up-to-date/upgrade-from-0.10>
- MVVM パターン: <https://docs.avaloniaui.net/docs/how-to/mvvm-how-to/>
- TextBox: <https://docs.avaloniaui.net/docs/how-to/textbox-how-to>