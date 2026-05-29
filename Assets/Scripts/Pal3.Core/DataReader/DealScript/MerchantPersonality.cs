// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Core.DataReader.DealScript
{
    /// <summary>
    /// 商人性格类型
    /// </summary>
    public enum MerchantPersonality
    {
        /// <summary>
        /// 普通
        /// </summary>
        Normal = 0,

        /// <summary>
        /// 慷慨（更容易还价）
        /// </summary>
        Generous = 1,

        /// <summary>
        /// 吝啬（更难还价）
        /// </summary>
        Stingy = 2,

        /// <summary>
        /// 暴躁（还价失败会生气）
        /// </summary>
        HotTempered = 3,
    }
}
