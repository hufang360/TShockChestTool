using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.UI;
using TShockAPI;

namespace ChestTool
{
    public partial class ChestTool
    {
        /// <summary>名字以此结尾时，/chest org 清除词缀并整理堆叠。</summary>
        public const string NoPrefixMark = "清除词缀";

        /// <summary>名字以此结尾时，/chest org 按首个物品自动命名。</summary>
        public const string RenameMark = "--";

        // ItemSorting 内部使用静态共享列表，非线程安全，串行化
        private static readonly object SortLock = new();

        // 上次查询的物品（按玩家 index），用于 /chest f - 快速重复查询（控制台 index 为 -1）
        private static readonly Dictionary<int, string> LastQuery = new Dictionary<int, string>();

        /// <summary>查找范围与行为。</summary>
        private enum FindMode
        {
            /// <summary>视野内已命名的箱子，找到就传送。</summary>
            View,
            /// <summary>出生点附近，只报数量（管理员）。</summary>
            Base,
            /// <summary>全图，只报数量。</summary>
            All,
        }

        // stats 的分类展示顺序（不在表里的分类排最后）
        private static readonly string[] CategoryOrder =
        {
            "肉前矿", "肉后矿", "肉前材料", "肉后材料",
            "近战", "远程", "魔法", "召唤", "武器",
            "饰品", "盔甲", "时装",
            "家具", "箱子", "物块", "电路", "墓碑",
            "草药", "材料鱼", "药水",
            "boss召唤", "小动物", "坐骑", "宠物",
            "弹药", "工具", "叫声", "吃的", "染料",
            "材料", "杂项",
        };

        // 兼容 [i:123]、[item:123]、[i/p3:123]、[i/s5:123]、[i/p3,s5:123]、[i/s5/p3:123]
        private static readonly Regex ItemTagRegex = new Regex(
            @"\[i(?:tem)?(?<opts>(?:[/,][spx]\d{1,5})*)?:(?<id>-?\d{1,5})\]",
            RegexOptions.Compiled);

        private void Manage(CommandArgs args)
        {
            TSPlayer op = args.Player;

            // 帮助
            void SendHelp()
            {
                bool admin = op.HasPermission(PermAdmin);
                List<string> lines=
                [
                    "/chest <物品>，视野内找，会传送。@1=快捷栏第1格",
                    "/chest findall <物品>，全图找，只报数量",
                    "/chest findbase <物品>，基地里找，只报数量。含没名字的箱子。（管理员）",
                    "/chest show @1，展示快捷栏1~10格的物品到聊天框",
                    "/chest org [help]，整理视野内箱子",
                    "/chest stats，统计箱子（管理员），可加 --base --bychest --nopage",
                ];

                // 非管理员看不到管理员命令
                if (!admin)
                {
                    lines.RemoveAll(line =>
                        line.StartsWith("/chest findbase", StringComparison.Ordinal)
                        || line.StartsWith("/chest stats", StringComparison.Ordinal));
                }

                // 控制台：没有视野，也没有可交互的对象
                if (!op.RealPlayer)
                {
                    lines.RemoveAll(line =>
                        line.StartsWith("/chest org", StringComparison.Ordinal)
                        || line.StartsWith("/chest <", StringComparison.Ordinal)
                        || line.StartsWith("/chest show", StringComparison.Ordinal));
                }

                Pagination(args, lines, "/chest");
            }

            // 权限
            bool HasPermission(string permission)
            {
                if (!op.HasPermission(permission))
                {
                    op.SendErrorMessage("你没有权限执行该指令");
                    return false;
                }
                return true;
            }

            if (args.Parameters.Count == 0)
            {
                SendHelp();
                return;
            }

            string first = args.Parameters[0];
            args.Parameters.RemoveAt(0);

            switch (first.ToLowerInvariant())
            {
                // 整理
                case "org":
                case "organize":
                    if (!HasPermission(PermUse)) return;
                    Organize(args);
                    return;

                // 帮助
                case "help":
                case "h":
                    SendHelp();
                    return;

                // 查找
                case "f":
                case "find":
                    Find(args, FindMode.View);
                    return;
                case "fb":
                case "findbase":
                    Find(args, FindMode.Base);
                    return;
                case "fa":
                case "findall":
                    Find(args, FindMode.All);
                    return;

                // 统计（仅管理员）
                case "s":
                case "stats":
                    Stats(args);
                    return;

                // 展示快捷栏 / 背包物品到聊天框
                case "show":
                    ShowItem(args);
                    return;

                // 其余情况视为直接查找物品：/chest <物品>
                default:
                    args.Parameters.Insert(0, first);
                    Find(args, FindMode.View);
                    return;
            }
        }

