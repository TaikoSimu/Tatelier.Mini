using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Tatelier.Mini
{
	/// <summary>
	/// TJAファイルを安全に読み込むためのヘルパー。
	///
	/// Tatelier.Score.dll側のTJAパーサーには、
	/// 「#コマンド行の途中(引数の有無や直前の空白の有無を問わず)に
	/// // コメントが続く」場合、コメント終端の改行がmeasureSBバッファに
	/// 残らず、コメント直前までの内容と次の行がそのまま連結されてしまう
	/// 既知の不具合がある(Score.cs の isIgnore 処理が "//" 検出後、
	/// 次の '\n' も含めて丸ごと読み飛ばしてしまうため)。
	///
	/// 例えば
	///   #DELAY 0.001 ////BLA
	///   #BPMCHANGE -120
	/// のような並びは、"#BPMCHANGE -120" 行が丸ごと直前の #DELAY コマンドの
	/// 引数の一部として飲み込まれてしまい、BPMCHANGEが一切適用されない
	/// (＝直前のBPM/MEASUREの組み合わせのまま音符送りが計算され、
	/// 極端な場合は再生時刻が巨大な値に吹き飛ぶ)という形で症状が出る。
	///
	/// 元のtjaファイル自体には一切手を加えず、読み込み直前にだけ
	/// 一時キャッシュへ改行を補ったコピーを作成し、そちらのパスを
	/// TJALoadInfo.FilePath に渡すことで回避する。
	/// これにより、譜面を再ダウンロードして元のtjaに戻っても
	/// 都度自動的に補正される。
	/// </summary>
	static class TJASafeLoader
	{
		// 例: "#GOGOSTART//42" や "#DELAY 0.001 ////BLA" のように、
		// #コマンド行の中に(直後・引数の後どちらでも) "//" が現れるパターンを
		// 同一行内で(改行を跨がずに)検出する。
		static readonly Regex DangerousCommentPattern = new Regex(@"(#[A-Za-z]+[^\r\n]*?)(//)", RegexOptions.Compiled);

		static readonly string CacheDirectory = Path.Combine(Path.GetTempPath(), "Tatelier.Mini", "TJASafeCache");

		/// <summary>
		/// 指定されたtjaファイルパスを検査し、パーサーが誤動作するパターンが
		/// 含まれていれば改行を補った一時ファイルのパスを返す。
		/// 問題が無ければ余計なコピーは作らず、元のパスをそのまま返す。
		/// </summary>
		/// <param name="originalPath">元のtjaファイルパス</param>
		/// <returns>安全に読み込めるファイルパス</returns>
		public static string GetSafeFilePath(string originalPath)
		{
			try
			{
				if (string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath))
				{
					return originalPath;
				}

				byte[] bytes = File.ReadAllBytes(originalPath);
				Encoding encoding = Tatelier.Score.Utility.GetCode(bytes) ?? Encoding.UTF8;
				string text = encoding.GetString(bytes);

				if (!DangerousCommentPattern.IsMatch(text))
				{
					// 危険なパターンが無ければ元のファイルをそのまま使う
					return originalPath;
				}

				Directory.CreateDirectory(CacheDirectory);
				string cachePath = Path.Combine(CacheDirectory, GetCacheFileName(originalPath));

				// 元ファイルが前回キャッシュ生成時から変更されていなければ使い回す
				if (File.Exists(cachePath)
					&& File.GetLastWriteTimeUtc(cachePath) >= File.GetLastWriteTimeUtc(originalPath))
				{
					return cachePath;
				}

				string fixedText = DangerousCommentPattern.Replace(text, "$1\r\n$2");
				File.WriteAllBytes(cachePath, encoding.GetBytes(fixedText));

				return cachePath;
			}
			catch
			{
				// 何か問題が起きても、必ず元のファイルへフォールバックする
				return originalPath;
			}
		}

		/// <summary>
		/// 日本語ファイル名や記号を含むパスでも安全に使えるよう、
		/// パスのハッシュ値をキャッシュファイル名にする。
		/// </summary>
		static string GetCacheFileName(string originalPath)
		{
			using (var md5 = MD5.Create())
			{
				byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(originalPath)));
				var sb = new StringBuilder(hash.Length * 2 + 4);
				foreach (byte b in hash)
				{
					sb.Append(b.ToString("x2"));
				}
				sb.Append(".tja");
				return sb.ToString();
			}
		}
	}
}
