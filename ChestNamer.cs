using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Group = Terraria.ID.ContentSamples.CreativeHelper.ItemGroup;

namespace ChestTool
{
	/// <summary>
	/// 箱子自动命名：根据箱内物品推断一个中文分类名。
	///
	/// 分类优先使用游戏自身的物品分组数据
	/// （<see cref="ContentSamples.CreativeHelper.GetItemGroup"/> / ContentSamples.ItemCreativeSortingId），
	/// 这样版本更新后新增物品也能自动归类；随后再针对
	/// 矿石（肉前/肉后）、材料（肉前/肉后）、箱子、墓碑等游戏没有单独列出的分类做补充判断。
	/// </summary>
	internal static class ChestNamer
	{
		// 肉后矿石对应的物块 id（钴/钯/秘银/山铜/精金/钛金/叶绿）
		private static readonly HashSet<int> HardmodeOreTiles = new HashSet<int>
		{
			107, 221, 108, 222, 111, 223, 211,
		};

		// 肉后矿锭（特殊锭，矿石外）
		private static readonly HashSet<int> HardmodeBars = new HashSet<int>
		{
			381, 382, 391, 1006, 1184, 1191, 1198, 1225, 1552, 3261, 3467,
		};

		// 类似矿石但不在 TileID.Sets.Ore 里的资源（手动归到肉前矿/肉后矿）
		private static readonly HashSet<int> OreLikeItems = new HashSet<int>
		{
			29,   // 生命水晶
			1922, // 煤炭
		};

		// 游戏分组没有归到 Boss/事件召唤里的召唤物
		private static readonly HashSet<int> BossSummonExtras = new HashSet<int>
		{
			4271, // 血泪（血月）
		};

		// 肉后常见材料（无法在肉前正常获得）
		private static readonly HashSet<int> HardmodeMaterials = new HashSet<int>
		{
			501,  // 妖精尘
			502,  // 水晶碎块
			519,  // 诅咒焰
			520,  // 光明之魂
			521,  // 暗影之魂
			522,  // 诅咒焰（武器材料）
			526,  // 独角兽角
			547,  // 恐惧之魂
			548,  // 力量之魂
			549,  // 视域之魂
			575,  // 飞翔之魂
			1006, // 叶绿锭
			1225, // 神圣锭
			1328, // 海龟壳
			1332, // 脓血
			1508, // 灵气
			1552, // 蘑菇锭
			1570, // 断裂英雄剑
			2161, // 寒霜核
			2218, // 甲虫壳
			3261, // 幽灵锭
			3456, // 星云碎片
			3457, // 日耀碎片
			3458, // 星尘碎片
			3459, // 漩涡碎片
			3460, // 夜明矿
			3467, // 夜明锭
		};

