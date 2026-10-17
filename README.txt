LiveHenkan

ローマ字で打つそばから漢字かな交じりの文に変わる、Windows 用の日本語入力です。
かな入力のまま日本語と英語を続けて入力できます。変換・辞書・学習はこのパソコンの中で動きます。

必要なもの
  Windows 10 / 11（64-bit）、Windows PowerShell 5.1、.NET Framework 4.8。
  追加の EXE / DLL、管理者権限、使用中のネット接続は不要です。

使い方
  配布フォルダをすべて展開し、install.bat を実行します。
  デスクトップかフォルダーの何もないところを右クリック →「LiveHenkan」→「Turn on」で始めます。
  Windows 11 では「その他のオプションを確認」の中にあります。
  止めるときは「Turn off」。登録を外すときは uninstall.bat を実行します。
  フォルダを移したときは、もう一度 install.bat を実行してください。

主なキー
  変換 / 無変換        かな / 英数に切り替える
  Enter               表示のとおりに確定する
  Space / ↓           次の候補を選ぶ
  ← / →               変換を直す文節を選ぶ
  F6 / F10            選んだ語をひらがな / 英字に直す
  Ctrl + Shift + F12   緊急停止する

詳しいキー操作、単語登録、設定、制限は Portable-README.txt を参照してください。
配布フォルダでは同じ説明が README.txt に入っています。
メモ帳などでは変換中の文字を文書の中に表示し、Word・Excel・ブラウザーではカーソル位置に重ねて表示します。
ブラウザーのパスワード欄では、入力する前に無変換キーで英数に切り替えてください。

利用者のデータ
  user フォルダに登録した単語、学習、設定、開始・終了・エラーの記録を保存します。
  入力した文章をログに記録したり、外へ送信したりすることはありません。

ソースから配布フォルダを作る
  モデル xsmall.jinen を用意し、そのフォルダを -ModelDir に指定します。
  例（このフォルダで実行し、.\models をモデルのあるフォルダに置き換えてください）:
    powershell -NoProfile -ExecutionPolicy Bypass -File .\MakePortable.ps1 -ModelDir .\models
  dist\LiveHenkan に本体とモデルが揃います。このフォルダ全体をコピーして使います。
  -Destination で出力先を指定できます。作成済みの配布フォルダを作り直すときは -Replace を付けます。
  モデルは -ModelDir 引数 → JINEN_MODEL_DIR 環境変数 → 配布フォルダ直下の順に探します。

モデルなどのライセンスと出典は THIRD-PARTY.txt を参照してください。
