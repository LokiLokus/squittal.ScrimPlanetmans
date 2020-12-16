﻿using Microsoft.EntityFrameworkCore.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace squittal.ScrimPlanetmans.ScrimMatch.Models
{
    public class JsonRuleset
    {
        public string Name { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateLastModified { get; set; }

        public bool IsDefault { get; set; }

        public string FileName { get; set; }

        public string DefaultMatchTitle { get; set; } = string.Empty;
        public int DefaultRoundLength { get; set; } = 900;

        #region Overlay Settings
        public bool? UseCompactOverlay { get; set; }
        public OverlayStatsDisplayType? OverlayStatsDisplayType { get; set; }
        #endregion Overlay Settings

        public ICollection<JsonRulesetActionRule> RulesetActionRules { get; set; }
        public ICollection<JsonRulesetItemCategoryRule> RulesetItemCategoryRules { get; set; }
        public ICollection<JsonRulesetFacilityRule> RulesetFacilityRules { get; set; }

        public JsonRuleset()
        {
        }

        public JsonRuleset(Ruleset ruleset, string fileName)
        {
            Name = ruleset.Name;
            DateCreated = ruleset.DateCreated;
            DateLastModified = ruleset.DateLastModified;
            IsDefault = ruleset.IsDefault;
            FileName = fileName;
            DefaultMatchTitle = ruleset.DefaultMatchTitle;
            DefaultRoundLength = ruleset.DefaultRoundLength;
            UseCompactOverlay = ruleset.UseCompactOverlay;
            OverlayStatsDisplayType = ruleset.OverlayStatsDisplayType;

            if (ruleset.RulesetActionRules.Any())
            {
                RulesetActionRules = ruleset.RulesetActionRules.Select(r => new JsonRulesetActionRule(r)).ToArray();
            }

            if (ruleset.RulesetItemCategoryRules.Any())
            {
                RulesetItemCategoryRules = ruleset.RulesetItemCategoryRules.Select(r => new JsonRulesetItemCategoryRule(r, GetItemCategoryJsonItemRules(ruleset.RulesetItemRules, r.ItemCategoryId))).ToArray();
            }
            
            if (ruleset.RulesetFacilityRules.Any())
            {
                RulesetFacilityRules = ruleset.RulesetFacilityRules.Select(r => new JsonRulesetFacilityRule(r)).ToArray();
            }
        }

        private ICollection<JsonRulesetItemRule> GetItemCategoryJsonItemRules(ICollection<RulesetItemRule> allItemRules, int itemCategoryId)
        {
            if (allItemRules == null || !allItemRules.Any(r => r.ItemCategoryId == itemCategoryId))
            {
                return new List<JsonRulesetItemRule>().ToArray();
            }

            return allItemRules.Where(r => r.ItemCategoryId == itemCategoryId).Select(r => new JsonRulesetItemRule(r)).ToArray();
        }
    }
}