        /// <summary>
        /// 整理箱子：
        /// <para>名字以「清除词缀」结尾：清除物品词缀 + 合并堆叠 + 压实空格。</para>
        /// <para>名字以「--」结尾：按首个物品自动命名。</para>
        /// 目标：玩家视野内的箱子。加上 help / h 则查看详细说明。
        /// </summary>
        private void Organize(CommandArgs args)
        {
            TSPlayer op = args.Player;

            // /chest org help：查看整理说明
            if (args.Parameters.Count > 0)
            {
                string sub = args.Parameters[0].ToLowerInvariant();
                if (sub == "help" || sub == "h")
                {
                    SendOrgHelp(args);
                    return;
                }
            }

            if (!op.RealPlayer)
            {
                op.SendErrorMessage("请进入游戏后，再进行操作！");
                return;
            }

            // 箱子名只在关箱时才同步到服务器，开着箱子时读到的可能是旧名字
            if (op.ActiveChest >= 0)
            {
                op.SendErrorMessage($"请先合上箱子（按 Esc 或右键收起）再整理，并确保箱子名以「{NoPrefixMark}」或「{RenameMark}」结尾");
                return;
            }

            Rectangle screen = new Rectangle(op.TileX - 61, op.TileY - 34 + 3, 122, 68);
            int sorted = 0;
            int cleared = 0;
            int renamed = 0;
            int renameTargets = 0;

            for (int index = 0; index < Main.chest.Length; index++)
            {
                Chest chest = Main.chest[index];
                if (chest == null)
                {
                    continue;
                }

                if (!new Rectangle(chest.x, chest.y, 2, 2).Intersects(screen))
                {
                    continue;
                }

                if (NameEndsWith(chest.name, NoPrefixMark))
                {
                    // 清词缀
                    for (int i = 0; i < chest.item.Length; i++)
                    {
                        Item item = chest.item[i];
                        if (item == null || item.IsAir) continue;
                        if (item.prefix <= 0 || item.prefix >= Lang.prefix.Length) continue;
                        item.prefix = 0;
                        cleared++;
                    }

                    // 复用原版「整理」：合并同堆 + 分类排序 + 压实空格
                    lock (SortLock)
                    {
                        ItemSorting.SortInventory(chest, withSync: false, withFeedback: false);
                    }

                    // 服务端 SortInventory 不会自己发包，逐格广播给所有客户端
                    for (int i = 0; i < chest.item.Length; i++)
                    {
                        SyncChestItem(index, i, chest.item[i]);
                    }

                    sorted++;
                }
                else if (NameEndsWith(chest.name, RenameMark))
                {
                    renameTargets++;
                    if (TryRenameChest(index, chest))
                    {
                        renamed++;
                    }
                }
            }

            if (sorted == 0 && renameTargets == 0)
            {
                op.SendErrorMessage($"视野范围内没有名字以「{NoPrefixMark}」或「{RenameMark}」结尾的箱子");
                return;
            }

            List<string> parts = [];
            if (sorted > 0) parts.Add($"整理 {sorted} 个箱子、清除 {cleared} 件词缀");
            if (renamed > 0) parts.Add($"重命名 {renamed} 个箱子");
            if (renameTargets > renamed) parts.Add($"{renameTargets - renamed} 个箱子空箱或无法识别，未命名");
            op.SendSuccessMessage("已" + string.Join("，", parts));
        }

