# HUSH Alarm

広告・課金・常時通信なし。WindowsとiPhoneのイヤホン専用アラーム。

[使い方・無料のiPhone導入・実機確認](docs/使い方.md)

Windows: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-windows.ps1`

iOS: XcodeGenで `ios/project.yml` から生成し、Xcodeでビルド。無料の公開GitHub Actionsは未署名IPAを作成する。署名と端末へのインストールはユーザーのPCで行う。

このリリースは実機音声テスト前です。スピーカーへ切り替えない設計ですが、物理的な音漏れとバックグラウンド動作の最終確認は利用端末で行ってください。

設計上の判断は [DECISIONS.md](DECISIONS.md)。
