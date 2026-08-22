using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HjsonEx;

namespace Tatelier.Common.SongSelect
{
	[DebuggerDisplay("Name = {Name}, Count = {ItemList.Count}")]
	public class Category
		: IItem
	{
		public SortedDictionary<string, List<IItem>> Items = new SortedDictionary<string, List<IItem>>();

		public IList<IItem> ItemList
		{
			get
			{
				var list = new List<IItem>();

				foreach (var item in Items)
				{
					list.AddRange(item.Value);
				}

				return list;
			}
		}

		public bool IsOpen = false;

		public string Name { get; set; }

		public string Detail { get; set; }

		public string ImageFolder { get; set; }

		public SongSelectItemType Type => SongSelectItemType.Category;

		public void Add(IItem item, string sortText)
		{
			string[] renewSortTextSplit = new string[3];

			var split = sortText.Split('-');

			if (split.Length > 3)
			{
				// 4つ以上に分割された場合、4つ目以降はソートキーとして使われず切り捨てられる。
				// 開発中に気づけるよう出力しておく(呼び出し側の想定外の入力の可能性がある)。
				Debug.WriteLine($"Category.Add: sortText \"{sortText}\" は3つを超える要素に分割されました。4つ目以降は無視されます。");
			}

			for (int i = 0; i < 3; i++)
			{
				// 以前はここが「i < sortText.Length」(分割前の文字列全体の長さ)になっており、
				// splitの要素数が3未満の場合に存在しないインデックスへアクセスして例外になりうる
				// 不具合があった。分割後の要素数(split.Length)で判定するよう修正。
				if (i < split.Length)
				{
					renewSortTextSplit[i] = split[i].PadLeft(5, '0');
				}
				else
				{
					renewSortTextSplit[i] = "ZZZZZ";
				}
			}

			string renewSortText = string.Join("-", renewSortTextSplit);

			if (!Items.ContainsKey(renewSortText))
			{
				Items[renewSortText] = new List<IItem>();
			}
			Items[renewSortText].Add(item);
		}

		public Category()
		{

		}

		public Category(Hjson.JsonValue json)
		{
			Name = json.EQs("Name") ?? "";
			Detail = json.EQs("Detail") ?? "";
			ImageFolder = json.EQs("ImageFolder");
		}
	}
}