        /// <summary>/chest org help：整理箱子的详细说明。</summary>
        private static void SendOrgHelp(CommandArgs args)
        {
            List<string> lines =
            [
                "/chest organize，整理视野内的箱子",
                $"名字以「{NoPrefixMark}」结尾：清除物品词缀 + 合并堆叠 + 压实空格",
                $"名字以「{RenameMark}」结尾：按首个物品的分类自动命名",
                "提示：整理前请先合上箱子，箱子名关箱后才会同步给服务器",
            ];

            Pagination(args.Player, args.Parameters, lines, "/chest org",
                expectedParameterIndex: 1,
                headerFormat: "===== 整理箱子 {0}/{1} =====",
                footerFormat: "输入 /chest org help {0} 查看更多");
        }

        /// <summary>
        /// 把名字以「--」结尾的箱子按首个物品自动命名。
        /// 首个物品：优先第 1 格，第 1 格为空时取最靠前的物品。
        /// </summary>
        private static bool TryRenameChest(int index, Chest chest)
        {
            int firstType = ChestNamer.FirstItemType(chest);
            if (firstType <= 0)
            {
                return false;
            }

            string category = ChestNamer.Classify(firstType);
            if (string.IsNullOrEmpty(category))
            {
                return false;
            }

            // 去掉结尾的「--」
            string name = chest.name.TrimEnd();
            name = name.Substring(0, name.Length - RenameMark.Length).TrimEnd();

            int icon = ChestNamer.ExtractIconType(name);
            if (icon == 0)
            {
                icon = firstType;
            }

            string tag = icon == 0 ? string.Empty : $"[i:{icon}]";
            chest.name = TrimName(tag, ChestNamer.GetText(name) + category);

            SyncChestName(index, chest);
            return true;
        }

