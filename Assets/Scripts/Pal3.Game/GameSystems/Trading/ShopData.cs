// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Trading
{
    using System.Collections.Generic;

    /// <summary>
    /// 商店数据（用于UI显示）
    /// </summary>
    public sealed class ShopData
    {
        public string ShopName { get; set; }
        public int ShopType { get; set; }
        public List<ShopItem> Items { get; set; } = new();
    }

    /// <summary>
    /// 商店物品
    /// </summary>
    public sealed class ShopItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Price { get; set; }
        public int Type { get; set; }
    }
}