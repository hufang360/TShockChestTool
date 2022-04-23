using System;
using System.Reflection;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace ChestTool
{
    [ApiVersion(2, 1)]
    public class ChestTool : TerrariaPlugin
    {
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
        }
        private void OnChestChange(object sender, GetDataHandlers.ChestItemEventArgs args)
        {
            //ID,Slot,Stacks,Prefix,Type
            int index = Main.player[args.Player.Index].chest;
            if (index == -1 || args.Slot != 0) return;

            Chest ch = Main.chest[index];
            string text = ch.name.Contains("]") ? ch.name.Substring(ch.name.IndexOf("]") + 1) : ch.name;
            ch.name = args.Type == 0 ? text : $"[i:{args.Type}]{text}";

            foreach (TSPlayer op in TShock.Players)
            {
                // 更新箱子名
                if (op != null && op.Active) NetMessage.TrySendData(69, op.Index, -1, null, index, ch.x, ch.y);
            }
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