        /// <summary>
        /// 统计箱子里的物品：按 id 合并堆叠数量，忽略词缀。仅管理员可用。
        /// 默认分页、按物品分类分组；--base 改为出生点附近，--bychest 按箱子名分组，
        /// --nopage 一次显示全部；控制台只能配合 --base 使用，且默认不分页。
        /// </summary>
        private void Stats(CommandArgs args)
        {
            TSPlayer op = args.Player;

            if (!op.HasPermission(PermAdmin))
            {
                op.SendErrorMessage($"统计需要 {PermAdmin} 权限");
                return;
            }

            bool noPage = false;
            bool baseArea = false;
            bool byChest = false;
            List<string> pageArgs = new List<string>();
            foreach (string parameter in args.Parameters)
            {
                switch (parameter.ToLowerInvariant())
                {
                    case "--nopage":
                    case "-n":
                        noPage = true;
                        break;
                    case "--base":
                        baseArea = true;
                        break;
                    case "--bychest":
                    case "-c":
                        byChest = true;
                        break;
                    default:
                        pageArgs.Add(parameter);
                        break;
                }
            }

            // 控制台没有视野，只能统计出生点附近，而且看日志不分页
            if (!op.RealPlayer)
            {
                if (!baseArea)
                {
                    op.SendErrorMessage("控制台没有视野，请用 /chest stats --base");
                    return;
                }
                noPage = true;
            }

            Rectangle screen = GetViewRectangle(op, baseArea);
            int chestCount = 0;
            int typeCount = 0;

            const int ItemsPerLine = 10;
            const int LinesPerPage = 8;

            List<string> lines = new List<string>();
            List<List<string>> lineTags = new List<List<string>>();   // 与 lines 对齐，标题行为 null

            if (byChest)
            {
                // 按箱子名分组：每个箱子一组，组标题为箱子名（无名箱子标题留空）
                Dictionary<int, int> allTypes = new Dictionary<int, int>();

                foreach (Chest chest in Main.chest)
                {
                    if (chest == null) continue;
                    if (!new Rectangle(chest.x, chest.y, 2, 2).Intersects(screen)) continue;

                    Dictionary<int, int> chestTotals = new Dictionary<int, int>();
                    foreach (Item item in chest.item)
                    {
                        if (item == null || item.IsAir || item.type <= 0) continue;

                        chestTotals.TryGetValue(item.type, out int sum);
                        chestTotals[item.type] = sum + item.stack;

                        allTypes.TryGetValue(item.type, out int all);
                        allTypes[item.type] = all + item.stack;
                    }

                    if (chestTotals.Count == 0) continue;

                    chestCount++;
                    lines.Add(FormatChestTitle(chest.name));
                    lineTags.Add(null);

                    List<KeyValuePair<int, int>> ordered = chestTotals.ToList();
                    ordered.Sort((a, b) =>
                    {
                        int cmp = b.Value.CompareTo(a.Value);
                        return cmp != 0 ? cmp : a.Key.CompareTo(b.Key);
                    });

                    List<string> tags = new List<string>();
                    foreach (KeyValuePair<int, int> pair in ordered)
                    {
                        AppendItemTags(tags, pair.Key, pair.Value);
                    }

                    for (int i = 0; i < tags.Count; i += ItemsPerLine)
                    {
                        lines.Add(string.Empty);
                        lineTags.Add(tags.GetRange(i, Math.Min(ItemsPerLine, tags.Count - i)));
                    }
                }

                typeCount = allTypes.Count;

                if (chestCount == 0)
                {
                    op.SendInfoMessage(baseArea ? "出生点附近没有可统计的箱子" : "视野内没有可统计的箱子");
                    return;
                }
            }
            else
            {
                // 默认：按物品分类分组（视野内 / 出生点附近的所有箱子）
                Dictionary<int, int> totals = new Dictionary<int, int>();

                foreach (Chest chest in Main.chest)
                {
                    if (chest == null) continue;
                    if (!new Rectangle(chest.x, chest.y, 2, 2).Intersects(screen)) continue;

                    chestCount++;
                    foreach (Item item in chest.item)
                    {
                        if (item == null || item.IsAir || item.type <= 0) continue;

                        totals.TryGetValue(item.type, out int sum);
                        totals[item.type] = sum + item.stack;
                    }
                }

                if (totals.Count == 0)
                {
                    op.SendInfoMessage(baseArea ? "出生点附近没有箱子，或箱子里没有物品" : "视野内没有箱子，或箱子里没有物品");
                    return;
                }

                typeCount = totals.Count;

                // 按分类分组
                Dictionary<string, List<KeyValuePair<int, int>>> groups = new Dictionary<string, List<KeyValuePair<int, int>>>();
                foreach (KeyValuePair<int, int> pair in totals)
                {
                    string category = ChestNamer.Classify(pair.Key) ?? "杂项";
                    if (!groups.TryGetValue(category, out List<KeyValuePair<int, int>> group))
                    {
                        group = new List<KeyValuePair<int, int>>();
                        groups[category] = group;
                    }
                    group.Add(pair);
                }

                // 分类按固定顺序排
                List<string> categories = groups.Keys.ToList();
                categories.Sort((a, b) =>
                {
                    int ia = Array.IndexOf(CategoryOrder, a);
                    int ib = Array.IndexOf(CategoryOrder, b);
                    if (ia < 0) ia = CategoryOrder.Length;
                    if (ib < 0) ib = CategoryOrder.Length;
                    int cmp = ia.CompareTo(ib);
                    return cmp != 0 ? cmp : string.CompareOrdinal(a, b);
                });

                // 每个分类：一行分类名，然后每行 10 个 tag
                foreach (string category in categories)
                {
                    List<KeyValuePair<int, int>> group = groups[category];
                    group.Sort((a, b) =>
                    {
                        int cmp = b.Value.CompareTo(a.Value);
                        return cmp != 0 ? cmp : a.Key.CompareTo(b.Key);
                    });

                    List<string> tags = new List<string>();
                    foreach (KeyValuePair<int, int> pair in group)
                    {
                        AppendItemTags(tags, pair.Key, pair.Value);
                    }

                    lines.Add($"【{category}】");
                    lineTags.Add(null);

                    for (int i = 0; i < tags.Count; i += ItemsPerLine)
                    {
                        lines.Add(string.Empty);
                        lineTags.Add(tags.GetRange(i, Math.Min(ItemsPerLine, tags.Count - i)));
                    }
                }
            }

            // 每页统一间隔：该页有 3 位数（>=100）的堆叠就 4 空格，否则 2 空格
            int pageCount = (lines.Count + LinesPerPage - 1) / LinesPerPage;
            bool[] widePage = new bool[Math.Max(1, pageCount)];
            for (int i = 0; i < lines.Count; i++)
            {
                List<string> tags = lineTags[i];
                if (tags == null) continue;

                int pageIndex = i / LinesPerPage;
                foreach (string tag in tags)
                {
                    if (HasWideStack(tag))
                    {
                        widePage[pageIndex] = true;
                        break;
                    }
                }
            }
            for (int i = 0; i < lines.Count; i++)
            {
                if (lineTags[i] == null) continue;
                lines[i] = string.Join(widePage[i / LinesPerPage] ? "    " : "  ", lineTags[i]);
            }

            Pagination(op, pageArgs, lines, "/chest stats",
                headerFormat: noPage
                    ? $"箱子物品统计（{chestCount}箱 / {typeCount}种）"
                    : $"箱子物品统计（{chestCount}箱 / {typeCount}种） {{0}}/{{1}}",
                footerFormat: "输入 /chest stats {0} 翻页",
                maxLinesPerPage: noPage ? Math.Max(1, lines.Count) : 8);
        }

