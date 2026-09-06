using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tatelier.DxLibDLL;
using Tatelier.Score.Component.NoteSystem;
using Tatelier.Score.Play.Chart;
using static DxLibDLL.DX;

namespace Tatelier.Mini.Play
{
	public interface IJudgeFramePoint
	{
		float CX { get; }
		float CY { get; }
	}
	/// <summary>
	/// 標準譜面描画の対象データ
	/// </summary>
	interface INormalScoreRendererTarget
	{
		/// <summary>
		/// 判定枠座標情報
		/// </summary>
		IJudgeFramePoint JudgeFramePoint { get; }

		/// <summary>
		/// 音符画像管理
		/// </summary>
		NoteImageControl NoteImageControl { get; }

		/// <summary>
		/// 小節線画像管理
		/// </summary>
		MeasureLineImageControl BarLineImageControl { get; }

		/// <summary>
		/// 音符文字(#SENOTECHANGE)描画
		/// </summary>
		NoteText NoteText { get; }
	}
	internal interface IScoreRenderer
	{
		/// <summary>
		/// 小節線描画処理
		/// </summary>
		/// <param name="bscore">譜面</param>
		/// <param name="nowMillisec">現在時間(ms)</param>
		void DrawMeasureBranchScore(BranchScore bscore, int nowMillisec);

		/// <summary>
		/// 音符描画クラス
		/// </summary>
		/// <param name="bscore">譜面</param>
		/// <param name="nowMillisec">現在時刻(ms)</param>
		void DrawNoteBranchScore(BranchScore bscore, int nowMillisec);
	}

	/// <summary>
	/// 標準譜面描画処理クラス
	/// </summary>
	class NormalScoreRenderer : IScoreRenderer

	{
		readonly INormalScoreRendererTarget module;

		public bool IsNoteHide { get; set; }

		void DrawNormalNote(INote note, int nowMillisec)
		{
			if (note.StartDrawMillisec <= nowMillisec
				&& nowMillisec < note.FinishDrawMillisec)
			{
				int diffMillisec = note.StartMillisec - nowMillisec;
				int handle = module.NoteImageControl.GetImageHandle(note.NoteType);
				float x = module.JudgeFramePoint.CX + (diffMillisec * note.MovementPerMillisec);
				float y = module.JudgeFramePoint.CY;

				if (!IsNoteHide)
				{
					DrawRotaGraphFastF(x, y, NoteImageControl.GetScale(note.NoteType), 0.0F, handle, DX_TRUE);
				}

				using (DrawAreaGuard.Create())
				{
					module.NoteText.Draw(x, y, note.NoteTextType);
				}
			}
		}

		void DrawBalloonNote(INote note, int nowMillisec)
		{
			if (note.StartDrawMillisec <= nowMillisec
				&& nowMillisec < note.FinishDrawMillisec)
			{
				int diffMillisec = note.StartMillisec - nowMillisec;

				int handle = module.NoteImageControl.GetImageHandle(note.NoteType);

				float x;
				float y = module.JudgeFramePoint.CY;

				if (diffMillisec < 0)
				{
					var finishDiffMillisec = (note.FinishMillisec - nowMillisec);
					x = finishDiffMillisec < 0 ? module.JudgeFramePoint.CX + (finishDiffMillisec * note.MovementPerMillisec) : module.JudgeFramePoint.CX;
				}
				else
				{
					x = module.JudgeFramePoint.CX + (diffMillisec * note.MovementPerMillisec);
				}

				if (!IsNoteHide)
				{
					float scale = NoteImageControl.GetScale(note.NoteType);

					// 表(丸)・裏(尾)の境界がバイリニア補間で滲んで隙間や線に見えないよう、
					// 連打胴体の描画と同様にニアレストネイバーで描画する。
					using (DrawModeGuard.Create())
					{
						SetDrawMode(DX_DRAWMODE_NEAREST);

						DrawRotaGraphFastF(x, y, scale, 0.0F, handle, DX_TRUE);

						// 風船の「うしろ(尾)」を表(丸)のすぐ後方(スクロール方向の後ろ)に並べて描画する。
						// notes.pngでは風船の絵が48px1セルに収まらず表(丸)・裏(尾)の2セルに
						// 分かれているため、隣接させないと元の1枚絵にならず尾が描画されない。
						int backHandle = module.NoteImageControl.GetEndNoteImageHandle(note.NoteType);
						if (backHandle != -1)
						{
							DrawRotaGraphFastF(x + NoteImageControl.GetScaledCellWidth(note.NoteType), y, scale, 0.0F, backHandle, DX_TRUE);
						}
					}
				}

				using (DrawAreaGuard.Create())
				{
					module.NoteText.Draw(x, y, note.NoteTextType);
				}
			}
		}

