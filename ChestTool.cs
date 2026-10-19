using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace ChestTool
{
    [ApiVersion(2, 1)]
    public partial class ChestTool : TerrariaPlugin
    {
        /// <summary>箱子名的最大长度（含图标 tag）。</summary>
        public const int MaxChestNameLength = 20;

        /// <summary>整理箱子的权限。</summary>
        public const string PermUse = "hf.chest.use";

        /// <summary>管理员权限（全图查找显示坐标 / 传送）。</summary>
        public const string PermAdmin = "hf.chest.admin";

        # region Plugin Info
        public override string Name => "ChestTool";
        public override string Description => "箱子小工具";
        public override string Author => "hufang360";
        public override Version Version => Assembly.GetExecutingAssembly().GetName().Version;
        #endregion

        public ChestTool(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            GetDataHandlers.ChestItemChange += OnChestChange;
            Commands.ChatCommands.Add(new Command( [PermUse, PermAdmin], Manage, "chest", "ch") { HelpText = "箱子小工具", AllowServer = true, });
        }

        private void OnChestChange(object sender, GetDataHandlers.ChestItemEventArgs args)
        {
            //ID,Slot,Stacks,Prefix,Type
            if (args.Player == null || args.Player.Index < 0 || args.Player.Index >= Main.player.Length)
            {
                return;
            }

            int index = Main.player[args.Player.Index].chest;
            if (index == -1 || args.Slot != 0) return;

            Chest ch = Main.chest[index];
            string text = ch.name.Contains("]") ? ch.name.Substring(ch.name.IndexOf("]") + 1) : ch.name;
            string tag = args.Type == 0 ? string.Empty : $"[i:{args.Type}]";
            ch.name = TrimName(tag, text);

            foreach (TSPlayer op in TShock.Players)
            {
                // 更新箱子名
                if (op != null && op.Active) NetMessage.TrySendData(69, op.Index, -1, null, index, ch.x, ch.y);
            }
        }

        /// <summary>拼出箱子名，并限制总长度，且不会截断图标 tag。</summary>
        private static string TrimName(string tag, string text)
        {
            int room = MaxChestNameLength - tag.Length;
            if (room < 0)
            {
                room = 0;
            }

            if (text.Length > room)
            {
                text = text.Substring(0, room);
            }

            return tag + text;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GetDataHandlers.ChestItemChange -= OnChestChange;
            }
            base.Dispose(disposing);
        }
    }

}