        /// <summary>tag 里的堆叠数是否达到 3 位（>=100）。</summary>
        private static bool HasWideStack(string tag)
        {
            int s = tag.IndexOf("/s", StringComparison.Ordinal);
            if (s < 0) return false;

            int colon = tag.IndexOf(':', s + 2);
            if (colon < 0) return false;

            return int.TryParse(tag.Substring(s + 2, colon - s - 2), out int stack) && stack >= 100;
        }

        /// <summary>
        /// 把某物品的数量拆成若干 tag：每个 tag 最多 maxStack 个，超过就多来几个 tag。
        /// </summary>
        private static void AppendItemTags(List<string> tags, int type, int total)
        {
            if (total <= 0)
            {
                return;
            }

            int maxStack = GetMaxStack(type);
            while (total > 0)
            {
                int stack = Math.Min(total, maxStack);
                tags.Add(stack > 1 ? $"[i/s{stack}:{type}]" : $"[i:{type}]");
                total -= stack;
            }
        }

        private static int GetMaxStack(int type)
        {
            if (ContentSamples.ItemsByType.TryGetValue(type, out Item item) && item != null)
            {
                return Math.Max(1, item.maxStack);
            }
            return 1;
        }

        /// <summary>
        /// 查找物品所在的箱子。
        /// <para>View：视野内已命名的箱子，找到就传送。</para>
        /// <para>Base：出生点附近，只报数量（管理员）。</para>
        /// <para>All：全图，只报数量。</para>
        /// </summary>
        private void Find(CommandArgs args, FindMode mode)
        {
            TSPlayer op = args.Player;

            if (args.Parameters.Count == 0)
            {
                op.SendErrorMessage("语法：/chest <物品>（findbase 出生点附近，findall 全图）");
                return;
            }

            string query = args.Parameters[0];

            // 控制台没有视野，不能用默认的视野内查找
            if (!op.RealPlayer && mode == FindMode.View)
            {
                op.SendErrorMessage("控制台没有视野，请用 /chest findall <物品> 或 /chest findbase <物品>");
                return;
            }

            // 出生点附近查找仅管理员
            if (mode == FindMode.Base && !op.HasPermission(PermAdmin))
            {
                op.SendErrorMessage($"出生点附近查找需要 {PermAdmin} 权限");
                return;
            }

            Item item = ResolveItem(op, query);
            if (item == null) return;

            // 记住这次的物品，方便 /chest - 重复查询
            if (query != "-")
            {
                LastQuery[op.Index] = query;
            }

            Rectangle screen = GetViewRectangle(op, mode == FindMode.Base);
            List<Point16> positions = new List<Point16>();
            int total = 0;

            foreach (Chest chest in Main.chest)
            {
                if (chest == null) continue;

                if (mode == FindMode.View || mode == FindMode.Base)
                {
                    // 视野 / 出生点：只找屏幕范围内的箱子
                    if (!new Rectangle(chest.x, chest.y, 2, 2).Intersects(screen)) continue;

                    // 视野内：只找已命名的箱子
                    if (mode == FindMode.View && string.IsNullOrEmpty(chest.name)) continue;
                }

                int stack = 0;
                foreach (Item it in chest.item)
                {
                    if (it != null && !it.IsAir && it.type == item.type) stack += it.stack;
                }

                if (stack <= 0) continue;

                positions.Add(new Point16(chest.x, chest.y));
                total += stack;
            }

            string label = $"[i:{item.type}]{item.Name}";
            if (positions.Count == 0)
            {
                string where = mode switch
                {
                    FindMode.View => "附近有名字的箱子里",
                    FindMode.Base => "出生点附近的箱子里",
                    _ => "所有箱子里",
                };
                string hint = mode == FindMode.View ? $"，试试 /chest findall {query}" : string.Empty;
                op.SendInfoMessage($"{where}没有 {label}{hint}");
                return;
            }

            op.SendInfoMessage($"共{positions.Count}个箱子 存放了{label}，共计{total}件");

            // 只有视野内查找才传送；出生点 / 全图只报数量
            if (mode == FindMode.View && op.RealPlayer)
            {
                Point16 first = positions[0];
                op.Teleport(first.X * 16, (first.Y - 2) * 16);
            }
        }