		void DrawRollNote(INote note, int nowMillisec)
		{
			var prevNote = note.PrevNote;

			// 前回音符によって処理を変える
			switch (prevNote.NoteType)
			{
				case NoteType.Roll:
				case NoteType.RollBig:
					{
						// 連打中身の描画
						if (prevNote.StartDrawMillisec <= nowMillisec
							&& nowMillisec < prevNote.FinishDrawMillisec)
						{
							int diffMillisec = note.StartMillisec - nowMillisec;

							int handle = module.NoteImageControl.GetContentNoteImageHandle(prevNote.NoteType);
							GetGraphSizeF(handle, out float w, out float h);
							float rollScale = NoteImageControl.GetScale(prevNote.NoteType);

							float hHalf = h * rollScale / 2;
							float x = module.JudgeFramePoint.CX + (diffMillisec * note.MovementPerMillisec);
							float y = module.JudgeFramePoint.CY;

							int prevDiffMillisec = (prevNote.StartMillisec - nowMillisec);
							float prevX = module.JudgeFramePoint.CX + (prevDiffMillisec * prevNote.MovementPerMillisec);

							using (DrawModeGuard.Create())
							{
								SetDrawMode(DX_DRAWMODE_NEAREST);

								if (!IsNoteHide)
								{
									DrawModiGraphF(prevX - 1, y - hHalf, x + 1, y - hHalf, x + 1, y + hHalf, prevX - 1, y + hHalf, handle, DX_TRUE);

									// 終端キャップも胴体と同じくニアレストネイバーで描画する。
									// 胴体(硬いエッジ)とキャップ(バイリニアで滲む)の補間方式が
									// 揃っていないと、同じ色のはずの境界がわずかに滲んで
									// 継ぎ目の線のように見えてしまう。
									if (note.StartDrawMillisec <= nowMillisec
										&& nowMillisec < note.FinishDrawMillisec)
									{
										DrawRotaGraphFastF(x, y, rollScale, 0.0F, module.NoteImageControl.GetEndNoteImageHandle(prevNote.NoteType), DX_TRUE, note.ScrollSpeedInfo.Value < 0 ? 1 : 0);
									}
								}
							}
						}
					}
					break;
			}
		}


		void IScoreRenderer.DrawMeasureBranchScore(BranchScore bscore, int nowTime)
		{
			// 小節線処理・描画
			foreach (var item in bscore.Measures.Reverse<IMeasureLine>().Where(v => v.Visible))
			{
				float x = module.JudgeFramePoint.CX + ((item.StartMillisec - nowTime) * item.MovementPerMillisec);
				float y = module.JudgeFramePoint.CY;

				//DrawRotaGraphFastF(x, y, 1.0F, 0.0F, module.BarLineImageControl.GetHandle(item.MeasureLineType), DX_TRUE);
				//DrawMeasureIdForDebug(x, y, item);
			}
		}

		void IScoreRenderer.DrawNoteBranchScore(BranchScore bscore, int nowTime)
		{
			// レイヤー層
			foreach (var layer in bscore.NoteList)
			{
				// セクション層(後ろから)
				foreach (var section in layer.Reverse())
				{
					// 音符層
					foreach (var note in section.Reverse())
					{
						if (note.Visible)
						{
							switch (note.NoteType)
							{
								case NoteType.Don:
								case NoteType.Kat:
								case NoteType.DonBig:
								case NoteType.KatBig:
								case NoteType.Roll:
								case NoteType.RollBig:
									DrawNormalNote(note, nowTime);
									break;
								case NoteType.Balloon:
									DrawBalloonNote(note, nowTime);
									break;
								case NoteType.End:
									DrawRollNote(note, nowTime);
									break;
							}
						}
					}
				}
			}
		}

		public NormalScoreRenderer(INormalScoreRendererTarget module)
		{
			this.module = module;
		}
	}
}