		/// <summary>
		/// 对单个物品分类。无法分类时返回 null。
		/// </summary>
		public static string Classify(int type)
		{
			if (type <= 0 || type >= ItemID.Count)
			{
				return null;
			}

			Item item = GetSample(type);
			if (item == null)
			{
				return null;
			}

			// 换声道具（游戏分组里没有单列）
			if (ItemID.Sets.IsAVoiceChangeItem[type])
			{
				return "叫声";
			}

			// 小动物（部分可放置的小动物会被游戏归到家具，这里提前判断）
			if (item.makeNPC > 0 && item.makeNPC < NPCID.Count && NPCID.Sets.CountsAsCritter[item.makeNPC])
			{
				return "小动物";
			}

			// 食物（部分食物带的是醉酒等 buff，游戏分组会归到药水）
			if (ItemID.Sets.IsFood[type])
			{
				return "吃的";
			}

			// 工具（钓鱼得到的镐等会被游戏归到鱼类，这里提前判断）
			if (item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.tileWand >= 0)
			{
				return "工具";
			}

			// 矿石 / 矿锭（先于物块、家具判断）
			if (IsOreOrBar(item))
			{
				return IsHardmodeOre(item) ? "肉后矿" : "肉前矿";
			}

			// boss/事件召唤物（游戏清单没有覆盖到的）
			if (BossSummonExtras.Contains(type))
			{
				return "boss召唤";
			}

			// 箱子
			if (item.createTile >= 0
				&& (TileID.Sets.BasicChest[item.createTile] || TileID.Sets.BasicChestFake[item.createTile]))
			{
				return "箱子";
			}

			// 墓碑
			if (item.createTile == TileID.Tombstones)
			{
				return "墓碑";
			}

			// 电路（开关、机关、促动器等游戏分组未单列的机械物品）
			if (item.mech || ItemID.Sets.SortingPriorityWiring[type] != -1)
			{
				return "电路";
			}

			switch (GetGroup(item))
			{
				case Group.MeleeWeapon:
					return "近战";
				case Group.RangedWeapon:
					return "远程";
				case Group.MagicWeapon:
					return "魔法";
				case Group.SummonWeapon:
					return "召唤";

				case Group.ConsumableThatDamages:
				case Group.Bombs:
					return BulkWeapon(item);

				case Group.Ammo:
					return "弹药";

				case Group.LifePotions:
				case Group.ManaPotions:
				case Group.BuffPotion:
				case Group.Flask:
					return "药水";

				case Group.Food:
					return "吃的";

				case Group.Headgear:
				case Group.Torso:
				case Group.Pants:
					return item.vanity ? "时装" : "盔甲";

				case Group.Accessories:
				case Group.Hook:
					return item.vanity ? "时装" : "饰品";

				case Group.Mount:
				case Group.Minecart:
					return "坐骑";

				case Group.VanityPet:
				case Group.LightPet:
					return "宠物";

				case Group.BossItem:
				case Group.EventItem:
				case Group.BossSpawners:
					return "boss召唤";

				case Group.Critters:
					return "小动物";

				case Group.FishingQuestFish:
				case Group.Fish:
				case Group.FishingBait:
				case Group.Crates:
					return "材料鱼";

				case Group.AlchemyPlants:
				case Group.AlchemySeeds:
					return "草药";

				case Group.Wiring:
					return "电路";

				case Group.Dye:
				case Group.HairDye:
				case Group.DyeMaterial:
					return "染料";

				case Group.Paint:
					// 画作属于家具，油漆/涂料属于染料
					return item.PaintOrCoating ? "染料" : "家具";

				case Group.CraftingObjects:
				case Group.Torches:
				case Group.Glowsticks:
				case Group.PlaceableObjects:
					return "家具";

				case Group.Wood:
				case Group.Blocks:
				case Group.Walls:
				case Group.Rope:
					return "物块";

				case Group.Pickaxe:
				case Group.Axe:
				case Group.Hammer:
				case Group.Wands:
				case Group.FishingRods:
				case Group.Golf:
				case Group.Solutions:
					return "工具";

				case Group.Material:
					return HardmodeMaterials.Contains(type) ? "肉后材料" : "肉前材料";
			}

			// 游戏分组没有覆盖到的，按物品属性兜底
			return Fallback(item);
		}

		/// <summary>
		/// 统计箱子内的物品，返回出现次数最多的分类（空箱返回 null）。
		/// 相同数量时，先出现的分类优先。
		/// </summary>
		public static string NameChest(Chest chest)
		{
			if (chest?.item == null)
			{
				return null;
			}

			int slots = chest.maxItems;
			if (slots <= 0 || slots > chest.item.Length)
			{
				slots = chest.item.Length;
			}

			Dictionary<string, int> counts = new Dictionary<string, int>();
			for (int i = 0; i < slots; i++)
			{
				Item item = chest.item[i];
				if (item == null || item.IsAir || item.type <= 0)
				{
					continue;
				}

				string category = Classify(item.type);
				if (string.IsNullOrEmpty(category))
				{
					continue;
				}

				counts.TryGetValue(category, out int count);
				counts[category] = count + 1;
			}

			string best = null;
			int bestCount = 0;
			foreach (KeyValuePair<string, int> pair in counts)
			{
				if (pair.Value > bestCount)
				{
					best = pair.Key;
					bestCount = pair.Value;
				}
			}

			return best;
		}

