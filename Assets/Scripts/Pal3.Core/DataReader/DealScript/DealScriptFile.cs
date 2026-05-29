// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Core.DataReader.DealScript
{
    /// <summary>
    /// 商店/交易脚本数据文件
    /// </summary>
    public sealed class DealScriptFile
    {
        /// <summary>
        /// 商店类型
        /// </summary>
        public ShopType Type { get; set; }

        /// <summary>
        /// 商人名字
        /// </summary>
        public string MerchantName { get; set; }

        /// <summary>
        /// 商人ID
        /// </summary>
        public int MerchantId { get; set; }

        /// <summary>
        /// 商人性格
        /// </summary>
        public MerchantPersonality Personality { get; set; }

        /// <summary>
        /// 忍耐度（还价次数限制）
        /// </summary>
        public int Patience { get; set; }

        /// <summary>
        /// 进店欢迎语
        /// </summary>
        public string WelcomeDialogue { get; set; }

        /// <summary>
        /// 离店对话
        /// </summary>
        public string LeaveDialogue { get; set; }

        /// <summary>
        /// 购买成功对话
        /// </summary>
        public string BuySuccessDialogue { get; set; }

        /// <summary>
        /// 购买失败对话
        /// </summary>
        public string BuyFailDialogue { get; set; }

        /// <summary>
        /// 出售对话
        /// </summary>
        public string SellDialogue { get; set; }

        /// <summary>
        /// 还价开始对话
        /// </summary>
        public string BargainStartDialogue { get; set; }

        /// <summary>
        /// 还价成功对话
        /// </summary>
        public string BargainSuccessDialogue { get; set; }

        /// <summary>
        /// 还价失败对话
        /// </summary>
        public string BargainFailDialogue { get; set; }

        /// <summary>
        /// 商人生气对话
        /// </summary>
        public string BargainAngryDialogue { get; set; }

        /// <summary>
        /// 强制接收对话（商人说）
        /// </summary>
        public string[] ForceAcceptToDialogues { get; set; }

        /// <summary>
        /// 强制接收对话（玩家说）
        /// </summary>
        public string ForceAcceptBackDialogue { get; set; }

        /// <summary>
        /// 还价档位数据
        /// </summary>
        public BargainTierData[] BargainTiers { get; set; }

        /// <summary>
        /// 可购买的装备物品ID列表
        /// </summary>
        public int[] EquipmentIds { get; set; }

        /// <summary>
        /// 可购买的道具物品ID列表
        /// </summary>
        public int[] PropIds { get; set; }
    }
}
