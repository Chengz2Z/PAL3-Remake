// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Core.DataReader.DealScript
{
    /// <summary>
    /// 还价档位数据
    /// </summary>
    public sealed class BargainTierData
    {
        /// <summary>
        /// 折扣百分比
        /// </summary>
        public int Discount { get; set; }

        /// <summary>
        /// 还价对话（商人说）
        /// </summary>
        public string ToDialogue { get; set; }

        /// <summary>
        /// 回应对话（玩家说）
        /// </summary>
        public string BackDialogue { get; set; }

        /// <summary>
        /// 成功率
        /// </summary>
        public int SuccessRate { get; set; }
    }
}
