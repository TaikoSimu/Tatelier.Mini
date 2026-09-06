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
	/// MainConfig.Singleton.Debugによるデバッグ用ID表示だけは、Miniに
	/// MainConfigクラス自体が存在しないため省いてある。
	/// </remarks>
	class HBScrollScoreRenderer
		: IScoreRenderer
	{
		IHBScrollScoreRendererTarget target;

		public bool IsNoteHide { get; set; }

		HBScrollDrawDataItem[] b = new HBScrollDrawDataItem[2];

		void a(IReadOnlyList<HBScrollDrawDataItem> itemList, int nowTime, HBScrollDrawDataItem[] result)
		{
			for (int i = 0; i < b.Length; i++)
			{
				b[i] = null;
			}

			int index = 0;
			for (int i = itemList.Count - 1; i >= 0; i--)
			{
				var v = itemList[i];
				if (index == 0)
				{
					if (v.StartMillisec <= nowTime
						&& nowTime < v.FinishMillisec)
					{
						result[index] = v;
						index++;
						if (index >= result.Length)
						{
							return;
						}
					}
				}
				else
				{
					if (index >= result.Length)
					{
						return;
					}
				}
			}
		}
		void IScoreRenderer.DrawNoteBranchScore(BranchScore bscore, int nowTime)
		{
			var firstDrawDataItem = bscore.HBScrollDrawDataControl.ItemList.LastOrDefault(
				v => v.IsApplicable(nowTime));

			if (firstDrawDataItem == null
				&& nowTime < 0)
			{
				firstDrawDataItem = bscore.HBScrollDrawDataControl.ItemList.LastOrDefault(
				v => v.IsApplicable(0));
			}

			System.Diagnostics.Trace.WriteLine($"{(firstDrawDataItem != null ? $"Start:{firstDrawDataItem.StartMillisec}, Finish:{firstDrawDataItem.FinishMillisec}" : "null")}");

			double per = 0;

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
							var reFirstDrawDataItem = firstDrawDataItem;

							// 音符がfirstDrawDataItemの時間範囲内の場合は、その音符の時間範囲内のデータで再構築する
							if ((reFirstDrawDataItem == null)
								|| (!reFirstDrawDataItem.IsDelay && reFirstDrawDataItem.IsApplicable(note)))
							{
								reFirstDrawDataItem = note.HBScrollDrawDataItem;
								//reFirstDrawDataItem = bscore.HBScrollDrawDataControl.ItemList.LastOrDefault(
								//	v => v.StartMillisec <= note.StartMillisec
								//	&& note.FinishMillisec < v.EndMillisec);

								if (reFirstDrawDataItem == null)
								{
									continue;
								}
							}

							per = reFirstDrawDataItem.GetElapsedRate(nowTime);
							int diffTime = note.StartMillisec - nowTime;
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

										float x;

										if (diffTime < 0
											&& !reFirstDrawDataItem.IsDelay)
										{
											x = target.JudgeFramePoint.CX + (diffTime * note.MovementPerMillisec);
										}
										else
										{
											double hbscrollPivotX = reFirstDrawDataItem.GetHBScrollPivotX(per);
											x = (float)(target.JudgeFramePoint.CX + (note.HBScrollStartPointX - hbscrollPivotX) * note.ScrollSpeedInfo.Value * target.PlayOptionScrollSpeed);
										}

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
										if (diffTime < 0
											&& !reFirstDrawDataItem.IsDelay)
										{
											var finishDiffMillisec = (note.FinishMillisec - nowTime);
											x = finishDiffMillisec < 0 ? target.JudgeFramePoint.CX + (finishDiffMillisec * note.MovementPerMillisec) : target.JudgeFramePoint.CX;
										}
										else
										{
											double hbscrollPivotX = reFirstDrawDataItem.GetHBScrollPivotX(per);
											x = (float)(target.JudgeFramePoint.CX + (note.HBScrollStartPointX - hbscrollPivotX) * note.ScrollSpeedInfo.Value * target.PlayOptionScrollSpeed);
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
													float x;
													float prevT;
													float prevX;

													if (diffTime < 0
														&& !reFirstDrawDataItem.IsDelay)
													{
														x = target.JudgeFramePoint.CX + (diffTime * note.MovementPerMillisec);

														prevT = (note.PrevNote.StartMillisec - nowTime);
														prevX = target.JudgeFramePoint.CX + (prevT * note.PrevNote.MovementPerMillisec);
													}
													else
													{
														double hbscrollPivotX = reFirstDrawDataItem.GetHBScrollPivotX(per);
														x = (float)(target.JudgeFramePoint.CX + (note.HBScrollStartPointX - hbscrollPivotX) * note.ScrollSpeedInfo.Value * target.PlayOptionScrollSpeed);

														prevT = (note.PrevNote.StartMillisec - nowTime);
														if (prevT < 0)
														{
															prevX = target.JudgeFramePoint.CX + (prevT * note.PrevNote.MovementPerMillisec);
														}
														else
														{
															prevX = (float)(target.JudgeFramePoint.CX + (note.PrevNote.HBScrollStartPointX - hbscrollPivotX) * note.PrevNote.ScrollSpeedInfo.Value * target.PlayOptionScrollSpeed);
														}
													}
													// 連打の胴体幅が不自然に狭い(ほぼ0px)、または救済しても
													// 中途半端に大きすぎる場合の救済・描画可否判定(Issue #18)。
													// 詳細はHBScrollIssue18Fix.csのコメント参照。
													x = HBScrollIssue18Fix.RescueRollEndX(
														prevX, x,
														note.PrevNote.HBScrollDrawDataItem,
														note.StartMillisec - note.PrevNote.StartMillisec,
														note.PrevNote.ScrollSpeedInfo.Value,
														target.PlayOptionScrollSpeed);

													if (HBScrollIssue18Fix.ShouldDrawRollBody(prevX, x, target.FinishDrawPointX, target.StartDrawPointX))
													{
														if (!IsNoteHide)
														{
															// 胴体(硬いエッジ)とキャップ(バイリニアで滲む)の補間方式が
															// 揃っていないと、同じ色のはずの境界がわずかに滲んで継ぎ目の
															// 線のように見えてしまうため、両方ともニアレストネイバーで描画する。
															using (DrawModeGuard.Create())
															{
																SetDrawMode(DX_DRAWMODE_NEAREST);

																DrawModiGraphF(prevX - 1, y - hHalf, x + 1, y - hHalf, x + 1, y + hHalf, prevX - 1, y + hHalf, handle, DX_TRUE);
																DrawRotaGraphFastF(x, y, rollScale, 0.0F, target.NoteImageControl.GetEndNoteImageHandle(note.PrevNote.NoteType), DX_TRUE, note.ScrollSpeedInfo.Value < 0 ? 1 : 0);
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
		}

		void IScoreRenderer.DrawMeasureBranchScore(BranchScore bscore, int nowTime)
		{
			double per = 0;
			float x;
			float y = target.JudgeFramePoint.CY;

			var firstDrawDataItem = bscore.HBScrollDrawDataControl.ItemList.LastOrDefault(v => v.IsApplicable(nowTime));

			if (firstDrawDataItem == null
				&& nowTime < 0)
			{
				firstDrawDataItem = bscore.HBScrollDrawDataControl.ItemList.LastOrDefault(v => v.IsApplicable(0));
			}

			int diffTime;

			// レイヤー層
			foreach (var item in bscore.Measures)
			{
				if (item.Visible)
				{
					diffTime = item.StartMillisec - nowTime;

					var reFirstDrawDataItem = firstDrawDataItem;

					// 音符がfirstDrawDataItemの時間範囲内の場合は、その音符の時間範囲内のデータで再構築する
					if ((reFirstDrawDataItem == null)
						|| (!reFirstDrawDataItem.IsDelay && reFirstDrawDataItem.IsApplicable(item)))
					{
						reFirstDrawDataItem = item.HBScrollDrawDataItem;

						if (reFirstDrawDataItem == null)
						{
							continue;
						}
					}

					per = reFirstDrawDataItem.GetElapsedRate(nowTime);

					if (diffTime < 0
						&& !reFirstDrawDataItem.IsDelay)
					{
						x = target.JudgeFramePoint.CX + (diffTime * item.MovementPerMillisec);
					}
					else
					{
						double hbscrollPivotX = reFirstDrawDataItem.GetHBScrollPivotX(per);
						x = (float)(target.JudgeFramePoint.CX + (item.HBScrollStartPointX - hbscrollPivotX) * item.ScrollSpeedInfo.Value * target.PlayOptionScrollSpeed);
					}

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
