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
	/// 「#コマンドの直後に空白・改行を挟まず // コメントが続く」場合、
	/// コメント終端の改行がバッファに残らず、後続の音符データが
	/// まるごとコマンドの一部として消えてしまう(＝その小節が
	/// 音符ゼロの休み小節として扱われる)既知の不具合がある。
	///
	/// 元のtjaファイル自体には一切手を加えず、読み込み直前にだけ
	/// 一時キャッシュへ改行を補ったコピーを作成し、そちらのパスを
	/// TJALoadInfo.FilePath に渡すことで回避する。
	/// これにより、譜面を再ダウンロードして元のtjaに戻っても
	/// 都度自動的に補正される。
	/// </summary>
	static class TJASafeLoader
	{
		// 例: "#GOGOSTART//42" のように、#コマンドの直後に空白なしで "//" が続くパターン
		static readonly Regex DangerousCommentPattern = new Regex(@"(#[A-Za-z]+)(//)", RegexOptions.Compiled);

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