        /// <summary>
        /// 解析物品：支持 id、物品名、聊天物品链接（Alt+左键单击物品得到），
        /// 以及 @格 / -（上次查的物品）。
        /// 聊天链接兼容 [i:123]、[item:123]、[i/p3:123]、[i/s5:123]、[i/p3,s5:123]、[i/s5/p3:123]。
        /// </summary>
        private static Item ResolveItem(TSPlayer op, string query)
        {
            // - 表示重复上一次查询的物品
            if (query == "-")
            {
                if (!LastQuery.TryGetValue(op.Index, out string last))
                {
                    op.SendErrorMessage("还没有查询过物品，先用 /chest f <物品> 查一次");
                    return null;
                }
                query = last;
            }

            // @n / @bn 取自己格子里的物品
            if (query.Length > 1 && query[0] == '@')
            {
                return ResolveInventoryItem(op, query);
            }

            // 游戏生成的链接用逗号分隔（[i/p3,s5:123]），TShock 自带的解析不支持，这里自己解析
            Match tag = ItemTagRegex.Match(query);
            if (tag.Success
                && int.TryParse(tag.Groups["id"].Value, out int tagId)
                && tagId > 0 && tagId < ItemID.Count)
            {
                Item linked = new Item();
                linked.netDefaults(tagId);
                return linked;
            }

            if (int.TryParse(query, out int id))
            {
                if (id <= 0 || id >= ItemID.Count)
                {
                    op.SendErrorMessage("物品 id 不正确");
                    return null;
                }

                Item byId = new Item();
                byId.netDefaults(id);
                return byId;
            }

            List<Item> found = TShock.Utils.GetItemByIdOrName(query);
            if (found.Count == 0)
            {
                op.SendErrorMessage("没有找到该物品");
                return null;
            }

            if (found.Count > 1)
            {
                op.SendMultipleMatchError(found.Select(i => $"{i.Name}({i.type})"));
                return null;
            }

            return found[0];
        }

        /// <summary>从玩家背包取物品：@n 快捷栏第 n 格（1-10），@bn 背包第 n 格（1-40）。</summary>
        private static Item ResolveInventoryItem(TSPlayer op, string query)
        {
            if (!op.RealPlayer)
            {
                op.SendErrorMessage("控制台没有物品栏，请直接输入物品 id 或名字");
                return null;
            }

            string text = query.Substring(1);
            bool backpack = text.StartsWith("b", StringComparison.OrdinalIgnoreCase);
            if (backpack) text = text.Substring(1);

            if (!int.TryParse(text, out int slot) || slot <= 0)
            {
                op.SendErrorMessage("格子写法：@1～@10 是快捷栏，@b1～@b40 是背包");
                return null;
            }

            int index = backpack ? 9 + slot : slot - 1;
            Item[] inventory = op.TPlayer.inventory;
            if (inventory == null || index < 0 || index >= inventory.Length)
            {
                op.SendErrorMessage(backpack ? "背包只有 @b1～@b40" : "快捷栏只有 @1～@10");
                return null;
            }

            Item item = inventory[index];
            if (item == null || item.IsAir || item.type <= 0)
            {
                op.SendErrorMessage($"第 {index + 1} 格没有物品");
                return null;
            }

            return item;
        }

