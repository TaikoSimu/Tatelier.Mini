using System;
using Tatelier.Score.Component.NoteSystem;
using static DxLibDLL.DX;

namespace Tatelier.Mini.Play
{
	/// <summary>
	/// 音符文字(#SENOTECHANGEで指定される「ドン」「カッ」等の文字)描画クラス
	/// </summary>
	/// <remarks>
	/// OpenTatelierのNoteText.csと違い、テーマ設定(hjsonでのフォルダ指定・
	/// 表示オフセット)は行わず、固定パスの画像を読み込むだけの簡易版。
	/// Tatelier Miniには設定画面・テーマ機構が無いため。
	/// </remarks>
	class NoteText : IDisposable
	{
		bool disposed = false;

		int handle = -1;

		int itemWidth = 0;
		int itemHeight = 0;

		/// <summary>
		/// 音符の中心から音符文字を表示する位置までのYオフセット(上が負)
		/// </summary>
		const float RelativePointCY = -30;

		public void Draw(float xf, float yf, NoteTextType noteTextType)
		{
			if (handle == -1 || noteTextType == NoteTextType.None)
			{
				return;
			}

			DrawRectRotaGraphFastF(xf, yf + RelativePointCY, 0, ((int)noteTextType) * itemHeight, itemWidth, itemHeight, 1.0F, 0.0F, handle, DX_TRUE);
		}

		void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (handle != -1)
				{
					DeleteGraph(handle);
				}
				disposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}

		~NoteText()
		{
			Dispose();
		}

		public NoteText(string filePath)
		{
			handle = LoadGraph(filePath);

			if (handle != -1)
			{
				GetGraphSize(handle, out var w, out var h);
				itemWidth = w;
				itemHeight = h / 9;
			}
		}
	}
}