		/// <summary>取名字里图标 tag 后面的描述文本（没有则为空串）。</summary>
		public static string GetText(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return string.Empty;
			}

			int end = name.IndexOf(']');
			string text = end >= 0 ? name.Substring(end + 1) : name;
			return text.Trim();
		}

		/// <summary>从 <c>[i:123]</c> 中取出物品 id，没有则返回 0。</summary>
		public static int ExtractIconType(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return 0;
			}

			int start = name.IndexOf("[i:", StringComparison.Ordinal);
			if (start < 0)
			{
				return 0;
			}

			int end = name.IndexOf(']', start + 3);
			if (end < 0)
			{
				return 0;
			}

			string number = name.Substring(start + 3, end - start - 3);
			if (int.TryParse(number, out int type) && type > 0 && type < ItemID.Count)
			{
				return type;
			}

			return 0;
		}

		/// <summary>箱内第一个非空物品的 id，没有则返回 0。</summary>
		public static int FirstItemType(Chest chest)
		{
			if (chest?.item == null)
			{
				return 0;
			}

			foreach (Item item in chest.item)
			{
				if (item != null && !item.IsAir && item.type > 0)
				{
					return item.type;
				}
			}

			return 0;
		}

		#region 内部辅助

		private static bool IsOreOrBar(Item item)
		{
			if (OreLikeItems.Contains(item.type))
			{
				return true;
			}

			if (item.createTile == TileID.MetalBars)
			{
				return true;
			}

			return item.createTile >= 0 && TileID.Sets.Ore[item.createTile];
		}

		private static bool IsHardmodeOre(Item item)
		{
			if (item.createTile == TileID.MetalBars)
			{
				return HardmodeBars.Contains(item.type);
			}

			return item.createTile >= 0 && HardmodeOreTiles.Contains(item.createTile);
		}

		private static string BulkWeapon(Item item)
		{
			if (item.summon)
			{
				return "召唤";
			}
			if (item.magic)
			{
				return "魔法";
			}
			if (item.ranged)
			{
				return "远程";
			}
			if (item.melee)
			{
				return "近战";
			}
			return "武器";
		}

		private static string Fallback(Item item)
		{
			if (item.damage > 0 && item.ammo == AmmoID.None
				&& item.pick < 1 && item.axe < 1 && item.hammer < 1)
			{
				return BulkWeapon(item);
			}

			if (item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.fishingPole > 1)
			{
				return "工具";
			}

			if (item.accessory)
			{
				return item.vanity ? "时装" : "饰品";
			}

			if (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0)
			{
				return item.vanity ? "时装" : "盔甲";
			}

			if (item.createWall > 0)
			{
				return "物块";
			}

			if (item.createTile >= 0)
			{
				return Main.tileFrameImportant[item.createTile] ? "家具" : "物块";
			}

			if (item.material)
			{
				return HardmodeMaterials.Contains(item.type) ? "肉后材料" : "肉前材料";
			}

			return "杂项";
		}

		private static Group GetGroup(Item item)
		{
			if (ContentSamples.ItemCreativeSortingId.TryGetValue(item.type, out var entry))
			{
				return entry.Group;
			}

			return ContentSamples.CreativeHelper.GetItemGroup(item, out _);
		}

		private static Item GetSample(int type)
		{
			if (ContentSamples.ItemsByType.TryGetValue(type, out Item sample) && sample != null)
			{
				return sample;
			}

			try
			{
				Item item = new Item();
				item.SetDefaults(type);
				return item;
			}
			catch
			{
				return null;
			}
		}

		#endregion
	}
}