        /// <summary>
        /// /chest show @1：把快捷栏 1~10 格（@b1~@b40 背包）的物品发到自己的聊天框。
        /// 消息正文就是物品 tag，带词缀与堆叠：如 [i:2]、[i/p57:4]、[i/s10:29]。
        /// </summary>
        private void ShowItem(CommandArgs args)
        {
            TSPlayer op = args.Player;
            if (args.Parameters.Count == 0)
            {
                op.SendErrorMessage("语法：/chest show @1（@1~@10 快捷栏，@b1~@b40 背包）");
                return;
            }

            Item item = ResolveInventoryItem(op, args.Parameters[0]);
            if (item == null) return;

            List<string> opts = new List<string>();
            if (item.prefix > 0) opts.Add($"p{item.prefix}");
            if (item.stack > 1) opts.Add($"s{item.stack}");
            string optText = opts.Count > 0 ? "/" + string.Join(",", opts) : string.Empty;

            op.SendInfoMessage($"[i{optText}:{item.type}]");
        }

        /// <summary>视野矩形（122×68）：默认跟随玩家，baseArea 时以出生点为中心。</summary>
        private static Rectangle GetViewRectangle(TSPlayer op, bool baseArea)
        {
            int tileX = baseArea ? Main.spawnTileX : op.TileX;
            int tileY = baseArea ? Main.spawnTileY : op.TileY;
            return new Rectangle(tileX - 61, tileY - 34 + 3, 122, 68);
        }

        private static bool NameEndsWith(string name, string suffix)
        {
            return !string.IsNullOrEmpty(name) && name.TrimEnd().EndsWith(suffix, StringComparison.Ordinal);
        }

        /// <summary>同步箱子单格给所有客户端。</summary>
        private static void SyncChestItem(int chestIndex, int slot, Item item)
        {
            NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null,
                chestIndex, slot, item.stack, item.prefix, item.type);
        }

        /// <summary>同步箱子名给所有客户端。</summary>
        private static void SyncChestName(int index, Chest chest)
        {
            foreach (TSPlayer op in TShock.Players)
            {
                if (op != null && op.Active)
                {
                    NetMessage.TrySendData(69, op.Index, -1, null, index, chest.x, chest.y);
                }
            }
        }

        #region 分页 / 箱子名标题

        /// <summary>
        /// 分页显示（参考 TShockWorldModify/WorldModify/utils.cs 的 Pagination）。
        /// </summary>
        private static void Pagination(CommandArgs args, List<string> lines, string parentCMD,
            int expectedParameterIndex = 0, string headerFormat = null, string footerFormat = null,
            int maxLinesPerPage = 8)
        {
            Pagination(args.Player, args.Parameters, lines, parentCMD,
                expectedParameterIndex, headerFormat, footerFormat, maxLinesPerPage);
        }

        /// <summary>分页显示（页码从 parameters 的第 expectedParameterIndex 位取）。</summary>
        private static void Pagination(TSPlayer op, List<string> parameters, List<string> lines, string parentCMD,
            int expectedParameterIndex = 0, string headerFormat = null, string footerFormat = null,
            int maxLinesPerPage = 8)
        {
            if (!PaginationTools.TryParsePageNumber(parameters, expectedParameterIndex, op, out int pageNumber))
            {
                return;
            }

            PaginationTools.SendPage(op, pageNumber, lines, new PaginationTools.Settings
            {
                HeaderFormat = headerFormat ?? $"[c/96FF0A:{parentCMD}]指令用法 ({{0}}/{{1}})：",
                FooterFormat = footerFormat ?? $"输入 [c/96FF0A:{parentCMD} help {{0}}] 查看更多",
                MaxLinesPerPage = maxLinesPerPage,
            });
        }

        /// <summary>箱子名标题：不高亮，图标放在【】内（图标 tag 不能和颜色 tag 嵌套）。</summary>
        private static string FormatChestTitle(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            string text = ChestNamer.GetText(name);
            int icon = ChestNamer.ExtractIconType(name);
            string tag = icon == 0 ? string.Empty : $"[i:{icon}]";
            return $"【{tag}{text}】";
        }

        #endregion
    }
}
