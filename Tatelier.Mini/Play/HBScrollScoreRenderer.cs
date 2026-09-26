using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tatelier.DxLibDLL;
using Tatelier.Mini.Play;
using Tatelier.Score.Component.NoteSystem;
using Tatelier.Score.Play.Chart;
using static DxLibDLL.DX;

namespace Tatelier.Mini.Play
{
	/// <summary>
	/// HBSCROLL譜面描画の対象データ
	/// </summary>
	interface IHBScrollScoreRendererTarget
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
		/// 譜面描画開始X座標
		/// </summary>
		float StartDrawPointX { get; }

		/// <summary>
		/// 譜面描画終了X座標
		/// </summary>
		float FinishDrawPointX { get; }

		/// <summary>
		/// 演奏オプションスクロールスピード
		/// </summary>
		double PlayOptionScrollSpeed { get; }

		/// <summary>
		/// 音符文字(#SENOTECHANGE)描画
		/// </summary>
		NoteText NoteText { get; }
	}

	/// <summary>
	/// HBSCROLL譜面描画クラス
	/// </summary>
	/// <remarks>
	/// OpenTatelier本体のHBScrollScoreRenderer.csをそのまま移植したもの
	/// (見た目・挙動をOpenTatelierと完全に一致させるため)。
	/// 音符・小節線の座標計算は太鼓さん次郎(ver2.92)と同じ式(HBScrollDrawDataControl参照)。
	/// MainConfig.Singleton.Debugによるデバッグ用ID表示だけは、Miniに
	/// MainConfigクラス自体が存在しないため省いてある。
	/// </remarks>
	class HBScrollScoreRenderer
		: IScoreRenderer
	{
		IHBScrollScoreRendererTarget target;

		public bool IsNoteHide { get; set; }

		/// <summary>
		/// 指定時刻の音符等の画面X座標を求める
		/// </summary>
		float GetX(HBScrollDrawDataControl control, HBScrollCamera camera, int millisec, HBScrollDrawDataItem item, double scrollSpeed)
		{
			int distance = control.GetDistance(millisec, item, camera, control.GetScrollSpeed(scrollSpeed) * target.PlayOptionScrollSpeed);
			return target.JudgeFramePoint.CX + distance;
		}

		static float Clamp(float value, float min, float max)
		{
			return Math.Max(min, Math.Min(max, value));
		}

		void IScoreRenderer.DrawNoteBranchScore(BranchScore bscore, int nowTime)
		{
			var control = bscore.HBScrollDrawDataControl;
			if (control.ItemList.Count == 0)
			{
				return;
			}

			var camera = control.GetCamera(nowTime);

			// レイヤー層
			foreach (var layer in bscore.NoteList)
			{
				// セクション層(後ろから)
				foreach (var section in layer.Reverse())
				{
					// 音符層
					foreach (var note in section.Reverse())
					{
						if (!note.Visible)
						{
							continue;
						}

						float y = target.JudgeFramePoint.CY;

						switch (note.NoteType)
						{
							case NoteType.Don:
							case NoteType.Kat:
							case NoteType.DonBig:
							case NoteType.KatBig:
							case NoteType.Roll:
							case NoteType.RollBig:
								{
									int handle = target.NoteImageControl.GetImageHandle(note.NoteType);

									float x = GetX(control, camera, note.HBScrollMillisec, note.HBScrollDrawDataItem, note.ScrollSpeedInfo.Value);

									if (target.FinishDrawPointX < x && x < target.StartDrawPointX)
									{
										if (!IsNoteHide)
										{
											DrawRotaGraphFastF(x, y, NoteImageControl.GetScale(note.NoteType), 0.0F, handle, DX_TRUE);
										}
										using (DrawAreaGuard.Create())
										{
											target.NoteText.Draw(x, y, note.NoteTextType);
										}
									}
								}
								break;
							case NoteType.Balloon:
								{
									int handle = target.NoteImageControl.GetImageHandle(note.NoteType);
									float x;
									if (note.StartMillisec <= nowTime
										&& nowTime < note.FinishMillisec)
									{
										// 風船を叩いている間は判定枠に留まる
										x = target.JudgeFramePoint.CX;
									}
									else if (note.FinishMillisec <= nowTime)
									{
										x = GetX(control, camera, note.HBScrollMillisec + (note.FinishMillisec - note.StartMillisec), null, note.ScrollSpeedInfo.Value);
									}
									else
									{
										x = GetX(control, camera, note.HBScrollMillisec, note.HBScrollDrawDataItem, note.ScrollSpeedInfo.Value);
									}

									if (target.FinishDrawPointX < x && x < target.StartDrawPointX)
									{
										if (!IsNoteHide)
										{
											float scale = NoteImageControl.GetScale(note.NoteType);

											// 表(丸)・裏(尾)の境界がバイリニア補間で滲んで隙間や線に
											// 見えないよう、連打胴体の描画と同様にニアレストネイバーで
											// 描画する。
											using (DrawModeGuard.Create())
											{
												SetDrawMode(DX_DRAWMODE_NEAREST);

												DrawRotaGraphFastF(x, y, scale, 0.0F, handle, DX_TRUE);

												// 風船の「うしろ(尾)」を表(丸)のすぐ後方(スクロール方向の
												// 後ろ)に並べて描画する。notes.pngでは風船の絵が48px1セルに
												// 収まらず表(丸)・裏(尾)の2セルに分かれているため、隣接させ
												// ないと元の1枚絵にならず尾が描画されない。
												int backHandle = target.NoteImageControl.GetEndNoteImageHandle(note.NoteType);
												if (backHandle != -1)
												{
													DrawRotaGraphFastF(x + NoteImageControl.GetScaledCellWidth(note.NoteType), y, scale, 0.0F, backHandle, DX_TRUE);
												}
											}
										}

										using (DrawAreaGuard.Create())
										{
											target.NoteText.Draw(x, y, note.NoteTextType);
										}
									}
								}
								break;
							case NoteType.End:
								{
									// 前回音符によって処理を変える
									switch (note.PrevNote.NoteType)
									{
										case NoteType.Roll:
										case NoteType.RollBig:
											{
												int handle = target.NoteImageControl.GetContentNoteImageHandle(note.PrevNote.NoteType);
												GetGraphSizeF(handle, out float w, out float h);
												float rollScale = NoteImageControl.GetScale(note.PrevNote.NoteType);

												float hHalf = h * rollScale / 2;

												// 連打の頭・終端とも、他の音符と同じ式で求める
												float prevX = GetX(control, camera, note.PrevNote.HBScrollMillisec, note.PrevNote.HBScrollDrawDataItem, note.PrevNote.ScrollSpeedInfo.Value);
												float x = GetX(control, camera, note.HBScrollMillisec, note.HBScrollDrawDataItem, note.ScrollSpeedInfo.Value);

												// 太鼓さん次郎と同じく、胴体は連打の頭の#SCROLLの向きに沿って
												// (正なら頭→終端、0以下なら終端→頭へ)並べて描くため、
												// 頭と終端の左右がその向きと逆転している場合は描画しない
												// (例: 頭と終端で#SCROLLの符号が異なる連打)。
												double headScrollSpeed = control.GetScrollSpeed(note.PrevNote.ScrollSpeedInfo.Value) * target.PlayOptionScrollSpeed;
												bool isOrdered = headScrollSpeed > 0 ? prevX < x : x < prevX;

												float rollLeft = Math.Min(prevX, x);
												float rollRight = Math.Max(prevX, x);
												if (isOrdered && target.FinishDrawPointX < rollRight && rollLeft < target.StartDrawPointX)
												{
													if (!IsNoteHide)
													{
														// 胴体(硬いエッジ)とキャップ(バイリニアで滲む)の補間方式が
														// 揃っていないと、同じ色のはずの境界がわずかに滲んで継ぎ目の
														// 線のように見えてしまうため、両方ともニアレストネイバーで描画する。
														using (DrawModeGuard.Create())
														{
															SetDrawMode(DX_DRAWMODE_NEAREST);

															// 高速なBPM区間では胴体が画面の何倍もの長さになることがあるため、
															// 見た目が変わらない範囲(描画範囲の少し外側)で座標を切り詰める
															float bodyStartX = Clamp(prevX, target.FinishDrawPointX - 100, target.StartDrawPointX + 100);
															float bodyEndX = Clamp(x, target.FinishDrawPointX - 100, target.StartDrawPointX + 100);
															DrawModiGraphF(bodyStartX - 1, y - hHalf, bodyEndX + 1, y - hHalf, bodyEndX + 1, y + hHalf, bodyStartX - 1, y + hHalf, handle, DX_TRUE);
															DrawRotaGraphFastF(x, y, rollScale, 0.0F, target.NoteImageControl.GetEndNoteImageHandle(note.PrevNote.NoteType), DX_TRUE, control.GetScrollSpeed(note.ScrollSpeedInfo.Value) < 0 ? 1 : 0);
														}
													}
												}
											}
											break;
									}
								}
								break;
						}
					}
				}
			}
		}

		void IScoreRenderer.DrawMeasureBranchScore(BranchScore bscore, int nowTime)
		{
			var control = bscore.HBScrollDrawDataControl;
			if (control.ItemList.Count == 0)
			{
				return;
			}

			var camera = control.GetCamera(nowTime);
			float y = target.JudgeFramePoint.CY;

			foreach (var item in bscore.Measures)
			{
				if (item.Visible)
				{
					float x = GetX(control, camera, item.HBScrollMillisec, item.HBScrollDrawDataItem, item.ScrollSpeedInfo.Value);

					if (target.FinishDrawPointX < x && x < target.StartDrawPointX)
					{
						DrawRotaGraphFastF(x, y, 1.0F, 0.0F, target.BarLineImageControl.GetHandle(item.MeasureLineType), DX_TRUE);
					}
				}
			}
		}

		public HBScrollScoreRenderer(IHBScrollScoreRendererTarget target)
		{
			this.target = target;
		}
	}
}
