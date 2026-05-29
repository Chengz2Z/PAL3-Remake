// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Core.DataReader.DealScript
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    public sealed class DealScriptFileReader : IFileReader<DealScriptFile>
    {
        public DealScriptFile Read(IBinaryReader reader, int codepage)
        {
            throw new NotImplementedException("DealScript files are text-based, use Read(byte[], int) instead.");
        }

        public DealScriptFile Read(byte[] data, int codepage)
        {
            string content = Encoding.GetEncoding(codepage).GetString(data, 0, data.Length);
            return Parse(content);
        }

        private static DealScriptFile Parse(string content)
        {
            var file = new DealScriptFile();
            var equipmentIds = new List<int>();
            var propIds = new List<int>();
            var bargainTiers = new List<BargainTierData>();

            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";"))
                    continue;

                string[] parts = trimmed.Split('$');
                if (parts.Length < 2) continue;

                string key = parts[0].Trim();
                string value = parts[1].Trim().TrimEnd('&');

                switch (key)
                {
                    case "ST": // 商店类型
                        if (int.TryParse(value, out int shopType))
                            file.Type = (ShopType)shopType;
                        break;

                    case "Name": // 商人名字
                        file.MerchantName = value;
                        break;

                    case "ID": // 商人ID
                        if (int.TryParse(value, out int merchantId))
                            file.MerchantId = merchantId;
                        break;

                    case "Nature": // 商人性格
                        if (int.TryParse(value, out int personality))
                            file.Personality = (MerchantPersonality)personality;
                        break;

                    case "Endure": // 忍耐度
                        if (int.TryParse(value, out int patience))
                            file.Patience = patience;
                        break;

                    case "Hello": // 进店欢迎语
                        file.WelcomeDialogue = value;
                        break;

                    case "Leave": // 离店对话
                        file.LeaveDialogue = value;
                        break;

                    case "BuyOk": // 购买成功对话
                        file.BuySuccessDialogue = value;
                        break;

                    case "BuyNo": // 购买失败对话
                        file.BuyFailDialogue = value;
                        break;

                    case "Sell": // 出售对话
                        file.SellDialogue = value;
                        break;

                    case "DkStart": // 还价开始对话
                        file.BargainStartDialogue = value;
                        break;

                    case "DkOk": // 还价成功对话
                        file.BargainSuccessDialogue = value;
                        break;

                    case "DkNo": // 还价失败对话
                        file.BargainFailDialogue = value;
                        break;

                    case "DkAngry": // 商人生气对话
                        file.BargainAngryDialogue = value;
                        break;

                    case "DK": // 还价档位开始
                        {
                            if (int.TryParse(value, out int discount))
                            {
                                var tier = new BargainTierData { Discount = discount };
                                // 读取后续的还价数据
                                string toKey = $"Dkto{discount}";
                                string backKey = $"Dkback{discount}";
                                string rateKey = $"DkRate{discount}";

                                foreach (string subLine in lines)
                                {
                                    string[] subParts = subLine.Trim().Split('$');
                                    if (subParts.Length < 2) continue;
                                    string subKey = subParts[0].Trim();
                                    string subValue = subParts[1].Trim().TrimEnd('&');

                                    if (subKey == toKey)
                                        tier.ToDialogue = subValue;
                                    else if (subKey == backKey)
                                        tier.BackDialogue = subValue;
                                    else if (subKey == rateKey && int.TryParse(subValue, out int rate))
                                        tier.SuccessRate = rate;
                                }
                                bargainTiers.Add(tier);
                            }
                        }
                        break;

                    case "ForceAcceptTo": // 强制接收对话（商人说）
                        {
                            var dialogues = new List<string>();
                            foreach (string part in parts)
                            {
                                string p = part.Trim().TrimEnd('&');
                                if (!string.IsNullOrEmpty(p) && p != key)
                                    dialogues.Add(p);
                            }
                            file.ForceAcceptToDialogues = dialogues.ToArray();
                        }
                        break;

                    case "ForceAcceptBack": // 强制接收对话（玩家说）
                        file.ForceAcceptBackDialogue = value;
                        break;

                    case "Equip": // 装备物品
                        {
                            foreach (string part in parts)
                            {
                                string p = part.Trim().TrimEnd('&');
                                if (int.TryParse(p, out int id) && id > 0)
                                    equipmentIds.Add(id);
                            }
                        }
                        break;

                    case "Prop": // 道具物品
                        {
                            foreach (string part in parts)
                            {
                                string p = part.Trim().TrimEnd('&');
                                if (int.TryParse(p, out int id) && id > 0)
                                    propIds.Add(id);
                            }
                        }
                        break;
                }
            }

            file.BargainTiers = bargainTiers.ToArray();
            file.EquipmentIds = equipmentIds.ToArray();
            file.PropIds = propIds.ToArray();

            return file;
        }
    }
}